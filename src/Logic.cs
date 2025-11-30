using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace UGTLive
{
    public class Logic
    {
        private static Logic? _instance;
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        private List<TextObject> _textObjects;
        private List<TextObject> _textObjectsOld;
        private Random _random;
        private Grid? _overlayContainer;
        private int _textIDCounter = 0;
        
        private string _lastOcrHash = string.Empty;
        
        // Session ID to track validity of OCR requests
        private long _overlaySessionId = 0;

        // Track the current capture position
        private int _currentCaptureX;
        private int _currentCaptureY;
        private DateTime _lastChangeTime = DateTime.MinValue;
        private DateTime _settlingStartTime = DateTime.MinValue;
   
        // Properties to expose to other classes
        public List<TextObject> TextObjects => _textObjects;
        public List<TextObject> TextObjectsOld => _textObjectsOld;

        // Centralized logging method with consistent timestamp format
        private static void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine($"[{timestamp}] {message}");
        }

        // Events
        public event EventHandler<TextObject>? TextObjectAdded;
        
        // Event when translation is completed
        public event EventHandler<TranslationEventArgs>? TranslationCompleted;
     
        bool _waitingForTranslationToFinish = false;
        
        // Flag to indicate we're keeping old translation visible while waiting for new translation
        private bool _keepingTranslationVisible = false;
        
        // Flag to indicate snapshot mode (bypasses settling)
        private bool _isSnapshotMode = false;

        public bool GetWaitingForTranslationToFinish()
        {
            return _waitingForTranslationToFinish;
        }
        public int GetNextTextID()
        {
            return _textIDCounter++;
        }
        public void SetWaitingForTranslationToFinish(bool value)
        {
            _waitingForTranslationToFinish = value;
        }

        // Singleton pattern
        public static Logic Instance 
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Logic();
                }
                return _instance;
            }
        }
        Stopwatch _translationStopwatch = new Stopwatch();
        Stopwatch _ocrProcessingStopwatch = new Stopwatch();
        
        // Cancellation token source for in-progress translations
        private CancellationTokenSource? _translationCancellationTokenSource;
        
        // Centralized OCR status tracking
        private DispatcherTimer? _ocrStatusTimer;
        private bool _isOCRActive = false;
        private DateTime _lastOcrFrameTime = DateTime.MinValue;
        private Queue<double> _ocrFrameTimes = new Queue<double>();
        private const int MAX_FPS_SAMPLES = 10;

        // Constructor
        private Logic()
        {
            // Private constructor to enforce singleton pattern
            _textObjects = new List<TextObject>();
            _textObjectsOld = new List<TextObject>();
            _random = new Random();
        }

        
        // Set reference to the overlay container
        public void SetOverlayContainer(Grid overlayContainer)
        {
            _overlayContainer = overlayContainer;
        }
        
        // Get the list of current text objects
        public IReadOnlyList<TextObject> GetTextObjects()
        {
            return _textObjects.AsReadOnly();
        }
        public IReadOnlyList<TextObject> GetTextObjectsOld()
        {
            return _textObjectsOld.AsReadOnly();
        }
        
        public bool GetKeepingTranslationVisible()
        {
            return _keepingTranslationVisible;
        }

        // Called when the application starts
        public async void Init()
        {
            try
            {
                // Initialize resources, settings, etc.
                Log("Logic initialized");
                
                // Load configuration
                string geminiApiKey = ConfigManager.Instance.GetGeminiApiKey();

                // Warm up shared WebView2 environment early to reduce overlay latency
                _ = WebViewEnvironmentManager.GetEnvironmentAsync();
                Log($"Loaded Gemini API key: {(string.IsNullOrEmpty(geminiApiKey) ? "Not set" : "Set")}");
                
                // Load LLM prompt
                string llmPrompt = ConfigManager.Instance.GetLlmPrompt();
                Log($"Loaded LLM prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : $"{llmPrompt.Length} chars")}");
                
                // Load force cursor visible setting
                // Force cursor visibility is now handled by MouseManager
                
                // Discover Python services
                PythonServicesManager.Instance.DiscoverServices();
                
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                if (ocrMethod == "Google Vision")
                {
                    Log("Using Google Cloud Vision - socket connection not needed");
                    
                    // Update status message in the UI
                    MainWindow.Instance.SetStatus("Using Google Cloud Vision (non-local, costs $)");
                }
                else
                {
                    Log("Using Windows OCR - socket connection not needed");
                    
                    // Update status message in the UI
                    MainWindow.Instance.SetStatus("Using Windows OCR (built-in)");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during initialization: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
      
     
        void OnFinishedThings(bool bResetTranslationStatus)
        {
            SetWaitingForTranslationToFinish(false);
            _settlingStartTime = DateTime.MinValue;
            
            // Notify MainWindow if snapshot mode is completing
            bool wasSnapshotMode = _isSnapshotMode;
            _isSnapshotMode = false; // Reset snapshot mode after processing
            
            if (wasSnapshotMode)
            {
                // Check if we have results to display
                bool hasResults = _textObjects.Count > 0;
                MainWindow.Instance?.OnSnapshotComplete(hasResults);
            }
            
            // If we were keeping translation visible but translation failed/cancelled,
            // clear the old overlays now and reset the flag
            if (_keepingTranslationVisible)
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log("Leave translation onscreen: Clearing flag in OnFinishedThings");
                }
                // Dispose old text objects that were kept visible
                foreach (TextObject textObject in _textObjectsOld)
                {
                    textObject.Dispose();
                }
                _textObjectsOld.Clear();
                MonitorWindow.Instance?.ClearOverlays();
                MainWindow.Instance?.RefreshMainWindowOverlays();
                _keepingTranslationVisible = false;
            }
            
            MonitorWindow.Instance.RefreshOverlays();
            MainWindow.Instance.RefreshMainWindowOverlays();

            // Hide translation status
            if (bResetTranslationStatus)
            {
                MainWindow.Instance.HideTranslationStatus();
                ChatBoxWindow.Instance?.HideTranslationStatus();
            }
            
            // Re-enable OCR if it was paused during translation
            if (ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled())
            {
                MainWindow.Instance.SetOCRCheckIsWanted(true);
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log("Translation finished - re-enabling OCR");
                }
            }
        }

        public void ResetHash()
        {
            // Force mismatch on next comparison by using a unique string
            _lastOcrHash = "RESET_" + Guid.NewGuid().ToString();
            _lastChangeTime = DateTime.Now;
            _settlingStartTime = DateTime.MinValue; // Ensure settling restarts clean
        }
        
        // Prepare for snapshot OCR, bypassing settling delays
        // Call this before triggering capture manually
        public void PrepareSnapshotOCR()
        {
            Log("Preparing snapshot OCR");
            
            // Set snapshot mode to bypass settling
            _isSnapshotMode = true;
            
            // Clear settling state
            _lastChangeTime = DateTime.MinValue;
            _settlingStartTime = DateTime.MinValue;
            
            // Force new OCR analysis
            ResetHash();
        }
        
        // Check if snapshot mode is active
        public bool IsSnapshotMode()
        {
            return _isSnapshotMode;
        }

    
        private void ProcessGoogleTranslateJson(JsonElement translatedRoot)
        {
            try
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log("Processing Google Translate JSON response");
                }
                
                // If we were keeping translation visible, we don't need to explicitly clear old overlays
                // because RefreshOverlays will replace them with the new translation shortly.
                // This prevents a "blank frame" blink.
                if (_keepingTranslationVisible)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log("Leave translation onscreen: Resetting flag, new translation ready");
                    }
                    
                    // Dispose old text objects that were kept visible
                    foreach (TextObject textObject in _textObjectsOld)
                    {
                        textObject.Dispose();
                    }
                    _textObjectsOld.Clear();
                    _keepingTranslationVisible = false;
                }
                
                if (translatedRoot.TryGetProperty("translations", out JsonElement translationsElement) &&
                    translationsElement.ValueKind == JsonValueKind.Array)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Found {translationsElement.GetArrayLength()} translations in Google Translate JSON");
                    }
                    
                    
                    for (int i = 0; i < translationsElement.GetArrayLength(); i++)
                    {
                        var translation = translationsElement[i];
                        
                        if (translation.TryGetProperty("id", out JsonElement idElement) &&
                            translation.TryGetProperty("original_text", out JsonElement originalTextElement) &&
                            translation.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                        {
                            string id = idElement.GetString() ?? "";
                            string originalText = originalTextElement.GetString() ?? "";
                            string translatedText = translatedTextElement.GetString() ?? "";
                            
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                            {
                               
                                var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                if (matchingTextObj != null)
                                {
                                   
                                    matchingTextObj.TextTranslated = translatedText;

                                    // Don't modify the original text orientation - it should remain as detected by OCR

                                    matchingTextObj.UpdateUIElement();
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"Updated text object {id} with Google translation");
                                    }
                                }
                                else if (id.StartsWith("text_"))
                                {
                                    // Thử trích xuất index từ ID (định dạng text_X)
                                    string indexStr = id.Substring(5); // Bỏ tiền tố "text_"
                                    if (int.TryParse(indexStr, out int index) && index >= 0 && index < _textObjects.Count)
                                    {
                                        // Cập nhật theo index nếu ID khớp định dạng
                                        _textObjects[index].TextTranslated = translatedText;

                                        // Don't modify the original text orientation - it should remain as detected by OCR

                                        _textObjects[index].UpdateUIElement();
                                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                        {
                                            Log($"Updated text object at index {index} with Google translation");
                                        }
                                    }
                                    else
                                    {
                                        Log($"Could not find text object with ID {id}");
                                    }
                                }
                                
                                // Thêm vào lịch sử dịch
                                if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                                {
                                    TranslationCompleted?.Invoke(this, new TranslationEventArgs
                                    {
                                        OriginalText = originalText,
                                        TranslatedText = translatedText
                                    });
                                }
                            }
                        }
                    }
                    
                    
                    if (ChatBoxWindow.Instance != null)
                    {
                        ChatBoxWindow.Instance.UpdateChatHistory();
                    }
                    
                   
                    MonitorWindow.Instance.RefreshOverlays();
                    MainWindow.Instance.RefreshMainWindowOverlays();
                    
                    // Trigger target audio preloading if enabled
                    TriggerTargetAudioPreloading();
                }
                else
                {
                    Log("No translations array found in Google Translate JSON");
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing Google Translate JSON: {ex.Message}");
            }
        }


        //! Process the OCR text data, this is before it's been translated
        public void ProcessReceivedTextJsonData(string data)
        {
            // Log OCR reply for debugging (fire-and-forget, non-blocking)
            LogManager.Instance.LogOcrResponse(data);
            
            _ocrProcessingStopwatch.Restart();
            
            // Reset auto-play trigger flag to allow auto-play on new OCR results
            AudioPlaybackManager.Instance.ResetAutoPlayTrigger();
            
            // Check if we should pause OCR while translating
            bool pauseOcrWhileTranslating = ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled();
            bool waitingForTranslation = GetWaitingForTranslationToFinish();
            
            // Only re-enable OCR if we're not waiting for translation, or if pause setting is disabled
            if (!waitingForTranslation || !pauseOcrWhileTranslating)
            {
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log("Pause OCR while translating is enabled - keeping OCR paused until translation finishes");
                }
            }
            
            // Notify that OCR has completed
            NotifyOCRCompleted();

            if (waitingForTranslation)
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log("Skipping OCR results - waiting for translation to finish");
                }
                // Clear stored bitmap since we're not processing results
                ClearCurrentProcessingBitmap();
                return;
            }

            try
            {
                // Check if the data is JSON
                if (data.StartsWith("{") && data.EndsWith("}"))
                {
                    // Try to parse as JSON
                    try
                    {
                        // Parse JSON with options that preserve Unicode characters
                        var options = new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip
                        };
                        
                        using JsonDocument doc = JsonDocument.Parse(data, options);
                        JsonElement root = doc.RootElement;
                        bool bForceRender = false;

                        // Check if it's an OCR response
                        if (root.TryGetProperty("status", out JsonElement statusElement))
                        {
                            string status = statusElement.GetString() ?? "unknown";
                            
                            // Try "results" first (Windows OCR format), then "texts" (HTTP service format)
                            JsonElement resultsElement;
                            bool hasResults = root.TryGetProperty("results", out resultsElement);
                            bool hasTexts = root.TryGetProperty("texts", out JsonElement textsElement);
                            
                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                            {
                                Log($"ProcessReceivedTextJsonData: status={status}, hasResults={hasResults}, hasTexts={hasTexts}");
                            }
                            
                            if (hasTexts && !hasResults)
                            {
                                resultsElement = textsElement;
                                hasResults = true;
                                
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Log($"Using 'texts' property as results, array length: {resultsElement.GetArrayLength()}");
                                }
                            }
                            
                            if (status == "success" && hasResults)
                            {
                                // Pre-filter low-confidence characters before block detection
                                // Pass the current OCR method to use method-specific confidence settings
                                string ocrMethod = ConfigManager.Instance.GetOcrMethod();
                                JsonElement filteredResults = FilterLowConfidenceCharacters(resultsElement, ocrMethod);
                                
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Log($"After FilterLowConfidenceCharacters: {filteredResults.GetArrayLength()} items");
                                }
                                
                                // Check if block detection should be skipped (e.g., for Google Vision results)
                                bool skipBlockDetection = false;
                                if (root.TryGetProperty("skip_block_detection", out JsonElement skipElement))
                                {
                                    skipBlockDetection = skipElement.GetBoolean();
                                }
                                
                                JsonElement modifiedResults;
                                if (skipBlockDetection)
                                {
                                    // Skip block detection for pre-grouped results (e.g., Google Vision)
                                    modifiedResults = filteredResults;
                                    Log($"Skipping block detection for pre-grouped results");
                                }
                                else
                                {
                                    // Process OCR data using UniversalBlockDetector
                                    // Use the filtered results for consistency
                                    // Pass the current OCR method to use method-specific confidence settings
                                    modifiedResults = UniversalBlockDetector.Instance.ProcessResults(filteredResults, ConfigManager.Instance.GetOcrMethod());
                                    
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"After UniversalBlockDetector: {modifiedResults.GetArrayLength()} blocks");
                                    }
                                }
                                
                                // Filter out text objects that should be ignored based on ignore phrases
                                modifiedResults = FilterIgnoredPhrases(modifiedResults);
                                
                                // Generate content hash AFTER block detection and filtering
                                string contentHash = GenerateContentHash(modifiedResults);

                                // Handle settle time if enabled
                                double settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
                                double maxSettleTime = ConfigManager.Instance.GetBlockDetectionMaxSettleTime();
                                
                                // If in snapshot mode, bypass settling entirely
                                if (_isSnapshotMode)
                                {
                                    Log("Snapshot mode: bypassing settle time");
                                    _lastChangeTime = DateTime.MinValue;
                                    _settlingStartTime = DateTime.MinValue;
                                    _lastOcrHash = contentHash;
                                    bForceRender = true;
                                }
                                // If settle time is 0 or negative, disable settling completely
                                else if (settleTime <= 0)
                                {
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"Settle time disabled (settleTime: {settleTime})");
                                    }
                                    _lastChangeTime = DateTime.MinValue;
                                    _settlingStartTime = DateTime.MinValue;
                                    _lastOcrHash = contentHash;
                                    bForceRender = true;
                                }
                                else
                                {
                                    // Debug outputs to track settling behavior
                                    bool isSettling = _settlingStartTime != DateTime.MinValue;
                                    double settlingElapsed = isSettling ? (DateTime.Now - _settlingStartTime).TotalSeconds : 0;
                                    double lastChangeElapsed = _lastChangeTime != DateTime.MinValue ? (DateTime.Now - _lastChangeTime).TotalSeconds : 0;
                                    
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff() && isSettling)
                                    {
                                        Log($"Settling - Elapsed: {settlingElapsed:F2}s, MaxSettleTime: {maxSettleTime}s, LastChange: {lastChangeElapsed:F2}s, SettleTime: {settleTime}s");
                                    }

                                    // Check for max settle time first, regardless of hash match
                                    bool maxSettleTimeExceeded = maxSettleTime > 0 && 
                                                                _settlingStartTime != DateTime.MinValue &&
                                                                (DateTime.Now - _settlingStartTime).TotalSeconds >= maxSettleTime;
                                    
                                    if (maxSettleTimeExceeded)
                                    {
                                        Log($"Max settle time exceeded ({maxSettleTime}s), forcing translation after {(DateTime.Now - _settlingStartTime).TotalSeconds:F2}s of settling.");
                                        _lastChangeTime = DateTime.MinValue;
                                        _settlingStartTime = DateTime.MinValue;
                                        _lastOcrHash = contentHash;
                                        bForceRender = true;
                                    }
                                    // Check if content hash matches and we're NOT forcing a render
                                    else if (contentHash == _lastOcrHash && !_lastOcrHash.StartsWith("RESET_"))
                                    {
                                        // Content is stable
                                        if (_lastChangeTime == DateTime.MinValue) // Already settled and rendered
                                        {
                                            // Reset settling start time as content is stable and has been processed
                                            if (ConfigManager.Instance.GetLogExtraDebugStuff() && _settlingStartTime != DateTime.MinValue)
                                            {
                                                Log($"Settling reset (content stable, already processed). Elapsed: {(DateTime.Now - _settlingStartTime).TotalSeconds:F2}s");
                                            }
                                            _settlingStartTime = DateTime.MinValue; 
                                            // Clear stored bitmap since we're not displaying results
                                            ClearCurrentProcessingBitmap();
                                            OnFinishedThings(true); // Reset status, hide "settling"
                                            return;
                                        }
                                        else
                                        {
                                            // Check if normal settle time is reached
                                            if ((DateTime.Now - _lastChangeTime).TotalSeconds >= settleTime)
                                            {
                                                double totalSettlingTime = _settlingStartTime != DateTime.MinValue ? 
                                                    (DateTime.Now - _settlingStartTime).TotalSeconds : 0;
                                                Log($"Settle time reached ({settleTime}s), content is stable for {(DateTime.Now - _lastChangeTime).TotalSeconds:F2}s. Total settling: {totalSettlingTime:F2}s");
                                                _lastChangeTime = DateTime.MinValue;
                                                _settlingStartTime = DateTime.MinValue;
                                                bForceRender = true;
                                            }
                                            else
                                            {
                                                // Still within normal settle time, content is stable but waiting
                                                double elapsedSettlingTime = _settlingStartTime != DateTime.MinValue ? 
                                                    (DateTime.Now - _settlingStartTime).TotalSeconds : 0;
                                                double remainingSettleTime = settleTime - (DateTime.Now - _lastChangeTime).TotalSeconds;
                                                double remainingMaxSettleTime = maxSettleTime > 0 ? maxSettleTime - elapsedSettlingTime : 0;
                                                
                                                Log($"Content stable for {(DateTime.Now - _lastChangeTime).TotalSeconds:F2}s, waiting {remainingSettleTime:F2}s more (settle: {settleTime}s, max: {maxSettleTime}s, elapsed: {elapsedSettlingTime:F2}s, remaining max: {remainingMaxSettleTime:F2}s).");
                                                ChatBoxWindow.Instance?.ShowTranslationStatus(true, elapsedSettlingTime, maxSettleTime);
                                                MainWindow.Instance.ShowTranslationStatus(true, elapsedSettlingTime, maxSettleTime);
                                                // Clear stored bitmap since we're not displaying results yet
                                                ClearCurrentProcessingBitmap();
                                                return; 
                                            }
                                        }
                                    }
                                    else // contentHash != _lastOcrHash (text has changed)
                                    {
                                        // Content has changed
                                        if (_lastOcrHash != string.Empty && !_lastOcrHash.StartsWith("RESET_"))
                                        {
                                            Log($"Content changed! Old hash: {_lastOcrHash.Substring(0, Math.Min(20, _lastOcrHash.Length))}..., New hash: {contentHash.Substring(0, Math.Min(20, contentHash.Length))}...");
                                        }
                                        
                                        _lastChangeTime = DateTime.Now;
                                        _lastOcrHash = contentHash;

                                        // Initialize settling start time when hash changes (only start settling when content actually changes)
                                        if (_settlingStartTime == DateTime.MinValue)
                                        {
                                            _settlingStartTime = DateTime.Now;
                                            Log($"Settling started, Hash: {contentHash.Substring(0, Math.Min(20, contentHash.Length))}..., SettleTime: {settleTime}s, MaxSettleTime: {maxSettleTime}s");
                                        }

                                        // Calculate elapsed settle time for status display
                                        double elapsedSettlingTimeOnChange = _settlingStartTime != DateTime.MinValue ? 
                                            (DateTime.Now - _settlingStartTime).TotalSeconds : 0;

                                        if (MainWindow.Instance.GetIsStarted())
                                        {
                                            ChatBoxWindow.Instance?.ShowTranslationStatus(true, elapsedSettlingTimeOnChange, maxSettleTime);
                                            MainWindow.Instance.ShowTranslationStatus(true, elapsedSettlingTimeOnChange, maxSettleTime);
                                        }
                                        
                                        // Check again for max settle time to ensure it's enforced even with changing content
                                        if (maxSettleTime > 0 && 
                                            _settlingStartTime != DateTime.MinValue &&
                                            (DateTime.Now - _settlingStartTime).TotalSeconds >= maxSettleTime)
                                        {
                                            double totalSettlingTime = (DateTime.Now - _settlingStartTime).TotalSeconds;
                                            Log($"Max settle time exceeded ({maxSettleTime}s) while content was changing, forcing translation after {totalSettlingTime:F2}s.");
                                            _lastChangeTime = DateTime.MinValue;
                                            _settlingStartTime = DateTime.MinValue;
                                            bForceRender = true;
                                            // Don't return - continue to process the forced rendering
                                        }
                                        else
                                        {
                                            // We return here to wait for either the content to stabilize or maxSettleTime to be hit
                                            double elapsedSettlingTime = _settlingStartTime != DateTime.MinValue ? 
                                                (DateTime.Now - _settlingStartTime).TotalSeconds : 0;
                                            double remainingMaxSettleTime = maxSettleTime > 0 ? maxSettleTime - elapsedSettlingTime : 0;
                                            
                                            Log($"Content unstable, settling for {elapsedSettlingTime:F2}s, max {maxSettleTime}s (remaining: {remainingMaxSettleTime:F2}s).");
                                            
                                            if (MainWindow.Instance.GetIsStarted())
                                            {
                                                ChatBoxWindow.Instance?.ShowTranslationStatus(true, elapsedSettlingTime, maxSettleTime);
                                                MainWindow.Instance.ShowTranslationStatus(true, elapsedSettlingTime, maxSettleTime);
                                            }
                                            // Clear stored bitmap since we're not displaying results yet
                                            ClearCurrentProcessingBitmap();
                                            return;
                                        }
                                    }
                                }

                                if (contentHash == _lastOcrHash && bForceRender == false)
                                {
                                    // Before returning, check if we need to clear displayed text
                                    // This handles the case where we move to a blank area and OCR finds no text,
                                    // but we still have old text objects displayed
                                    if (resultsElement.GetArrayLength() == 0 && _textObjects.Count > 0)
                                    {
                                        // OCR found no text but we have text displayed - clear it
                                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                        {
                                            Log("Hash matches but OCR found no text while text objects exist - clearing display");
                                        }
                                        // Ensure we clear the display even if we were keeping translation visible
                                        _keepingTranslationVisible = false;
                                        ClearAllTextObjects();
                                        MonitorWindow.Instance.RefreshOverlays();
                                        MainWindow.Instance.RefreshMainWindowOverlays();
                                    }
                                    // Clear stored bitmap since hash matches and we're not displaying new results
                                    ClearCurrentProcessingBitmap();
                                    OnFinishedThings(true);
                                    return;
                                }
                               
                                // Looks like new stuff
                                _lastOcrHash = contentHash;
                                
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Log($"Character-level processing: {resultsElement.GetArrayLength()} characters → {modifiedResults.GetArrayLength()} blocks");
                                }
                                
                                // Create a new JsonDocument with the modified results
                                using (var stream = new MemoryStream())
                                {
                                    using (var writer = new Utf8JsonWriter(stream))
                                    {
                                        writer.WriteStartObject();
                                        
                                        // Copy over all existing properties except 'results' and 'texts'
                                        // (we'll add 'results' with the modified data)
                                        foreach (var property in root.EnumerateObject())
                                        {
                                            if (property.Name != "results" && property.Name != "texts")
                                            {
                                                property.WriteTo(writer);
                                            }
                                        }
                                        
                                        // Add our modified results
                                        writer.WritePropertyName("results");
                                        modifiedResults.WriteTo(writer);
                                        
                                        // Add marker to indicate this is character-level data
                                        writer.WriteBoolean("char_level", true);
                                        
                                        writer.WriteEndObject();
                                    }
                                    
                                    stream.Position = 0;
                                    
                                    // Clone the JsonElement before the JsonDocument is disposed
                                    // This is necessary because DisplayOcrResults is async and may not complete
                                    // before the using block exits
                                    JsonElement clonedRoot;
                                    using (JsonDocument newDoc = JsonDocument.Parse(stream))
                                    {
                                        clonedRoot = newDoc.RootElement.Clone();
                                    }
                                    
                                    // Now call DisplayOcrResults with the cloned element
                                    // Note: This is async void, so it returns immediately on first await
                                    // Translation triggering has been moved inside DisplayOcrResults
                                    DisplayOcrResults(clonedRoot);

                                    _ocrProcessingStopwatch.Stop();
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"OCR JSON processing took {_ocrProcessingStopwatch.ElapsedMilliseconds} ms");
                                    }

                                }
                            }
                            else if (status == "error" && root.TryGetProperty("message", out JsonElement messageElement))
                            {
                                // Display error message
                                string errorMsg = messageElement.GetString() ?? "Unknown error";
                                Log($"OCR service returned error: {errorMsg}");
                            }
                            else if (status == "success" && !hasResults)
                            {
                                // Success status but no results/texts property
                                Log("ERROR: OCR response has status='success' but no 'results' or 'texts' property");
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Log($"Response JSON properties: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");
                                    Log($"Full response (first 500 chars): {data.Substring(0, Math.Min(500, data.Length))}");
                                }
                            }
                        }
                        else
                        {
                            // No status property at all
                            Log("ERROR: OCR response missing 'status' property");
                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                            {
                                Log($"Response JSON properties: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");
                                Log($"Full response (first 500 chars): {data.Substring(0, Math.Min(500, data.Length))}");
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Log($"JSON parsing error: {ex.Message}");
                        AddTextObject($"JSON Error: {ex.Message}");
                    }
                }
                else
                {
                    // Just display the raw data
                    AddTextObject(data);
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing socket data: {ex.Message}");
            }
            
           }
        
        // Filter results array to remove objects that should be ignored based on ignore phrases
        private JsonElement FilterIgnoredPhrases(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;
                
            try
            {
                // Create a new JSON array for filtered results
                using (var ms = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();
                        
                        // Process each element in the array
                        for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                        {
                            var item = resultsElement[i];
                            
                            // Skip items that don't have the text property
                            if (!item.TryGetProperty("text", out var textElement))
                            {
                                // Include items without text values (might be non-character elements)
                                item.WriteTo(writer);
                                continue;
                            }
                            
                            // Get the text from the element
                            string text = textElement.GetString() ?? "";
                            
                            // Check if we should ignore this text
                            var (shouldIgnore, filteredText) = ShouldIgnoreText(text);
                            
                            if (shouldIgnore)
                            {
                                // Skip this element entirely
                                continue;
                            }
                            
                            // If the text was filtered but not ignored completely
                            if (filteredText != text)
                            {
                                // We need to create a new JSON object with the filtered text
                                writer.WriteStartObject();
                                
                                // Copy all properties except 'text'
                                foreach (var property in item.EnumerateObject())
                                {
                                    if (property.Name != "text")
                                    {
                                        property.WriteTo(writer);
                                    }
                                }
                                
                                // Write the filtered text
                                writer.WritePropertyName("text");
                                writer.WriteStringValue(filteredText);
                                
                                writer.WriteEndObject();
                            }
                            else
                            {
                                // No change to the text, write the entire item
                                item.WriteTo(writer);
                            }
                        }
                        
                        writer.WriteEndArray();
                        writer.Flush();
                        
                        // Read the filtered JSON back
                        ms.Position = 0;
                        using (JsonDocument doc = JsonDocument.Parse(ms))
                        {
                            return doc.RootElement.Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error filtering ignored phrases: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }
        
        // Check if a text should be ignored based on ignore phrases
        private (bool ShouldIgnore, string FilteredText) ShouldIgnoreText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (true, string.Empty);
                
            // Get all ignore phrases from ConfigManager
            var ignorePhrases = ConfigManager.Instance.GetIgnorePhrases();
            
            if (ignorePhrases.Count == 0)
                return (false, text); // No phrases to check, keep the text as is
                
            string filteredText = text;
            
            //Log($"Checking text '{text}' against {ignorePhrases.Count} ignore phrases");
            
            foreach (var (phrase, exactMatch) in ignorePhrases)
            {
                if (string.IsNullOrEmpty(phrase))
                    continue;
                    
                if (exactMatch)
                {
                    // Check for exact match
                    if (text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        //Log($"Ignoring text due to exact match: '{phrase}'");
                        return (true, string.Empty);
                    }
                }
                else
                {
                    // Remove the phrase from the text
                    string before = filteredText;
                    filteredText = filteredText.Replace(phrase, "", StringComparison.OrdinalIgnoreCase);
                    
                    if (before != filteredText)
                    {
                        //Log($"Applied non-exact match filter: '{phrase}' removed from text");
                    }
                }
            }
            
            // Check if after removing non-exact-match phrases, the text is empty or whitespace
            if (string.IsNullOrWhiteSpace(filteredText))
            {
                Log("Ignoring text because it's empty after filtering");
                return (true, string.Empty);
            }
            
            // Return the filtered text if it changed
            if (filteredText != text)
            {
                //Log($"Text filtered: '{text}' -> '{filteredText}'");
                return (false, filteredText);
            }
            
            return (false, text);
        }
        
        // Display OCR results from JSON - processes character-level blocks
        private async void DisplayOcrResults(JsonElement root)
        {
            try
            {
                // Check for results array
                if (root.TryGetProperty("results", out JsonElement resultsElement) && 
                    resultsElement.ValueKind == JsonValueKind.Array)
                {
                    // Get processing time if available
                    double processingTime = 0;
                    if (root.TryGetProperty("processing_time_seconds", out JsonElement timeElement))
                    {
                        processingTime = timeElement.GetDouble();
                    }
                    
                    // Get minimum text fragment size from config
                    int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();
                    
                    // Check if we should keep old translation visible (per-OCR setting)
                    string currentOcrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                    bool leaveTranslationOnscreen = ConfigManager.Instance.GetLeaveTranslationOnscreen(currentOcrMethod);
                    bool autoTranslateEnabled = MainWindow.Instance.GetTranslateEnabled();
                    bool hasExistingTranslations = _textObjects.Any(t => !string.IsNullOrEmpty(t.TextTranslated));
                    
                    if (leaveTranslationOnscreen && autoTranslateEnabled && hasExistingTranslations)
                    {
                        // Set flag to keep overlay frozen while we wait for new translation
                        _keepingTranslationVisible = true;
                        
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log("Leave translation onscreen: Freezing overlay display until new translation arrives");
                        }
                    }
                    else
                    {
                        _keepingTranslationVisible = false;
                    }
                    
                    // Clear existing text objects before adding new ones
                    ClearAllTextObjects();
                    
                    // Process text blocks that have already been grouped by UniversalBlockDetector
                    int resultCount = resultsElement.GetArrayLength();
                    
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"DisplayOcrResults: Processing {resultCount} text blocks");
                    }
                    
                    // Determine if we should perform color correction now (only if enabled and we have a bitmap)
                    bool performColorCorrection = ConfigManager.Instance.IsCloudOcrColorCorrectionEnabled();
                    System.Drawing.Bitmap? currentBitmap = GetCurrentProcessingBitmap();
                    
                    for (int i = 0; i < resultCount; i++)
                    {
                        JsonElement item = resultsElement[i];
                        
                        if (item.TryGetProperty("text", out JsonElement textElement) && 
                            item.TryGetProperty("confidence", out JsonElement confElement))
                        {
                            // Get the text and ensure it's properly decoded from Unicode
                            string text = textElement.GetString() ?? "";
                            
                            // Skip if text is smaller than minimum fragment size
                            if (text.Length < minTextFragmentSize)
                            {
                                continue;
                            }

                            string textOrientation = "horizontal";
                            if (item.TryGetProperty("text_orientation", out JsonElement textOrientationElement))
                            {
                                textOrientation = textOrientationElement.GetString() ?? "horizontal";
                            }

                            // Note: We no longer need to filter ignore phrases here
                            // as it's now done earlier in ProcessReceivedTextJsonData before hash generation


                            double confidence = 1.0;
                            if (confElement.ValueKind != JsonValueKind.Null)
                            {
                                confidence = confElement.GetDouble();
                            }
                            
                            // Extract bounding box coordinates if available
                            double x = 0, y = 0, width = 0, height = 0;
                            
                            // Check for "rect" or "vertices" property (polygon points format)
                            JsonElement boxElement;
                            bool hasBox = item.TryGetProperty("rect", out boxElement);
                            if (!hasBox)
                            {
                                hasBox = item.TryGetProperty("vertices", out boxElement);
                            }

                            if (hasBox && boxElement.ValueKind == JsonValueKind.Array)
                            {
                                try
                                {
                                    // Format: [[x1,y1], [x2,y2], [x3,y3], [x4,y4]]
                                    // Calculate bounding box from polygon points
                                    double minX = double.MaxValue, minY = double.MaxValue;
                                    double maxX = double.MinValue, maxY = double.MinValue;
                                    
                                    // Iterate through each point
                                    for (int p = 0; p < boxElement.GetArrayLength(); p++)
                                    {
                                        if (boxElement[p].ValueKind == JsonValueKind.Array && 
                                            boxElement[p].GetArrayLength() >= 2)
                                        {
                                            double pointX = boxElement[p][0].GetDouble();
                                            double pointY = boxElement[p][1].GetDouble();
                                            
                                            minX = Math.Min(minX, pointX);
                                            minY = Math.Min(minY, pointY);
                                            maxX = Math.Max(maxX, pointX);
                                            maxY = Math.Max(maxY, pointY);
                                        }
                                    }
                                    
                                    // Set coordinates to the calculated bounding box
                                    x = minX;
                                    y = minY;
                                    width = maxX - minX;
                                    height = maxY - minY;
                                    
                                    // Apply text area size expansion
                                    int expansionWidth = ConfigManager.Instance.GetMonitorTextAreaExpansionWidth();
                                    int expansionHeight = ConfigManager.Instance.GetMonitorTextAreaExpansionHeight();
                                    
                                    // Expand width symmetrically (add expansionWidth/2 to each side)
                                    double expansionWidthHalf = expansionWidth / 2.0;
                                    x = minX - expansionWidthHalf;
                                    width = width + expansionWidth;
                                    
                                    // Expand height symmetrically (add expansionHeight/2 to top and bottom)
                                    double expansionHeightHalf = expansionHeight / 2.0;
                                    y = minY - expansionHeightHalf;
                                    height = height + expansionHeight;
                                }
                                catch (Exception ex)
                                {
                                    Log($"Error parsing rect: {ex.Message}");
                                }
                            }
                         
                            // Extract colors from JSON if available
                            // Use Color? instead of SolidColorBrush? to avoid threading issues
                            Color? foregroundColor = null;
                            Color? backgroundColor = null;
                            
                            if (item.TryGetProperty("foreground_color", out JsonElement foregroundColorElement))
                            {
                                foregroundColor = ParseColorFromJson(foregroundColorElement, isBackground: false);
                            }
                            
                            if (item.TryGetProperty("background_color", out JsonElement backgroundColorElement))
                            {
                                backgroundColor = ParseColorFromJson(backgroundColorElement, isBackground: true);
                            }
                            
                            // Perform delayed color correction for Cloud/Windows OCR if enabled and colors are missing
                            // NOTE: x, y, width, height are already calculated above from the rect/vertices
                            if (performColorCorrection && currentBitmap != null && (foregroundColor == null || backgroundColor == null) && hasBox)
                            {
                                try 
                                {
                                    // Use integer coordinates for cropping
                                    int cropX = Math.Max(0, (int)x);
                                    int cropY = Math.Max(0, (int)y);
                                    int cropW = Math.Min((int)width, currentBitmap.Width - cropX);
                                    int cropH = Math.Min((int)height, currentBitmap.Height - cropY);
                                    
                                    if (cropW > 0 && cropH > 0)
                                    {
                                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                        {
                                            Log($"Color detection for block {i+1}/{resultCount}: '{text.Substring(0, Math.Min(20, text.Length))}...'");
                                        }
                                        
                                        using (var crop = currentBitmap.Clone(new System.Drawing.Rectangle(cropX, cropY, cropW, cropH), currentBitmap.PixelFormat))
                                        {
                                            // We await here - this might slow down rendering of multiple blocks
                                            // but ensures colors are correct before creating the TextObject
                                            // Since this method is 'async void', awaiting is allowed.
                                            var colorInfo = await GetColorAnalysisAsync(crop);
                                            
                                            if (colorInfo.HasValue)
                                            {
                                                if (colorInfo.Value.TryGetProperty("foreground_color", out JsonElement fg)) 
                                                    foregroundColor = ParseColorFromJson(fg, isBackground: false);
                                                if (colorInfo.Value.TryGetProperty("background_color", out JsonElement bg)) 
                                                    backgroundColor = ParseColorFromJson(bg, isBackground: true);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                        Log($"Delayed color correction failed: {ex.Message}");
                                }
                            }
                         
                            // Create text object with bounding box coordinates and colors
                            if (hasBox)
                            {
                                CreateTextObjectAtPosition(text, x, y, width, height, confidence, textOrientation, foregroundColor, backgroundColor);
                            }
                        }
                    }
                    
                    // Clean up the current bitmap clone and stored bitmap as we are done with this OCR cycle
                    if (currentBitmap != null)
                    {
                        try { currentBitmap.Dispose(); } catch { }
                    }
                    
                    lock (_bitmapLock)
                    {
                        if (_currentProcessingBitmap != null)
                        {
                            try { _currentProcessingBitmap.Dispose(); } catch { }
                            _currentProcessingBitmap = null;
                        }
                    }
                    
            // Refresh overlay displays
            // Always refresh both windows - they will use old objects if keeping translation visible
            // The overlay refresh methods will check GetKeepingTranslationVisible() and use appropriate objects
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => 
            {
                MonitorWindow.Instance.RefreshOverlays();
                
                // Main window always refreshes - it will use old objects if keeping translation visible
                // unless in Source mode (Source mode always shows current objects)
                MainWindow.Instance.RefreshMainWindowOverlays();
            }));
                    
                    // Trigger source audio preloading right after OCR results are displayed
                    TriggerSourceAudioPreloading();
                    
                    // Trigger translation if enabled - MUST be done here after all awaits complete
                    // because this method is async void and returns immediately on first await
                    if (_textObjects.Count > 0)
                    {
                        // Build a string with all the detected text
                        StringBuilder detectedText = new StringBuilder();
                        foreach (var textObject in _textObjects)
                        {
                            detectedText.AppendLine(textObject.Text);
                        }
                        
                        // Add to ChatBox with empty translation if translate is disabled
                        string combinedText = detectedText.ToString().Trim();
                        if (!string.IsNullOrEmpty(combinedText))
                        {
                            if (MainWindow.Instance.GetTranslateEnabled())
                            {
                                // If translation is enabled, translate the text
                                if (!GetWaitingForTranslationToFinish())
                                {
                                    //Log($"Translating text: {combinedText}");
                                    // Translate the text objects
                                    _lastChangeTime = DateTime.MinValue;
                                    _ = TranslateTextObjectsAsync();
                                    return;
                                }
                            }
                            else
                            {
                                // Only add to chat history if translation is disabled
                                _lastChangeTime = DateTime.MinValue;
                                MainWindow.Instance.AddTranslationToHistory(combinedText, "");
                                
                                if (ChatBoxWindow.Instance != null)
                                {
                                    ChatBoxWindow.Instance.OnTranslationWasAdded(combinedText, "");
                                }
                            }
                        }
                        
                        OnFinishedThings(true);
                    }
                    else
                    {
                        OnFinishedThings(true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error displaying OCR results: {ex.Message}");
                OnFinishedThings(true);
            }
        }
        
        // Helper method to parse color from JSON element
        // Returns Color? instead of SolidColorBrush? to avoid threading issues
        // Brushes should be created on the UI thread in CreateTextObjectAtPosition
        private Color? ParseColorFromJson(JsonElement colorElement, bool isBackground = false)
        {
            try
            {
                // Try to get RGB array first
                if (colorElement.TryGetProperty("rgb", out JsonElement rgbElement) && 
                    rgbElement.ValueKind == JsonValueKind.Array && 
                    rgbElement.GetArrayLength() >= 3)
                {
                    // Extract RGB values - handle both int and double
                    int r, g, b;
                    if (rgbElement[0].ValueKind == JsonValueKind.Number)
                    {
                        // Try int first, fall back to double if needed
                        if (rgbElement[0].TryGetInt32(out int rInt))
                        {
                            r = rInt;
                        }
                        else
                        {
                            r = (int)Math.Round(rgbElement[0].GetDouble());
                        }
                        
                        if (rgbElement[1].TryGetInt32(out int gInt))
                        {
                            g = gInt;
                        }
                        else
                        {
                            g = (int)Math.Round(rgbElement[1].GetDouble());
                        }
                        
                        if (rgbElement[2].TryGetInt32(out int bInt))
                        {
                            b = bInt;
                        }
                        else
                        {
                            b = (int)Math.Round(rgbElement[2].GetDouble());
                        }
                    }
                    else
                    {
                        Log($"Warning: RGB values are not numbers in color JSON");
                        return null;
                    }
                    
                    // Clamp to 0-255
                    r = Math.Max(0, Math.Min(255, r));
                    g = Math.Max(0, Math.Min(255, g));
                    b = Math.Max(0, Math.Min(255, b));
                    
                    // Use fully opaque (alpha 255) for both foreground and background
                    byte alpha = (byte)255;
                    
                    return Color.FromArgb(alpha, (byte)r, (byte)g, (byte)b);
                }
                
                // Fallback: try hex value if RGB is not available
                if (colorElement.TryGetProperty("hex", out JsonElement hexElement))
                {
                    string hex = hexElement.GetString() ?? "";
                    if (!string.IsNullOrEmpty(hex) && hex.StartsWith("#") && hex.Length == 7)
                    {
                        // Parse hex color (#RRGGBB)
                        int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                        int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                        int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                        
                        // Use fully opaque (alpha 255) for both foreground and background
                        byte alpha = (byte)255;
                        return Color.FromArgb(alpha, (byte)r, (byte)g, (byte)b);
                    }
                }
                
            }
            catch (Exception ex)
            {
                Log($"Error parsing color from JSON: {ex.Message}");
            }
            
            return null; // Return null to indicate fallback to defaults
        }
        
        // Create a text object at the specified position with confidence info
        // Accepts Color? instead of SolidColorBrush? to avoid threading issues
        // Creates brushes on the UI thread
        private void CreateTextObjectAtPosition(string text, double x, double y, double width, double height, double confidence, string textOrientation = "horizontal", Color? foregroundColor = null, Color? backgroundColor = null)
        {

            try
            {
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    Application.Current.Dispatcher.Invoke(() => 
                        CreateTextObjectAtPosition(text, x, y, width, height, confidence, textOrientation, foregroundColor, backgroundColor));
                    return;
                }
                
                using IDisposable profiler = OverlayProfiler.Measure("Logic.CreateTextObjectAtPosition");
                // Store current capture position with the text object
                int captureX = _currentCaptureX;
                int captureY = _currentCaptureY;
                
                // Validate input parameters
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log("Cannot create text object with empty text");
                    return;
                }
                
                // Ensure width and height are valid
                if (double.IsNaN(width) || double.IsInfinity(width) || width < 0)
                {
                    width = 0; // Let the text determine natural width
                }
                
                if (double.IsNaN(height) || double.IsInfinity(height) || height < 0)
                {
                    height = 0; // Let the text determine natural height
                }
                
                // Ensure coordinates are valid
                if (double.IsNaN(x) || double.IsInfinity(x))
                {
                    x = 10; // Default x position
                }
                
                if (double.IsNaN(y) || double.IsInfinity(y))
                {
                    y = 10; // Default y position
                }
                
                // Create default font size based on height
                int fontSize = 16;  // Default
                if (height > 0)
                {
                    // Adjust font size based on the height of the text area
                    // Increased from 0.7 to 0.9 to better match the actual text size
                    fontSize = Math.Max(10, Math.Min(36, (int)(height * 0.9)));
                }
                
                // Create SolidColorBrush objects from Color structs on the UI thread
                // Use provided colors or fall back to defaults (white text on fully opaque black background)
                SolidColorBrush textColor = foregroundColor.HasValue 
                    ? new SolidColorBrush(foregroundColor.Value) 
                    : new SolidColorBrush(Colors.White);
                SolidColorBrush bgColor = backgroundColor.HasValue 
                    ? new SolidColorBrush(backgroundColor.Value) 
                    : new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                
                // Debug: Log final colors being used
                if (ConfigManager.Instance.GetLogExtraDebugStuff() && (foregroundColor.HasValue || backgroundColor.HasValue))
                {
                    Log($"Creating TextObject '{text.Substring(0, Math.Min(20, text.Length))}...' - TextColor: {textColor.Color}, BackgroundColor: {bgColor.Color}");
                }
                
                // Add the text object to the UI
                Stopwatch textObjectCtorStopwatch = Stopwatch.StartNew();
                TextObject textObject = new TextObject(
                    text,  // Just the text, without confidence
                    x, y, width, height,
                    textColor,
                    bgColor,
                    captureX, captureY,  // Store original capture coordinates
                    textOrientation
                );
                textObjectCtorStopwatch.Stop();
                OverlayProfiler.Record("Logic.TextObjectConstructor", textObjectCtorStopwatch.ElapsedMilliseconds);
                textObject.ID = "text_"+GetNextTextID();

                // Adjust font size
                Stopwatch fontAdjustStopwatch = Stopwatch.StartNew();
                textObject.SetFontSize(fontSize);
                fontAdjustStopwatch.Stop();
                OverlayProfiler.Record("Logic.TextObjectSetFontSize", fontAdjustStopwatch.ElapsedMilliseconds);
                
                // Add to our collection
                _textObjects.Add(textObject);
                
                // Raise event to notify listeners (MonitorWindow)
                TextObjectAdded?.Invoke(this, textObject);

                // Notify MonitorWindow to update overlay
                MonitorWindow.Instance.CreateMonitorOverlayFromTextObject(this, textObject);

                // Log($"Added text '{text}' at position ({x}, {y}) with size {width}x{height}");
            }
            catch (Exception ex)
            {
                Log($"Error creating text object: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Log($"Stack trace: {ex.StackTrace}");
                }
            }
        }
        
        
        // Set the current capture position
        public void SetCurrentCapturePosition(int x, int y)
        {
            _currentCaptureX = x;
            _currentCaptureY = y;
        }
        
        // Update text object positions based on capture position changes
        public void UpdateTextObjectPositions(int offsetX, int offsetY)
        {
            try
            {
                // Only proceed if we have text objects
                if (_textObjects.Count == 0) return;
                
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure UI updates are thread-safe
                    Application.Current.Dispatcher.Invoke(() => 
                        UpdateTextObjectPositions(offsetX, offsetY));
                    return;
                }
                
                foreach (TextObject textObj in _textObjects)
                {
                    // Calculate new position based on original capture position and current offset
                    // Use negative offset since we want text to move in opposite direction of the window
                    double newX = textObj.X - offsetX;
                    double newY = textObj.Y - offsetY;
                    
                    // Update position
                    textObj.X = newX;
                    textObj.Y = newY;
                    
                    // Update UI element
                    textObj.UpdateUIElement();
                }
                
                // Refresh the monitor window to show updated positions
                if (MonitorWindow.Instance.IsVisible)
                {
                    MonitorWindow.Instance.RefreshOverlays();
                }
                MainWindow.Instance.RefreshMainWindowOverlays();
            }
            catch (Exception ex)
            {
                Log($"Error updating text positions: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Filters out low-confidence text objects from the OCR results.
        /// Different OCR providers return different granularities:
        /// - Line-level (PaddleOCR, EasyOCR): Use line confidence threshold
        /// - Word-level (Windows OCR, Google Vision, docTR): Use letter/word confidence threshold
        /// - Block-level (MangaOCR): No filtering (confidence is null)
        /// </summary>
        private JsonElement FilterLowConfidenceCharacters(JsonElement resultsElement, string ocrProvider = "")
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;
                
            try
            {
                // Get minimum confidence threshold based on OCR provider granularity
                double minConfidence;
                if (string.Equals(ocrProvider, "PaddleOCR", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ocrProvider, "EasyOCR", StringComparison.OrdinalIgnoreCase))
                {
                    // These providers return line-level text objects
                    minConfidence = ConfigManager.Instance.GetMinLineConfidence(ocrProvider);
                }
                else
                {
                    // Windows OCR, Google Vision, docTR return word-level text objects
                    // Use letter confidence threshold (which is also applied to words)
                    minConfidence = ConfigManager.Instance.GetMinLetterConfidence(ocrProvider);
                }
                
                // Create output array for high-confidence results only
                using (var ms = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();
                        
                        // Process each element
                        for (int i = 0; i < resultsElement.GetArrayLength(); i++)
                        {
                            var item = resultsElement[i];
                            
                            // Skip items that don't have required properties
                            if (!item.TryGetProperty("confidence", out var confElement))
                            {
                                // Include items without confidence values (might be non-character elements)
                                item.WriteTo(writer);
                                continue;
                            }
                            
                            // Handle null confidence (docTR returns null for character-level)
                            if (confElement.ValueKind == JsonValueKind.Null)
                            {
                                // Include items with null confidence
                                item.WriteTo(writer);
                                continue;
                            }
                            
                            // Get confidence value
                            double confidence = confElement.GetDouble();
                            
                            // Normalize 0-100 range to 0-1
                            if (confidence > 1.0)
                            {
                                confidence /= 100.0;
                            }
                            
                            // Only include elements with confidence above threshold
                            if (confidence >= minConfidence)
                            {
                                item.WriteTo(writer);
                            }
                        }
                        
                        writer.WriteEndArray();
                        writer.Flush();
                        
                        // Read the filtered JSON back
                        ms.Position = 0;
                        using (JsonDocument doc = JsonDocument.Parse(ms))
                        {
                            return doc.RootElement.Clone();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error filtering low-confidence characters: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }

        static readonly HashSet<char> g_charsToStripFromHash =
             new(" \n\r,.-:;ー・…。、~』!^へ·･");


        private string GenerateContentHash(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return string.Empty;


         
            StringBuilder contentBuilder = new();

            // Add the length to the hash to detect array size changes

            foreach (JsonElement element in resultsElement.EnumerateArray())
            {
                if (!element.TryGetProperty("text", out JsonElement textElement))
                {
                    continue;
                }

                foreach (char c in textElement.GetString() ?? string.Empty)
                {
                    //replace ツ with ッ because OCR confuses them but the LLM won't
                    if (c == 'ツ')
                    {
                        contentBuilder.Append('ッ');
                    }
                    else
                    {

                        if (!g_charsToStripFromHash.Contains(c))
                        {
                            contentBuilder.Append(c);
                        }
                    }
                }
            }

            string hash = contentBuilder.ToString();
            //Log($"Generated hash: {hash}");
            return hash;
        }
       
        // Store the current bitmap being processed for delayed color analysis
        private System.Drawing.Bitmap? _currentProcessingBitmap;
        private readonly object _bitmapLock = new object();

        private void SetCurrentProcessingBitmap(System.Drawing.Bitmap bitmap)
        {
            lock (_bitmapLock)
            {
                // Dispose previous if exists
                if (_currentProcessingBitmap != null)
                {
                    try { _currentProcessingBitmap.Dispose(); } catch { }
                }
                
                // Clone the new one
                try 
                {
                    _currentProcessingBitmap = (System.Drawing.Bitmap)bitmap.Clone();
                }
                catch (Exception ex)
                {
                    Log($"Failed to clone current processing bitmap: {ex.Message}");
                    _currentProcessingBitmap = null;
                }
            }
        }
        
        private System.Drawing.Bitmap? GetCurrentProcessingBitmap()
        {
            lock (_bitmapLock)
            {
                if (_currentProcessingBitmap == null) return null;
                try 
                {
                    return (System.Drawing.Bitmap)_currentProcessingBitmap.Clone();
                }
                catch 
                {
                    return null;
                }
            }
        }
        
        private void ClearCurrentProcessingBitmap()
        {
            lock (_bitmapLock)
            {
                if (_currentProcessingBitmap != null)
                {
                    try { _currentProcessingBitmap.Dispose(); } catch { }
                    _currentProcessingBitmap = null;
                }
            }
        }

        // Process bitmap directly with Windows OCR (no file saving)
        public async void ProcessWithWindowsOCR(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            // Create a clone of the bitmap immediately to avoid race conditions with the caller disposing it
            System.Drawing.Bitmap? bitmapClone = null;
            
            try
            {
                 try
                {
                    bitmapClone = (System.Drawing.Bitmap)bitmap.Clone();
                    // Store bitmap for color analysis - will be cleared if hash matches and we don't display
                    SetCurrentProcessingBitmap(bitmapClone);
                }
                catch (Exception ex)
                {
                    Log($"Failed to clone bitmap for Windows OCR: {ex.Message}");
                    return;
                }
            
                //Log("Starting Windows OCR processing directly from bitmap...");
                
                try
                {
                    // Get the text lines from Windows OCR directly from the bitmap
                    long currentSessionId = _overlaySessionId;
                    var textLines = await WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmapClone, sourceLanguage);
                    
                    // Check if session is still valid
                    if (currentSessionId != _overlaySessionId)
                    {
                        Log($"Ignoring stale Windows OCR result (Session ID mismatch: {currentSessionId} vs {_overlaySessionId})");
                        // Clear stored bitmap since we're not processing this result
                        ClearCurrentProcessingBitmap();
                        return;
                    }

                    // Process the OCR results with language code
                    await WindowsOCRManager.Instance.ProcessWindowsOcrResults(textLines, bitmapClone, sourceLanguage);
                    // Note: bitmapClone will be disposed in DisplayOcrResults after color analysis, or cleared if hash matches
                }
                catch (Exception ex)
                {
                    Log($"Windows OCR error: {ex.Message}");
                    Log($"Stack trace: {ex.StackTrace}");
                    // Clear stored bitmap on error
                    ClearCurrentProcessingBitmap();
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing bitmap with Windows OCR: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                // Clear stored bitmap on error
                ClearCurrentProcessingBitmap();
            }
            finally
            {
                // Don't dispose bitmapClone here - it will be disposed in DisplayOcrResults after color analysis
                // or cleared if hash matches and we don't display results
                // The caller handles disposal of the original bitmap parameter

                // Check if we should pause OCR while translating
                bool pauseOcrWhileTranslating = ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled();
                bool waitingForTranslation = GetWaitingForTranslationToFinish();
                
                // Only re-enable OCR if we're not waiting for translation, or if pause setting is disabled
                if (!waitingForTranslation || !pauseOcrWhileTranslating)
                {
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                }
                else
                {
                    Log("Windows OCR: Pause OCR while translating is enabled - keeping OCR paused until translation finishes");
                }
                
                // Notify that OCR has completed
                NotifyOCRCompleted();
            }
        }
        
        // Process bitmap directly with Google Vision API (no file saving)
        public async void ProcessWithGoogleVision(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            // Create a clone of the bitmap to use for the duration of this async method
            // because the original bitmap might be disposed by the caller
            System.Drawing.Bitmap? bitmapClone = null;
            
            try
            {
                try
                {
                    bitmapClone = (System.Drawing.Bitmap)bitmap.Clone();
                    // Store bitmap for color analysis - will be cleared if hash matches and we don't display
                    SetCurrentProcessingBitmap(bitmapClone);
                }
                catch (Exception ex)
                {
                    Log($"Failed to clone bitmap for Google Vision: {ex.Message}");
                    return;
                }
            
                Log("Starting Google Vision OCR processing...");
                
                try
                {
                    // Get the text objects from Google Vision API
                    long currentSessionId = _overlaySessionId;
                    
                    // We pass the clone for OCR
                    var textObjects = await GoogleVisionOCRService.Instance.ProcessImageAsync(bitmapClone, sourceLanguage);
                    
                    // Check if session is still valid
                    if (currentSessionId != _overlaySessionId)
                    {
                        Log($"Ignoring stale Google Vision OCR result (Session ID mismatch: {currentSessionId} vs {_overlaySessionId})");
                        // Clear stored bitmap since we're not processing this result
                        ClearCurrentProcessingBitmap();
                        return;
                    }

                    Log($"Google Vision OCR found {textObjects.Count} text objects");
                    
                    // Process the OCR results
                    // IMPORTANT: Pass the CLONE for color analysis
                    await GoogleVisionOCRService.Instance.ProcessGoogleVisionResults(textObjects, bitmapClone);
                    // Note: bitmapClone will be disposed in DisplayOcrResults after color analysis, or cleared if hash matches
                }
                catch (Exception ex)
                {
                    Log($"Google Vision OCR error: {ex.Message}");
                    Log($"Stack trace: {ex.StackTrace}");
                    
                    // Clear stored bitmap on error
                    ClearCurrentProcessingBitmap();
                    
                    // Show error to user if API key might be missing
                    if (ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show("Google Cloud Vision API key not configured. Please set your API key in Settings.", 
                            "Google Cloud Vision Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing bitmap with Google Vision: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                // Clear stored bitmap on error
                ClearCurrentProcessingBitmap();
            }
            finally
            {
                // Don't dispose bitmapClone here - it will be disposed in DisplayOcrResults after color analysis
                // or cleared if hash matches and we don't display results
                // The caller handles disposal of the original bitmap parameter
                
                // Check if we should pause OCR while translating
                bool pauseOcrWhileTranslating = ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled();
                bool waitingForTranslation = GetWaitingForTranslationToFinish();
                
                // Only re-enable OCR if we're not waiting for translation, or if pause setting is disabled
                if (!waitingForTranslation || !pauseOcrWhileTranslating)
                {
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                }
                else
                {
                    Log("Google Vision: Pause OCR while translating is enabled - keeping OCR paused until translation finishes");
                }
                
                // Notify that OCR has completed
                NotifyOCRCompleted();
            }
        }
        
     
        
        /// <summary>
        /// Process image using HTTP Python service
        /// </summary>
        private async Task<string?> ProcessImageWithHttpServiceAsync(byte[] imageBytes, string serviceName, string language)
        {
            try
            {
                var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
                
                if (service == null)
                {
                    Log($"Service {serviceName} not found");
                    return null;
                }
                
                // Only check if service is running if not already marked as running
                // This avoids the /info endpoint check on every OCR request
                if (!service.IsRunning)
                {
                    bool isRunning = await service.CheckIsRunningAsync();
                    
                    if (!isRunning)
                    {
                        Log($"Service {serviceName} is not running");
                        
                        // Show error dialog offering to start the service (if not already showing)
                        bool openManager = ErrorPopupManager.ShowServiceWarning(
                            $"The {serviceName} service is not running.\n\nWould you like to open the GPU Service Console to start it?",
                            "Service Not Available");
                        
                        if (openManager)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ServerSetupDialog.ShowDialogSafe(fromSettings: true);
                            });
                        }
                        
                        return null;
                    }
                }
                
                // Build query parameters
                string langParam = MapLanguageForService(language);
                // Note: OCR Processing Mode (char_level) removed; all services now default to natural units (Lines/Words).
                // UniversalBlockDetector handles the rest.
                
                string url = $"{service.ServerUrl}:{service.Port}/process?lang={langParam}";
                
                // Add MangaOCR-specific parameters
                if (serviceName == "MangaOCR")
                {
                    int minWidth = ConfigManager.Instance.GetMangaOcrMinRegionWidth();
                    int minHeight = ConfigManager.Instance.GetMangaOcrMinRegionHeight();
                    double overlapPercent = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent();
                    double yoloConfidence = ConfigManager.Instance.GetMangaOcrYoloConfidence();
                    url += $"&min_region_width={minWidth}&min_region_height={minHeight}&overlap_allowed_percent={overlapPercent}&yolo_confidence={yoloConfidence}";
                }
                
                // Add PaddleOCR-specific parameters
                if (serviceName == "PaddleOCR")
                {
                    bool useAngleCls = ConfigManager.Instance.GetPaddleOcrUseAngleCls();
                    url += $"&use_angle_cls={useAngleCls}";
                }
                
                // Send HTTP request with keep-alive
                var content = new ByteArrayContent(imageBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                request.Headers.ConnectionClose = false;
                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    Log($"HTTP request failed: {response.StatusCode}");
                    service.MarkAsNotRunning();
                    return null;
                }
                
                // Get JSON response and return it directly
                string jsonResponse = await response.Content.ReadAsStringAsync();
                
                // Quick validation that we got valid JSON with expected structure
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        var root = doc.RootElement;
                        
                        if (!root.TryGetProperty("status", out var statusProp))
                        {
                            Log($"{serviceName}: Response missing 'status' property");
                            return null;
                        }
                        
                        string status = statusProp.GetString() ?? "unknown";
                        
                        if (status == "success")
                        {
                            // Check for texts property
                            if (root.TryGetProperty("texts", out var textsArray))
                            {
                                int count = textsArray.GetArrayLength();
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Log($"Received {count} text objects from {serviceName}");
                                }
                            }
                            else
                            {
                                Log($"{serviceName}: Response missing 'texts' property");
                                return null;
                            }
                        }
                        else if (status == "error")
                        {
                            string errorMsg = root.TryGetProperty("message", out var msgElement) 
                                ? msgElement.GetString() ?? "Unknown error" 
                                : "Unknown error";
                            Log($"{serviceName} returned error: {errorMsg}");
                            return null;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Log($"{serviceName} returned invalid JSON: {ex.Message}");
                    return null;
                }
                
                // Return the JSON response to be processed by ProcessReceivedTextJsonData
                return jsonResponse;
            }
            catch (Exception ex)
            {
                Log($"Error processing image with HTTP service: {ex.Message}");
                
                // Mark service as not running if we get a connection error
                var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
                if (service != null)
                {
                    service.MarkAsNotRunning();
                }
                
                return null;
            }
        }
        
        /// <summary>
        /// Maps internal language codes to service-specific language codes
        /// </summary>
        private string MapLanguageForService(string language)
        {
            // Map common language codes
            return language switch
            {
                "en" => "en",
                "ja" => "japan",
                "zh" => "ch_sim",
                "ko" => "korean",
                "vi" => "vi",
                "th" => "th",
                _ => language
            };
        }
        
        private static bool _hasWarnedAboutEasyOCRForColor = false;

        /// <summary>
        /// Get color analysis for a specific image crop using the EasyOCR service
        /// </summary>
        public async Task<JsonElement?> GetColorAnalysisAsync(System.Drawing.Bitmap bitmap)
        {
            try
            {
                // Skip color analysis if not enabled or hash is unstable
                // We only want to do this expensive operation when we are about to display the text
                // But wait, GetColorAnalysisAsync is called from the OCR managers (Windows/Google)
                // BEFORE we know the hash of the full result set.
                
                // However, the user asked to optimize this:
                // "Google cloud vision color detection works, but it seems to be detecting the colors after each OCR, 
                // it should only do that if the hash has changed and we're going to actually put the OCR we did on the screen"
                
                // This is tricky because the current architecture passes the color data AS PART OF the OCR result structure.
                // And the hash is generated FROM the result structure (which includes color data if present).
                
                // If we strip color data from hash generation, then we can generate hash first.
                // But we generate hash in Logic.ProcessReceivedTextJsonData, which receives the ALREADY PROCESSED JSON.
                // The Windows/Google managers build this JSON.
                
                // OPTION:
                // 1. Windows/Google managers return raw OCR data (without color).
                // 2. Logic.ProcessReceivedTextJsonData generates hash.
                // 3. If hash is new/stable, Logic asks to enrich the data with color?
                //    But we've lost the original bitmap reference by then inside the JSON flow?
                //    No, we haven't. But Logic.ProcessReceivedTextJsonData takes a string JSON.
                
                // ALTERNATIVE (Simpler but less clean architecture):
                // Since we want to update the text objects ON THE FLY or after settling.
                // The TextObject class has the text and coordinates.
                // We can perform color detection AFTER creating the TextObjects in DisplayOcrResults.
                // But DisplayOcrResults is called after hash checks.
                
                // So, we should:
                // 1. Disable color detection in WindowsOCRManager and GoogleVisionOCRService initial pass.
                // 2. Let Logic.ProcessReceivedTextJsonData proceed, generate hash, check for settling.
                // 3. Inside DisplayOcrResults (which is called when content is stable/new), we perform color detection.
                //    BUT, DisplayOcrResults doesn't have the bitmap!
                
                // We need to store the latest bitmap temporarily in Logic?
                // We have `_lastOcrBitmap`? No.
                
                // Let's store the bitmap in a member variable in Logic when we start processing.
                // `ProcessWithWindowsOCR` and `ProcessWithGoogleVision` have the bitmap.
                // We can store it in `_currentProcessingBitmap`.
                
                // Then in `DisplayOcrResults`, we can use `_currentProcessingBitmap` to extract colors.
                
                // Let's use the cloned bitmap.
                
                // Use EasyOCR service for color analysis
                var service = PythonServicesManager.Instance.GetServiceByName("EasyOCR");
                if (service == null)
                {
                    return null;
                }

                // Check if running, warn once if not
                if (!service.IsRunning)
                {
                    if (!_hasWarnedAboutEasyOCRForColor)
                    {
                        _hasWarnedAboutEasyOCRForColor = true;
                        
                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            ErrorPopupManager.ShowServiceWarning(
                                "The EasyOCR service is required for color correction but is not running.\n\nPlease start it in the GPU Service Console, or disable 'Use EasyOCR service for detecting colors' in Settings.",
                                "Color Correction Service Missing");
                        });
                    }
                    return null;
                }

                // Convert bitmap to bytes
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }

                string url = $"{service.ServerUrl}:{service.Port}/analyze_color";

                var content = new ByteArrayContent(imageBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        if (doc.RootElement.TryGetProperty("color_info", out var colorInfo) && colorInfo.ValueKind != JsonValueKind.Null)
                        {
                            return colorInfo.Clone();
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log($"Color analysis failed: {ex.Message}");
                }
                return null;
            }
        }

        // Called when a screenshot is captured (sends directly to HTTP service)
        public async void SendImageToHttpOCR(System.Drawing.Bitmap bitmap)
        {
            // Clone the bitmap immediately to avoid race conditions
            System.Drawing.Bitmap? bitmapClone = null;
            byte[] imageBytes;
            
            try
            {
                // Clone and store for color analysis
                bitmapClone = (System.Drawing.Bitmap)bitmap.Clone();
                SetCurrentProcessingBitmap(bitmapClone);
                
                // Convert to bytes for HTTP request
                using (var ms = new MemoryStream())
                {
                    bitmapClone.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to prepare bitmap for HTTP OCR: {ex.Message}");
                MainWindow.Instance.SetOCRCheckIsWanted(true);
                return;
            }

            try
            {
                // Check if we should pause OCR while translating
                bool pauseOcrWhileTranslating = ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled();
                bool waitingForTranslation = GetWaitingForTranslationToFinish();
                
                if (pauseOcrWhileTranslating && waitingForTranslation)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log("Pause OCR while translating is enabled - skipping HTTP OCR request");
                    }
                    // Re-enable OCR check so it can be triggered again after translation finishes
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    return;
                }
                
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                
                if (ocrMethod == "Windows OCR" || ocrMethod == "Google Vision")
                {
                    // These are handled in MainWindow.PerformCapture() directly
                    Log($"SendImageToHttpOCR called for {ocrMethod} - should not happen");
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    return;
                }
                else if (ocrMethod == "EasyOCR" || ocrMethod == "MangaOCR" || ocrMethod == "PaddleOCR" || string.Equals(ocrMethod, "docTR", StringComparison.OrdinalIgnoreCase))
                {
                    // Get source language
                    string sourceLanguage = GetSourceLanguage()!;
                    
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Processing {imageBytes.Length} bytes with {ocrMethod} HTTP service, language: {sourceLanguage}");
                    }
                    
                    // Process with HTTP service - returns JSON directly
                    long currentSessionId = _overlaySessionId;
                    var jsonResponse = await ProcessImageWithHttpServiceAsync(imageBytes, ocrMethod, sourceLanguage);
                    
                    // Check if session is still valid
                    if (currentSessionId != _overlaySessionId)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log($"Ignoring stale OCR result from {ocrMethod} (Session ID mismatch: {currentSessionId} vs {_overlaySessionId})");
                        }
                        ClearCurrentProcessingBitmap();
                        return;
                    }

                    if (jsonResponse != null)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log($"=== JSON Response from {ocrMethod} (first 1000 chars) ===");
                            Log(jsonResponse.Substring(0, Math.Min(1000, jsonResponse.Length)));
                            Log("=== End JSON Response ===");
                        }
                        
                        // Pass the JSON response directly to ProcessReceivedTextJsonData
                        // The Python service returns {"status": "success", "texts": [...]}
                        // and ProcessReceivedTextJsonData now handles both "texts" and "results"
                        ProcessReceivedTextJsonData(jsonResponse);
                    }
                    else
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log($"No valid response received from {ocrMethod} service");
                        }
                        ClearCurrentProcessingBitmap();
                    }
                    
                    // Re-enable OCR check
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    
                    // Notify that OCR has completed
                    NotifyOCRCompleted();
                }
                else
                {
                    Log($"Unknown OCR method: {ocrMethod}");
                    // Disable OCR to prevent loop
                    MainWindow.Instance.SetOCRCheckIsWanted(false);
                    ClearCurrentProcessingBitmap();

                    // Show error popup
                    ErrorPopupManager.ShowError(
                        $"Unknown OCR method: '{ocrMethod}'.\n\nPlease select a valid OCR method in the Settings or Monitor window.",
                        "Invalid OCR Method"
                    );
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing screenshot: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                ClearCurrentProcessingBitmap();
                
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
        }

        // Called when the application is closing
        public async Task Finish()
        {
            try
            {
                // Clean up resources
                Log("Logic finalized");
                
                // Cancel any in-progress audio preloading
                AudioPreloadService.Instance.CancelAllPreloads();
                
                // Stop any currently playing audio
                AudioPlaybackManager.Instance.StopCurrentPlayback();
                
                // Note: Audio cache is NOT deleted on shutdown to allow reuse between sessions
                // Cache can be deleted on startup if the setting is enabled
                
                // Stop owned Python services
                await PythonServicesManager.Instance.StopOwnedServicesAsync();
                
                // Clear all text objects
                ClearAllTextObjects();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during cleanup: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
       
        
        // Add a text object with specific text and optional position and styling
        public void AddTextObject(string text, 
                                 double x = 0, 
                                 double y = 0, 
                                 double width = 0, 
                                 double height = 0,
                                 SolidColorBrush? textColor = null,
                                 SolidColorBrush? backgroundColor = null)
        {
            try
            {
                // Validate text
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log("Cannot add text object with empty text");
                    return;
                }
                
                // Ensure position and dimensions are valid
                if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
                if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
                if (double.IsNaN(width) || double.IsInfinity(width) || width < 0) width = 0;
                if (double.IsNaN(height) || double.IsInfinity(height) || height < 0) height = 0;
               
                backgroundColor ??= new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)); // Half-transparent black
                
                // Create the text object with specified parameters
                TextObject textObject = new TextObject(
                    text, 
                    x, y, 
                    width, height, 
                    textColor, 
                    backgroundColor);
                
                // Add to our collection
                _textObjects.Add(textObject);
                
                // Don't add to main window UI anymore
                // Just raise the event to notify MonitorWindow
                TextObjectAdded?.Invoke(this, textObject);
                
                Log($"Added text '{text}' at position {x}, {y}");
            }
            catch (Exception ex)
            {
                Log($"Error adding text: {ex.Message}");
            }
        }
        
     
        // Clear all text objects
        public void ClearAllTextObjects()
        {
            try
            {
                // Increment session ID to invalidate any pending OCR requests
                _overlaySessionId++;

                // Cancel any in-progress audio preloading
                AudioPreloadService.Instance.CancelAllPreloads();
                
                // Stop any currently playing audio
                AudioPlaybackManager.Instance.StopCurrentPlayback();
                
                // Reset auto-play trigger flag to allow auto-play on next OCR
                AudioPlaybackManager.Instance.ResetAutoPlayTrigger();
                
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread asynchronously to avoid blocking
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => ClearAllTextObjects()), DispatcherPriority.Send);
                    return;
                }
                
                // If keeping translation visible, only clear the internal list but NOT the visual overlays
                if (_keepingTranslationVisible)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log("Leave translation onscreen: Clearing text objects but keeping overlays visible");
                    }
                    
                    // Save a copy of current text objects to display while waiting for new translation
                    _textObjectsOld.Clear();
                    foreach (TextObject textObject in _textObjects)
                    {
                        // Create a shallow copy - we'll keep the original objects alive for display
                        // Don't dispose them yet - they'll be disposed when new translation arrives
                        _textObjectsOld.Add(textObject);
                    }
                    
                    // Clear the collection (but old objects are still in _textObjectsOld for display)
                    _textObjects.Clear();
                    _textIDCounter = 0;
                }
                else
                {
                    // Normal clearing - remove both text objects and visual overlays
                    foreach (TextObject textObject in _textObjects)
                    {
                        MonitorWindow.Instance?.RemoveOverlay(textObject);
                        textObject.Dispose();
                    }

                    MonitorWindow.Instance?.ClearOverlays();
                    MainWindow.Instance?.RefreshMainWindowOverlays();

                    // Clear the collection
                    _textObjects.Clear();
                    _textIDCounter = 0;
                    
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"All text objects cleared");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error clearing text objects: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Log($"Stack trace: {ex.StackTrace}");
                }
            }
        }
        
        // Trigger source audio preloading
        private void TriggerSourceAudioPreloading()
        {
            try
            {
                // Check if preloading is enabled
                if (!ConfigManager.Instance.IsTtsPreloadEnabled())
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log("Logic: Source audio preloading skipped (feature disabled)");
                    }
                    return;
                }
                
                string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log($"Logic: TriggerSourceAudioPreloading called, preloadMode={preloadMode}");
                }
                
                if (preloadMode == "Source language" || preloadMode == "Both source and target languages")
                {
                    var textObjects = _textObjects.ToList();
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Logic: Found {textObjects.Count} text objects for source audio preloading");
                    }
                    
                    if (textObjects.Count > 0)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log("Logic: Starting source audio preload...");
                        }
                        _ = AudioPreloadService.Instance.PreloadSourceAudioAsync(textObjects);
                    }
                    else
                    {
                        Log("Logic: No text objects to preload source audio");
                    }
                }
                else
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Logic: Source audio preloading skipped (preloadMode={preloadMode})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error triggering source audio preloading: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
        }
        
        // Trigger target audio preloading
        private void TriggerTargetAudioPreloading()
        {
            try
            {
                // Check if preloading is enabled
                if (!ConfigManager.Instance.IsTtsPreloadEnabled())
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log("Logic: Target audio preloading skipped (feature disabled)");
                    }
                    return;
                }
                
                string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log($"Logic: TriggerTargetAudioPreloading called, preloadMode={preloadMode}");
                }
                
                if (preloadMode == "Target language" || preloadMode == "Both source and target languages")
                {
                    var textObjects = _textObjects.ToList();
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Logic: Found {textObjects.Count} text objects for target audio preloading");
                    }
                    
                    if (textObjects.Count > 0)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log("Logic: Starting target audio preload...");
                        }
                        _ = AudioPreloadService.Instance.PreloadTargetAudioAsync(textObjects);
                    }
                    else
                    {
                        Log("Logic: No text objects to preload target audio");
                    }
                }
                else
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Logic: Target audio preloading skipped (preloadMode={preloadMode})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error triggering target audio preloading: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
        }

        //! Process structured JSON translation from ChatGPT or other services
        private void ProcessStructuredJsonTranslation(JsonElement translatedRoot)
        {
  
            try
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log("Processing structured JSON translation");
                }
                
                // If we were keeping translation visible, we don't need to explicitly clear old overlays
                // because RefreshOverlays will replace them with the new translation shortly.
                // This prevents a "blank frame" blink.
                if (_keepingTranslationVisible)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log("Leave translation onscreen: Resetting flag, new translation ready");
                    }
                    
                    // Dispose old text objects that were kept visible
                    foreach (TextObject textObject in _textObjectsOld)
                    {
                        textObject.Dispose();
                    }
                    _textObjectsOld.Clear();
                    _keepingTranslationVisible = false;
                }
                
                // Check if we have text_blocks array in the translated JSON
                if (translatedRoot.TryGetProperty("text_blocks", out JsonElement textBlocksElement) &&
                    textBlocksElement.ValueKind == JsonValueKind.Array)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Found {textBlocksElement.GetArrayLength()} text blocks in translated JSON");
                    }
                    
                    // Process each translated block
                    for (int i = 0; i < textBlocksElement.GetArrayLength(); i++)
                    {
                        var block = textBlocksElement[i];
                        
                        if (!block.TryGetProperty("id", out JsonElement idElement))
                        {
                            Log($"ERROR: Text block at index {i} is missing 'id' field");
                            continue;
                        }
                        
                        string blockId = idElement.GetString() ?? "";
                        
                        if (!block.TryGetProperty("text", out JsonElement translatedTextElement))
                        {
                            Log($"ERROR: Text block '{blockId}' is missing 'text' field. Block content: {block.ToString()}");
                            
                            // Check if LLM used wrong field names
                            if (block.TryGetProperty("translated_text", out _))
                            {
                                Log($"ERROR: Text block '{blockId}' has 'translated_text' field instead of 'text'. The LLM is not following the prompt correctly.");
                            }
                            else if (block.TryGetProperty("english_text", out _) || 
                                     block.TryGetProperty("japanese_text", out _) ||
                                     block.TryGetProperty("chinese_text", out _))
                            {
                                Log($"ERROR: Text block '{blockId}' has language-specific field (like 'english_text') instead of 'text'. The LLM is not following the prompt correctly.");
                            }
                            continue;
                        }
                        
                        string id = blockId;
                        string translatedText = translatedTextElement.GetString() ?? "";
                        
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                        {
                            // Find the matching text object by ID
                            var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                            if (matchingTextObj != null)
                            {
                                // Update the corresponding text object
                                matchingTextObj.TextTranslated = translatedText;

                                // Don't modify the original text orientation - it should remain as detected by OCR

                                matchingTextObj.UpdateUIElement();
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Log($"Updated text object {id} with translation");
                                }
                            }
                            else if (id.StartsWith("text_"))
                            {
                                // Try to extract index from ID (text_X format)
                                string indexStr = id.Substring(5); // Remove "text_" prefix
                                if (int.TryParse(indexStr, out int index) && index >= 0 && index < _textObjects.Count)
                                {
                                    // Update by index if ID matches format
                                    _textObjects[index].TextTranslated = translatedText;
                                    _textObjects[index].UpdateUIElement();
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"Updated text object at index {index} with translation");
                                    }
                                }
                                else
                                {
                                    Log($"Could not find text object with ID {id}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Log("No text_blocks array found in translated JSON");
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing structured JSON translation: {ex.Message}");
            }




            // Update overlays
            MonitorWindow.Instance.RefreshOverlays();
            MainWindow.Instance.RefreshMainWindowOverlays();
            
            // Trigger target audio preloading if enabled
            TriggerTargetAudioPreloading();
            
            // Sort text objects by Y coordinate
            var sortedTextObjects = _textObjects.OrderBy(t => t.Y).ToList();
            // Add each translated text to the ChatBox
            foreach (var textObject in sortedTextObjects)
            {
                string originalText = textObject.Text;
                string translatedText = textObject.TextTranslated; // Assuming translation is done in-place

                // Only add to chatbox if we have both texts and translation is not empty
                if (!string.IsNullOrEmpty(originalText) && !string.IsNullOrEmpty(translatedText))
                {
                    //Log($"Adding to chatbox: Original: '{originalText}', Translated: '{translatedText}'");
                    // Add to TranslationCompleted, this will add it to the chatbox also
                    TranslationCompleted?.Invoke(this, new TranslationEventArgs
                    {
                        OriginalText = originalText,
                        TranslatedText = translatedText
                    });
                }
                else
                {
                    Log($"Skipping empty translation - Original: '{originalText}', Translated: '{translatedText}'");
                }

            }

        }
        
        //!Process the finished translation into text blocks and the chatbox
        void ProcessTranslatedJSON(string translationResponse)
        {
            try
            {
                // Check the service we're using to determine the format
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                //Log($"Processing translation response from {currentService} service");
                
                // Log full response for debugging
                //Log($"Raw translationResponse: {translationResponse}");
                
                // Parse the translation response
                using JsonDocument doc = JsonDocument.Parse(translationResponse);
                JsonElement textToProcess;
                
                // Different services have different response formats
                if (currentService == "ChatGPT" || currentService == "llama.cpp")
                {
                    // ChatGPT/llama.cpp format: {"translated_text": "...", "original_text": "...", "detected_language": "..."}
                    if (doc.RootElement.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                    {
                        string translatedTextJson = translatedTextElement.GetString() ?? "";
                        // Debug line removed - too slow for logs
                        // Log($"{currentService} translated_text: {translatedTextJson}");
                        
                        // If the translated_text is a JSON string, parse it
                        if (!string.IsNullOrEmpty(translatedTextJson) && 
                            translatedTextJson.StartsWith("{") && 
                            translatedTextJson.EndsWith("}"))
                        {
                            try
                            {
                                // Create options to handle escaped characters properly
                                var options = new JsonDocumentOptions
                                {
                                    AllowTrailingCommas = true,
                                    CommentHandling = JsonCommentHandling.Skip
                                };
                                
                                // Parse the inner JSON
                                using JsonDocument innerDoc = JsonDocument.Parse(translatedTextJson, options);
                                textToProcess = innerDoc.RootElement;
                                
                                // Process directly with this JSON
                                ProcessStructuredJsonTranslation(textToProcess);
                                return;
                            }
                            catch (Exception ex)
                            {
                                Log($"Error parsing inner JSON from translated_text: {ex.Message}");
                                // Fall back to normal processing
                            }
                        }
                    }
                }
                else if (currentService == "Gemini" || currentService == "Ollama")
                {
                    // Gemini and Ollama response structure:
                    // { "candidates": [ { "content": { "parts": [ { "text": "..." } ] } } ] }
                    if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) && 
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        var content = firstCandidate.GetProperty("content");
                        var parts = content.GetProperty("parts");

                        if (parts.GetArrayLength() > 0)
                        {
                            var text = parts[0].GetProperty("text").GetString();

                            // Log the raw text for debugging
                            //Log($"Raw text from {currentService} API: {text}");

                            // Try to extract the JSON object from the text
                            // The model might surround it with markdown or explanatory text
                            if (text != null)
                            {
                                // Check if we already have a proper translation with source_language, target_language, text_blocks
                                if (text.Contains("\"source_language\"") && text.Contains("\"text_blocks\""))
                                {
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log("Direct translation detected, using it as is");
                                    }
                                    
                                    // Look for JSON within the text
                                    int directJsonStart = text.IndexOf('{');
                                    int directJsonEnd = text.LastIndexOf('}');
                                    
                                    if (directJsonStart >= 0 && directJsonEnd > directJsonStart)
                                    {
                                        string directJsonText = text.Substring(directJsonStart, directJsonEnd - directJsonStart + 1);
                                        
                                        try
                                        {
                                            using JsonDocument translatedDoc = JsonDocument.Parse(directJsonText);
                                            var translatedRoot = translatedDoc.RootElement;
                                            
                                            // Now update the text objects with the translation
                                            ProcessStructuredJsonTranslation(translatedRoot);
                                            return; // Bỏ qua xử lý tiếp theo
                                        }
                                        catch (JsonException ex)
                                        {
                                            Log($"Error parsing direct translation JSON: {ex.Message}");
                                            // Continue with normal processing
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (currentService == "Google Translate")
                {
                    // Xử lý phản hồi từ Google Translate
                    // Google Translate trả về định dạng: {"translations": [{"id": "...", "original_text": "...", "translated_text": "..."}]}
                    if (doc.RootElement.TryGetProperty("translations", out JsonElement _))
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Log("Google Translate response detected");
                        }
                        ProcessGoogleTranslateJson(doc.RootElement);
                        return; // Bỏ qua xử lý tiếp theo
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in ProcessTranslatedJSON: {ex.Message}");
                OnFinishedThings(true);
            }
        }

        //!Cancel any in-progress translation
        public void CancelTranslation()
        {
            if (_translationCancellationTokenSource != null && !_translationCancellationTokenSource.Token.IsCancellationRequested)
            {
                Log("Cancelling in-progress translation");
                _translationCancellationTokenSource.Cancel();
                _translationCancellationTokenSource.Dispose();
                _translationCancellationTokenSource = null;
                SetWaitingForTranslationToFinish(false);
            }
        }

        //!Convert textobjects to json and send for translation
        public async Task TranslateTextObjectsAsync()
        {
            // Cancel any existing translation
            CancelTranslation();
            
            // Create new cancellation token source for this translation
            _translationCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _translationCancellationTokenSource.Token;
            
            try
            {
                // Show translation status at the beginning
                MainWindow.Instance.ShowTranslationStatus(false);
                
                // Also show translation status in ChatBoxWindow if it's open
                if (ChatBoxWindow.Instance != null)
                {
                    ChatBoxWindow.Instance.ShowTranslationStatus(false);
                }
                
                if (_textObjects.Count == 0)
                {
                    Log("No text objects to translate");
                    OnFinishedThings(true);
                    return;
                }

                // Get API key
                string apiKey = GetGeminiApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    Log("Gemini API key not set, cannot translate");
                    return;
                }

                // Check for cancellation before proceeding
                cancellationToken.ThrowIfCancellationRequested();

                // Prepare JSON data for translation with rectangle coordinates
                var textsToTranslate = new List<object>();
                for (int i = 0; i < _textObjects.Count; i++)
                {
                    var textObj = _textObjects[i];
                    textsToTranslate.Add(new
                    {
                        id = textObj.ID,
                        text = textObj.Text,
                        rect = new
                        {
                            x = textObj.X,
                            y = textObj.Y,
                            width = textObj.Width,
                            height = textObj.Height
                        }
                    });
                }

                // Get previous context if enabled
                var previousContext = GetPreviousContext();
                
                // Get game info if available
                string gameInfo = ConfigManager.Instance.GetGameInfo();
                
                // Create the full JSON object with OCR results, context and game info
                var ocrData = new
                {
                    source_language = GetSourceLanguage(),
                    target_language = GetTargetLanguage(),
                    text_blocks = textsToTranslate,
                    previous_context = previousContext,
                    game_info = gameInfo
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonToTranslate = JsonSerializer.Serialize(ocrData, jsonOptions);

                // Get the prompt template
                string prompt = GetLlmPrompt();
                
                // Replace language placeholders in prompt with actual language names
                string sourceLanguageName = GetLanguageName(GetSourceLanguage() ?? "en");
                string targetLanguageName = GetLanguageName(GetTargetLanguage());
                prompt = prompt.Replace("source_language", sourceLanguageName);
                prompt = prompt.Replace("target_language", targetLanguageName);
               
               
                // Log the LLM request
                LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                _translationStopwatch.Restart();

                SetWaitingForTranslationToFinish(true);

                // Check for cancellation before making API call
                cancellationToken.ThrowIfCancellationRequested();

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                
                // Call the translation API with the modified prompt if context exists
                string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt, cancellationToken);
                
                // Check if translation was cancelled
                if (cancellationToken.IsCancellationRequested)
                {
                    Log("Translation was cancelled");
                    return;
                }
                
                if (string.IsNullOrEmpty(translationResponse))
                {
                    Log($"Translation failed with {currentService} - empty response");
                    OnFinishedThings(true);
                    return;
                }

                _translationStopwatch.Stop();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Log($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");
                }

                // We've already logged the raw LLM response in the respective service
                // This would log the post-processed response, which we don't need
                // LogManager.Instance.LogLlmReply(translationResponse);

                ProcessTranslatedJSON(translationResponse);
              
            }
            catch (OperationCanceledException)
            {
                Log("Translation was cancelled");
                SetWaitingForTranslationToFinish(false);
            }
            catch (Exception ex)
            {
                Log($"Error translating text objects: {ex.Message}");
                OnFinishedThings(true);
            }
            finally
            {
                // Clean up cancellation token source
                if (_translationCancellationTokenSource != null)
                {
                    _translationCancellationTokenSource.Dispose();
                    _translationCancellationTokenSource = null;
                }
            }

            //all done
            OnFinishedThings(true);
        }

        public string GetGeminiApiKey()
        {
            return ConfigManager.Instance.GetGeminiApiKey();
        }
     
        public string GetLlmPrompt()
        {
            return ConfigManager.Instance.GetLlmPrompt();
        }

        private string? GetSourceLanguage()
        {
            return ConfigManager.Instance.GetSourceLanguage();
        }

        // Get target language from ConfigManager
        private string GetTargetLanguage()
        {
            return ConfigManager.Instance.GetTargetLanguage();
        }
        
        // Convert language code to full language name
        private string GetLanguageName(string languageCode)
        {
            return languageCode.ToLower() switch
            {
                "ja" => "Japanese",
                "en" => "English",
                "ch_sim" => "Chinese",
                "es" => "Spanish",
                "fr" => "French",
                "it" => "Italian",
                "de" => "German",
                "ru" => "Russian",
                "id" => "Indonesian",
                "pl" => "Polish",
                "hi" => "Hindi",
                "ko" => "Korean",
                "vi" => "Vietnamese",
                "ar" => "Arabic",
                "tr" => "Turkish",
                "pt" => "Portuguese",
                "nl" => "Dutch",
                "th" => "Thai",
                _ => languageCode
            };
        }
        
        // Get previous context based on configuration settings
        private List<string> GetPreviousContext()
        {
            // Check if context is enabled
            int maxContextPieces = ConfigManager.Instance.GetMaxContextPieces();
            if (maxContextPieces <= 0)
            {
                return new List<string>(); // Empty list if context is disabled
            }
            
            int minContextSize = ConfigManager.Instance.GetMinContextSize();
           
            // Get context from ChatBoxWindow's history
            if (ChatBoxWindow.Instance != null)
            {
                return ChatBoxWindow.Instance.GetRecentOriginalTexts(maxContextPieces, minContextSize);
               
            }
            
            return new List<string>();
        }

        private bool LanguageCanOnlyBeDrawnHorizontally(string language)
        {
            switch (language.ToLower())
            {
                case "ja": //japanese
                case "ko": //korean
                case "vi": //vietnamese
                case "ch_sim": //chinese
                    return false;
                default:
                    return true;
            }
        }
        
        // Centralized OCR Status Management
        
        // Calculate average FPS from recent samples
        private double CalculateAverageFPS()
        {
            if (_ocrFrameTimes.Count == 0) return 0.0;
            
            double averageFrameTime = _ocrFrameTimes.Average();
            return averageFrameTime > 0 ? 1.0 / averageFrameTime : 0.0;
        }
        
        // Get the display name for the current OCR method
        private string GetCurrentOCRMethodDisplayName()
        {
            string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            
            // Try to get the actual service name from the manager if it's a managed service
            var service = PythonServicesManager.Instance.GetServiceByName(ocrMethod);
            if (service != null && !string.IsNullOrEmpty(service.ServiceName))
            {
                return service.ServiceName;
            }
            
            // Map to display names
            return ocrMethod switch
            {
                "Google Cloud Vision" => "Google",
                "Windows OCR" => "Windows",
                _ => ocrMethod
            };
        }
        
        // Centralized method to notify OCR frame completed
        public void NotifyOCRCompleted()
        {
            DateTime now = DateTime.Now;
            if (_lastOcrFrameTime != DateTime.MinValue)
            {
                double frameTime = (now - _lastOcrFrameTime).TotalSeconds;
                _ocrFrameTimes.Enqueue(frameTime);
                
                // Keep only last N samples
                while (_ocrFrameTimes.Count > MAX_FPS_SAMPLES)
                {
                    _ocrFrameTimes.Dequeue();
                }
            }
            _lastOcrFrameTime = now;
            
            // Only show OCR status if OCR is still running AND no other status is showing
            if (MainWindow.Instance.GetIsStarted())
            {
                ShowOCRStatus();
            }
        }
        
        // Show OCR status on both windows
        public void ShowOCRStatus()
        {
            if (!MainWindow.Instance.GetIsStarted())
            {
                return;
            }
            
            _isOCRActive = true;
            double fps = CalculateAverageFPS();
            string ocrMethod = GetCurrentOCRMethodDisplayName();
            
            // Update both windows with the same data
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MainWindow.Instance.UpdateOCRStatusDisplay(ocrMethod, fps);
            });
            
            // Start the timer if not already running
            if (_ocrStatusTimer == null)
            {
                _ocrStatusTimer = new DispatcherTimer();
                _ocrStatusTimer.Interval = TimeSpan.FromMilliseconds(250); // Update 4 times per second
                _ocrStatusTimer.Tick += OCRStatusTimer_Tick;
            }
            
            if (!_ocrStatusTimer.IsEnabled)
            {
                _ocrStatusTimer.Start();
            }
        }
        
        // OCR status timer tick
        private void OCRStatusTimer_Tick(object? sender, EventArgs e)
        {
            // Check if OCR is still running
            if (!MainWindow.Instance.GetIsStarted())
            {
                HideOCRStatus();
                return;
            }
            
            if (_isOCRActive)
            {
                double fps = CalculateAverageFPS();
                string ocrMethod = GetCurrentOCRMethodDisplayName();
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MainWindow.Instance.UpdateOCRStatusDisplay(ocrMethod, fps);
                });
            }
        }
        
        // Hide OCR status on both windows
        public void HideOCRStatus()
        {
            _isOCRActive = false;
            
            // Stop the timer
            if (_ocrStatusTimer != null && _ocrStatusTimer.IsEnabled)
            {
                _ocrStatusTimer.Stop();
            }
            
            // Clear frame times
            _ocrFrameTimes.Clear();
            
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MainWindow.Instance.HideOCRStatusDisplay();
            });
        }
    }
}