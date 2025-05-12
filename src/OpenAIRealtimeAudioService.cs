using System.Net.WebSockets;
using System.Text;
using NAudio.Wave;
using System.IO;
using System.Text.Json;


namespace UGTLive
{
    public class OpenAIRealtimeAudioServiceWhisper
    {
        private ClientWebSocket? ws;
        private CancellationTokenSource? cts;
        private WaveInEvent? waveIn;
        private WasapiLoopbackCapture? loopbackCapture;
        private WaveOutEvent? waveOut;
        private BufferedWaveProvider? bufferedWaveProvider;
        private bool useLoopback;
        private readonly List<byte> audioBuffer = new List<byte>();
        private readonly object bufferLock = new object();
        private int chunkSize;

        // Renamed state variables for clarity
        private string _currentRawTranscriptText = string.Empty;
        private string _currentTranslationText = string.Empty;
        // Holds the specific transcript for which an OpenAI translation is currently being processed
        private string _activeTranscriptForTurn = string.Empty; 
        // Flag to track whether we are waiting for a translation to finish
        private bool _translationInProgress = false;
        // Track the last few transcript/translation pairs for context
        private Queue<(string Transcript, string Translation)> _recentUtterances = new Queue<(string, string)>();
        private const int MAX_RECENT_UTTERANCES = 5;
        
        // Track all session content for debugging
        private StringBuilder _fullSessionContent = new StringBuilder();
        
        // Audio playback settings
        private bool _audioPlaybackEnabled = true;
        private int _audioOutputDeviceIndex = -1; // Default to system default

        public void StartRealtimeAudioService(Action<string, string> onResult, bool useLoopback = false)
        {
            this.useLoopback = useLoopback;
            Stop();
            cts = new CancellationTokenSource();
            Task.Run(() => RunAsync(onResult, cts.Token), cts.Token);
        }

        public void Stop()
        {
            try
            {
                cts?.Cancel();
                StopAudioCapture();
                StopAudioPlayback();
                ws?.Dispose();
            }
            catch (Exception ex)
            {
                Log($"Error during stop: {ex.Message}");
            }
            finally
            {
                waveIn = null;
                loopbackCapture = null;
                ws = null;
            }
        }
        
        private void StopAudioCapture()
        {
            try
            {
                // Make a local copy of the references to avoid race conditions
                var localWaveIn = waveIn;
                var localLoopbackCapture = loopbackCapture;
                
                // First set the class variables to null to prevent new access
                waveIn = null;
                loopbackCapture = null;
                
                // Now safely dispose the local copies
                if (localWaveIn != null)
                {
                    try
                    {
                        // Detach event handler first
                        localWaveIn.DataAvailable -= OnDataAvailable;
                        
                        // Stop recording safely - WaveInEvent doesn't have RecordingState
                        try
                        {
                            localWaveIn.StopRecording();
                        }
                        catch (InvalidOperationException)
                        {
                            // Already stopped, ignore this exception
                            Log("WaveIn was already stopped");
                        }
                        
                        // Dispose with a small delay to allow any pending operations to complete
                        Task.Run(() => {
                            try 
                            {
                                Thread.Sleep(100); // Give time for any pending operations
                                localWaveIn.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Log($"Error during delayed WaveIn disposal: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"Error stopping WaveIn: {ex.Message}");
                    }
                }
                
                if (localLoopbackCapture != null)
                {
                    try
                    {
                        // Detach event handler first
                        localLoopbackCapture.DataAvailable -= OnDataAvailable;
                        
                        // Stop recording safely
                        try
                        {
                            localLoopbackCapture.StopRecording();
                        }
                        catch (InvalidOperationException)
                        {
                            // Already stopped, ignore this exception
                            Log("Loopback capture was already stopped");
                        }
                        
                        // Dispose with a small delay
                        Task.Run(() => {
                            try 
                            {
                                Thread.Sleep(100); // Give time for any pending operations
                                localLoopbackCapture.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Log($"Error during delayed loopback capture disposal: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"Error stopping loopback capture: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in StopAudioCapture: {ex.Message}");
            }
        }
        
        private void StopAudioPlayback()
        {
            try
            {
                if (waveOut != null)
                {
                    waveOut.Stop();
                    waveOut.Dispose();
                    waveOut = null;
                }
                
                bufferedWaveProvider = null;
            }
            catch (Exception ex)
            {
                Log($"Error stopping audio playback: {ex.Message}");
            }
        }

        private async Task RunAsync(Action<string, string> onResult, CancellationToken token)
        {
            string apiKey = ConfigManager.Instance.GetOpenAiRealtimeApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                System.Windows.MessageBox.Show(
                    "OpenAI Realtime API key is not set. Please configure it in Settings.",
                    "API Key Missing"
                );
                return;
            }

            // Reset state variables
            _currentRawTranscriptText = string.Empty;
            _currentTranslationText = string.Empty;
            _activeTranscriptForTurn = string.Empty;
            _translationInProgress = false;
            _recentUtterances.Clear(); // Clear history
            _fullSessionContent.Clear(); // Clear session content
            
            // Load audio playback settings
            LoadAudioPlaybackSettings();
            
            // Initialize audio playback if enabled
            if (_audioPlaybackEnabled)
            {
                InitializeAudioPlayback();
            }

            // Connect to OpenAI
            try
            {
                ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
                await ws.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview"), token);
                Log("Connected to OpenAI Realtime API");
            }
            catch (Exception ex)
            {
                Log($"Failed to connect to OpenAI: {ex.Message}");
                return;
            }

            // Configuration for OpenAI Translation
            bool useOpenAITranslation = ConfigManager.Instance.IsOpenAITranslationEnabled(); 
            string targetLanguage = ConfigManager.Instance.GetTargetLanguage();
            string sourceLanguage = ConfigManager.Instance.GetWhisperSourceLanguage();
            bool isSourceLanguageSpecified = !string.IsNullOrEmpty(sourceLanguage) && 
                                           !sourceLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase);

            // Track accumulated translation for this session (helpful for debugging)
            Log("Starting new session, tracking all content...");

            // Prepare the system prompt which includes speech style and translation instructions
            string systemPrompt = "";
            if (useOpenAITranslation)
            {
                // Add speech style instructions if audio playback is enabled
                if (_audioPlaybackEnabled)
                {
                    // Get custom speech prompt and apply it as the primary instruction
                    string speechPromptConfig = ConfigManager.Instance.GetOpenAISpeechPrompt();
                    if (!string.IsNullOrEmpty(speechPromptConfig))
                    {
                        systemPrompt = speechPromptConfig + " ";
                        Log($"Applied speech prompt as primary instruction: '{speechPromptConfig}'");
                    }
                    else
                    {
                        systemPrompt = "You are a real-time translator for a dialogue in a video game or movie. ";
                        Log("No custom speech prompt found, using default translator role for speech style.");
                    }
                }
                else
                {
                    systemPrompt = "You are a real-time translator for a dialogue in a video game or movie. ";
                    Log("Audio playback disabled, not applying speech prompt for style.");
                }
                
                // Add translation instructions
                systemPrompt += $"Translate speech from ";
                
                if (isSourceLanguageSpecified)
                {
                    systemPrompt += $"{sourceLanguage} to {targetLanguage}";
                }
                else
                {
                    systemPrompt += $"the detected language to {targetLanguage}";
                }
                systemPrompt += ". Return only the translation, with no extra text. Always translate complete thoughts and preserve the full meaning of the original text. Wait for complete sentences before finalizing translations. Translate idioms naturally to maintain the intended meaning rather than translating them literally. Ensure each utterance is completely translated before sending it. Never cut off translations mid-sentence.";
                Log($"Constructed system prompt for session instructions: {systemPrompt}");
            }
            else
            {
                Log("OpenAI translation is disabled. No system prompt/instructions will be sent for translation or speech style.");
            }
            
            // Configure Whisper transcription
            object transcriptionConfig;
            if (isSourceLanguageSpecified)
            {
                transcriptionConfig = new
                {
                    model = "whisper-1",
                    language = sourceLanguage
                };
                Log($"Configuring Whisper with language: {sourceLanguage}");
            }
            else
            {
                transcriptionConfig = new
                {
                    model = "whisper-1"
                };
                Log("Configuring Whisper with auto language detection");
            }

            // Set up a higher silence duration to reduce utterance fragmentation
            int silenceDurationMs = ConfigManager.Instance.GetOpenAiSilenceDurationMs();
            
            var sessionUpdateMessage = new
            {
                type = "session.update",
                session = new
                {
                    input_audio_format = "pcm16",
                    input_audio_transcription = transcriptionConfig,
                    turn_detection = new
                    {
                        type = "server_vad",
                        silence_duration_ms = silenceDurationMs,
                        create_response = useOpenAITranslation,
                        interrupt_response = false,
                        prefix_padding_ms = 500, // Increase from 300 to 500ms for better context
                        threshold = 0.6 // Increase threshold slightly for more definitive utterance detection


                    //another method that didn't seem to work well:
                    /*
                         type = "semantic_vad",
                        // The semantic VAD classifier determines turn-taking based on linguistic cues
                        eagerness = "auto", // Can be low | medium | high | auto. Auto balances latency & completeness
                        create_response = useOpenAITranslation,
                        interrupt_response = false
                    */
                    },
                    // Get voice from config instead of hardcoding
                    voice = ConfigManager.Instance.GetOpenAIVoice(),
                    // Add instructions for translation, otherwise send empty string to satisfy API requirement
                    instructions = useOpenAITranslation && !string.IsNullOrEmpty(systemPrompt) ? systemPrompt : "" 
                }
            };
            
            await SendJson(ws, sessionUpdateMessage, token);
            Log(useOpenAITranslation 
                ? $"Session configured with instructions and for OpenAI transcription and translation (silence duration {silenceDurationMs}ms)" 
                : $"Session configured for transcription only (silence duration {silenceDurationMs}ms)");

            // Initialize audio capture
            InitializeAudioCapture();

            // Main receive loop
            await ReceiveMessagesLoop(onResult, token);
        }
        
        private void LoadAudioPlaybackSettings()
        {
            try
            {
                // Get audio playback settings from ConfigManager
                _audioPlaybackEnabled = ConfigManager.Instance.IsOpenAIAudioPlaybackEnabled();
                _audioOutputDeviceIndex = ConfigManager.Instance.GetAudioOutputDeviceIndex();
                
                Log($"Audio playback enabled: {_audioPlaybackEnabled}, Output device index: {_audioOutputDeviceIndex}");
            }
            catch (Exception ex)
            {
                Log($"Error loading audio playback settings: {ex.Message}");
                // Use defaults if loading fails
                _audioPlaybackEnabled = true;
                _audioOutputDeviceIndex = -1;
            }
        }
        
        private void InitializeAudioPlayback()
        {
            try
            {
                // Create wave format for playback - 24kHz stereo 16-bit PCM (OpenAI's format)
                var waveFormat = new WaveFormat(24000, 16, 1);
                
                // Create buffer
                bufferedWaveProvider = new BufferedWaveProvider(waveFormat);
                bufferedWaveProvider.DiscardOnBufferOverflow = true;
                
                // Create wave output
                waveOut = new WaveOutEvent();
                
                // Set device if specified
                if (_audioOutputDeviceIndex >= 0 && _audioOutputDeviceIndex < WaveOut.DeviceCount)
                {
                    waveOut.DeviceNumber = _audioOutputDeviceIndex;
                    Log($"Using audio output device: {WaveOut.GetCapabilities(_audioOutputDeviceIndex).ProductName}");
                }
                else
                {
                    Log("Using default audio output device");
                }
                
                // Initialize playback
                waveOut.Init(bufferedWaveProvider);
                waveOut.Play();
                
                Log("Audio playback initialized successfully");
            }
            catch (Exception ex)
            {
                Log($"Error initializing audio playback: {ex.Message}");
                StopAudioPlayback();
                _audioPlaybackEnabled = false;
            }
        }
        
        private void InitializeAudioCapture()
        {
            try
            {
                if (useLoopback)
                {
                    loopbackCapture = new WasapiLoopbackCapture();
                    loopbackCapture.DataAvailable += OnDataAvailable;
                    chunkSize = loopbackCapture.WaveFormat.AverageBytesPerSecond / 8;
                    loopbackCapture.StartRecording();
                    Log("Started loopback audio capture");
                }
                else
                {
                    waveIn = new WaveInEvent();
                    int deviceIndex = ConfigManager.Instance.GetAudioInputDeviceIndex();
                    
                    if (deviceIndex >= 0 && deviceIndex < WaveInEvent.DeviceCount)
                    {
                        waveIn.DeviceNumber = deviceIndex;
                        Log($"Using audio input device: {WaveInEvent.GetCapabilities(deviceIndex).ProductName} (Index: {deviceIndex})");
                    }
                    else
                    {
                        waveIn.DeviceNumber = 0;
                        if (WaveInEvent.DeviceCount > 0)
                        {
                            Log($"Invalid device index {deviceIndex}. Using device 0: {WaveInEvent.GetCapabilities(0).ProductName}");
                        }
                        else
                        {
                            Log("No audio input devices found. Using device 0 (may fail)");
                        }
                    }

                    // Configure for 24kHz mono 16-bit PCM
                    waveIn.WaveFormat = new WaveFormat(24000, 16, 1);
                    chunkSize = waveIn.WaveFormat.AverageBytesPerSecond / 8;
                    
                    // Clear buffer and start recording
                    audioBuffer.Clear();
                    waveIn.DataAvailable += OnDataAvailable;
                    waveIn.StartRecording();
                    Log("Started microphone audio capture");
                }
            }
            catch (Exception ex)
            {
                Log($"Error initializing audio capture: {ex.Message}");
                StopAudioCapture();
            }
        }

        private async Task ReceiveMessagesLoop(Action<string, string> onResult, CancellationToken token)
        {
            var buffer = new byte[16384]; // Larger buffer for audio messages
            var messageBuffer = new MemoryStream();
            var transcriptBuilder = new StringBuilder(); // For accumulating Whisper deltas

            try
            {
                while (!token.IsCancellationRequested && ws != null && ws.State == WebSocketState.Open)
                {
                    messageBuffer.SetLength(0);
                    WebSocketReceiveResult result;
                    
                    // Read the complete message
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
                            break;
                        }
                        messageBuffer.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (messageBuffer.Length == 0 || result.MessageType == WebSocketMessageType.Close) 
                        continue;

                    // Get the message as string
                    var jsonBytes = messageBuffer.ToArray();
                    var json = Encoding.UTF8.GetString(jsonBytes);
                    
                    // Check if it's an audio message (which is large)
                    bool isAudioMessage = json.Contains("response.audio.delta");
                    
                    // For non-audio messages, log the full content
                    if (!isAudioMessage)
                    {
                        Log($"Received message: {json}");
                    }
                    else
                    {
                        Log($"Received audio message ({jsonBytes.Length} bytes)");
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        
                        // Check for error messages
                        if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "error")
                        {
                            if (root.TryGetProperty("error", out var errorProp) && 
                                errorProp.TryGetProperty("message", out var messageProp))
                            {
                                Log($"API Error: {messageProp.GetString()}");
                            }
                            else
                            {
                                Log($"Unknown API Error: {json}");
                            }
                            continue;
                        }
                        
                        // Get the message type
                        string? messageType = typeProp.GetString();
                        
                        switch (messageType)
                        {
                            // Handle session creation confirmation
                            case "session.created":
                                if (root.TryGetProperty("session", out var sessionCreatedProp) && 
                                    sessionCreatedProp.TryGetProperty("id", out var sessionIdProp))
                                {
                                    Log($"Session created successfully. ID: {sessionIdProp.GetString()}");
                                }
                                else
                                {
                                    Log("Session created message received, but ID could not be parsed.");
                                }
                                break;

                            // Handle session update confirmation
                            case "session.updated":
                                Log("Session updated successfully by server.");
                                // Optionally, you could parse and log specific updated fields from root.GetProperty("session") if needed for debugging.
                                break;

                            // Handle whisper transcription (partial)
                            case "conversation.item.input_audio_transcription.delta":
                                if (root.TryGetProperty("delta", out var deltaProp))
                                {
                                    var delta = deltaProp.GetString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        transcriptBuilder.Append(delta);
                                        _currentRawTranscriptText = transcriptBuilder.ToString();
                                        
                                        // If not using OpenAI for translation, update the UI with partial transcript
                                        // The translation will come once complete (from Google)
                                        if (!ConfigManager.Instance.IsOpenAITranslationEnabled())
                                        {
                                            onResult(_currentRawTranscriptText, string.Empty); // Show live Whisper transcript
                                        }
                                        // If OpenAI translation is enabled, we display the raw transcript
                                        // when it's completed, then pair it with incoming translation deltas.
                                    }
                                }
                                break;
                                
                            // Handle whisper transcription (complete)
                            case "conversation.item.input_audio_transcription.completed":
                                if (root.TryGetProperty("transcript", out var transcriptProp))
                                {
                                    var transcript = transcriptProp.GetString() ?? string.Empty;
                                    transcriptBuilder.Clear(); // Reset for next utterance
                                    _currentRawTranscriptText = transcript;
                                    
                                    if (ConfigManager.Instance.IsOpenAITranslationEnabled())
                                    {
                                        // If we already had an active transcript, this means it's a new utterance
                                        if (!string.IsNullOrEmpty(_activeTranscriptForTurn))
                                        {
                                            Log($"New transcript while another was still active. Resetting translation state.");
                                            // Reset translation for the new turn
                                            _currentTranslationText = string.Empty;
                                        }
                                        
                                        // Store the completed transcript
                                        _activeTranscriptForTurn = transcript;
                                        
                                        // Only show the transcript (with no translation) if no translation is in progress
                                        // This will appear with Japanese text only until translation completes
                                        if (!_translationInProgress)
                                        {
                                            onResult(_activeTranscriptForTurn, string.Empty);
                                        }
                                        
                                        // Mark that we're waiting for a translation
                                        _translationInProgress = true;
                                        
                                        // Log that we have a new transcript
                                        Log($"New transcript: '{_activeTranscriptForTurn}', waiting for translation");
                                    }
                                    else // Use Google Translate
                                    {
                                        string translationJson = await TranslateLineAsync(_currentRawTranscriptText);
                                        string translatedTextFromGoogle = string.Empty;
                                        
                                        if (!string.IsNullOrEmpty(translationJson))
                                        {
                                            try
                                            {
                                                using var transDoc = JsonDocument.Parse(translationJson);
                                                var transRoot = transDoc.RootElement;
                                                
                                                if (transRoot.TryGetProperty("translations", out var translations) &&
                                                    translations.ValueKind == JsonValueKind.Array &&
                                                    translations.GetArrayLength() > 0)
                                                {
                                                    // Assuming original_text from Google matches our _currentRawTranscriptText
                                                    translatedTextFromGoogle = translations[0].GetProperty("translated_text").GetString() ?? string.Empty;
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Log($"Error parsing Google translation: {ex.Message}");
                                            }
                                        }
                                        _currentTranslationText = translatedTextFromGoogle;
                                        onResult(_currentRawTranscriptText, _currentTranslationText);
                                    }
                                }
                                break;
                                
                            // Handle OpenAI translation delta
                            case "response.audio_transcript.delta": // Transcript of AI's spoken audio
                                if (ConfigManager.Instance.IsOpenAITranslationEnabled())
                                {
                                    if (root.TryGetProperty("delta", out var currentDeltaProp))
                                    {
                                        var deltaText = currentDeltaProp.GetString();
                                        if (!string.IsNullOrEmpty(deltaText))
                                        {
                                            // This is the transcript of the audio being spoken by the AI.
                                            // Log it, but do not append it to _currentTranslationText for display.
                                            // The displayed text will come from response.text.delta.
                                            Log($"Received AI spoken audio transcript delta: '{deltaText}'");
                                        }
                                    }
                                }
                                break;

                            case "response.text.delta": // Textual translation from AI
                                if (ConfigManager.Instance.IsOpenAITranslationEnabled())
                                {
                                    if (root.TryGetProperty("delta", out var textDeltaProp))
                                    {
                                        var delta = textDeltaProp.GetString();
                                        if (!string.IsNullOrEmpty(delta))
                                        {
                                            _currentTranslationText += delta;
                                            _translationInProgress = true; // A translation is actively being received
                                            Log($"Accumulating text translation delta: '{delta}'");
                                            
                                            // Display is typically handled by response.done, or when a new full transcript arrives.
                                            // However, if a transcript is already active, we can send intermediate updates.
                                            if (!string.IsNullOrEmpty(_activeTranscriptForTurn))
                                            {
                                                onResult(_activeTranscriptForTurn, _currentTranslationText);
                                            }
                                        }
                                    }
                                }
                                break;
                                
                            // Handle audio data
                            case "response.audio.delta":
                                // Handle audio data for playback (if enabled)
                                if (_audioPlaybackEnabled && bufferedWaveProvider != null && 
                                    root.TryGetProperty("delta", out var audioDeltaProp))
                                {
                                    var audioBase64 = audioDeltaProp.GetString();
                                    if (!string.IsNullOrEmpty(audioBase64))
                                    {
                                        try
                                        {
                                            // Decode the Base64 audio data
                                            byte[] audioData = Convert.FromBase64String(audioBase64);
                                            
                                            // Log audio data size to help with debugging
                                            Log($"Received audio data: {audioData.Length} bytes");
                                            
                                            // Add to buffer for playback
                                            bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log($"Error processing audio data: {ex.Message}");
                                        }
                                    }
                                }
                                else if (!_audioPlaybackEnabled)
                                {
                                    // Just log that we're ignoring the audio data
                                    Log("Received audio data but playback is disabled - ignoring");
                                }
                                break;
                                
                            // Handle audio transcript completion
                            // This message indicates that the spoken audio's transcript is complete
                            case "response.audio_transcript.completed":
                                if (ConfigManager.Instance.IsOpenAITranslationEnabled())
                                {
                                    if (root.TryGetProperty("transcript", out var audioTranscriptProp))
                                    {
                                        var completeAudioTranscript = audioTranscriptProp.GetString() ?? string.Empty;
                                        Log($"Complete AI spoken audio transcript received: '{completeAudioTranscript}'");
                                        // Do not modify _currentTranslationText here.
                                        // This event is informational about the audio playback.
                                    }
                                }
                                break;
                                
                            // Handle response completion
                            case "response.done":
                                if (ConfigManager.Instance.IsOpenAITranslationEnabled())
                                {
                                    // Attempt to extract text from the response.done message structure
                                    bool foundTextInDoneOutput = false;
                                    if (root.TryGetProperty("response", out var responseProp) &&
                                        responseProp.TryGetProperty("output", out var outputArray) &&
                                        outputArray.ValueKind == JsonValueKind.Array && outputArray.GetArrayLength() > 0)
                                    {
                                        foreach (JsonElement outputItem in outputArray.EnumerateArray())
                                        {
                                            if (outputItem.TryGetProperty("content", out var contentArray) && 
                                                contentArray.ValueKind == JsonValueKind.Array && contentArray.GetArrayLength() > 0)
                                            {
                                                // First, look for an explicit text part
                                                foreach (JsonElement contentPart in contentArray.EnumerateArray())
                                                {
                                                    if (contentPart.TryGetProperty("type", out var partTypeProp) && partTypeProp.GetString() == "text")
                                                    {
                                                        if (contentPart.TryGetProperty("text", out var textValueProp) && !string.IsNullOrEmpty(textValueProp.GetString()))
                                                        {
                                                            _currentTranslationText = textValueProp.GetString() ?? string.Empty;
                                                            foundTextInDoneOutput = true;
                                                            Log($"Extracted final text from response.done output.content[type=text]: '{_currentTranslationText}'");
                                                            break; 
                                                        }
                                                    }
                                                }

                                                if (foundTextInDoneOutput) break; 

                                                if (string.IsNullOrEmpty(_currentTranslationText))
                                                {
                                                    foreach (JsonElement contentPart in contentArray.EnumerateArray())
                                                    {
                                                        if (contentPart.TryGetProperty("type", out var partTypePropAudio) && partTypePropAudio.GetString() == "audio")
                                                        {
                                                            if (contentPart.TryGetProperty("transcript", out var transcriptValueProp) && !string.IsNullOrEmpty(transcriptValueProp.GetString()))
                                                            {
                                                                _currentTranslationText = transcriptValueProp.GetString() ?? string.Empty;
                                                                Log($"Using audio transcript from response.done output.content[type=audio] as translation: '{_currentTranslationText}'");
                                                                break; 
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            if (foundTextInDoneOutput || !string.IsNullOrEmpty(_currentTranslationText)) break; 
                                        }
                                    }

                                    if (string.IsNullOrEmpty(_currentTranslationText) && !foundTextInDoneOutput)
                                    {
                                        Log("No translation text found from response.text.delta or in response.done output structure.");
                                    }
                                    else if (!string.IsNullOrEmpty(_currentTranslationText) && !foundTextInDoneOutput)
                                    {
                                        Log($"Retaining translation from response.text.delta: '{_currentTranslationText}'");
                                    }

                                    // Log completion of translation (this log will now reflect the potentially updated _currentTranslationText)
                                    Log($"OpenAI response done for transcript: '{_activeTranscriptForTurn}' -> '{_currentTranslationText}'");
                                    
                                    // Check if translation appears incomplete (ends with comma, no final punctuation)
                                    bool seemsIncomplete = false;
                                    if (!string.IsNullOrEmpty(_currentTranslationText))
                                    {
                                        _currentTranslationText = _currentTranslationText.Trim();
                                        
                                        // Check for indicators of incomplete sentences
                                        if (_currentTranslationText.EndsWith(",") || 
                                            _currentTranslationText.EndsWith("...") ||
                                            (_currentTranslationText.Length > 3 && !_currentTranslationText.EndsWith(".") && 
                                             !_currentTranslationText.EndsWith("!") && !_currentTranslationText.EndsWith("?")))
                                        {
                                            seemsIncomplete = true;
                                            Log($"Translation appears incomplete: '{_currentTranslationText}'");
                                        }
                                    }
                                    
                                    // Mark that translation is no longer in progress
                                    _translationInProgress = false;
                                    
                                    // Only update the UI if we have both transcript and translation
                                    if (!string.IsNullOrEmpty(_activeTranscriptForTurn) && !string.IsNullOrEmpty(_currentTranslationText))
                                    {
                                        // If translation seems incomplete, add ellipsis to indicate it
                                        if (seemsIncomplete && !_currentTranslationText.EndsWith("..."))
                                        {
                                            _currentTranslationText += "...";
                                        }
                                        
                                        // Final update to UI with both transcript and complete translation
                                        onResult(_activeTranscriptForTurn, _currentTranslationText);
                                        
                                        // Store this pair in recent utterances for context
                                        _recentUtterances.Enqueue((_activeTranscriptForTurn, _currentTranslationText));
                                        
                                        // Limit the queue size
                                        while (_recentUtterances.Count > MAX_RECENT_UTTERANCES)
                                        {
                                            _recentUtterances.Dequeue();
                                        }
                                        
                                        // Log the complete translation pair
                                        Log($"Sent complete translation pair to UI - Original: '{_activeTranscriptForTurn}', Translation: '{_currentTranslationText}'");
                                        
                                        // Append to full session for debugging
                                        _fullSessionContent.AppendLine($"JP: {_activeTranscriptForTurn}");
                                        _fullSessionContent.AppendLine($"EN: {_currentTranslationText}");
                                        _fullSessionContent.AppendLine();
                                    }
                                    else
                                    {
                                        Log($"Missing transcript or translation - skipping UI update. Transcript: '{_activeTranscriptForTurn}', Translation: '{_currentTranslationText}'");
                                    }
                                    
                                    // Reset for the next turn
                                    _activeTranscriptForTurn = string.Empty;
                                    _currentTranslationText = string.Empty;
                                }
                                break;
                                
                            // Log other message types but don't process them
                            default:
                                Log($"Unhandled message type: {messageType}");
                                break;
                        }
                    }
                    catch (JsonException jex)
                    {
                        Log($"Error parsing JSON: {jex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error processing message: {ex.Message}");
                    }
                }
            }
            catch (WebSocketException wsex)
            {
                Log($"WebSocket error: {wsex.Message}");
            }
            catch (TaskCanceledException)
            {
                Log("Task was canceled");
            }
            catch (Exception ex)
            {
                Log($"Error in receive loop: {ex.Message}");
            }
            finally
            {
                // Clean up
                StopAudioCapture();
                StopAudioPlayback();
                
                try
                {
                    if (ws != null && ws.State == WebSocketState.Open)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing connection", CancellationToken.None);
                    }
                }
                catch { }
            }
        }

        private async Task FlushRemainingBuffer(CancellationToken token)
        {
            if (ws == null || ws.State != WebSocketState.Open)
                return;
                
            byte[] leftover;
            lock (bufferLock)
            {
                leftover = audioBuffer.ToArray();
                audioBuffer.Clear();
            }

            if (leftover.Length > 0)
            {
                try
                {
                    string base64 = Convert.ToBase64String(leftover);
                    var audioEvent = new { type = "input_audio_buffer.append", audio = base64 };
                    await SendJson(ws, audioEvent, token);
                    Log($"Flushed {leftover.Length} bytes of audio");
                }
                catch (Exception ex)
                {
                    Log($"Error flushing buffer: {ex.Message}");
                }
            }

            try
            {
                var commitEvent = new { type = "input_audio_buffer.commit" };
                await SendJson(ws, commitEvent, token);
                Log("Committed final audio buffer");
            }
            catch (Exception ex)
            {
                Log($"Error committing buffer: {ex.Message}");
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;

            lock (bufferLock)
            {
                audioBuffer.AddRange(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded));
            }

            Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        byte[]? chunkData = null;
                        lock (bufferLock)
                        {
                            if (audioBuffer.Count >= chunkSize)
                            {
                                chunkData = audioBuffer.GetRange(0, chunkSize).ToArray();
                                audioBuffer.RemoveRange(0, chunkSize);
                            }
                        }

                        if (chunkData == null) break;

                        if (ws != null && ws.State == WebSocketState.Open && !cts!.IsCancellationRequested)
                        {
                            string base64Audio = Convert.ToBase64String(chunkData);
                            var audioEvent = new { type = "input_audio_buffer.append", audio = base64Audio };
                            await SendJson(ws, audioEvent, cts.Token);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error sending audio chunk: {ex.Message}");
                }
            });
        }

        private async Task SendJson(ClientWebSocket ws, object obj, CancellationToken token)
        {
            try
            {
                if (ws == null || ws.State != WebSocketState.Open || token.IsCancellationRequested)
                    return;

                var json = JsonSerializer.Serialize(obj);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            catch (ObjectDisposedException)
            {
                // WebSocket was disposed while attempting to send; ignore as we're shutting down.
            }
            catch (WebSocketException)
            {
                // Socket closed or aborted. Safe to ignore during shutdown.
            }
            catch (Exception ex)
            {
                Log($"SendJson error: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "openai_audio_log.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
            Console.WriteLine("[OpenAIRealtimeAudioService] " + message);
        }

        // Translate a single line if auto-translate is enabled
        private async Task<string> TranslateLineAsync(string text)
        {
            try
            {
                if (!ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled() || string.IsNullOrEmpty(text))
                    return string.Empty;

                // When audio auto-translate is ON, explicitly use Google Translate.
                var service = TranslationServiceFactory.CreateService("Google Translate");
                Log("Using Google Translate for translation");

                // Prepare minimal JSON with one text block
                var payload = new
                {
                    text_blocks = new[] { new { id = "text_0", text = text } }
                };
                string json = JsonSerializer.Serialize(payload);
                string? response = await service.TranslateAsync(json, string.Empty);
                if (!string.IsNullOrEmpty(response))
                    return response;
            }
            catch (Exception ex)
            {
                Log($"Translation error: {ex.Message}");
            }
            return string.Empty;
        }
    }
}
