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
        
        // **** NEW: Callbacks for transcript/translation separation ****
        private Func<string, string, string>? _onTranscriptReceived; // text, translation -> returns ID
        private Action<string, string, string>? _onTranslationUpdate; // id, originalText, translation
        // Keep the original for backward compatibility or simple use cases? Maybe remove later.
        // private Action<string, string>? _onResult; 

        // Audio playback settings
        private bool _audioPlaybackEnabled = true;
        private int _audioOutputDeviceIndex = -1; // Default to system default

        public void StartRealtimeAudioService(Func<string, string, string> onTranscriptReceived, Action<string, string, string> onTranslationUpdate, bool useLoopback = false)
        {
            this.useLoopback = useLoopback;
            this._onTranscriptReceived = onTranscriptReceived;
            this._onTranslationUpdate = onTranslationUpdate;
            // this._onResult = onResult; // Keep original if needed? Let's remove for now.

            Stop();
            cts = new CancellationTokenSource();
            // Pass the callbacks down
            Task.Run(() => RunAsync(cts.Token), cts.Token);
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

        private async Task RunAsync(CancellationToken token)
        {
            // Ensure callbacks are set
            if (_onTranscriptReceived == null || _onTranslationUpdate == null)
            {
                Log("Error: Callbacks not provided to StartRealtimeAudioService.");
                return;
            }

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
                Uri serverToUse = new("wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview");



                await ws.ConnectAsync(serverToUse, token);
               
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
                systemPrompt += ". Return only the translation, with no extra text.";
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
            
            // **** Determine mode based on existing boolean flags ****
            bool isOpenAiEnabled = ConfigManager.Instance.IsOpenAITranslationEnabled();
            bool isGoogleEnabled = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
            string currentAudioMode;
            if (isOpenAiEnabled) 
            {
                currentAudioMode = "openai";
            }
            else if (isGoogleEnabled)
            {
                currentAudioMode = "google";
            }
            else
            {
                currentAudioMode = "none";
            }
            
            bool createOpenAIResponse = (currentAudioMode == "openai"); // Only create response if mode is openai
            Log($"Effective Audio Mode: {currentAudioMode} (OpenAI Enabled: {isOpenAiEnabled}, Google Enabled: {isGoogleEnabled}). OpenAI creates response: {createOpenAIResponse}");

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
                        // **** Use the conditional boolean ****
                        create_response = createOpenAIResponse, 
                        interrupt_response = false,
                        prefix_padding_ms = 500, // Increase from 300 to 500ms for better context
                        threshold = 0.6 // Increase threshold slightly for more definitive utterance detection
                        
                    //another method that didn't seem to work well:
                    
                        // type = "semantic_vad",
                        //// The semantic VAD classifier determines turn-taking based on linguistic cues
                        //eagerness = "auto", // Can be low | medium | high | auto. Auto balances latency & completeness
                        //create_response = useOpenAITranslation,
                        //interrupt_response = false
                    
                    },
                    // Get voice from config instead of hardcoding
                    voice = ConfigManager.Instance.GetOpenAIVoice(),
                    // Add instructions for translation, otherwise send empty string to satisfy API requirement
                    instructions = useOpenAITranslation && !string.IsNullOrEmpty(systemPrompt) ? systemPrompt : "" 
                }
            };
            
            await SendJson(ws, sessionUpdateMessage, token);
            Log($"Session configured (Mode: {currentAudioMode}). Silence: {silenceDurationMs}ms. Instructions: {!string.IsNullOrEmpty(systemPrompt)}. OpenAI generates response: {createOpenAIResponse}");

            // Initialize audio capture
            InitializeAudioCapture();

            // Main receive loop
            await ReceiveMessagesLoop(token);
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

        private async Task ReceiveMessagesLoop(CancellationToken token)
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
                                            // _onResult?.Invoke(_currentRawTranscriptText, string.Empty); // Show live Whisper transcript
                                            // TODO: Decide how to handle partials with new callback system if needed
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
                                    
                                    // **** Determine Mode Again based on flags ****
                                    bool _isOpenAiEnabled = ConfigManager.Instance.IsOpenAITranslationEnabled();
                                    bool _isGoogleEnabled = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
                                    string _currentAudioMode;
                                    if (_isOpenAiEnabled) _currentAudioMode = "openai";
                                    else if (_isGoogleEnabled) _currentAudioMode = "google";
                                    else _currentAudioMode = "none";

                                    // **** BRANCH BASED ON MODE ****
                                    if (_currentAudioMode == "google") // External Translation Path
                                    {
                                        Log($"New transcript received (Mode: {_currentAudioMode}): \'{_currentRawTranscriptText}\'. Requesting external translation...");
                                        
                                        // Call first callback to display transcript and get ID
                                        string lineId = _onTranscriptReceived?.Invoke(_currentRawTranscriptText, string.Empty) ?? string.Empty;

                                        if (string.IsNullOrEmpty(lineId))
                                        {
                                            Log("Warning: _onTranscriptReceived did not return a line ID.");
                                        }
                                        
                                        // Call external translation service
                                        string translationJson = await TranslateLineAsync(_currentRawTranscriptText);
                                        string translatedTextExternal = ParseTranslationResult(translationJson); // Use helper to parse
                                        
                                        if (!string.IsNullOrEmpty(translatedTextExternal))
                                        {
                                            Log($"External translation received: \'{translatedTextExternal}\' for ID: {lineId}");
                                            // Call second callback to update the line with translation
                                            _onTranslationUpdate?.Invoke(lineId, _currentRawTranscriptText, translatedTextExternal); 
                                            
                                            // Store this pair in recent utterances for context
                                            _recentUtterances.Enqueue((_currentRawTranscriptText, translatedTextExternal));
                                            while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();
                                            
                                            // Append to full session for debugging
                                            _fullSessionContent.AppendLine($"(Ext) JP: {_currentRawTranscriptText}");
                                            _fullSessionContent.AppendLine($"(Ext) EN: {translatedTextExternal}");
                                            _fullSessionContent.AppendLine();
                                        }
                                        else
                                        {
                                            Log($"External translation failed or returned empty for ID: {lineId}");
                                            // Optionally call update with an error message?
                                            // _onTranslationUpdate?.Invoke(lineId, _currentRawTranscriptText, "[Translation Failed]"); 
                                        }
                                        
                                        // Reset state (less critical now, but good practice)
                                        _activeTranscriptForTurn = string.Empty; 
                                        _currentTranslationText = string.Empty;
                                        _translationInProgress = false; 
                                    }
                                    else if (_currentAudioMode == "openai") // OpenAI Realtime Translation Path
                                    {
                                         Log($"New transcript received (Mode: {_currentAudioMode}): \'{_currentRawTranscriptText}\'. Waiting for OpenAI translation...");
                                        // If we already had an active transcript, this means it's a new utterance
                                        if (!string.IsNullOrEmpty(_activeTranscriptForTurn))
                                        {
                                            Log($"New transcript while another was still active. Resetting translation state.");
                                            // Reset translation for the new turn
                                            _currentTranslationText = string.Empty;
                                        }
                                        
                                        // Store the completed transcript
                                        _activeTranscriptForTurn = transcript;
                                        
                                        // Display the transcript immediately using the first callback
                                        // We don't get an ID back here, as the translation update comes via delta/done
                                        _onTranscriptReceived?.Invoke(_activeTranscriptForTurn, string.Empty);
                                        
                                        // Mark that we're waiting for a translation from OpenAI
                                        _translationInProgress = true;
                                    }
                                    else // Mode is "none" (Transcription only)
                                    {
                                         Log($"New transcript received (Mode: {_currentAudioMode}): \'{_currentRawTranscriptText}\'. No translation configured.");
                                         // Display the transcript immediately using the first callback, with empty translation
                                         _onTranscriptReceived?.Invoke(_currentRawTranscriptText, string.Empty);
                                         
                                         // Store this pair (with empty translation) in recent utterances for context
                                        _recentUtterances.Enqueue((_currentRawTranscriptText, string.Empty));
                                        while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();

                                        // Append to full session for debugging
                                        _fullSessionContent.AppendLine($"(None) JP: {_currentRawTranscriptText}");
                                        _fullSessionContent.AppendLine($"(None) EN: ");
                                        _fullSessionContent.AppendLine();

                                        // Reset state variables
                                        _activeTranscriptForTurn = string.Empty;
                                        _currentTranslationText = string.Empty;
                                        _translationInProgress = false;
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
                                // **** Execute only if effective mode is openai ****
                                bool delta_isOpenAiEnabled = ConfigManager.Instance.IsOpenAITranslationEnabled();
                                bool delta_isGoogleEnabled = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
                                if (delta_isOpenAiEnabled) // Check if OpenAI mode is active
                                {
                                    if (root.TryGetProperty("delta", out var textDeltaProp))
                                    {
                                        var delta = textDeltaProp.GetString();
                                        if (!string.IsNullOrEmpty(delta))
                                        {
                                            _currentTranslationText += delta;
                                            _translationInProgress = true; // A translation is actively being received
                                            Log($"Accumulating OpenAI text delta: \'{delta}\'");
                                            
                                            // Use the UPDATE callback to show intermediate results
                                            // Need an ID... how do we get it here? We don't have one.
                                            // Option 1: Look up ID based on _activeTranscriptForTurn (if stored in MainWindow) - complex
                                            // Option 2: Send update without ID, MainWindow figures it out (e.g., updates the *last* line added) - fragile
                                            // Option 3: Use the *original* _onResult callback for this path? Requires having both callback types.
                                            // Let's stick with the new callbacks for now and accept that intermediate OpenAI deltas won't update the specific line via ID.
                                            // We can call _onTranscriptReceived again, which might just add a new line or update the last one depending on MainWindow impl.
                                            _onTranscriptReceived?.Invoke(_activeTranscriptForTurn, _currentTranslationText); 

                                        }
                                    }
                                } 
                                else 
                                {
                                     Log("Received response.text.delta - Ignoring as external translation is used.");
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
                                // **** Determine mode again ****
                                bool done_isOpenAiEnabled = ConfigManager.Instance.IsOpenAITranslationEnabled();
                                bool done_isGoogleEnabled = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
                                string doneModeCheck;
                                if (done_isOpenAiEnabled) doneModeCheck = "openai";
                                else if (done_isGoogleEnabled) doneModeCheck = "google";
                                else doneModeCheck = "none";

                                if (doneModeCheck == "openai")
                                {
                                    // OpenAI mode processing (original logic)
                                    {
                                        // **** THIS IS THE ORIGINAL LOGIC from before external translation was added ****
                                        // Attempt to extract text from the response.done message structure
                                        bool foundTextInDoneOutput = false;
                                        // Need responseProp declared here
                                        JsonElement responseProp = default; 
                                        if (root.TryGetProperty("response", out responseProp))
                                        {
                                            if (responseProp.TryGetProperty("output", out var outputArray) &&
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
                                                                    Log($"Extracted final text from response.done output.content[type=text]: \'{_currentTranslationText}\'");
                                                                    break; 
                                                                }
                                                            }
                                                        }

                                                        if (foundTextInDoneOutput) break; 

                                                        // Fallback to audio transcript if text part missing and no delta received
                                                        if (string.IsNullOrEmpty(_currentTranslationText)) 
                                                        {
                                                            foreach (JsonElement contentPart in contentArray.EnumerateArray())
                                                            {
                                                                if (contentPart.TryGetProperty("type", out var partTypePropAudio) && partTypePropAudio.GetString() == "audio")
                                                                {
                                                                    if (contentPart.TryGetProperty("transcript", out var transcriptValueProp) && !string.IsNullOrEmpty(transcriptValueProp.GetString()))
                                                                    {
                                                                        _currentTranslationText = transcriptValueProp.GetString() ?? string.Empty;
                                                                        Log($"Using audio transcript from response.done output.content[type=audio] as translation: \'{_currentTranslationText}\'");
                                                                        break; 
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (foundTextInDoneOutput || !string.IsNullOrEmpty(_currentTranslationText)) break; 
                                                }
                                            }
                                        } // end if TryGetProperty response


                                        if (string.IsNullOrEmpty(_currentTranslationText) && !foundTextInDoneOutput)
                                        {
                                            Log("No translation text found from response.text.delta or in response.done output structure.");
                                        }
                                        else if (!string.IsNullOrEmpty(_currentTranslationText) && !foundTextInDoneOutput)
                                        {
                                            Log($"Retaining translation from response.text.delta: \'{_currentTranslationText}\'");
                                        }

                                        // Log completion status
                                        string responseStatus = responseProp.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "unknown" : "unknown";
                                        string cancelReason = "";
                                        if (responseStatus == "cancelled" && 
                                            responseProp.TryGetProperty("status_details", out var statusDetailsProp) && 
                                            statusDetailsProp.TryGetProperty("reason", out var reasonProp))
                                        {
                                            cancelReason = reasonProp.GetString() ?? "";
                                        }

                                        if (responseStatus == "cancelled" && cancelReason == "turn_detected")
                                        {
                                            Log($"OpenAI response CANCELLED due to turn detection for transcript: \'{_activeTranscriptForTurn}\'");
                                        }
                                        else
                                        {
                                            Log($"OpenAI response done (Status: {responseStatus}) for transcript: \'{_activeTranscriptForTurn}\' -> \'{_currentTranslationText}\'");
                                        }
                                        
                                        // Final UI Update - Use the primary callback
                                        if (!string.IsNullOrEmpty(_activeTranscriptForTurn) && !string.IsNullOrEmpty(_currentTranslationText))
                                        {
                                             // Check for incompleteness (optional, consider removing if problematic)
                                            bool seemsIncomplete = false;
                                            if (!string.IsNullOrEmpty(_currentTranslationText))
                                            {
                                                var trimmed = _currentTranslationText.Trim();
                                                if (trimmed.EndsWith(",") || trimmed.EndsWith("...") || (trimmed.Length > 3 && !trimmed.EndsWith(".") && !trimmed.EndsWith("!") && !trimmed.EndsWith("?")))
                                                {
                                                    seemsIncomplete = true;
                                                }
                                            }
                                            string finalTranslation = _currentTranslationText;
                                            if (seemsIncomplete && !finalTranslation.EndsWith("...")) finalTranslation += "...";

                                            // Final update to UI with both transcript and complete translation via the main callback
                                            // Again, we don't have an ID here. This might add a new line or update the last one.
                                            _onTranscriptReceived?.Invoke(_activeTranscriptForTurn, finalTranslation); 
                                            
                                            // Store this pair in recent utterances for context
                                            _recentUtterances.Enqueue((_activeTranscriptForTurn, finalTranslation));
                                            while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();
                                            
                                            Log($"Sent final OpenAI translation pair to UI - Original: \'{_activeTranscriptForTurn}\', Translation: \'{finalTranslation}\'");
                                            
                                            _fullSessionContent.AppendLine($"(OpenAI) JP: {_activeTranscriptForTurn}");
                                            _fullSessionContent.AppendLine($"(OpenAI) EN: {finalTranslation}");
                                            _fullSessionContent.AppendLine();
                                        }
                                        else
                                        {
                                             Log($"Missing transcript or translation - skipping final UI update. Transcript: \'{_activeTranscriptForTurn}\', Translation: \'{_currentTranslationText}\'");
                                        }

                                        // Reset for the next turn
                                        _activeTranscriptForTurn = string.Empty;
                                        _currentTranslationText = string.Empty;
                                        _translationInProgress = false;
                                    }
                                } 
                                else // External Translation Mode or None Mode
                                {
                                     Log($"Received response.done (Mode: {doneModeCheck}). Status: {root.GetProperty("response").GetProperty("status").GetString()} - Ignoring for translation.");
                                     // Reset state variables just in case
                                     _activeTranscriptForTurn = string.Empty;
                                     _currentTranslationText = string.Empty;
                                     _translationInProgress = false;
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

        // Translate a single line using configured external service
        private async Task<string> TranslateLineAsync(string text)
        {
            // This logic needs to align with the mode detection.
            // It should only be called when the mode is determined to be "google".

            try
            {
                 // Determine effective mode
                 bool _isOpenAiEnabled = ConfigManager.Instance.IsOpenAITranslationEnabled();
                 bool _isGoogleEnabled = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
                 string _currentAudioMode;
                 if (_isOpenAiEnabled) _currentAudioMode = "openai";
                 else if (_isGoogleEnabled) _currentAudioMode = "google";
                 else _currentAudioMode = "none";

                 if (string.IsNullOrEmpty(text)) return string.Empty;

                 // Only proceed if mode is google
                 if (_currentAudioMode == "google")
                 {
                     ITranslationService service = TranslationServiceFactory.CreateService("Google Translate"); 
                     string serviceName = "Google Translate (External)";
                     Log($"Using {serviceName} for external translation (Mode: {_currentAudioMode}).");

                     // Prepare minimal JSON with one text block (Specific to Google Translate endpoint?)
                     var payload = new
                     {
                         text_blocks = new[] { new { id = "text_0", text = text } }
                     };
                     string json = JsonSerializer.Serialize(payload);
                     string? response = await service.TranslateAsync(json, string.Empty); // Assuming TranslateAsync handles different services
                     
                     if (!string.IsNullOrEmpty(response))
                         return response; // Return raw JSON response
                 }
                 else
                 {
                     Log($"TranslateLineAsync called, but current mode ({_currentAudioMode}) is not 'google'. Skipping external translation.");
                     return string.Empty; 
                 }
            }
            catch (Exception ex)
            {
                Log($"Translation error in TranslateLineAsync: {ex.Message}");
            }
            return string.Empty; // Return empty on error or no response
        }
        
        // Helper function to parse translation result JSON (assuming Google format for now)
        private string ParseTranslationResult(string translationJson)
        {
            if (string.IsNullOrEmpty(translationJson)) return string.Empty;

            try
            {
                using var transDoc = JsonDocument.Parse(translationJson);
                var transRoot = transDoc.RootElement;

                if (transRoot.TryGetProperty("translations", out var translations) &&
                    translations.ValueKind == JsonValueKind.Array &&
                    translations.GetArrayLength() > 0)
                {
                    // Assuming structure like Google Translate API response
                    return translations[0].GetProperty("translated_text").GetString() ?? string.Empty;
                }
                 // TODO: Add parsing logic if using OpenAI Chat Completions API response format
                 // else if (transRoot.TryGetProperty("choices", out var choices) && ...) { ... }
            }
            catch (JsonException ex)
            {
                Log($"Error parsing translation JSON: {ex.Message} - JSON: {translationJson}");
            }
            catch (Exception ex)
            {
                 Log($"Generic error parsing translation: {ex.Message}");
            }
            return string.Empty;
        }
    }
}
