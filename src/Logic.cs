using System;
using System.Globalization;
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
    public partial class Logic
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
        
        // Hash of the last successfully rendered/translated content
        // This is only updated when we actually render, not during settling
        private string _lastOcrHash = string.Empty;
        
        // Hash of the content we're currently settling on
        // This tracks OCR variations during the settling phase
        // null means "not settling", empty string is a valid hash (blank area with no text)
        private string? _settlingHash = null;
        
        // Timestamp of last translation completion - used for cooldown to prevent rapid re-translations
        // due to OCR instability on static screens
        private DateTime _lastTranslationTime = DateTime.MinValue;
        
        // Cooldown period in seconds after translation completes before allowing new translations
        // This prevents re-translations due to OCR instability on static content
        private const double TRANSLATION_COOLDOWN_SECONDS = 2.0;
        
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
      
     
        void OnFinishedThings(bool bResetTranslationStatus, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "", [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0)
        {
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Log($"[SETTLE DEBUG] OnFinishedThings called from {callerName}:{callerLine}, bResetTranslationStatus={bResetTranslationStatus}");
            }
            SetWaitingForTranslationToFinish(false);
            _settlingStartTime = DateTime.MinValue;
            _settlingHash = null;
            
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
            _settlingHash = null;
            _lastChangeTime = DateTime.Now;
            _settlingStartTime = DateTime.MinValue; // Ensure settling restarts clean
            _lastTranslationTime = DateTime.MinValue; // Clear cooldown on reset
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
            _settlingHash = null;
            
            // Force new OCR analysis
            ResetHash();
        }
        
        // Check if snapshot mode is active
        public bool IsSnapshotMode()
        {
            return _isSnapshotMode;
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
                                    _settlingHash = null;
                                    _lastOcrHash = contentHash;
                                    bForceRender = true;
                                }
                                // If OCR found no text, bypass settling — nothing to settle on
                                else if (modifiedResults.GetArrayLength() == 0)
                                {
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log("No text detected, bypassing settle time");
                                    }
                                    _lastChangeTime = DateTime.MinValue;
                                    _settlingStartTime = DateTime.MinValue;
                                    _settlingHash = null;
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
                                    _settlingHash = null;
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
                                    
                                    // FIRST: Check if content matches the LAST RENDERED hash - skip entirely if already processed
                                    // This prevents re-triggering when OCR flips back to what we already translated
                                    // IMPORTANT: We check this regardless of _lastChangeTime - if the hash matches what we
                                    // last rendered, we should ALWAYS skip, even if OCR noise caused a temporary "change"
                                    if (contentHash == _lastOcrHash && !_lastOcrHash.StartsWith("RESET_"))
                                    {
                                        // Already processed this exact content - reset settling state and return
                                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                        {
                                            Log($"[SETTLE DEBUG] Hash matches last rendered, already processed - returning early. Hash: {contentHash.Substring(0, Math.Min(25, contentHash.Length))}...");
                                        }
                                        _lastChangeTime = DateTime.MinValue; // Reset in case OCR noise had started settling
                                        _settlingStartTime = DateTime.MinValue;
                                        _settlingHash = null;
                                        ClearCurrentProcessingBitmap();
                                        OnFinishedThings(true);
                                        return;
                                    }
                                    
                                    if (maxSettleTimeExceeded)
                                    {
                                        Log($"Max settle time exceeded ({maxSettleTime}s), forcing translation after {(DateTime.Now - _settlingStartTime).TotalSeconds:F2}s of settling.");
                                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                        {
                                            Log($"[SETTLE DEBUG] Setting bForceRender=true (max settle time exceeded). Hash: {contentHash.Substring(0, Math.Min(25, contentHash.Length))}...");
                                        }
                                        _lastChangeTime = DateTime.MinValue;
                                        _settlingStartTime = DateTime.MinValue;
                                        _settlingHash = null;
                                        bForceRender = true;
                                    }
                                    // Check if content hash matches the SETTLING hash (what we're currently settling on)
                                    else if (_settlingHash != null && contentHash == _settlingHash)
                                    {
                                        // Content matches what we're settling on - check if settle time reached
                                        if ((DateTime.Now - _lastChangeTime).TotalSeconds >= settleTime)
                                        {
                                            double totalSettlingTime = _settlingStartTime != DateTime.MinValue ? 
                                                (DateTime.Now - _settlingStartTime).TotalSeconds : 0;
                                            Log($"Settle time reached ({settleTime}s), content is stable for {(DateTime.Now - _lastChangeTime).TotalSeconds:F2}s. Total settling: {totalSettlingTime:F2}s");
                                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                            {
                                                Log($"[SETTLE DEBUG] Setting bForceRender=true (settle time reached). Hash: {contentHash.Substring(0, Math.Min(25, contentHash.Length))}...");
                                            }
                                            _lastChangeTime = DateTime.MinValue;
                                            _settlingStartTime = DateTime.MinValue;
                                            _settlingHash = null;
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
                                            MainWindow.Instance.ShowTranslationStatus(true, elapsedSettlingTime, maxSettleTime);
                                            ClearCurrentProcessingBitmap();
                                            return; 
                                        }
                                    }
                                    else // Content is different from both last rendered AND settling hash
                                    {
                                        // COOLDOWN CHECK: If we recently completed a translation, check if this looks like
                                        // OCR instability on the same content (hashes start similarly but aren't identical)
                                        // This prevents rapid re-translations due to OCR noise on static screens
                                        if (_lastTranslationTime != DateTime.MinValue && 
                                            (DateTime.Now - _lastTranslationTime).TotalSeconds < TRANSLATION_COOLDOWN_SECONDS)
                                        {
                                            // Check if the content is "similar enough" to the last rendered - first N chars match
                                            int configuredCompareLength = ConfigManager.Instance.GetCooldownHashCompareLength();
                                            int compareLength = Math.Min(configuredCompareLength, Math.Min(contentHash.Length, _lastOcrHash.Length));
                                            bool isSimilar = configuredCompareLength > 0 && compareLength > 0 && 
                                                           contentHash.Substring(0, compareLength) == _lastOcrHash.Substring(0, compareLength);
                                            
                                            if (isSimilar)
                                            {
                                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                                {
                                                    double cooldownRemaining = TRANSLATION_COOLDOWN_SECONDS - (DateTime.Now - _lastTranslationTime).TotalSeconds;
                                                    Log($"[SETTLE DEBUG] Cooldown active ({cooldownRemaining:F1}s remaining), content similar to last translated - skipping");
                                                }
                                                ClearCurrentProcessingBitmap();
                                                OnFinishedThings(true);
                                                return;
                                            }
                                        }
                                        
                                        // Content has changed - update settling hash (NOT _lastOcrHash!)
                                        if (_settlingHash != null)
                                        {
                                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                            {
                                                Log($"Content changed during settling! Old settling hash: {_settlingHash.Substring(0, Math.Min(20, _settlingHash.Length))}..., New hash: {contentHash.Substring(0, Math.Min(20, contentHash.Length))}...");
                                            }
                                        }
                                        else if (_lastOcrHash != string.Empty && !_lastOcrHash.StartsWith("RESET_"))
                                        {
                                            Log($"Content changed! Old hash: {_lastOcrHash.Substring(0, Math.Min(20, _lastOcrHash.Length))}..., New hash: {contentHash.Substring(0, Math.Min(20, contentHash.Length))}...");
                                        }
                                        
                                        _lastChangeTime = DateTime.Now;
                                        _settlingHash = contentHash; // Update settling hash, NOT _lastOcrHash!

                                        // Initialize settling start time when settling first begins
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
                                            MainWindow.Instance.ShowTranslationStatus(true, elapsedSettlingTimeOnChange, maxSettleTime);
                                        }
                                        
                                        // Check again for max settle time to ensure it's enforced even with changing content
                                        if (maxSettleTime > 0 && 
                                            _settlingStartTime != DateTime.MinValue &&
                                            (DateTime.Now - _settlingStartTime).TotalSeconds >= maxSettleTime)
                                        {
                                            double totalSettlingTime = (DateTime.Now - _settlingStartTime).TotalSeconds;
                                            Log($"Max settle time exceeded ({maxSettleTime}s) while content was changing, forcing translation after {totalSettlingTime:F2}s.");
                                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                            {
                                                Log($"[SETTLE DEBUG] Setting bForceRender=true (max settle during content change). Hash: {contentHash.Substring(0, Math.Min(25, contentHash.Length))}...");
                                            }
                                            _lastChangeTime = DateTime.MinValue;
                                            _settlingStartTime = DateTime.MinValue;
                                            _settlingHash = null;
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
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"[SETTLE DEBUG] Hash matches, bForceRender=false - returning without DisplayOcrResults");
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
                                    Log($"[SETTLE DEBUG] Proceeding to DisplayOcrResults (bForceRender={bForceRender}). Hash: {contentHash.Substring(0, Math.Min(25, contentHash.Length))}...");
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
        
        // Display OCR results from JSON - processes character-level blocks
        private async void DisplayOcrResults(JsonElement root)
        {
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Log($"[SETTLE DEBUG] DisplayOcrResults ENTERED");
            }
            
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
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"[SETTLE DEBUG] TRIGGERING TRANSLATION. Text: {combinedText.Substring(0, Math.Min(40, combinedText.Length))}...");
                                    }
                                    // Translate the text objects
                                    _lastChangeTime = DateTime.MinValue;
                                    _ = TranslateTextObjectsAsync();
                                    return;
                                }
                                else
                                {
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Log($"[SETTLE DEBUG] SKIPPING TRANSLATION - already waiting for translation to finish");
                                    }
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
        
        /// <summary>
        /// Convert language code to full language name.
        /// This is the single source of truth for language display names used in:
        /// - Settings ComboBoxes
        /// - LLM translation prompts
        /// </summary>
        public static string GetLanguageName(string languageCode)
        {
            return languageCode.ToLower() switch
            {
                "ja" => "Japanese",
                "en" => "English",
                "ch_sim" => "Chinese (Simplified)",
                "ch_tra" => "Chinese (Traditional)",
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
                "cs" => "Czech",
                "sv" => "Swedish",
                "hu" => "Hungarian",
                "ro" => "Romanian",
                "uk" => "Ukrainian",
                "el" => "Greek",
                "fa" => "Persian",
                _ => languageCode
            };
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
    }
}
