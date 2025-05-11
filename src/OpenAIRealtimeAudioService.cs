using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

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

        // Simple tracking structures for current translations
        private string _currentTranscript = string.Empty;
        private string _currentTranslation = string.Empty;
        
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
                if (waveIn != null)
                {
                    waveIn.DataAvailable -= OnDataAvailable;
                    waveIn.StopRecording();
                    waveIn.Dispose();
                    waveIn = null;
                }
                
                if (loopbackCapture != null)
                {
                    loopbackCapture.DataAvailable -= OnDataAvailable;
                    loopbackCapture.StopRecording();
                    loopbackCapture.Dispose();
                    loopbackCapture = null;
                }
            }
            catch (Exception ex)
            {
                Log($"Error stopping audio capture: {ex.Message}");
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
            _currentTranscript = string.Empty;
            _currentTranslation = string.Empty;
            
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

            if (useOpenAITranslation)
            {
                // Set up the system message for translation
                string systemPrompt = $"You are a real-time translator. For all speech input, translate it from ";
                
                if (isSourceLanguageSpecified)
                {
                    systemPrompt += $"{sourceLanguage} to {targetLanguage}";
                }
                else
                {
                    systemPrompt += $"the detected language to {targetLanguage}";
                }
                
                systemPrompt += ". Return only the translation, with no extra text.";
                
                var instructionMessage = new
                {
                    type = "conversation.item.create",
                    item = new
                    {
                        type = "message",
                        role = "system",
                        content = new[] { new { type = "input_text", text = systemPrompt } }
                    }
                };
                
                await SendJson(ws, instructionMessage, token);
                Log($"Sent translation instruction: {systemPrompt}");
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

            // Update session with our configuration
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
                        silence_duration_ms = 400,
                        create_response = useOpenAITranslation,
                        interrupt_response = false
                    }
                }
            };
            
            await SendJson(ws, sessionUpdateMessage, token);
            Log(useOpenAITranslation 
                ? "Session configured for OpenAI transcription and translation" 
                : "Session configured for transcription only");

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
            var transcriptBuilder = new StringBuilder();

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
                            // Handle whisper transcription (partial)
                            case "conversation.item.input_audio_transcription.delta":
                                if (root.TryGetProperty("delta", out var deltaProp))
                                {
                                    var delta = deltaProp.GetString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        transcriptBuilder.Append(delta);
                                        _currentTranscript = transcriptBuilder.ToString();
                                        
                                        // If not using OpenAI for translation, update the UI with partial transcript
                                        if (!ConfigManager.Instance.IsOpenAITranslationEnabled())
                                        {
                                            onResult(_currentTranscript, string.Empty);
                                        }
                                    }
                                }
                                break;
                                
                            // Handle whisper transcription (complete)
                            case "conversation.item.input_audio_transcription.completed":
                                if (root.TryGetProperty("transcript", out var transcriptProp))
                                {
                                    var transcript = transcriptProp.GetString() ?? string.Empty;
                                    transcriptBuilder.Clear();
                                    _currentTranscript = transcript;
                                    
                                    // If not using OpenAI for translation, translate with Google
                                    if (!ConfigManager.Instance.IsOpenAITranslationEnabled())
                                    {
                                        string translationJson = await TranslateLineAsync(transcript);
                                        
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
                                                    var first = translations[0];
                                                    string origText = first.GetProperty("original_text").GetString() ?? transcript;
                                                    string translatedText = first.GetProperty("translated_text").GetString() ?? string.Empty;
                                                    
                                                    // Update transcript and translation
                                                    _currentTranscript = origText;
                                                    _currentTranslation = translatedText;
                                                    
                                                    // Update UI
                                                    onResult(_currentTranscript, _currentTranslation);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Log($"Error parsing translation: {ex.Message}");
                                            }
                                        }
                                    }
                                }
                                break;
                                
                            // Handle OpenAI translation delta
                            case "response.audio_transcript.delta":
                            case "response.text.delta":
                                if (root.TryGetProperty("delta", out var textDeltaProp))
                                {
                                    var delta = textDeltaProp.GetString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        // Append to the current translation
                                        _currentTranslation += delta;
                                        
                                        // Update UI with current transcript and translation
                                        onResult(_currentTranscript, _currentTranslation);
                                    }
                                }
                                break;
                                
                            // Handle audio data
                            case "response.audio.delta":
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
                                            
                                            // Add to buffer for playback
                                            bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log($"Error processing audio data: {ex.Message}");
                                        }
                                    }
                                }
                                break;
                                
                            // Handle response completion
                            case "response.done":
                                if (ConfigManager.Instance.IsOpenAITranslationEnabled())
                                {
                                    // Log completion of translation
                                    Log($"Translation complete: '{_currentTranscript}' -> '{_currentTranslation}'");
                                    
                                    // Final update to UI
                                    onResult(_currentTranscript, _currentTranslation);
                                    
                                    // Reset translation for next utterance
                                    _currentTranslation = string.Empty;
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
