using System.IO;
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
using System.Collections.Generic;

namespace UGTLive
{
    public class Logic
    {
        private static Logic? _instance;
        private List<TextObject> _textObjects;
        private List<TextObject> _textObjectsOld;
        private Random _random;
        private Grid? _overlayContainer;
        private int _textIDCounter = 0;
        
        private DispatcherTimer _reconnectTimer;
        private string _lastOcrHash = string.Empty;
        
        // Track the current capture position
        private int _currentCaptureX;
        private int _currentCaptureY;
        private DateTime _lastChangeTime = DateTime.MinValue;
   
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

        // Constructor
        private Logic()
        {
            // Private constructor to enforce singleton pattern
            _textObjects = new List<TextObject>();
            _textObjectsOld = new List<TextObject>();
            _random = new Random();
            
            // Initialize reconnect timer with 3-second interval
            _reconnectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _reconnectTimer.Tick += ReconnectTimer_Tick;
            
            // Subscribe to SocketManager events
            SocketManager.Instance.DataReceived += OnSocketDataReceived;
            SocketManager.Instance.ConnectionChanged += OnSocketConnectionChanged;
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
                Console.WriteLine($"Loaded Gemini API key: {(string.IsNullOrEmpty(geminiApiKey) ? "Not set" : "Set")}");
                
                // Load LLM prompt
                string llmPrompt = ConfigManager.Instance.GetLlmPrompt();
                Console.WriteLine($"Loaded LLM prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : $"{llmPrompt.Length} chars")}");
                
                // Load force cursor visible setting
                // Force cursor visibility is now handled by MouseManager
                
                // Only connect to socket server if using EasyOCR
                if (MainWindow.Instance.GetSelectedOcrMethod() == "EasyOCR")
                {
                    await ConnectToSocketServerAsync();
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
        
        // Connect to socket server
        private async Task ConnectToSocketServerAsync()
        {
            try
            {
                Console.WriteLine("Attempting to connect to socket server...");
                
                // Check if already connected
                if (SocketManager.Instance.IsConnected)
                {
                    Console.WriteLine("Already connected to socket server");
                    return;
                }
                
                await SocketManager.Instance.ConnectAsync();
                
                // Start the reconnect timer if connection failed
                if (!SocketManager.Instance.IsConnected)
                {
                    Console.WriteLine("Connection failed, starting reconnect timer");
                    _reconnectAttempts = 0;
                    _hasShownConnectionErrorMessage = false;
                    _reconnectTimer.Start();
                }
                else
                {
                    Console.WriteLine("Successfully connected to socket server");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket connection error: {ex.Message}");
                
                // Start the reconnect timer
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
                _reconnectTimer.Start();
            }
        }
        
        // Track reconnection attempts
        private int _reconnectAttempts = 0;
        private bool _hasShownConnectionErrorMessage = false;
        
        // Reconnect timer tick event
        private async void ReconnectTimer_Tick(object? sender, EventArgs e)
        {
            // Only try to reconnect if we're using EasyOCR
            if (MainWindow.Instance.GetSelectedOcrMethod() != "EasyOCR")
            {
                _reconnectTimer.Stop();
                _reconnectAttempts = 0;
                //_hasShownConnectionErrorMessage = false;
                return;
            }
            
            if (!SocketManager.Instance.IsConnected)
            {
                _reconnectAttempts++;
                await SocketManager.Instance.TryReconnectAsync();
                
                // Stop the timer if connected
                if (SocketManager.Instance.IsConnected)
                {
                    _reconnectTimer.Stop();
                    _reconnectAttempts = 0;
                    //_hasShownConnectionErrorMessage = false;
                }
                // Show error message after several failed attempts (approximately 15 seconds)
                else if (_reconnectAttempts >= 1 && !_hasShownConnectionErrorMessage)
                {
                    _hasShownConnectionErrorMessage = true;
                    string serverUrl = $"localhost:{SocketManager.Instance.GetPort()}";
                    
                    string message = $"Connection Error: AI server not running at {serverUrl}\n\n" +
                                     "To fix this problem:\n\n" +
                                     "1. Navigate to the 'app/webserver' folder in your UGTLive installation\n" +
                                     "2. Run \"SetupServerCondaEnv.bat\" (only need to do this once during initial setup)\n" +
                                     "3. Run \"RunServer.bat\" to start the EasyOCR server\n\n" +
                                     "The server window should remain open while using UGTLive with EasyOCR.\n\n" +
                                     "Alternatively, you can switch to Windows OCR in the settings (no server needed).";
                    
                    MessageBoxResult result = MessageBox.Show(message + "\n\nWould you like to open the GitHub page for more detailed instructions?", 
                                              "Server Connection Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    
                    // Open web browser with documentation link only if user chooses Yes
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://github.com/SethRobinson/UGTLive",
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to open browser: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                // Stop the timer if already connected
                _reconnectTimer.Stop();
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
            }
        }
        
        // Socket data received event handler
        private void OnSocketDataReceived(object? sender, string data)
        {
            LogManager.Instance.LogOcrResponse(data);

            // Process the received data
            ProcessReceivedTextJsonData(data);
        }
        
        // Socket connection changed event handler
        private void OnSocketConnectionChanged(object? sender, bool isConnected)
        {
            // If not connected and we're using EasyOCR, start the reconnect timer
            if (!isConnected && MainWindow.Instance.GetSelectedOcrMethod() == "EasyOCR")
            {
                Console.WriteLine("Connection status changed to disconnected. Starting reconnect timer.");
                SocketManager.Instance._isConnected = false;

                _reconnectTimer.Start();
            }
            else if (isConnected)
            {
                Console.WriteLine("Connection status changed to connected. Stopping reconnect timer.");
                _reconnectTimer.Stop();
                _reconnectAttempts = 0;
                _hasShownConnectionErrorMessage = false;
            }
        }
        
        void OnFinishedThings(bool bResetTranslationStatus)
        {
            SetWaitingForTranslationToFinish(false);
            MonitorWindow.Instance.RefreshOverlays();

            // Hide translation status
            if (bResetTranslationStatus)
            {
                MonitorWindow.Instance.HideTranslationStatus();
            }
        }

        public void ResetHash()
        {
            _lastOcrHash = "";
            _lastChangeTime = DateTime.Now;
        }
        

        // Thêm phương thức xử lý kết quả từ Google Translate
        private void ProcessGoogleTranslateJson(JsonElement translatedRoot)
        {
            try
            {
                Console.WriteLine("Processing Google Translate JSON response");
                
                // Kiểm tra nếu có mảng 'translations' trong JSON
                if (translatedRoot.TryGetProperty("translations", out JsonElement translationsElement) &&
                    translationsElement.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"Found {translationsElement.GetArrayLength()} translations in Google Translate JSON");
                    
                    // Xử lý từng phần tử dịch
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
                                // Tìm text object tương ứng theo ID
                                var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                if (matchingTextObj != null)
                                {
                                    // Cập nhật text object với bản dịch
                                    matchingTextObj.TextTranslated = translatedText;
                                    matchingTextObj.UpdateUIElement();
                                    Console.WriteLine($"Updated text object {id} with Google translation");
                                }
                                else if (id.StartsWith("text_"))
                                {
                                    // Thử trích xuất index từ ID (định dạng text_X)
                                    string indexStr = id.Substring(5); // Bỏ tiền tố "text_"
                                    if (int.TryParse(indexStr, out int index) && index >= 0 && index < _textObjects.Count)
                                    {
                                        // Cập nhật theo index nếu ID khớp định dạng
                                        _textObjects[index].TextTranslated = translatedText;
                                        _textObjects[index].UpdateUIElement();
                                        Console.WriteLine($"Updated text object at index {index} with Google translation");
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
                    
                    // Cập nhật ChatBox với bản dịch mới
                    if (ChatBoxWindow.Instance != null)
                    {
                        ChatBoxWindow.Instance.UpdateChatHistory();
                    }
                    
                    // Cập nhật MonitorWindow
                    MonitorWindow.Instance.RefreshOverlays();
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
            _ocrProcessingStopwatch.Restart();
            MainWindow.Instance.SetOCRCheckIsWanted(true);

            if (GetWaitingForTranslationToFinish())
            {
                Console.WriteLine("Skipping OCR results - waiting for translation to finish");
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
                            
                            if (status == "success" && root.TryGetProperty("results", out JsonElement resultsElement))
                            {
                                // Pre-filter low-confidence characters before block detection
                                JsonElement filteredResults = FilterLowConfidenceCharacters(resultsElement);
                                
                                // Process character-level OCR data using CharacterBlockDetectionManager
                                // Use the filtered results for consistency
                                JsonElement modifiedResults = CharacterBlockDetectionManager.Instance.ProcessCharacterResults(filteredResults);
                                
                                // Filter out text objects that should be ignored based on ignore phrases
                                modifiedResults = FilterIgnoredPhrases(modifiedResults);
                                
                                // Generate content hash AFTER block detection and filtering
                                string contentHash = GenerateContentHash(modifiedResults);

                                // Handle settle time if enabled
                                double settleTime = ConfigManager.Instance.GetBlockDetectionSettleTime();
                                if (settleTime > 0)
                                {
                                    if (contentHash == _lastOcrHash)
                                    {
                                        if (_lastChangeTime == DateTime.MinValue)
                                        {
                                            OnFinishedThings(true);
                                            return; // Already rendered it, just ignore until it changes again
                                        }
                                        else
                                        {
                                            // Check if we are within the settling time
                                            if ((DateTime.Now - _lastChangeTime).TotalSeconds < settleTime)
                                            {
                                                return;
                                            }
                                            else
                                            {
                                                // Settle time reached
                                                _lastChangeTime = DateTime.MinValue;
                                                bForceRender = true;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        _lastChangeTime = DateTime.Now;
                                        _lastOcrHash = contentHash;

                                        //only run if translation is still active
                                        if (MainWindow.Instance.GetIsStarted())
                                        {

                                            MonitorWindow.Instance.ShowTranslationStatus(true);
                                            ChatBoxWindow.Instance?.ShowTranslationStatus(true);
                                        }

                                        OnFinishedThings(false);
                                        return; // Sure, it's new, but we probably aren't ready to show it yet
                                    }
                                }

                                if (contentHash == _lastOcrHash && bForceRender == false)
                                {
                                    OnFinishedThings(true);
                                    return;
                                }
                               
                                // Looks like new stuff
                                _lastOcrHash = contentHash;
                                double scale = BlockDetectionManager.Instance.GetBlockDetectionScale();
                                Console.WriteLine($"Character-level processing (scale={scale:F2}): {resultsElement.GetArrayLength()} characters → {modifiedResults.GetArrayLength()} blocks");
                                
                                // Create a new JsonDocument with the modified results
                                using (var stream = new MemoryStream())
                                {
                                    using (var writer = new Utf8JsonWriter(stream))
                                    {
                                        writer.WriteStartObject();
                                        
                                        // Copy over all existing properties except 'results'
                                        foreach (var property in root.EnumerateObject())
                                        {
                                            if (property.Name != "results")
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
                                    Console.WriteLine($"OCR JSON processing took {_ocrProcessingStopwatch.ElapsedMilliseconds} ms");

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
                                Console.WriteLine(errorMsg);
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
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error parsing rect: {ex.Message}");
                                }
                            }
                         
                            // Create text object with bounding box coordinates
                            CreateTextObjectAtPosition(text, x, y, width, height, confidence);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error displaying OCR results: {ex.Message}");
                OnFinishedThings(true);
            }
        }
        
        // Create a text object at the specified position with confidence info
        private void CreateTextObjectAtPosition(string text, double x, double y, double width, double height, double confidence)
        {

            try
            {
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    Application.Current.Dispatcher.Invoke(() => 
                        CreateTextObjectAtPosition(text, x, y, width, height, confidence));
                    return;
                }
                
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
                
                // Create text object with white text on semi-transparent black background
                SolidColorBrush textColor = new SolidColorBrush(Colors.White);
                SolidColorBrush bgColor = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0));
                
                // Add the text object to the UI
                TextObject textObject = new TextObject(
                    text,  // Just the text, without confidence
                    x, y, width, height,
                    textColor,
                    bgColor,
                    captureX, captureY  // Store original capture coordinates
                );
                textObject.ID = "text_"+GetNextTextID();

                // Adjust font size
                if (textObject.UIElement is Border border && border.Child is TextBlock textBlock)
                {
                    textBlock.FontSize = fontSize;
                }
                
                // Add to our collection
                _textObjects.Add(textObject);
                
                // Raise event to notify listeners (MonitorWindow)
                TextObjectAdded?.Invoke(this, textObject);

                if (ConfigManager.Instance.IsLeaveTranslationOnscreenEnabled()
                    && ConfigManager.Instance.IsAutoTranslateEnabled())
                {
                    //do nothing, don't want to show the source language
                } else
                {
                    textObject.UIElement = textObject.CreateUIElement();
                }
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

                MainWindow.Instance.SetOCRCheckIsWanted(true);

            }
        }
        
     
        
        // Called when a screenshot is saved (for EasyOCR method)
        public async void SendImageToEasyOCR(string filePath)
        {
            // Update Monitor Window with the screenshot
          
            try
            {
                // Check if we're using Windows OCR or EasyOCR
                string ocrMethod = MainWindow.Instance.GetSelectedOcrMethod();
                
                if (ocrMethod == "Windows OCR")
                {
                    // Windows OCR doesn't require socket connection
                    Console.WriteLine("Using Windows OCR (built-in)");
                    // ProcessScreenshot will handle the Windows OCR logic
                }
                else
                {
                    if (SocketManager.Instance.IsWaitingForSomething())
                    {
                        Console.WriteLine("Waiting for socket to connect to backend...");
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                        return;
                    }
                    
                    // Get the source language from MainWindow
                    string sourceLanguage = GetSourceLanguage()!;
                    
                    Console.WriteLine($"Processing screenshot with EasyOCR character-level OCR, language: {sourceLanguage}");
                    
                    // Check socket connection for EasyOCR
                    if (!SocketManager.Instance.IsConnected)
                    {
                        Console.WriteLine("Socket not connected, attempting to reconnect...");
                        
                        // Try to reconnect
                        bool reconnected = await SocketManager.Instance.TryReconnectAsync();

                        // Wait 300 ms
                        await Task.Delay(300);
                        
                        // Check if reconnection succeeded
                        if (!reconnected || !SocketManager.Instance.IsConnected)
                        {
                            Console.WriteLine("Reconnection failed, cannot perform OCR with EasyOCR");
                            
                            // Make sure the reconnect timer is running to keep trying
                            if (!_reconnectTimer.IsEnabled)
                            {
                                Console.WriteLine("Starting reconnect timer after failed immediate reconnection");
                                _reconnectAttempts = 0;
                                _hasShownConnectionErrorMessage = false;
                                _reconnectTimer.Start();
                            }
                            
                            MainWindow.Instance.SetOCRCheckIsWanted(true);
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Successfully reconnected to socket server");
                        }
                    }
                    
                    // If we got here, socket is connected - explicitly request character-level OCR
                    await SocketManager.Instance.SendDataAsync($"read_image|{sourceLanguage}|easyocr|char_level");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing screenshot: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error processing screenshot: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            // We'll do this after we get a reply
            // MainWindow.Instance.SetOCRCheckIsWanted(true);
        }

        // Called when the application is closing
        public void Finish()
        {
            try
            {
                // Clean up resources
                Console.WriteLine("Logic finalized");
                
                // Disconnect from socket server
                SocketManager.Instance.Disconnect();
                
                // Stop the reconnect timer
                _reconnectTimer.Stop();
                
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
                // Check if we need to run on the UI thread
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    // Run on UI thread to ensure STA compliance
                    Application.Current.Dispatcher.Invoke(() => ClearAllTextObjects());
                    return;
                }
                
                // Clear the collection
                _textObjects.Clear();
                _textIDCounter = 0;
                // No need to remove from the main window UI anymore
                
                Console.WriteLine("All text objects cleared");
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
        
        // Send text data through socket
        public async Task<bool> SendTextDataAsync(string text)
        {
            if (!SocketManager.Instance.IsConnected)
            {
                Console.WriteLine("Cannot send data: Socket not connected");
                return false;
            }
            
            try
            {
                return await SocketManager.Instance.SendDataAsync(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending text data: {ex.Message}");
                return false;
            }
        }

        //! Process structured JSON translation from ChatGPT or other services
        private void ProcessStructuredJsonTranslation(JsonElement translatedRoot)
        {
  
            try
            {
                Console.WriteLine("Processing structured JSON translation");
                // Check if we have text_blocks array in the translated JSON
                if (translatedRoot.TryGetProperty("text_blocks", out JsonElement textBlocksElement) &&
                    textBlocksElement.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"Found {textBlocksElement.GetArrayLength()} text blocks in translated JSON");
                    
                    // Process each translated block
                    for (int i = 0; i < textBlocksElement.GetArrayLength(); i++)
                    {
                        var block = textBlocksElement[i];
                        
                        if (block.TryGetProperty("id", out JsonElement idElement) &&
                            block.TryGetProperty("text", out JsonElement translatedTextElement))
                        {
                            string id = idElement.GetString() ?? "";
                            string translatedText = translatedTextElement.GetString() ?? "";
                            
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(translatedText))
                            {
                                //Console.WriteLine($"Processing text block with id={id}, text={translatedText}");
                                
                                // Find the matching text object by ID
                                var matchingTextObj = _textObjects.FirstOrDefault(t => t.ID == id);
                                if (matchingTextObj != null)
                                {
                                    // Update the corresponding text object
                                    matchingTextObj.TextTranslated = translatedText;
                                    matchingTextObj.UpdateUIElement();
                                    Console.WriteLine($"Updated text object {id} with translation");
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
                                        Console.WriteLine($"Updated text object at index {index} with translation");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Could not find text object with ID {id}");
                                    }
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
                // Kiểm tra dịch vụ đang sử dụng để xác định định dạng
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                //Console.WriteLine($"Processing translation response from {currentService} service");
                
                // Ghi log phản hồi đầy đủ để gỡ lỗi
                //Console.WriteLine($"Raw translationResponse: {translationResponse}");
                
                // Phân tích phản hồi dịch
                using JsonDocument doc = JsonDocument.Parse(translationResponse);
                JsonElement textToProcess;
                
                // Các dịch vụ khác nhau có định dạng phản hồi khác nhau
                if (currentService == "ChatGPT")
                {
                    // Định dạng ChatGPT: {"translated_text": "...", "original_text": "...", "detected_language": "..."}
                    if (doc.RootElement.TryGetProperty("translated_text", out JsonElement translatedTextElement))
                    {
                        string translatedTextJson = translatedTextElement.GetString() ?? "";
                        Console.WriteLine($"ChatGPT translated_text: {translatedTextJson}");
                        
                        // Nếu translated_text là chuỗi JSON, phân tích nó
                        if (!string.IsNullOrEmpty(translatedTextJson) && 
                            translatedTextJson.StartsWith("{") && 
                            translatedTextJson.EndsWith("}"))
                        {
                            try
                            {
                                // Tạo tùy chọn để xử lý ký tự thoát đúng cách
                                var options = new JsonDocumentOptions
                                {
                                    AllowTrailingCommas = true,
                                    CommentHandling = JsonCommentHandling.Skip
                                };
                                
                                // Phân tích JSON bên trong
                                using JsonDocument innerDoc = JsonDocument.Parse(translatedTextJson, options);
                                textToProcess = innerDoc.RootElement;
                                
                                // Xử lý trực tiếp với JSON này
                                ProcessStructuredJsonTranslation(textToProcess);
                                return;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error parsing inner JSON from translated_text: {ex.Message}");
                                // Quay lại xử lý bình thường
                            }
                        }
                    }
                }
                else if (currentService == "Gemini" || currentService == "Ollama")
                {
                    // Cấu trúc phản hồi Gemini và Ollama:
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

                            // Ghi log văn bản thô để gỡ lỗi
                            //Console.WriteLine($"Raw text from {currentService} API: {text}");

                            // Thử trích xuất đối tượng JSON từ văn bản
                            // Mô hình có thể bao quanh nó bằng markdown hoặc văn bản giải thích
                            if (text != null)
                            {
                                // Kiểm tra xem chúng ta đã có bản dịch đúng với source_language, target_language, text_blocks chưa
                                if (text.Contains("\"source_language\"") && text.Contains("\"text_blocks\""))
                                {
                                    Console.WriteLine("Direct translation detected, using it as is");
                                    
                                    // Tìm JSON trong văn bản
                                    int directJsonStart = text.IndexOf('{');
                                    int directJsonEnd = text.LastIndexOf('}');
                                    
                                    if (directJsonStart >= 0 && directJsonEnd > directJsonStart)
                                    {
                                        string directJsonText = text.Substring(directJsonStart, directJsonEnd - directJsonStart + 1);
                                        
                                        try
                                        {
                                            using JsonDocument translatedDoc = JsonDocument.Parse(directJsonText);
                                            var translatedRoot = translatedDoc.RootElement;
                                            
                                            // Cập nhật các đối tượng văn bản với bản dịch
                                            ProcessStructuredJsonTranslation(translatedRoot);
                                            return; // Bỏ qua xử lý tiếp theo
                                        }
                                        catch (JsonException ex)
                                        {
                                            Console.WriteLine($"Error parsing direct translation JSON: {ex.Message}");
                                            // Tiếp tục xử lý bình thường
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
                        Console.WriteLine("Google Translate response detected");
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

        //!Convert textobjects to json and send for translation
        public async Task TranslateTextObjectsAsync()
        {
            try
            {
                // Show translation status at the beginning
                MonitorWindow.Instance.ShowTranslationStatus(false);
                
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
               
               
                // Log the LLM request
                LogManager.Instance.LogLlmRequest(prompt, jsonToTranslate);

                _translationStopwatch.Restart();

                SetWaitingForTranslationToFinish(true);

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                
                // Call the translation API with the modified prompt if context exists
                string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt);
                
                if (string.IsNullOrEmpty(translationResponse))
                {
                    Console.WriteLine($"Translation failed with {currentService} - empty response");
                    OnFinishedThings(true);
                    return;
                }

                _translationStopwatch.Stop();
                Console.WriteLine($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");

                // We've already logged the raw LLM response in the respective service
                // This would log the post-processed response, which we don't need
                // LogManager.Instance.LogLlmReply(translationResponse);

                ProcessTranslatedJSON(translationResponse);
              
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error translating text objects: {ex.Message}");
                OnFinishedThings(true);
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
    }
}