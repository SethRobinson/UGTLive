using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace UGTLive
{
    public class AudioPreloadService
    {
        private static AudioPreloadService? _instance;
        private readonly Dictionary<string, string> _audioCache; // text hash -> file path
        private readonly Dictionary<string, Task<string?>> _inProgressTasks; // textObject ID -> task
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly SemaphoreSlim _concurrencyLimiter;
        private const int MAX_CONCURRENT_REQUESTS = 3;
        
        public static AudioPreloadService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AudioPreloadService();
                }
                return _instance;
            }
        }
        
        private AudioPreloadService()
        {
            _audioCache = new Dictionary<string, string>();
            _inProgressTasks = new Dictionary<string, Task<string?>>();
            _concurrencyLimiter = new SemaphoreSlim(MAX_CONCURRENT_REQUESTS, MAX_CONCURRENT_REQUESTS);
        }
        
        public async Task PreloadSourceAudioAsync(List<TextObject> textObjects)
        {
            if (textObjects == null || textObjects.Count == 0)
            {
                Console.WriteLine("AudioPreloadService: No text objects to preload");
                return;
            }
            
            Console.WriteLine($"AudioPreloadService: Starting source audio preload for {textObjects.Count} text objects");
            
            // Cancel any existing preloads
            CancelAllPreloads();
            
            // Create new cancellation token
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;
            
            // Get settings
            string service = ConfigManager.Instance.GetTtsSourceService();
            string voice = ConfigManager.Instance.GetTtsSourceVoice();
            
            // Check for custom voice ID for ElevenLabs
            if (service == "ElevenLabs" && ConfigManager.Instance.GetTtsSourceUseCustomVoiceId())
            {
                string customVoice = ConfigManager.Instance.GetTtsSourceCustomVoiceId();
                if (!string.IsNullOrWhiteSpace(customVoice))
                {
                    voice = customVoice;
                    Console.WriteLine($"AudioPreloadService: Using custom ElevenLabs voice ID: {voice}");
                }
            }
            
            Console.WriteLine($"AudioPreloadService: Using service={service}, voice={voice}");
            
            // Preload audio for each text object
            var tasks = new List<Task>();
            foreach (var textObj in textObjects)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                
                if (string.IsNullOrWhiteSpace(textObj.Text))
                {
                    continue;
                }
                
                // Check if already ready
                if (textObj.SourceAudioReady && !string.IsNullOrEmpty(textObj.SourceAudioFilePath))
                {
                    continue;
                }
                
                // Create task for this text object
                Console.WriteLine($"AudioPreloadService: Queuing preload for text object {textObj.ID}: '{textObj.Text.Substring(0, Math.Min(50, textObj.Text.Length))}...'");
                var task = PreloadAudioForTextObjectAsync(textObj, textObj.Text, service, voice, isSource: true, cancellationToken);
                tasks.Add(task);
            }
            
            Console.WriteLine($"AudioPreloadService: Queued {tasks.Count} preload tasks");
            
            // Wait for all tasks to complete (or cancellation)
            try
            {
                await Task.WhenAll(tasks);
                Console.WriteLine($"AudioPreloadService: Completed all {tasks.Count} preload tasks");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("AudioPreloadService: Audio preloading cancelled");
            }
        }
        
        public async Task PreloadTargetAudioAsync(List<TextObject> textObjects)
        {
            if (textObjects == null || textObjects.Count == 0)
            {
                Console.WriteLine("AudioPreloadService: No text objects to preload");
                return;
            }
            
            Console.WriteLine($"AudioPreloadService: Starting target audio preload for {textObjects.Count} text objects");
            
            // Cancel any existing preloads
            CancelAllPreloads();
            
            // Create new cancellation token
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;
            
            // Get settings
            string service = ConfigManager.Instance.GetTtsTargetService();
            string voice = ConfigManager.Instance.GetTtsTargetVoice();
            
            // Check for custom voice ID for ElevenLabs
            if (service == "ElevenLabs" && ConfigManager.Instance.GetTtsTargetUseCustomVoiceId())
            {
                string customVoice = ConfigManager.Instance.GetTtsTargetCustomVoiceId();
                if (!string.IsNullOrWhiteSpace(customVoice))
                {
                    voice = customVoice;
                    Console.WriteLine($"AudioPreloadService: Using custom ElevenLabs voice ID: {voice}");
                }
            }
            
            Console.WriteLine($"AudioPreloadService: Using service={service}, voice={voice}");
            
            // Preload audio for each text object
            var tasks = new List<Task>();
            foreach (var textObj in textObjects)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                
                if (string.IsNullOrWhiteSpace(textObj.TextTranslated))
                {
                    continue;
                }
                
                // Check if already ready
                if (textObj.TargetAudioReady && !string.IsNullOrEmpty(textObj.TargetAudioFilePath))
                {
                    continue;
                }
                
                // Create task for this text object
                Console.WriteLine($"AudioPreloadService: Queuing preload for text object {textObj.ID}: '{textObj.TextTranslated.Substring(0, Math.Min(50, textObj.TextTranslated.Length))}...'");
                var task = PreloadAudioForTextObjectAsync(textObj, textObj.TextTranslated, service, voice, isSource: false, cancellationToken);
                tasks.Add(task);
            }
            
            Console.WriteLine($"AudioPreloadService: Queued {tasks.Count} preload tasks");
            
            // Wait for all tasks to complete (or cancellation)
            try
            {
                await Task.WhenAll(tasks);
                Console.WriteLine($"AudioPreloadService: Completed all {tasks.Count} preload tasks");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("AudioPreloadService: Audio preloading cancelled");
            }
        }
        
        private async Task PreloadAudioForTextObjectAsync(TextObject textObj, string text, string service, string voice, bool isSource, CancellationToken cancellationToken)
        {
            try
            {
                // Generate hash for caching
                string textHash = ComputeTextHash(text);
                
                // Check cache first
                if (_audioCache.TryGetValue(textHash, out string? cachedPath) && File.Exists(cachedPath))
                {
                    // Use cached file
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (isSource)
                        {
                            textObj.SourceAudioFilePath = cachedPath;
                            textObj.SourceAudioReady = true;
                        }
                        else
                        {
                            textObj.TargetAudioFilePath = cachedPath;
                            textObj.TargetAudioReady = true;
                        }
                        
                        // Refresh overlays
                        MainWindow.Instance?.RefreshMainWindowOverlays();
                        MonitorWindow.Instance?.RefreshOverlays();
                    });
                    return;
                }
                
                // Wait for available slot (rate limiting)
                await _concurrencyLimiter.WaitAsync(cancellationToken);
                
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    
                    // Generate audio file
                    string? audioFilePath = null;
                    int retryCount = 0;
                    const int maxRetries = 3;
                    
                    while (retryCount < maxRetries && audioFilePath == null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        
                        try
                        {
                            Console.WriteLine($"AudioPreloadService: Generating audio for text object {textObj.ID}, service={service}, voice={voice}");
                            
                            if (service == "ElevenLabs")
                            {
                                // Check API key
                                string apiKey = ConfigManager.Instance.GetElevenLabsApiKey();
                                if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("<your"))
                                {
                                    Console.WriteLine($"AudioPreloadService: ElevenLabs API key not configured, skipping preload");
                                    break;
                                }
                                
                                audioFilePath = await ElevenLabsService.Instance.GenerateAudioFileAsync(text, voice);
                            }
                            else if (service == "Google Cloud TTS")
                            {
                                // Check API key
                                string apiKey = ConfigManager.Instance.GetGoogleTtsApiKey();
                                if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("<your"))
                                {
                                    Console.WriteLine($"AudioPreloadService: Google TTS API key not configured, skipping preload");
                                    break;
                                }
                                
                                // Extract language code from voice if needed
                                string languageCode = ExtractLanguageCodeFromVoice(voice);
                                audioFilePath = await GoogleTTSService.Instance.GenerateAudioFileAsync(text, languageCode, voice);
                            }
                            else
                            {
                                Console.WriteLine($"AudioPreloadService: Unknown TTS service: {service}");
                                break;
                            }
                            
                            if (audioFilePath != null && File.Exists(audioFilePath))
                            {
                                // Cache the file
                                _audioCache[textHash] = audioFilePath;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error generating audio (attempt {retryCount + 1}/{maxRetries}): {ex.Message}");
                            
                            // Check status code from exception data if available
                            System.Net.HttpStatusCode? statusCode = null;
                            if (ex is HttpRequestException httpEx && httpEx.Data.Contains("StatusCode"))
                            {
                                statusCode = httpEx.Data["StatusCode"] as System.Net.HttpStatusCode?;
                            }
                            
                            // Check for unauthorized (401) or forbidden (403) - don't retry these
                            if (statusCode == System.Net.HttpStatusCode.Unauthorized || 
                                statusCode == System.Net.HttpStatusCode.Forbidden ||
                                ex.Message.Contains("Unauthorized") || 
                                ex.Message.Contains("Forbidden") ||
                                ex.Message.Contains("401") ||
                                ex.Message.Contains("403"))
                            {
                                Console.WriteLine($"TTS authentication/authorization error for {service}. Skipping this audio file.");
                                break; // Don't retry - just fail this file
                            }
                            
                            // Check for rate limiting (429) or TooManyRequests
                            if (statusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                ex.Message.Contains("429") || 
                                ex.Message.Contains("Too Many Requests") ||
                                ex.Message.Contains("TooManyRequests"))
                            {
                                Console.WriteLine($"TTS Rate limit hit for {service}. Retrying after delay...");
                                
                                // Exponential backoff: 1s, 2s, 4s
                                int delayMs = (int)Math.Pow(2, retryCount) * 1000;
                                await Task.Delay(delayMs, cancellationToken);
                                retryCount++;
                            }
                            else
                            {
                                // For other errors, don't retry
                                Console.WriteLine($"TTS error for {service} (not retryable). Skipping this audio file.");
                                break;
                            }
                        }
                    }
                    
                    if (audioFilePath != null && File.Exists(audioFilePath))
                    {
                        Console.WriteLine($"AudioPreloadService: Successfully generated audio file: {audioFilePath} for text object {textObj.ID}");
                        
                        // Update TextObject on UI thread
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (isSource)
                            {
                                textObj.SourceAudioFilePath = audioFilePath;
                                textObj.SourceAudioReady = true;
                                Console.WriteLine($"AudioPreloadService: Set source audio ready for text object {textObj.ID}");
                            }
                            else
                            {
                                textObj.TargetAudioFilePath = audioFilePath;
                                textObj.TargetAudioReady = true;
                                Console.WriteLine($"AudioPreloadService: Set target audio ready for text object {textObj.ID}");
                            }
                            
                            // Refresh overlays
                            MainWindow.Instance?.RefreshMainWindowOverlays();
                            MonitorWindow.Instance?.RefreshOverlays();
                        });
                        
                        // Check if all preloading is done and trigger auto-play if enabled
                        CheckAndTriggerAutoPlay();
                    }
                    else
                    {
                        Console.WriteLine($"AudioPreloadService: Failed to generate audio file for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                    }
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected, just return
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in PreloadAudioForTextObjectAsync: {ex.Message}");
            }
        }
        
        private string ComputeTextHash(string text)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
                return Convert.ToBase64String(hashBytes);
            }
        }
        
        private string ExtractLanguageCodeFromVoice(string voice)
        {
            // Extract language code from voice ID (e.g., "ja-JP-Neural2-B" -> "ja-JP")
            int dashIndex = voice.IndexOf("-Neural2");
            if (dashIndex == -1) dashIndex = voice.IndexOf("-Studio");
            if (dashIndex == -1) dashIndex = voice.IndexOf("-Standard");
            
            if (dashIndex > 0)
            {
                return voice.Substring(0, dashIndex);
            }
            
            return "ja-JP"; // Default
        }
        
        private void CheckAndTriggerAutoPlay()
        {
            // This will be called after each audio file is ready
            // Check if auto-play is enabled and all requested audio is ready
            if (!ConfigManager.Instance.IsTtsAutoPlayAllEnabled())
            {
                return;
            }
            
            // Get current preload mode
            string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
            if (preloadMode == "Off")
            {
                return;
            }
            
            // Get all text objects
            var textObjects = Logic.Instance?.GetTextObjects();
            if (textObjects == null || textObjects.Count == 0)
            {
                return;
            }
            
            // Determine which audio to check
            bool checkSource = preloadMode == "Source language" || preloadMode == "Both source and target language";
            bool checkTarget = preloadMode == "Target language" || preloadMode == "Both source and target language";
            
            // Check if all requested audio is ready
            bool allReady = true;
            foreach (var textObj in textObjects)
            {
                if (checkSource && !textObj.SourceAudioReady)
                {
                    allReady = false;
                    break;
                }
                if (checkTarget && !string.IsNullOrEmpty(textObj.TextTranslated) && !textObj.TargetAudioReady)
                {
                    allReady = false;
                    break;
                }
            }
            
            if (allReady)
            {
                // Trigger auto-play check in AudioPlaybackManager
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AudioPlaybackManager.Instance?.CheckAndTriggerAutoPlay();
                });
            }
        }
        
        public void CancelAllPreloads()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            
            _inProgressTasks.Clear();
        }
        
        public void ClearAudioCache()
        {
            // Delete cached audio files
            foreach (var filePath in _audioCache.Values)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting cached audio file {filePath}: {ex.Message}");
                }
            }
            
            _audioCache.Clear();
        }
        
        public string? GetCachedAudioPath(string textHash)
        {
            if (_audioCache.TryGetValue(textHash, out string? path) && File.Exists(path))
            {
                return path;
            }
            return null;
        }
    }
}

