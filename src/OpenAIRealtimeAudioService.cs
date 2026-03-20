using System.Net.WebSockets;
using System.Text;
using NAudio.Wave;
using System.IO;
using System.Text.Json;


namespace UGTLive
{
    public class OpenAIRealtimeAudioServiceWhisper
    {
        private ClientWebSocket? _transcribeWs;
        private ClientWebSocket? _translateWs;
        private CancellationTokenSource? _cts;
        private WaveInEvent? _waveIn;
        private WasapiLoopbackCapture? _loopbackCapture;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private bool _useLoopback;
        private readonly List<byte> _audioBuffer = new List<byte>();
        private readonly object _bufferLock = new object();
        private int _chunkSize;

        private string _currentPartialTranscript = string.Empty;
        private string _currentTranslationText = string.Empty;
        private string _activeTranscriptForTranslation = string.Empty;

        private Queue<(string Transcript, string Translation)> _recentUtterances = new Queue<(string, string)>();
        private const int MAX_RECENT_UTTERANCES = 5;

        // Callbacks
        private Func<string, string, string>? _onTranscriptReceived;
        private Action<string, string, string>? _onTranslationUpdate;
        private Action<string>? _onPartialTranscript;

        // Audio playback settings
        private bool _audioPlaybackEnabled = false;
        private int _audioOutputDeviceIndex = -1;

        // Track current line ID for OpenAI translation updates
        private string _currentTranslationLineId = string.Empty;

        public void StartRealtimeAudioService(
            Func<string, string, string> onTranscriptReceived,
            Action<string, string, string> onTranslationUpdate,
            Action<string>? onPartialTranscript,
            bool useLoopback = false)
        {
            _useLoopback = useLoopback;
            _onTranscriptReceived = onTranscriptReceived;
            _onTranslationUpdate = onTranslationUpdate;
            _onPartialTranscript = onPartialTranscript;

            Stop();
            _cts = new CancellationTokenSource();
            Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        }

        // Backward-compatible overload
        public void StartRealtimeAudioService(
            Func<string, string, string> onTranscriptReceived,
            Action<string, string, string> onTranslationUpdate,
            bool useLoopback = false)
        {
            StartRealtimeAudioService(onTranscriptReceived, onTranslationUpdate, null, useLoopback);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                stopAudioCapture();
                stopAudioPlayback();
                closeWebSocket(_transcribeWs);
                closeWebSocket(_translateWs);
            }
            catch (Exception ex)
            {
                log($"Error during stop: {ex.Message}");
            }
            finally
            {
                _waveIn = null;
                _loopbackCapture = null;
                _transcribeWs = null;
                _translateWs = null;
            }
        }

        private void closeWebSocket(ClientWebSocket? ws)
        {
            try
            {
                ws?.Dispose();
            }
            catch (Exception ex)
            {
                log($"Error disposing WebSocket: {ex.Message}");
            }
        }

        private void stopAudioCapture()
        {
            try
            {
                var localWaveIn = _waveIn;
                var localLoopbackCapture = _loopbackCapture;

                _waveIn = null;
                _loopbackCapture = null;

                if (localWaveIn != null)
                {
                    try
                    {
                        localWaveIn.DataAvailable -= onDataAvailable;
                        try { localWaveIn.StopRecording(); }
                        catch (InvalidOperationException) { logVerbose("WaveIn was already stopped"); }

                        Task.Run(() =>
                        {
                            try
                            {
                                Thread.Sleep(100);
                                localWaveIn.Dispose();
                            }
                            catch (Exception ex)
                            {
                                log($"Error during delayed WaveIn disposal: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        log($"Error stopping WaveIn: {ex.Message}");
                    }
                }

                if (localLoopbackCapture != null)
                {
                    try
                    {
                        localLoopbackCapture.DataAvailable -= onDataAvailable;
                        try { localLoopbackCapture.StopRecording(); }
                        catch (InvalidOperationException) { logVerbose("Loopback capture was already stopped"); }

                        Task.Run(() =>
                        {
                            try
                            {
                                Thread.Sleep(100);
                                localLoopbackCapture.Dispose();
                            }
                            catch (Exception ex)
                            {
                                log($"Error during delayed loopback capture disposal: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        log($"Error stopping loopback capture: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Error in stopAudioCapture: {ex.Message}");
            }
        }

        private void stopAudioPlayback()
        {
            try
            {
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }
                _bufferedWaveProvider = null;
            }
            catch (Exception ex)
            {
                log($"Error stopping audio playback: {ex.Message}");
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            if (_onTranscriptReceived == null || _onTranslationUpdate == null)
            {
                log("Error: Callbacks not provided to StartRealtimeAudioService.");
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

            resetState();
            loadAudioPlaybackSettings();

            // Determine translation mode
            bool isOpenAiTranslation = ConfigManager.Instance.IsOpenAITranslationEnabled();
            bool isGoogleTranslation = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
            string translationMode = isOpenAiTranslation ? "openai" : isGoogleTranslation ? "google" : "none";
            log($"Translation mode: {translationMode}");

            // --- Session 1: Transcription-only ---
            string transcriptionModel = ConfigManager.Instance.GetOpenAITranscriptionModel();
            try
            {
                _transcribeWs = new ClientWebSocket();
                _transcribeWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                // GA API: connect with gpt-realtime, then set session type to "transcription"
                Uri transcribeUri = new("wss://api.openai.com/v1/realtime?model=gpt-realtime");
                await _transcribeWs.ConnectAsync(transcribeUri, token);
                log($"Connected to transcription session (transcription model: {transcriptionModel})");
            }
            catch (Exception ex)
            {
                log($"Failed to connect transcription session: {ex.Message}");
                return;
            }

            await configureTranscriptionSession(token);

            // --- Session 2: Translation (only if OpenAI mode) ---
            if (translationMode == "openai")
            {
                try
                {
                    _translateWs = new ClientWebSocket();
                    _translateWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                    Uri translateUri = new("wss://api.openai.com/v1/realtime?model=gpt-realtime-1.5");
                    await _translateWs.ConnectAsync(translateUri, token);
                    log("Connected to translation session (gpt-realtime-1.5)");
                }
                catch (Exception ex)
                {
                    log($"Failed to connect translation session: {ex.Message}");
                    return;
                }

                if (_audioPlaybackEnabled)
                {
                    initializeAudioPlayback();
                }

                await configureTranslationSession(token);
            }

            // Start audio capture
            initializeAudioCapture();

            // Run receive loops concurrently
            var transcribeTask = receiveTranscriptionMessages(token, translationMode);

            if (translationMode == "openai" && _translateWs != null)
            {
                var translateTask = receiveTranslationMessages(token);
                await Task.WhenAll(transcribeTask, translateTask);
            }
            else
            {
                await transcribeTask;
            }
        }

        private void resetState()
        {
            _currentPartialTranscript = string.Empty;
            _currentTranslationText = string.Empty;
            _activeTranscriptForTranslation = string.Empty;
            _currentTranslationLineId = string.Empty;
            _recentUtterances.Clear();
        }

        private async Task configureTranscriptionSession(CancellationToken token)
        {
            string sourceLanguage = ConfigManager.Instance.GetWhisperSourceLanguage();
            bool isSourceSpecified = !string.IsNullOrEmpty(sourceLanguage) &&
                                     !sourceLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            int silenceDurationMs = ConfigManager.Instance.GetOpenAiSilenceDurationMs();
            string transcriptionModel = ConfigManager.Instance.GetOpenAITranscriptionModel();
            string noiseReduction = ConfigManager.Instance.GetOpenAINoiseReduction();

            // Use session.update on a realtime session with transcription enabled
            // and create_response: false so it only transcribes, never generates responses
            var transcriptionObj = new Dictionary<string, object?>
            {
                ["model"] = transcriptionModel
            };
            if (isSourceSpecified)
            {
                transcriptionObj["language"] = sourceLanguage;
            }

            var turnDetectionObj = new Dictionary<string, object?>
            {
                ["type"] = "server_vad",
                ["threshold"] = 0.5,
                ["prefix_padding_ms"] = 300,
                ["silence_duration_ms"] = silenceDurationMs,
                ["create_response"] = false,
                ["interrupt_response"] = false
            };

            var inputObj = new Dictionary<string, object?>
            {
                ["format"] = new { type = "audio/pcm", rate = 24000 },
                ["transcription"] = transcriptionObj,
                ["turn_detection"] = turnDetectionObj
            };

            if (noiseReduction != "none")
            {
                inputObj["noise_reduction"] = new { type = noiseReduction };
            }

            var session = new Dictionary<string, object?>
            {
                ["type"] = "realtime",
                ["audio"] = new { input = inputObj }
            };

            var sessionUpdate = new Dictionary<string, object?>
            {
                ["type"] = "session.update",
                ["session"] = session
            };

            await sendJson(_transcribeWs!, sessionUpdate, token);
            log($"Transcription session configured. Model: {transcriptionModel}, Silence: {silenceDurationMs}ms, Noise reduction: {noiseReduction}, Language: {(isSourceSpecified ? sourceLanguage : "auto")}");
        }

        private async Task configureTranslationSession(CancellationToken token)
        {
            if (_translateWs == null) return;

            string targetLanguage = ConfigManager.Instance.GetTargetLanguage();
            string sourceLanguage = ConfigManager.Instance.GetWhisperSourceLanguage();
            bool isSourceSpecified = !string.IsNullOrEmpty(sourceLanguage) &&
                                     !sourceLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase);

            string langDirective = isSourceSpecified
                ? $"Translate from {sourceLanguage} to {targetLanguage}."
                : $"Translate from the detected language to {targetLanguage}.";

            string userPrompt = _audioPlaybackEnabled
                ? ConfigManager.Instance.GetListenSpokenPrompt()
                : ConfigManager.Instance.GetListenTextPrompt();

            string instructions = userPrompt + " " + langDirective;

            // API only supports ["audio"] or ["text"], not both simultaneously.
            // When audio is enabled, we get the text transcript via output_audio_transcript events.
            string[] outputModalities = _audioPlaybackEnabled ? new[] { "audio" } : new[] { "text" };
            string voice = ConfigManager.Instance.GetOpenAIVoice();

            // GA format: session.update with type: "realtime" and nested audio config
            // Disable turn_detection via audio.input since we only send text via conversation.item.create
            var session = new Dictionary<string, object?>
            {
                ["type"] = "realtime",
                ["instructions"] = instructions,
                ["output_modalities"] = outputModalities
            };

            if (_audioPlaybackEnabled)
            {
                session["audio"] = new
                {
                    input = new { turn_detection = (object?)null },
                    output = new { voice = voice }
                };
            }
            else
            {
                session["audio"] = new
                {
                    input = new { turn_detection = (object?)null }
                };
            }

            var sessionUpdate = new Dictionary<string, object?>
            {
                ["type"] = "session.update",
                ["session"] = session
            };

            await sendJson(_translateWs, sessionUpdate, token);
            log($"Translation session configured (GA format). Target: {targetLanguage}, Output: [{string.Join(", ", outputModalities)}], Voice: {voice}");
        }

        private void loadAudioPlaybackSettings()
        {
            try
            {
                _audioPlaybackEnabled = ConfigManager.Instance.IsOpenAIAudioPlaybackEnabled();
                _audioOutputDeviceIndex = ConfigManager.Instance.GetAudioOutputDeviceIndex();
                log($"Audio playback enabled: {_audioPlaybackEnabled}, Output device index: {_audioOutputDeviceIndex}");
            }
            catch (Exception ex)
            {
                log($"Error loading audio playback settings: {ex.Message}");
                _audioPlaybackEnabled = false;
                _audioOutputDeviceIndex = -1;
            }
        }

        private void initializeAudioPlayback()
        {
            try
            {
                var waveFormat = new WaveFormat(24000, 16, 1);
                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat);
                _bufferedWaveProvider.DiscardOnBufferOverflow = true;

                _waveOut = new WaveOutEvent();
                if (_audioOutputDeviceIndex >= 0 && _audioOutputDeviceIndex < WaveOut.DeviceCount)
                {
                    _waveOut.DeviceNumber = _audioOutputDeviceIndex;
                    logVerbose($"Using audio output device: {WaveOut.GetCapabilities(_audioOutputDeviceIndex).ProductName}");
                }
                else
                {
                    logVerbose("Using default audio output device");
                }

                _waveOut.Init(_bufferedWaveProvider);
                _waveOut.Play();
                logVerbose("Audio playback initialized successfully");
            }
            catch (Exception ex)
            {
                log($"Error initializing audio playback: {ex.Message}");
                stopAudioPlayback();
                _audioPlaybackEnabled = false;
            }
        }

        private void initializeAudioCapture()
        {
            try
            {
                if (_useLoopback)
                {
                    _loopbackCapture = new WasapiLoopbackCapture();
                    _loopbackCapture.DataAvailable += onDataAvailable;
                    _chunkSize = _loopbackCapture.WaveFormat.AverageBytesPerSecond / 8;
                    _loopbackCapture.StartRecording();
                    logVerbose("Started loopback audio capture");
                }
                else
                {
                    _waveIn = new WaveInEvent();
                    int deviceIndex = ConfigManager.Instance.GetAudioInputDeviceIndex();

                    if (deviceIndex >= 0 && deviceIndex < WaveInEvent.DeviceCount)
                    {
                        _waveIn.DeviceNumber = deviceIndex;
                        logVerbose($"Using audio input device: {WaveInEvent.GetCapabilities(deviceIndex).ProductName} (Index: {deviceIndex})");
                    }
                    else
                    {
                        _waveIn.DeviceNumber = 0;
                        if (WaveInEvent.DeviceCount > 0)
                        {
                            log($"Invalid device index {deviceIndex}. Using device 0: {WaveInEvent.GetCapabilities(0).ProductName}");
                        }
                        else
                        {
                            log("No audio input devices found. Using device 0 (may fail)");
                        }
                    }

                    _waveIn.WaveFormat = new WaveFormat(24000, 16, 1);
                    _chunkSize = _waveIn.WaveFormat.AverageBytesPerSecond / 8;
                    _audioBuffer.Clear();
                    _waveIn.DataAvailable += onDataAvailable;
                    _waveIn.StartRecording();
                    logVerbose("Started microphone audio capture");
                }
            }
            catch (Exception ex)
            {
                log($"Error initializing audio capture: {ex.Message}");
                stopAudioCapture();
            }
        }

        // =====================================================================
        // Transcription session receive loop
        // =====================================================================
        private async Task receiveTranscriptionMessages(CancellationToken token, string translationMode)
        {
            var buffer = new byte[16384];
            var messageBuffer = new MemoryStream();
            var transcriptBuilder = new StringBuilder();

            try
            {
                while (!token.IsCancellationRequested && _transcribeWs != null && _transcribeWs.State == WebSocketState.Open)
                {
                    messageBuffer.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _transcribeWs.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _transcribeWs.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
                            break;
                        }
                        messageBuffer.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (messageBuffer.Length == 0 || result.MessageType == WebSocketMessageType.Close)
                        continue;

                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    logVerbose($"[Transcribe] {json}");

                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("type", out var typeProp))
                            continue;

                        string? messageType = typeProp.GetString();

                        if (messageType == "error")
                        {
                            string errorMsg = root.TryGetProperty("error", out var errorProp) &&
                                              errorProp.TryGetProperty("message", out var msgProp)
                                ? msgProp.GetString() ?? "Unknown"
                                : json;
                            log($"[Transcribe] API Error: {errorMsg}");
                            continue;
                        }

                        switch (messageType)
                        {
                            case "session.created":
                            case "session.updated":
                            case "transcription_session.created":
                            case "transcription_session.updated":
                                logVerbose($"[Transcribe] Session event: {messageType}");
                                break;

                            case "conversation.item.input_audio_transcription.delta":
                                if (root.TryGetProperty("delta", out var deltaProp))
                                {
                                    var delta = deltaProp.GetString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        transcriptBuilder.Append(delta);
                                        _currentPartialTranscript = transcriptBuilder.ToString();
                                        _onPartialTranscript?.Invoke(_currentPartialTranscript);
                                    }
                                }
                                break;

                            case "conversation.item.input_audio_transcription.completed":
                                if (root.TryGetProperty("transcript", out var transcriptProp))
                                {
                                    var transcript = transcriptProp.GetString() ?? string.Empty;
                                    transcriptBuilder.Clear();
                                    _currentPartialTranscript = string.Empty;

                                    if (string.IsNullOrWhiteSpace(transcript))
                                    {
                                        logVerbose("[Transcribe] Empty transcript received, skipping.");
                                        break;
                                    }

                                    logVerbose($"[Transcribe] Completed: '{transcript}'");

                                    await handleCompletedTranscript(transcript, translationMode);
                                }
                                break;

                            case "input_audio_buffer.speech_started":
                                logVerbose("[Transcribe] Speech started");
                                break;

                            case "input_audio_buffer.speech_stopped":
                                logVerbose("[Transcribe] Speech stopped");
                                break;

                            case "input_audio_buffer.committed":
                                logVerbose("[Transcribe] Audio buffer committed");
                                break;

                            default:
                                break;
                        }
                    }
                    catch (JsonException jex)
                    {
                        log($"[Transcribe] JSON parse error: {jex.Message}");
                    }
                }
            }
            catch (WebSocketException wsex)
            {
                log($"[Transcribe] WebSocket error: {wsex.Message}");
            }
            catch (TaskCanceledException)
            {
                logVerbose("[Transcribe] Task cancelled");
            }
            catch (Exception ex)
            {
                log($"[Transcribe] Error: {ex.Message}");
            }
            finally
            {
                stopAudioCapture();
                try
                {
                    if (_transcribeWs != null && _transcribeWs.State == WebSocketState.Open)
                    {
                        await _transcribeWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                    }
                }
                catch { }
            }
        }

        private async Task handleCompletedTranscript(string transcript, string translationMode)
        {
            switch (translationMode)
            {
                case "openai":
                    await handleOpenAITranslation(transcript);
                    break;

                case "google":
                    await handleGoogleTranslation(transcript);
                    break;

                default:
                    _onTranscriptReceived?.Invoke(transcript, string.Empty);
                    _recentUtterances.Enqueue((transcript, string.Empty));
                    while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();
                    break;
            }
        }

        private async Task handleOpenAITranslation(string transcript)
        {
            if (_translateWs == null || _translateWs.State != WebSocketState.Open || _cts == null)
            {
                log("[Translate] Translation WebSocket not available, showing transcript only");
                _onTranscriptReceived?.Invoke(transcript, string.Empty);
                return;
            }

            _activeTranscriptForTranslation = transcript;
            _currentTranslationText = string.Empty;

            // Show the transcript immediately, get the line ID for later updates
            string lineId = _onTranscriptReceived?.Invoke(transcript, string.Empty) ?? string.Empty;
            _currentTranslationLineId = lineId;

            string targetLanguage = ConfigManager.Instance.GetTargetLanguage();

            // When audio mode is active, prefix with explicit translation directive
            string messageText = _audioPlaybackEnabled
                ? $"[Translate to {targetLanguage}]: {transcript}"
                : transcript;

            var createItem = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "message",
                    role = "user",
                    content = new[] { new { type = "input_text", text = messageText } }
                }
            };
            await sendJson(_translateWs, createItem, _cts.Token);

            // Request a response, with per-request instructions reinforcing translation-only behavior
            if (_audioPlaybackEnabled)
            {
                var responseCreate = new
                {
                    type = "response.create",
                    response = new
                    {
                        instructions = $"Speak only the {targetLanguage} translation. No other words."
                    }
                };
                await sendJson(_translateWs, responseCreate, _cts.Token);
            }
            else
            {
                var responseCreate = new { type = "response.create" };
                await sendJson(_translateWs, responseCreate, _cts.Token);
            }

            logVerbose($"[Translate] Sent transcript for translation: '{transcript}' (lineId: {lineId})");
        }

        private async Task handleGoogleTranslation(string transcript)
        {
            string lineId = _onTranscriptReceived?.Invoke(transcript, string.Empty) ?? string.Empty;

            if (string.IsNullOrEmpty(lineId))
            {
                logVerbose("[Google] Warning: _onTranscriptReceived did not return a line ID.");
            }

            string translationJson = await translateLineAsync(transcript);
            string translatedText = parseTranslationResult(translationJson);

            if (!string.IsNullOrEmpty(translatedText))
            {
                logVerbose($"[Google] Translation received: '{translatedText}' for ID: {lineId}");
                _onTranslationUpdate?.Invoke(lineId, transcript, translatedText);
                _recentUtterances.Enqueue((transcript, translatedText));
                while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();
            }
            else
            {
                logVerbose($"[Google] Translation failed or returned empty for ID: {lineId}");
            }
        }

        // =====================================================================
        // Translation session receive loop (OpenAI mode only)
        // =====================================================================
        private async Task receiveTranslationMessages(CancellationToken token)
        {
            var buffer = new byte[16384];
            var messageBuffer = new MemoryStream();

            try
            {
                while (!token.IsCancellationRequested && _translateWs != null && _translateWs.State == WebSocketState.Open)
                {
                    messageBuffer.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _translateWs.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _translateWs.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
                            break;
                        }
                        messageBuffer.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (messageBuffer.Length == 0 || result.MessageType == WebSocketMessageType.Close)
                        continue;

                    var jsonBytes = messageBuffer.ToArray();
                    var json = Encoding.UTF8.GetString(jsonBytes);

                    bool isAudioMessage = json.Contains("response.output_audio.delta");
                    if (!isAudioMessage)
                    {
                        logVerbose($"[Translate] {json}");
                    }
                    else
                    {
                        logVerbose($"[Translate] Audio message ({jsonBytes.Length} bytes)");
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("type", out var typeProp))
                            continue;

                        string? messageType = typeProp.GetString();

                        if (messageType == "error")
                        {
                            string errorMsg = root.TryGetProperty("error", out var errorProp) &&
                                              errorProp.TryGetProperty("message", out var msgProp)
                                ? msgProp.GetString() ?? "Unknown"
                                : json;
                            log($"[Translate] API Error: {errorMsg}");
                            continue;
                        }

                        switch (messageType)
                        {
                            case "session.created":
                            case "session.updated":
                                logVerbose($"[Translate] Session event: {messageType}");
                                break;

                            // Streaming text translation deltas
                            case "response.output_text.delta":
                                if (root.TryGetProperty("delta", out var textDeltaProp))
                                {
                                    var delta = textDeltaProp.GetString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        _currentTranslationText += delta;

                                        if (!string.IsNullOrEmpty(_currentTranslationLineId))
                                        {
                                            _onTranslationUpdate?.Invoke(
                                                _currentTranslationLineId,
                                                _activeTranscriptForTranslation,
                                                _currentTranslationText);
                                        }
                                    }
                                }
                                break;

                            case "response.output_text.done":
                                if (root.TryGetProperty("text", out var textDoneProp))
                                {
                                    var finalText = textDoneProp.GetString() ?? string.Empty;
                                    if (!string.IsNullOrEmpty(finalText))
                                    {
                                        _currentTranslationText = finalText;
                                    }
                                }
                                logVerbose($"[Translate] Text complete: '{_currentTranslationText}'");
                                break;

                            // Audio transcript of the spoken translation (used when output_modalities is ["audio"])
                            case "response.output_audio_transcript.delta":
                                if (root.TryGetProperty("delta", out var audioTransDeltaProp))
                                {
                                    var delta = audioTransDeltaProp.GetString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        _currentTranslationText += delta;

                                        if (!string.IsNullOrEmpty(_currentTranslationLineId))
                                        {
                                            _onTranslationUpdate?.Invoke(
                                                _currentTranslationLineId,
                                                _activeTranscriptForTranslation,
                                                _currentTranslationText);
                                        }
                                    }
                                }
                                break;

                            case "response.output_audio_transcript.done":
                                if (root.TryGetProperty("transcript", out var audioTransDoneProp))
                                {
                                    var finalText = audioTransDoneProp.GetString() ?? string.Empty;
                                    if (!string.IsNullOrEmpty(finalText))
                                    {
                                        _currentTranslationText = finalText;
                                    }
                                }
                                logVerbose($"[Translate] Audio transcript complete: '{_currentTranslationText}'");
                                break;

                            // Audio playback data
                            case "response.output_audio.delta":
                                if (_audioPlaybackEnabled && _bufferedWaveProvider != null &&
                                    root.TryGetProperty("delta", out var audioDeltaProp))
                                {
                                    var audioBase64 = audioDeltaProp.GetString();
                                    if (!string.IsNullOrEmpty(audioBase64))
                                    {
                                        try
                                        {
                                            byte[] audioData = Convert.FromBase64String(audioBase64);
                                            _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
                                        }
                                        catch (Exception ex)
                                        {
                                            log($"[Translate] Error processing audio data: {ex.Message}");
                                        }
                                    }
                                }
                                break;

                            case "response.done":
                                handleTranslationResponseDone(root);
                                break;

                            default:
                                break;
                        }
                    }
                    catch (JsonException jex)
                    {
                        log($"[Translate] JSON parse error: {jex.Message}");
                    }
                }
            }
            catch (WebSocketException wsex)
            {
                log($"[Translate] WebSocket error: {wsex.Message}");
            }
            catch (TaskCanceledException)
            {
                logVerbose("[Translate] Task cancelled");
            }
            catch (Exception ex)
            {
                log($"[Translate] Error: {ex.Message}");
            }
            finally
            {
                stopAudioPlayback();
                try
                {
                    if (_translateWs != null && _translateWs.State == WebSocketState.Open)
                    {
                        await _translateWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                    }
                }
                catch { }
            }
        }

        private void handleTranslationResponseDone(JsonElement root)
        {
            string responseStatus = "unknown";
            if (root.TryGetProperty("response", out var responseProp))
            {
                if (responseProp.TryGetProperty("status", out var statusProp))
                {
                    responseStatus = statusProp.GetString() ?? "unknown";
                }

                // Try to extract final text from the response output structure
                if (string.IsNullOrEmpty(_currentTranslationText))
                {
                    if (responseProp.TryGetProperty("output", out var outputArray) &&
                        outputArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement outputItem in outputArray.EnumerateArray())
                        {
                            if (outputItem.TryGetProperty("content", out var contentArray) &&
                                contentArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement contentPart in contentArray.EnumerateArray())
                                {
                                    if (contentPart.TryGetProperty("type", out var partType))
                                    {
                                        string typeStr = partType.GetString() ?? "";
                                        if (typeStr == "text" && contentPart.TryGetProperty("text", out var textVal))
                                        {
                                            _currentTranslationText = textVal.GetString() ?? string.Empty;
                                            break;
                                        }
                                        if (typeStr == "audio" && contentPart.TryGetProperty("transcript", out var transVal))
                                        {
                                            _currentTranslationText = transVal.GetString() ?? string.Empty;
                                            break;
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(_currentTranslationText)) break;
                            }
                            if (!string.IsNullOrEmpty(_currentTranslationText)) break;
                        }
                    }
                }
            }

            logVerbose($"[Translate] Response done (status: {responseStatus}). Transcript: '{_activeTranscriptForTranslation}' -> Translation: '{_currentTranslationText}'");

            // Final update to UI
            if (!string.IsNullOrEmpty(_activeTranscriptForTranslation) && !string.IsNullOrEmpty(_currentTranslationText))
            {
                if (!string.IsNullOrEmpty(_currentTranslationLineId))
                {
                    _onTranslationUpdate?.Invoke(
                        _currentTranslationLineId,
                        _activeTranscriptForTranslation,
                        _currentTranslationText);
                }
                else
                {
                    _onTranscriptReceived?.Invoke(_activeTranscriptForTranslation, _currentTranslationText);
                }

                _recentUtterances.Enqueue((_activeTranscriptForTranslation, _currentTranslationText));
                while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();
            }

            // Reset for next turn
            _activeTranscriptForTranslation = string.Empty;
            _currentTranslationText = string.Empty;
            _currentTranslationLineId = string.Empty;
        }

        // =====================================================================
        // Audio capture -> transcription session
        // =====================================================================
        private void onDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;

            lock (_bufferLock)
            {
                _audioBuffer.AddRange(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded));
            }

            Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        byte[]? chunkData = null;
                        lock (_bufferLock)
                        {
                            if (_audioBuffer.Count >= _chunkSize)
                            {
                                chunkData = _audioBuffer.GetRange(0, _chunkSize).ToArray();
                                _audioBuffer.RemoveRange(0, _chunkSize);
                            }
                        }

                        if (chunkData == null) break;

                        // Send audio only to the transcription session
                        if (_transcribeWs != null && _transcribeWs.State == WebSocketState.Open && !_cts!.IsCancellationRequested)
                        {
                            string base64Audio = Convert.ToBase64String(chunkData);
                            var audioEvent = new { type = "input_audio_buffer.append", audio = base64Audio };
                            await sendJson(_transcribeWs, audioEvent, _cts.Token);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log($"Error sending audio chunk: {ex.Message}");
                }
            });
        }

        // =====================================================================
        // Utilities
        // =====================================================================
        private async Task sendJson(ClientWebSocket ws, object obj, CancellationToken token)
        {
            try
            {
                if (ws == null || ws.State != WebSocketState.Open || token.IsCancellationRequested)
                    return;

                var json = JsonSerializer.Serialize(obj);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            catch (ObjectDisposedException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                log($"SendJson error: {ex.Message}");
            }
        }

        private void log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "openai_audio_log.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
            Console.WriteLine("[OpenAIRealtimeAudio] " + message);
        }

        private void logVerbose(string message)
        {
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                log(message);
            }
        }

        private async Task<string> translateLineAsync(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return string.Empty;

                bool isGoogleEnabled = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
                if (!isGoogleEnabled) return string.Empty;

                ITranslationService service = TranslationServiceFactory.CreateService("Google Translate");
                logVerbose($"[Google] Translating: '{text}'");

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
                log($"[Google] Translation error: {ex.Message}");
            }
            return string.Empty;
        }

        private string parseTranslationResult(string translationJson)
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
                    return translations[0].GetProperty("translated_text").GetString() ?? string.Empty;
                }
            }
            catch (JsonException ex)
            {
                log($"[Google] Error parsing translation JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                log($"[Google] Generic error parsing translation: {ex.Message}");
            }
            return string.Empty;
        }
    }
}
