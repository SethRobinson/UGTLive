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
        
        // Track the current capture position
        private int _currentCaptureX;
        private int _currentCaptureY;
        private DateTime _lastChangeTime = DateTime.MinValue;
        private DateTime _settlingStartTime = DateTime.MinValue;
   
        // Properties to expose to other classes
        public List<TextObject> TextObjects => _textObjects;
        public List<TextObject> TextObjectsOld => _textObjectsOld;

        // Events
        public event EventHandler<TextObject>? TextObjectAdded;
        
        // Event when translation is completed
        public event EventHandler<TranslationEventArgs>? TranslationCompleted;
     
        bool _waitingForTranslationToFinish = false;

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

        // Called when the application starts
        public async void Init()
        {
            try
            {
                // Initialize resources, settings, etc.
                Console.WriteLine("Logic initialized");
                
                // Load configuration
                string geminiApiKey = ConfigManager.Instance.GetGeminiApiKey();

                // Warm up shared WebView2 environment early to reduce overlay latency
                _ = WebViewEnvironmentManager.GetEnvironmentAsync();
                Console.WriteLine($"Loaded Gemini API key: {(string.IsNullOrEmpty(geminiApiKey) ? "Not set" : "Set")}");
                
                // Load LLM prompt
                string llmPrompt = ConfigManager.Instance.GetLlmPrompt();
                Console.WriteLine($"Loaded LLM prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : $"{llmPrompt.Length} chars")}");
                
                // Load force cursor visible setting
                // Force cursor visibility is now handled by MouseManager
                
                // Discover Python services
                PythonServicesManager.Instance.DiscoverServices();
                
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                if (ocrMethod == "Google Vision")
                {
                    Console.WriteLine("Using Google Cloud Vision - socket connection not needed");
                    
                    // Update status message in the UI
                    MainWindow.Instance.SetStatus("Using Google Cloud Vision (non-local, costs $)");
                }
                else
                {
                    Console.WriteLine("Using Windows OCR - socket connection not needed");
                    
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
            MonitorWindow.Instance.RefreshOverlays();
            MainWindow.Instance.RefreshMainWindowOverlays();

            // Hide translation status
            if (bResetTranslationStatus)
            {
                MonitorWindow.Instance.HideTranslationStatus();
                MainWindow.Instance.HideTranslationStatus();
                ChatBoxWindow.Instance?.HideTranslationStatus();
            }
            
            // Re-enable OCR if it was paused during translation
            if (ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled())
            {
                MainWindow.Instance.SetOCRCheckIsWanted(true);
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Translation finished - re-enabling OCR");
                }
            }
        }

        public void ResetHash()
        {
            _lastOcrHash = "";
            _lastChangeTime = DateTime.Now;
        }
        

    
        private void ProcessGoogleTranslateJson(JsonElement translatedRoot)
        {
            try
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Processing Google Translate JSON response");
                }
                
                
                if (translatedRoot.TryGetProperty("translations", out JsonElement translationsElement) &&
                    translationsElement.ValueKind == JsonValueKind.Array)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Found {translationsElement.GetArrayLength()} translations in Google Translate JSON");
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
                                        Console.WriteLine($"Updated text object {id} with Google translation");
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
                                            Console.WriteLine($"Updated text object at index {index} with Google translation");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Could not find text object with ID {id}");
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
                    Console.WriteLine("No translations array found in Google Translate JSON");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Google Translate JSON: {ex.Message}");
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
                    Console.WriteLine("Pause OCR while translating is enabled - keeping OCR paused until translation finishes");
                }
            }
            
            // Notify that OCR has completed
            NotifyOCRCompleted();

            if (waitingForTranslation)
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Skipping OCR results - waiting for translation to finish");
                }
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
                                Console.WriteLine($"ProcessReceivedTextJsonData: status={status}, hasResults={hasResults}, hasTexts={hasTexts}");
                            }
                            
                            if (hasTexts && !hasResults)
                            {
                                resultsElement = textsElement;
                                hasResults = true;
                                
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"Using 'texts' property as results, array length: {resultsElement.GetArrayLength()}");
                                }
                            }
                            
                            if (status == "success" && hasResults)
                            {
                                // Pre-filter low-confidence characters before block detection
                                JsonElement filteredResults = FilterLowConfidenceCharacters(resultsElement);
                                
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"After FilterLowConfidenceCharacters: {filteredResults.GetArrayLength()} items");
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
                                    Console.WriteLine("Skipping block detection for pre-grouped results");
                                }
                                else
                                {
                                    // Process character-level OCR data using CharacterBlockDetectionManager
                                    // Use the filtered results for consistency
                                    modifiedResults = CharacterBlockDetectionManager.Instance.ProcessCharacterResults(filteredResults);
                                    
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Console.WriteLine($"After CharacterBlockDetectionManager: {modifiedResults.GetArrayLength()} blocks");
                                    }
                                }
                                
                                // Filter out text objects that should be ignored based on ignore phrases
                                modifiedResults = FilterIgnoredPhrases(modifiedResults);
                                
                                // Generate content hash AFTER block detection and filtering
                                string contentHash = GenerateContentHash(modifiedResults);

                                // Handle settle time if enabled
                                double settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
                                double maxSettleTime = ConfigManager.Instance.GetBlockDetectionMaxSettleTime();
                                
                                // Debug outputs to track settling behavior
                                bool isSettling = _settlingStartTime != DateTime.MinValue;
                                double settlingElapsed = isSettling ? (DateTime.Now - _settlingStartTime).TotalSeconds : 0;
                                double lastChangeElapsed = _lastChangeTime != DateTime.MinValue ? (DateTime.Now - _lastChangeTime).TotalSeconds : 0;
                                
                                if (ConfigManager.Instance.GetLogExtraDebugStuff() && isSettling)
                                {
                                    Console.WriteLine($"Settling - Elapsed: {settlingElapsed:F2}s, MaxSettleTime: {maxSettleTime}s, LastChange: {lastChangeElapsed:F2}s, SettleTime: {settleTime}s");
                                }

                                // Initialize settling start time if it hasn't been set yet and settling is enabled
                                if (_settlingStartTime == DateTime.MinValue && settleTime > 0)
                                {
                                    _settlingStartTime = DateTime.Now;
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Console.WriteLine($"Settling started at: {_settlingStartTime:HH:mm:ss.fff}, Hash: {contentHash.Substring(0, Math.Min(20, contentHash.Length))}...");
                                    }
                                }

                                if (settleTime > 0)
                                {
                                    // Check for max settle time first, regardless of hash match
                                    bool maxSettleTimeExceeded = maxSettleTime > 0 && 
                                                                _settlingStartTime != DateTime.MinValue &&
                                                                (DateTime.Now - _settlingStartTime).TotalSeconds >= maxSettleTime;
                                    
                                    if (maxSettleTimeExceeded)
                                    {
                                        Console.WriteLine($"Max settle time exceeded ({maxSettleTime}s), forcing translation after {(DateTime.Now - _settlingStartTime).TotalSeconds:F2}s of settling.");
                                        _lastChangeTime = DateTime.MinValue; // Indicate settle completed
                                        _settlingStartTime = DateTime.MinValue; // Reset settling timer
                                        bForceRender = true;
                                    }
                                    else if (contentHash == _lastOcrHash)
                                    {
                                        // Content is stable
                                        if (_lastChangeTime == DateTime.MinValue) // Already settled and rendered
                                        {
                                            // Reset settling start time as content is stable and has been processed or was empty.
                                            _settlingStartTime = DateTime.MinValue; 
                                            OnFinishedThings(true); // Reset status, hide "settling"
                                            return; 
                                        }
                                        else
                                        {
                                            // Content is stable, check if normal settle time is reached
                                            if ((DateTime.Now - _lastChangeTime).TotalSeconds >= settleTime)
                                            {
                                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                                {
                                                    Console.WriteLine($"Settle time reached ({settleTime}s), content is stable for {(DateTime.Now - _lastChangeTime).TotalSeconds:F2}s.");
                                                }
                                                _lastChangeTime = DateTime.MinValue; // Indicate settle completed
                                                _settlingStartTime = DateTime.MinValue; // Reset settling timer
                                                bForceRender = true;
                                            }
                                            else
                                            {
                                                // Still within normal settle time, content is stable but waiting
                                                double remainingSettleTime = settleTime - (DateTime.Now - _lastChangeTime).TotalSeconds;
                                                Console.WriteLine($"Content stable for {(DateTime.Now - _lastChangeTime).TotalSeconds:F2}s, waiting {remainingSettleTime:F2}s more...");
                                                MonitorWindow.Instance.ShowTranslationStatus(true); // Keep showing "Settling..."
                                                ChatBoxWindow.Instance?.ShowTranslationStatus(true);
                                                MainWindow.Instance.ShowTranslationStatus(true);
                                                return; 
                                            }
                                        }
                                    }
                                    else // contentHash != _lastOcrHash (text has changed)
                                    {
                                        // Content has changed
                                        if (_lastOcrHash != string.Empty)
                                        {
                                            Console.WriteLine($"Content changed! Old hash: {_lastOcrHash.Substring(0, Math.Min(20, _lastOcrHash.Length))}..., New hash: {contentHash.Substring(0, Math.Min(20, contentHash.Length))}...");
                                        }
                                        
                                        _lastChangeTime = DateTime.Now;
                                        _lastOcrHash = contentHash;

                                        if (MainWindow.Instance.GetIsStarted())
                                        {
                                            MonitorWindow.Instance.ShowTranslationStatus(true); // Show "Settling..."
                                            ChatBoxWindow.Instance?.ShowTranslationStatus(true);
                                            MainWindow.Instance.ShowTranslationStatus(true);
                                        }
                                        
                                        // Check again for max settle time to ensure it's enforced even with changing content
                                        if (maxSettleTime > 0 && 
                                            _settlingStartTime != DateTime.MinValue &&
                                            (DateTime.Now - _settlingStartTime).TotalSeconds >= maxSettleTime)
                                        {
                                            Console.WriteLine($"Max settle time exceeded ({maxSettleTime}s) while content was changing, forcing translation.");
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
                                            double remainingMaxSettleTime = maxSettleTime - elapsedSettlingTime;
                                            
                                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                            {
                                                Console.WriteLine($"Content unstable, settling for {elapsedSettlingTime:F2}s, max {remainingMaxSettleTime:F2}s remaining.");
                                            }
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
                                            Console.WriteLine("Hash matches but OCR found no text while text objects exist - clearing display");
                                        }
                                        ClearAllTextObjects();
                                        MonitorWindow.Instance.RefreshOverlays();
                                        MainWindow.Instance.RefreshMainWindowOverlays();
                                    }
                                    OnFinishedThings(true);
                                    return;
                                }
                               
                                // Looks like new stuff
                                _lastOcrHash = contentHash;
                                double scale = BlockDetectionManager.Instance.GetBlockDetectionScale();
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"Character-level processing (scale={scale:F2}): {resultsElement.GetArrayLength()} characters → {modifiedResults.GetArrayLength()} blocks");
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
                                    using (JsonDocument newDoc = JsonDocument.Parse(stream))
                                    {
                                        DisplayOcrResults(newDoc.RootElement);
                                    }

                                    _ocrProcessingStopwatch.Stop();
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Console.WriteLine($"OCR JSON processing took {_ocrProcessingStopwatch.ElapsedMilliseconds} ms");
                                    }

                                }

                                // Add the detected text to the ChatBox
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
                                                //Console.WriteLine($"Translating text: {combinedText}");
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
                            else if (status == "error" && root.TryGetProperty("message", out JsonElement messageElement))
                            {
                                // Display error message
                                string errorMsg = messageElement.GetString() ?? "Unknown error";
                                Console.WriteLine($"OCR service returned error: {errorMsg}");
                            }
                            else if (status == "success" && !hasResults)
                            {
                                // Success status but no results/texts property
                                Console.WriteLine("ERROR: OCR response has status='success' but no 'results' or 'texts' property");
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"Response JSON properties: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");
                                    Console.WriteLine($"Full response (first 500 chars): {data.Substring(0, Math.Min(500, data.Length))}");
                                }
                            }
                        }
                        else
                        {
                            // No status property at all
                            Console.WriteLine("ERROR: OCR response missing 'status' property");
                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                            {
                                Console.WriteLine($"Response JSON properties: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");
                                Console.WriteLine($"Full response (first 500 chars): {data.Substring(0, Math.Min(500, data.Length))}");
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"JSON parsing error: {ex.Message}");
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
                Console.WriteLine($"Error processing socket data: {ex.Message}");
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
                Console.WriteLine($"Error filtering ignored phrases: {ex.Message}");
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
            
            //Console.WriteLine($"Checking text '{text}' against {ignorePhrases.Count} ignore phrases");
            
            foreach (var (phrase, exactMatch) in ignorePhrases)
            {
                if (string.IsNullOrEmpty(phrase))
                    continue;
                    
                if (exactMatch)
                {
                    // Check for exact match
                    if (text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        //Console.WriteLine($"Ignoring text due to exact match: '{phrase}'");
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
                        //Console.WriteLine($"Applied non-exact match filter: '{phrase}' removed from text");
                    }
                }
            }
            
            // Check if after removing non-exact-match phrases, the text is empty or whitespace
            if (string.IsNullOrWhiteSpace(filteredText))
            {
                Console.WriteLine("Ignoring text because it's empty after filtering");
                return (true, string.Empty);
            }
            
            // Return the filtered text if it changed
            if (filteredText != text)
            {
                //Console.WriteLine($"Text filtered: '{text}' -> '{filteredText}'");
                return (false, filteredText);
            }
            
            return (false, text);
        }
        
        // Display OCR results from JSON - processes character-level blocks
        private void DisplayOcrResults(JsonElement root)
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
                    
                    // Clear existing text objects before adding new ones
                    ClearAllTextObjects();
                    
                    // Process text blocks that have already been grouped by CharacterBlockDetectionManager
                    int resultCount = resultsElement.GetArrayLength();
                    
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"DisplayOcrResults: Processing {resultCount} text blocks");
                    }
                    
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


                            double confidence = confElement.GetDouble();
                            
                            // Extract bounding box coordinates if available
                            double x = 0, y = 0, width = 0, height = 0;
                            
                            // Check for "rect" property (polygon points format)
                            if (item.TryGetProperty("rect", out JsonElement boxElement) && 
                                boxElement.ValueKind == JsonValueKind.Array)
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
                                    Console.WriteLine($"Error parsing rect: {ex.Message}");
                                }
                            }
                         
                            // Extract colors from JSON if available
                            // Use Color? instead of SolidColorBrush? to avoid threading issues
                            Color? foregroundColor = null;
                            Color? backgroundColor = null;
                            
                            if (item.TryGetProperty("foreground_color", out JsonElement foregroundColorElement))
                            {
                                foregroundColor = ParseColorFromJson(foregroundColorElement, isBackground: false);
                                
                                // Debug: Log the RGB values we're parsing
                                if (foregroundColorElement.TryGetProperty("rgb", out JsonElement fgRgb) && 
                                    fgRgb.ValueKind == JsonValueKind.Array && fgRgb.GetArrayLength() >= 3)
                                {
                                    int r = fgRgb[0].TryGetInt32(out int rInt) ? rInt : (int)fgRgb[0].GetDouble();
                                    int g = fgRgb[1].TryGetInt32(out int gInt) ? gInt : (int)fgRgb[1].GetDouble();
                                    int b = fgRgb[2].TryGetInt32(out int bInt) ? bInt : (int)fgRgb[2].GetDouble();
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Console.WriteLine($"Foreground color for '{text}': RGB({r}, {g}, {b})");
                                    }
                                }
                            }
                            
                            if (item.TryGetProperty("background_color", out JsonElement backgroundColorElement))
                            {
                                backgroundColor = ParseColorFromJson(backgroundColorElement, isBackground: true);
                                
                                // Debug: Log the RGB values we're parsing
                                if (backgroundColorElement.TryGetProperty("rgb", out JsonElement bgRgb) && 
                                    bgRgb.ValueKind == JsonValueKind.Array && bgRgb.GetArrayLength() >= 3)
                                {
                                    int r = bgRgb[0].TryGetInt32(out int rInt) ? rInt : (int)bgRgb[0].GetDouble();
                                    int g = bgRgb[1].TryGetInt32(out int gInt) ? gInt : (int)bgRgb[1].GetDouble();
                                    int b = bgRgb[2].TryGetInt32(out int bInt) ? bInt : (int)bgRgb[2].GetDouble();
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Console.WriteLine($"Background color for '{text}': RGB({r}, {g}, {b})");
                                    }
                                }
                            }
                         
                            // Create text object with bounding box coordinates and colors
                            CreateTextObjectAtPosition(text, x, y, width, height, confidence, textOrientation, foregroundColor, backgroundColor);
                        }
                    }
                    
                    // Refresh monitor window overlays to ensure they're displayed
                    MonitorWindow.Instance.RefreshOverlays();
                    MainWindow.Instance.RefreshMainWindowOverlays();
                    
                    // Trigger source audio preloading right after OCR results are displayed
                    TriggerSourceAudioPreloading();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error displaying OCR results: {ex.Message}");
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
                        Console.WriteLine($"Warning: RGB values are not numbers in color JSON");
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
                Console.WriteLine($"Error parsing color from JSON: {ex.Message}");
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
                    Console.WriteLine("Cannot create text object with empty text");
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
                    Console.WriteLine($"Creating TextObject '{text.Substring(0, Math.Min(20, text.Length))}...' - TextColor: {textColor.Color}, BackgroundColor: {bgColor.Color}");
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

                // Console.WriteLine($"Added text '{text}' at position ({x}, {y}) with size {width}x{height}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating text object: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
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
                Console.WriteLine($"Error updating text positions: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Filters out low-confidence characters from the OCR results
        /// </summary>
        private JsonElement FilterLowConfidenceCharacters(JsonElement resultsElement)
        {
            if (resultsElement.ValueKind != JsonValueKind.Array)
                return resultsElement;
                
            try
            {
                // Get minimum confidence threshold from config
                double minLetterConfidence = ConfigManager.Instance.GetMinLetterConfidence();
                
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
                            
                            // Only include elements with confidence above threshold
                            if (confidence >= minLetterConfidence)
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
                Console.WriteLine($"Error filtering low-confidence characters: {ex.Message}");
                return resultsElement; // Return original on error
            }
        }

        static readonly HashSet<char> g_charsToStripFromHash =
             new(" \n\r,.-:;ー・…。、~』!^へ");


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
            //Console.WriteLine($"Generated hash: {hash}");
            return hash;
        }
       
        // Process bitmap directly with Windows OCR (no file saving)
        public async void ProcessWithWindowsOCR(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            try
            {
                //Console.WriteLine("Starting Windows OCR processing directly from bitmap...");
                
                try
                {
                    // Get the text lines from Windows OCR directly from the bitmap
                    var textLines = await WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, sourceLanguage);
                   // Console.WriteLine($"Windows OCR found {textLines.Count} text lines");
                    
                    // Process the OCR results with language code
                    await WindowsOCRManager.Instance.ProcessWindowsOcrResults(textLines, sourceLanguage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Windows OCR error: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing bitmap with Windows OCR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Make sure bitmap is properly disposed
                try
                {
                    // Dispose bitmap - System.Drawing.Bitmap doesn't have a Disposed property,
                    // so we'll just dispose it if it's not null
                    if (bitmap != null)
                    {
                        bitmap.Dispose();
                    }
                }
                catch
                {
                    // Ignore disposal errors
                }

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
                    Console.WriteLine("Windows OCR: Pause OCR while translating is enabled - keeping OCR paused until translation finishes");
                }
                
                // Notify that OCR has completed
                NotifyOCRCompleted();
            }
        }
        
        // Process bitmap directly with Google Vision API (no file saving)
        public async void ProcessWithGoogleVision(System.Drawing.Bitmap bitmap, string sourceLanguage)
        {
            try
            {
                Console.WriteLine("Starting Google Vision OCR processing...");
                
                try
                {
                    // Get the text objects from Google Vision API
                    var textObjects = await GoogleVisionOCRService.Instance.ProcessImageAsync(bitmap, sourceLanguage);
                    Console.WriteLine($"Google Vision OCR found {textObjects.Count} text objects");
                    
                    // Process the OCR results
                    await GoogleVisionOCRService.Instance.ProcessGoogleVisionResults(textObjects);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Google Vision OCR error: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    
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
                Console.WriteLine($"Error processing bitmap with Google Vision: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Make sure bitmap is properly disposed
                try
                {
                    if (bitmap != null)
                    {
                        bitmap.Dispose();
                    }
                }
                catch
                {
                    // Ignore disposal errors
                }

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
                    Console.WriteLine("Google Vision: Pause OCR while translating is enabled - keeping OCR paused until translation finishes");
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
                    Console.WriteLine($"Service {serviceName} not found");
                    return null;
                }
                
                // Only check if service is running if not already marked as running
                // This avoids the /info endpoint check on every OCR request
                if (!service.IsRunning)
                {
                    bool isRunning = await service.CheckIsRunningAsync();
                    
                    if (!isRunning)
                    {
                        Console.WriteLine($"Service {serviceName} is not running");
                        
                        // Show error dialog offering to start the service (if not already showing)
                        bool openManager = ErrorPopupManager.ShowServiceWarning(
                            $"The {serviceName} service is not running.\n\nWould you like to open the Python Services Manager to start it?",
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
                string url = $"{service.ServerUrl}:{service.Port}/process?lang={langParam}&char_level=true";
                
                // Add MangaOCR-specific parameters
                if (serviceName == "MangaOCR")
                {
                    int minWidth = ConfigManager.Instance.GetMangaOcrMinRegionWidth();
                    int minHeight = ConfigManager.Instance.GetMangaOcrMinRegionHeight();
                    double overlapPercent = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent();
                    url += $"&min_region_width={minWidth}&min_region_height={minHeight}&overlap_allowed_percent={overlapPercent}";
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
                    Console.WriteLine($"HTTP request failed: {response.StatusCode}");
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
                            Console.WriteLine($"{serviceName}: Response missing 'status' property");
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
                                    Console.WriteLine($"Received {count} text objects from {serviceName}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"{serviceName}: Response missing 'texts' property");
                                return null;
                            }
                        }
                        else if (status == "error")
                        {
                            string errorMsg = root.TryGetProperty("message", out var msgElement) 
                                ? msgElement.GetString() ?? "Unknown error" 
                                : "Unknown error";
                            Console.WriteLine($"{serviceName} returned error: {errorMsg}");
                            return null;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"{serviceName} returned invalid JSON: {ex.Message}");
                    return null;
                }
                
                // Return the JSON response to be processed by ProcessReceivedTextJsonData
                return jsonResponse;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image with HTTP service: {ex.Message}");
                
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
        
        // Called when a screenshot is captured (sends directly to HTTP service)
        public async void SendImageToHttpOCR(byte[] imageBytes)
        {
            try
            {
                // Check if we should pause OCR while translating
                bool pauseOcrWhileTranslating = ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled();
                bool waitingForTranslation = GetWaitingForTranslationToFinish();
                
                if (pauseOcrWhileTranslating && waitingForTranslation)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine("Pause OCR while translating is enabled - skipping HTTP OCR request");
                    }
                    // Re-enable OCR check so it can be triggered again after translation finishes
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    return;
                }
                
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                
                if (ocrMethod == "Windows OCR" || ocrMethod == "Google Vision")
                {
                    // These are handled in MainWindow.PerformCapture() directly
                    Console.WriteLine($"SendImageToHttpOCR called for {ocrMethod} - should not happen");
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    return;
                }
                else if (ocrMethod == "EasyOCR" || ocrMethod == "MangaOCR" || string.Equals(ocrMethod, "docTR", StringComparison.OrdinalIgnoreCase))
                {
                    // Get source language
                    string sourceLanguage = GetSourceLanguage()!;
                    
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Processing {imageBytes.Length} bytes with {ocrMethod} HTTP service, language: {sourceLanguage}");
                    }
                    
                    // Process with HTTP service - returns JSON directly
                    var jsonResponse = await ProcessImageWithHttpServiceAsync(imageBytes, ocrMethod, sourceLanguage);
                    
                    if (jsonResponse != null)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"=== JSON Response from {ocrMethod} (first 1000 chars) ===");
                            Console.WriteLine(jsonResponse.Substring(0, Math.Min(1000, jsonResponse.Length)));
                            Console.WriteLine("=== End JSON Response ===");
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
                            Console.WriteLine($"No valid response received from {ocrMethod} service");
                        }
                    }
                    
                    // Re-enable OCR check
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    
                    // Notify that OCR has completed
                    NotifyOCRCompleted();
                }
                else
                {
                    Console.WriteLine($"Unknown OCR method: {ocrMethod}");
                    // Disable OCR to prevent loop
                    MainWindow.Instance.SetOCRCheckIsWanted(false);

                    // Show error popup
                    ErrorPopupManager.ShowError(
                        $"Unknown OCR method: '{ocrMethod}'.\n\nPlease select a valid OCR method in the Settings or Monitor window.",
                        "Invalid OCR Method"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing screenshot: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
        }

        // Called when the application is closing
        public async Task Finish()
        {
            try
            {
                // Clean up resources
                Console.WriteLine("Logic finalized");
                
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
                    Console.WriteLine("Cannot add text object with empty text");
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
                
                Console.WriteLine($"Added text '{text}' at position {x}, {y}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding text: {ex.Message}");
            }
        }
        
     
        // Clear all text objects
        public void ClearAllTextObjects()
        {
            try
            {
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
                
                // Skip profiler for instant clearing
                foreach (TextObject textObject in _textObjects)
                {
                    MonitorWindow.Instance?.RemoveOverlay(textObject);
                    textObject.Dispose();
                }

                MonitorWindow.Instance?.ClearOverlays();

                // Clear the collection
                _textObjects.Clear();
                _textIDCounter = 0;
                // No need to remove from the main window UI anymore
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("All text objects cleared");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing text objects: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
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
                        Console.WriteLine("Logic: Source audio preloading skipped (feature disabled)");
                    }
                    return;
                }
                
                string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Logic: TriggerSourceAudioPreloading called, preloadMode={preloadMode}");
                }
                
                if (preloadMode == "Source language" || preloadMode == "Both source and target languages")
                {
                    var textObjects = _textObjects.ToList();
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Logic: Found {textObjects.Count} text objects for source audio preloading");
                    }
                    
                    if (textObjects.Count > 0)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine("Logic: Starting source audio preload...");
                        }
                        _ = AudioPreloadService.Instance.PreloadSourceAudioAsync(textObjects);
                    }
                    else
                    {
                        Console.WriteLine("Logic: No text objects to preload source audio");
                    }
                }
                else
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Logic: Source audio preloading skipped (preloadMode={preloadMode})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error triggering source audio preloading: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
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
                        Console.WriteLine("Logic: Target audio preloading skipped (feature disabled)");
                    }
                    return;
                }
                
                string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Logic: TriggerTargetAudioPreloading called, preloadMode={preloadMode}");
                }
                
                if (preloadMode == "Target language" || preloadMode == "Both source and target languages")
                {
                    var textObjects = _textObjects.ToList();
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Logic: Found {textObjects.Count} text objects for target audio preloading");
                    }
                    
                    if (textObjects.Count > 0)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine("Logic: Starting target audio preload...");
                        }
                        _ = AudioPreloadService.Instance.PreloadTargetAudioAsync(textObjects);
                    }
                    else
                    {
                        Console.WriteLine("Logic: No text objects to preload target audio");
                    }
                }
                else
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Logic: Target audio preloading skipped (preloadMode={preloadMode})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error triggering target audio preloading: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        //! Process structured JSON translation from ChatGPT or other services
        private void ProcessStructuredJsonTranslation(JsonElement translatedRoot)
        {
  
            try
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Processing structured JSON translation");
                }
                // Check if we have text_blocks array in the translated JSON
                if (translatedRoot.TryGetProperty("text_blocks", out JsonElement textBlocksElement) &&
                    textBlocksElement.ValueKind == JsonValueKind.Array)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Found {textBlocksElement.GetArrayLength()} text blocks in translated JSON");
                    }
                    
                    // Process each translated block
                    for (int i = 0; i < textBlocksElement.GetArrayLength(); i++)
                    {
                        var block = textBlocksElement[i];
                        
                        if (!block.TryGetProperty("id", out JsonElement idElement))
                        {
                            Console.WriteLine($"ERROR: Text block at index {i} is missing 'id' field");
                            continue;
                        }
                        
                        string blockId = idElement.GetString() ?? "";
                        
                        if (!block.TryGetProperty("text", out JsonElement translatedTextElement))
                        {
                            Console.WriteLine($"ERROR: Text block '{blockId}' is missing 'text' field. Block content: {block.ToString()}");
                            
                            // Check if LLM used wrong field names
                            if (block.TryGetProperty("translated_text", out _))
                            {
                                Console.WriteLine($"ERROR: Text block '{blockId}' has 'translated_text' field instead of 'text'. The LLM is not following the prompt correctly.");
                            }
                            else if (block.TryGetProperty("english_text", out _) || 
                                     block.TryGetProperty("japanese_text", out _) ||
                                     block.TryGetProperty("chinese_text", out _))
                            {
                                Console.WriteLine($"ERROR: Text block '{blockId}' has language-specific field (like 'english_text') instead of 'text'. The LLM is not following the prompt correctly.");
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
                                    Console.WriteLine($"Updated text object {id} with translation");
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
                                        Console.WriteLine($"Updated text object at index {index} with translation");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Could not find text object with ID {id}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No text_blocks array found in translated JSON");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing structured JSON translation: {ex.Message}");
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
                    //Console.WriteLine($"Adding to chatbox: Original: '{originalText}', Translated: '{translatedText}'");
                    // Add to TranslationCompleted, this will add it to the chatbox also
                    TranslationCompleted?.Invoke(this, new TranslationEventArgs
                    {
                        OriginalText = originalText,
                        TranslatedText = translatedText
                    });
                }
                else
                {
                    Console.WriteLine($"Skipping empty translation - Original: '{originalText}', Translated: '{translatedText}'");
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
                //Console.WriteLine($"Processing translation response from {currentService} service");
                
                // Log full response for debugging
                //Console.WriteLine($"Raw translationResponse: {translationResponse}");
                
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
                        // Console.WriteLine($"{currentService} translated_text: {translatedTextJson}");
                        
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
                                Console.WriteLine($"Error parsing inner JSON from translated_text: {ex.Message}");
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
                            //Console.WriteLine($"Raw text from {currentService} API: {text}");

                            // Try to extract the JSON object from the text
                            // The model might surround it with markdown or explanatory text
                            if (text != null)
                            {
                                // Check if we already have a proper translation with source_language, target_language, text_blocks
                                if (text.Contains("\"source_language\"") && text.Contains("\"text_blocks\""))
                                {
                                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                    {
                                        Console.WriteLine("Direct translation detected, using it as is");
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
                                            Console.WriteLine($"Error parsing direct translation JSON: {ex.Message}");
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
                            Console.WriteLine("Google Translate response detected");
                        }
                        ProcessGoogleTranslateJson(doc.RootElement);
                        return; // Bỏ qua xử lý tiếp theo
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ProcessTranslatedJSON: {ex.Message}");
                OnFinishedThings(true);
            }
        }

        //!Cancel any in-progress translation
        public void CancelTranslation()
        {
            if (_translationCancellationTokenSource != null && !_translationCancellationTokenSource.Token.IsCancellationRequested)
            {
                Console.WriteLine("Cancelling in-progress translation");
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
                MonitorWindow.Instance.ShowTranslationStatus(false);
                MainWindow.Instance.ShowTranslationStatus(false);
                
                // Also show translation status in ChatBoxWindow if it's open
                if (ChatBoxWindow.Instance != null)
                {
                    ChatBoxWindow.Instance.ShowTranslationStatus(false);
                }
                
                if (_textObjects.Count == 0)
                {
                    Console.WriteLine("No text objects to translate");
                    OnFinishedThings(true);
                    return;
                }

                // Get API key
                string apiKey = GetGeminiApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("Gemini API key not set, cannot translate");
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
                    Console.WriteLine("Translation was cancelled");
                    return;
                }
                
                if (string.IsNullOrEmpty(translationResponse))
                {
                    Console.WriteLine($"Translation failed with {currentService} - empty response");
                    OnFinishedThings(true);
                    return;
                }

                _translationStopwatch.Stop();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");
                }

                // We've already logged the raw LLM response in the respective service
                // This would log the post-processed response, which we don't need
                // LogManager.Instance.LogLlmReply(translationResponse);

                ProcessTranslatedJSON(translationResponse);
              
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Translation was cancelled");
                SetWaitingForTranslationToFinish(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text objects: {ex.Message}");
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
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => GetSourceLanguage());
            }
            return (MainWindow.Instance.sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        }

        // Get target language from MainWindow (for future implementation)
        private string GetTargetLanguage()
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => GetTargetLanguage());
            }
            
            // Find the MainWindow instance
            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            // Get the selected ComboBoxItem
            if (mainWindow!.targetLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                // Return the content as string
                return selectedItem.Content?.ToString()!;
            }
            return "en";
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
                MonitorWindow.Instance.UpdateOCRStatusDisplay(ocrMethod, fps);
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
                    MonitorWindow.Instance.UpdateOCRStatusDisplay(ocrMethod, fps);
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
                MonitorWindow.Instance.HideOCRStatusDisplay();
                MainWindow.Instance.HideOCRStatusDisplay();
            });
        }
    }
}