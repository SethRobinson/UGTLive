using System.Net.WebSockets;
using System.Text;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.IO;
using System.Text.Json;


namespace UGTLive
{
    public class OpenAIRealtimeAudioServiceWhisper
    {
        private ClientWebSocket? _transcribeWs;
        private ClientWebSocket? _translateWs;
        private ClientWebSocket? _audioWs;   // socket that receives captured mic/loopback audio
        private CancellationTokenSource? _cts;
        private WaveInEvent? _waveIn;
        private WasapiLoopbackCapture? _loopbackCapture;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private bool _useLoopback;
        private MMDevice? _loopbackDevice;
        // Linear-resampler state for loopback capture (src format -> 24k/16/mono).
        private int _loopbackSrcRate;
        private int _loopbackSrcChannels;
        private bool _loopbackSrcIsFloat;
        private int _loopbackSrcBits;
        private double _resamplePos;          // fractional read position in source-sample space
        private float _resamplePrev;          // last source sample (for interpolation across buffers)
        private bool _resampleHasPrev;
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

        // --- gpt-realtime-translate streaming session state ---
        // The translate session emits an unbroken stream of input/output transcript
        // deltas with no turn/segment/done events, so we segment client-side on
        // sentence-ending punctuation (or a length/inactivity fallback).
        private readonly StringBuilder _xlSource = new StringBuilder();
        private readonly StringBuilder _xlTrans = new StringBuilder();
        private string _xlLineId = string.Empty;
        private DateTime _xlLastDeltaUtc = DateTime.MinValue;
        private readonly object _xlLock = new object();
        private const int XL_MIN_SEGMENT_CHARS = 12;     // don't break on tiny fragments
        private const int XL_MAX_SEGMENT_CHARS = 220;    // hard wrap a run-on with no punctuation
        private const int XL_FLUSH_IDLE_MS = 1100;       // commit trailing segment after a speech gap
        private static readonly char[] XL_SENTENCE_ENDERS =
            { '.', '!', '?', '。', '！', '？', '…', '\n' };

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
                try { _loopbackDevice?.Dispose(); } catch { }
                _loopbackDevice = null;
                _waveIn = null;
                _loopbackCapture = null;
                _transcribeWs = null;
                _translateWs = null;
                _audioWs = null;
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
                        localLoopbackCapture.DataAvailable -= onLoopbackDataAvailable;
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

            if (translationMode == "openai")
            {
                // Single gpt-realtime-translate session: streams source transcript,
                // translated text AND translated speech simultaneously, keeping pace
                // with the speaker (no VAD pause). Ideal for fast news playback.
                await runTranslateSessionAsync(apiKey, token);
            }
            else
            {
                // Transcription-only path (no translation, or Google translation).
                await runTranscriptionSessionAsync(apiKey, token, translationMode);
            }
        }

        // =====================================================================
        // Transcription-only session (translationMode == "none" | "google")
        // =====================================================================
        private async Task runTranscriptionSessionAsync(string apiKey, CancellationToken token, string translationMode)
        {
            string transcriptionModel = ConfigManager.Instance.GetOpenAITranscriptionModel();
            try
            {
                _transcribeWs = new ClientWebSocket();
                _transcribeWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                // GA API: connect with gpt-realtime, then set session type to "transcription"
                Uri transcribeUri = new("wss://api.openai.com/v1/realtime?model=gpt-realtime");
                await _transcribeWs.ConnectAsync(transcribeUri, token);
                _audioWs = _transcribeWs;
                log($"Connected to transcription session (transcription model: {transcriptionModel})");
            }
            catch (Exception ex)
            {
                log($"Failed to connect transcription session: {ex.Message}");
                return;
            }

            await configureTranscriptionSession(token);

            initializeAudioCapture();
            await receiveTranscriptionMessages(token, translationMode);
        }

        // =====================================================================
        // gpt-realtime-translate session (translationMode == "openai")
        //
        // One WebSocket delivers everything: source-language transcript deltas,
        // target-language translated text deltas, and translated audio chunks,
        // auto-streamed as the speaker talks. No response.create / turn lifecycle.
        // =====================================================================
        private async Task runTranslateSessionAsync(string apiKey, CancellationToken token)
        {
            string translateModel = ConfigManager.Instance.GetOpenAITranslateModel();
            try
            {
                _translateWs = new ClientWebSocket();
                _translateWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                Uri translateUri = new($"wss://api.openai.com/v1/realtime/translations?model={translateModel}");
                await _translateWs.ConnectAsync(translateUri, token);
                _audioWs = _translateWs;
                log($"Connected to streaming translation session ({translateModel})");
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

            await configureTranslateSession(token);

            initializeAudioCapture();

            var receiveTask = receiveTranslateSessionMessages(token);
            var flushTask = translateIdleFlushLoop(token);
            await Task.WhenAll(receiveTask, flushTask);
        }

        private void resetState()
        {
            _currentPartialTranscript = string.Empty;
            _currentTranslationText = string.Empty;
            _activeTranscriptForTranslation = string.Empty;
            _currentTranslationLineId = string.Empty;
            _recentUtterances.Clear();
            lock (_xlLock)
            {
                _xlSource.Clear();
                _xlTrans.Clear();
                _xlLineId = string.Empty;
                _xlLastDeltaUtc = DateTime.MinValue;
            }
        }

        private async Task configureTranscriptionSession(CancellationToken token)
        {
            string sourceLanguage = ConfigManager.Instance.GetWhisperSourceLanguage();
            bool isSourceSpecified = !string.IsNullOrEmpty(sourceLanguage) &&
                                     !sourceLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            string transcriptionModel = ConfigManager.Instance.GetOpenAITranscriptionModel();
            string noiseReduction = ConfigManager.Instance.GetOpenAINoiseReduction();

            var transcriptionObj = new Dictionary<string, object?>
            {
                ["model"] = transcriptionModel
            };
            if (isSourceSpecified)
            {
                transcriptionObj["language"] = sourceLanguage;
            }

            // server_vad finalizes on a real ~silence gap rather than waiting for a
            // "complete thought" like semantic_vad did. This is the key latency fix:
            // partials arrive within a few hundred ms and finals shortly after the
            // speaker pauses, instead of after a long semantic boundary.
            int silenceMs = ConfigManager.Instance.GetOpenAiSilenceDurationMs();
            if (silenceMs <= 0) silenceMs = 250;
            var turnDetectionObj = new Dictionary<string, object?>
            {
                ["type"] = "server_vad",
                ["threshold"] = 0.5,
                ["prefix_padding_ms"] = 300,
                ["silence_duration_ms"] = silenceMs,
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
            log($"Transcription session configured. Model: {transcriptionModel}, VAD: server_vad (silence: {silenceMs}ms), Noise reduction: {noiseReduction}, Language: {(isSourceSpecified ? sourceLanguage : "auto")}");
        }

        private async Task configureTranslateSession(CancellationToken token)
        {
            if (_translateWs == null) return;

            string targetLanguage = ConfigManager.Instance.GetTargetLanguage();
            string targetCode = mapLanguageToIso639(targetLanguage);
            string noiseReduction = ConfigManager.Instance.GetOpenAINoiseReduction();
            string whisperModel = ConfigManager.Instance.GetOpenAITranscriptionModel();

            // gpt-realtime-translate: source language is auto-detected; the only
            // required knob is the target output language. Audio output uses
            // dynamic voice adaptation (no voice parameter). We attach a
            // gpt-realtime-whisper input transcription so we also get the
            // source-language transcript alongside the translation.
            //
            // NOTE: the /v1/realtime/translations session schema is strict and
            // does NOT accept an audio.input.format field (unlike the regular
            // transcription session). Including it makes the server reject the
            // whole session.update, so output.language / input.transcription
            // never apply -> wrong target language + no source transcript.
            var inputObj = new Dictionary<string, object?>
            {
                ["transcription"] = new Dictionary<string, object?> { ["model"] = whisperModel }
            };
            if (noiseReduction != "none")
            {
                inputObj["noise_reduction"] = new { type = noiseReduction };
            }

            var session = new Dictionary<string, object?>
            {
                ["audio"] = new Dictionary<string, object?>
                {
                    ["input"] = inputObj,
                    ["output"] = new Dictionary<string, object?> { ["language"] = targetCode }
                }
            };

            var sessionUpdate = new Dictionary<string, object?>
            {
                ["type"] = "session.update",
                ["session"] = session
            };

            await sendJson(_translateWs, sessionUpdate, token);
            log($"Translate session configured. Target: {targetLanguage} ({targetCode}), Input transcription: {whisperModel}, Noise reduction: {noiseReduction}, Audio playback: {_audioPlaybackEnabled}");
        }

        // gpt-realtime-translate expects an ISO 639-1 output language code.
        private static string mapLanguageToIso639(string language)
        {
            switch ((language ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "japanese": case "japan": case "ja": return "ja";
                case "english": case "en": return "en";
                case "chinese": case "zh": case "zh-cn": case "ch_sim": case "ch_tra": case "zh-tw": return "zh";
                case "korean": case "ko": return "ko";
                case "vietnamese": case "vi": return "vi";
                case "french": case "fr": return "fr";
                case "german": case "de": return "de";
                case "spanish": case "es": return "es";
                case "italian": case "it": return "it";
                case "portuguese": case "pt": return "pt";
                case "russian": case "ru": return "ru";
                case "hindi": case "hi": return "hi";
                case "indonesian": case "id": return "id";
                case "polish": case "pl": return "pl";
                case "arabic": case "ar": return "ar";
                case "turkish": case "tr": return "tr";
                case "dutch": case "nl": return "nl";
                case "thai": case "th": return "th";
                default:
                    // Already a code (or unknown) -> pass through first 2 chars.
                    var l = (language ?? "en").Trim();
                    return l.Length >= 2 ? l.Substring(0, 2).ToLowerInvariant() : "en";
            }
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
                // Capture mode comes from config; the constructor param is a fallback.
                string captureMode = ConfigManager.Instance.GetListenCaptureMode();
                bool useLoopback = captureMode == "loopback" || (_useLoopback && captureMode != "microphone");

                _audioBuffer.Clear();
                // OpenAI input is always 24kHz/16-bit/mono PCM, regardless of the
                // capture source, since loopback is converted before buffering.
                _chunkSize = (24000 * 2) / 8; // ~125ms chunks

                if (useLoopback)
                {
                    var enumerator = new MMDeviceEnumerator();
                    string wantId = ConfigManager.Instance.GetListenLoopbackDeviceId();
                    try
                    {
                        if (!string.IsNullOrEmpty(wantId))
                        {
                            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                            {
                                if (d.ID == wantId) { _loopbackDevice = d; }
                                else { d.Dispose(); }
                            }
                        }
                        _loopbackDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    finally { enumerator.Dispose(); }

                    _loopbackCapture = new WasapiLoopbackCapture(_loopbackDevice);
                    var wf = _loopbackCapture.WaveFormat;
                    _loopbackSrcRate = wf.SampleRate;
                    _loopbackSrcChannels = wf.Channels;
                    _loopbackSrcBits = wf.BitsPerSample;
                    _loopbackSrcIsFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat;
                    _resamplePos = 0;
                    _resamplePrev = 0f;
                    _resampleHasPrev = false;
                    _loopbackCapture.DataAvailable += onLoopbackDataAvailable;
                    _loopbackCapture.StartRecording();
                    log($"Started loopback capture of '{_loopbackDevice.FriendlyName}' " +
                        $"({_loopbackSrcRate}Hz {_loopbackSrcBits}-bit {_loopbackSrcChannels}ch " +
                        $"{(_loopbackSrcIsFloat ? "float" : "pcm")}) -> 24kHz/16/mono");
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
        // gpt-realtime-translate session receive loop
        //
        // Continuous stream, no turn/segment events. We get:
        //   session.input_transcript.delta  -> source-language text
        //   session.output_transcript.delta -> translated text
        //   session.output_audio.delta      -> translated speech (PCM16 24kHz)
        // and segment into UI lines client-side on sentence punctuation.
        // =====================================================================
        private async Task receiveTranslateSessionMessages(CancellationToken token)
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

                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());

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
                                // Log the server's echoed config (normal level) so
                                // it's easy to confirm output.language / input
                                // transcription were actually accepted.
                                log($"[Translate] {messageType}: {json}");
                                break;

                            case "session.closed":
                                logVerbose($"[Translate] Session event: {messageType}");
                                break;

                            case "session.input_transcript.delta":
                                if (root.TryGetProperty("delta", out var inDelta))
                                {
                                    var d = inDelta.GetString();
                                    if (!string.IsNullOrEmpty(d)) onTranslateInputDelta(d);
                                }
                                break;

                            case "session.output_transcript.delta":
                                if (root.TryGetProperty("delta", out var outDelta))
                                {
                                    var d = outDelta.GetString();
                                    if (!string.IsNullOrEmpty(d)) onTranslateOutputDelta(d);
                                }
                                break;

                            case "session.output_audio.delta":
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
                stopAudioCapture();
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

        // Source-language transcript fragment. Until the translation for this
        // segment starts (line locked), show it as a live partial caption.
        private void onTranslateInputDelta(string delta)
        {
            string? partialToShow = null;
            string? updLineId = null;
            string updSource = string.Empty;
            string updTrans = string.Empty;
            lock (_xlLock)
            {
                _xlSource.Append(delta);
                _xlLastDeltaUtc = DateTime.UtcNow;
                if (string.IsNullOrEmpty(_xlLineId))
                {
                    partialToShow = _xlSource.ToString();
                }
                else
                {
                    // Line already locked: keep the source column growing live
                    // (otherwise it freezes at the fragment present when the
                    // first translation delta arrived).
                    updLineId = _xlLineId;
                    updSource = _xlSource.ToString();
                    updTrans = _xlTrans.ToString();
                }
            }
            if (partialToShow != null)
            {
                _onPartialTranscript?.Invoke(partialToShow);
            }
            else if (updLineId != null)
            {
                _onTranslationUpdate?.Invoke(updLineId, updSource, updTrans);
            }
        }

        // Translated-text fragment. Lock the line on first fragment (converting
        // the partial source caption into a final line), then keep updating the
        // translation. Commit and start a fresh line on sentence boundaries.
        private void onTranslateOutputDelta(string delta)
        {
            string? lockSource = null;     // non-null => need to lock a new line first
            string? updateLineId = null;
            string updSource = string.Empty;
            string updTrans = string.Empty;
            bool commit = false;

            lock (_xlLock)
            {
                _xlTrans.Append(delta);
                _xlLastDeltaUtc = DateTime.UtcNow;

                // Lock the line as soon as ANY translation text arrives. If the
                // source transcript hasn't caught up yet, seed with a "…"
                // placeholder (replaced live by the real source via the
                // both-columns update path). Waiting for source here caused whole
                // sentences to be spoken but never shown when translation led.
                if (string.IsNullOrEmpty(_xlLineId))
                {
                    lockSource = _xlSource.Length > 0 ? _xlSource.ToString() : "…";
                }
            }

            // Lock the line OUTSIDE the buffer lock (callback marshals to UI thread).
            if (lockSource != null)
            {
                string id = _onTranscriptReceived?.Invoke(lockSource, string.Empty) ?? string.Empty;
                lock (_xlLock) { _xlLineId = id; }
            }

            lock (_xlLock)
            {
                updateLineId = _xlLineId;
                updSource = _xlSource.ToString();
                updTrans = _xlTrans.ToString();
                // Don't commit/clear until the line is locked, otherwise a fully
                // translated short segment could be dropped before it's shown.
                commit = !string.IsNullOrEmpty(updateLineId) && shouldCommitTranslation(updTrans);
                if (commit) commitTranslateSegmentLocked();
            }

            if (!string.IsNullOrEmpty(updateLineId))
            {
                _onTranslationUpdate?.Invoke(updateLineId, updSource, updTrans);
            }
        }

        private static bool shouldCommitTranslation(string trans)
        {
            string t = trans.TrimEnd();
            if (t.Length == 0) return false;
            if (t.Length >= XL_MAX_SEGMENT_CHARS) return true;
            if (t.Length >= XL_MIN_SEGMENT_CHARS &&
                Array.IndexOf(XL_SENTENCE_ENDERS, t[t.Length - 1]) >= 0)
            {
                return true;
            }
            return false;
        }

        // Caller must hold _xlLock. Finalizes the current segment so the next
        // delta starts a brand-new UI line.
        private void commitTranslateSegmentLocked()
        {
            string src = _xlSource.ToString().Trim();
            string trans = _xlTrans.ToString().Trim();
            if (trans.Length > 0)
            {
                _recentUtterances.Enqueue((src, trans));
                while (_recentUtterances.Count > MAX_RECENT_UTTERANCES) _recentUtterances.Dequeue();
            }
            _xlSource.Clear();
            _xlTrans.Clear();
            _xlLineId = string.Empty;
        }

        // The translate stream has no end-of-utterance event; commit a trailing
        // segment once the speaker has paused long enough.
        private async Task translateIdleFlushLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(300, token);
                    lock (_xlLock)
                    {
                        if (_xlTrans.Length > 0 &&
                            _xlLastDeltaUtc != DateTime.MinValue &&
                            (DateTime.UtcNow - _xlLastDeltaUtc).TotalMilliseconds >= XL_FLUSH_IDLE_MS)
                        {
                            commitTranslateSegmentLocked();
                        }
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                log($"[Translate] Idle flush loop error: {ex.Message}");
            }
        }

        // =====================================================================
        // Audio capture -> transcription session
        // =====================================================================
        // Microphone / input-device capture is already 24kHz/16-bit/mono PCM.
        private void onDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;
            pushAudioBytes(e.Buffer, e.BytesRecorded);
        }

        // Loopback capture is typically 48kHz/32-bit-float/stereo; convert to
        // 24kHz/16-bit/mono PCM before buffering so OpenAI gets what it expects.
        private void onLoopbackDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;
            try
            {
                byte[]? pcm = convertLoopbackToPcm24kMono(e.Buffer, e.BytesRecorded);
                if (pcm != null && pcm.Length > 0)
                {
                    pushAudioBytes(pcm, pcm.Length);
                }
            }
            catch (Exception ex)
            {
                log($"Loopback conversion error: {ex.Message}");
            }
        }

        // Read interleaved source samples -> mono float, then linearly resample
        // from the device rate to 24kHz and quantize to Int16 PCM.
        private byte[]? convertLoopbackToPcm24kMono(byte[] buffer, int count)
        {
            if (_loopbackSrcRate <= 0 || _loopbackSrcChannels <= 0) return null;

            int bytesPerSample = _loopbackSrcBits / 8;
            if (bytesPerSample <= 0) return null;
            int frameBytes = bytesPerSample * _loopbackSrcChannels;
            int frames = count / frameBytes;
            if (frames <= 0) return null;

            // Decode to mono float.
            var mono = new float[frames];
            int pos = 0;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _loopbackSrcChannels; ch++)
                {
                    float s;
                    if (_loopbackSrcIsFloat)
                    {
                        s = BitConverter.ToSingle(buffer, pos);
                    }
                    else if (_loopbackSrcBits == 16)
                    {
                        s = BitConverter.ToInt16(buffer, pos) / 32768f;
                    }
                    else if (_loopbackSrcBits == 32)
                    {
                        s = BitConverter.ToInt32(buffer, pos) / 2147483648f;
                    }
                    else // 24-bit PCM
                    {
                        int v = (buffer[pos] << 8) | (buffer[pos + 1] << 16) | (buffer[pos + 2] << 24);
                        s = (v >> 8) / 8388608f;
                    }
                    sum += s;
                    pos += bytesPerSample;
                }
                mono[f] = sum / _loopbackSrcChannels;
            }

            // Build ext[] = [prevTail, this buffer's mono samples]. Linearly
            // resample srcRate -> 24000 over ext, carrying the leftover phase
            // and the last sample to the next callback for gap-free continuity.
            float prev = _resampleHasPrev ? _resamplePrev : mono[0];
            var ext = new float[frames + 1];
            ext[0] = prev;
            Array.Copy(mono, 0, ext, 1, frames);

            double step = (double)_loopbackSrcRate / 24000.0;
            var outBytes = new List<byte>((int)(frames / step) * 2 + 4);
            double x = _resamplePos; // continuous index into ext
            int maxIdx = ext.Length - 1; // = frames
            while ((int)Math.Floor(x) < maxIdx)
            {
                int i = (int)Math.Floor(x);
                float frac = (float)(x - i);
                float sample = ext[i] + (ext[i + 1] - ext[i]) * frac;
                short s16 = (short)Math.Clamp((int)(sample * 32767f), short.MinValue, short.MaxValue);
                outBytes.Add((byte)(s16 & 0xFF));
                outBytes.Add((byte)((s16 >> 8) & 0xFF));
                x += step;
            }
            // ext[frames] becomes ext[0] next time, so shift the phase by `frames`.
            _resamplePos = x - frames;
            if (_resamplePos < 0) _resamplePos = 0;
            _resamplePrev = mono[frames - 1];
            _resampleHasPrev = true;

            return outBytes.ToArray();
        }

        private void pushAudioBytes(byte[] data, int count)
        {
            lock (_bufferLock)
            {
                _audioBuffer.AddRange(new ArraySegment<byte>(data, 0, count));
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

                        // Send audio to whichever session is active (transcription
                        // session, or the gpt-realtime-translate session).
                        var audioWs = _audioWs;
                        if (audioWs != null && audioWs.State == WebSocketState.Open && !_cts!.IsCancellationRequested)
                        {
                            string base64Audio = Convert.ToBase64String(chunkData);
                            // The gpt-realtime-translate session namespaces client
                            // events under "session." (session.input_audio_buffer.append);
                            // the regular transcription session does not.
                            string appendType = audioWs == _translateWs
                                ? "session.input_audio_buffer.append"
                                : "input_audio_buffer.append";
                            var audioEvent = new { type = appendType, audio = base64Audio };
                            await sendJson(audioWs, audioEvent, _cts.Token);
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
