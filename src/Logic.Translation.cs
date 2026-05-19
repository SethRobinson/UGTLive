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
        //!Convert textobjects to json and send for translation
        public async Task TranslateTextObjectsAsync()
        {
            // IMPORTANT: Set waiting flag IMMEDIATELY to prevent duplicate translation triggers
            // This must be done before any async operations or setup code
            SetWaitingForTranslationToFinish(true);
            
            // Cancel any existing translation
            CancelTranslation();
            
            // Create new cancellation token source for this translation
            _translationCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _translationCancellationTokenSource.Token;
            
            try
            {
                // Show translation status at the beginning (broadcasts to all windows via TranslationStatus)
                MainWindow.Instance.ShowTranslationStatus(false);
                
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
                prompt = prompt.Replace("{SOURCE_LANG}", sourceLanguageName);
                prompt = prompt.Replace("{TARGET_LANG}", targetLanguageName);
                _translationStopwatch.Restart();

                // Note: SetWaitingForTranslationToFinish(true) is now called at the start of this method
                // to prevent race conditions with duplicate translation triggers

                // Check for cancellation before making API call
                cancellationToken.ThrowIfCancellationRequested();

                // Create translation service based on current configuration
                ITranslationService translationService = TranslationServiceFactory.CreateService();
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                int maxRetries = ConfigManager.Instance.GetMaxTranslationRetries() + 1;
                TranslationErrorPolicy.Reset();

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    // Call the translation API with the modified prompt if context exists
                    string? translationResponse = await translationService.TranslateAsync(jsonToTranslate, prompt, cancellationToken);

                    // Check if translation was cancelled
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Log("Translation was cancelled");
                        return;
                    }

                    // A fatal error (bad model slug, bad key, 4xx) will never
                    // succeed on retry - stop immediately instead of hammering it.
                    if (TranslationErrorPolicy.AbortRetries)
                    {
                        Log($"Translation failed with {currentService} ({TranslationErrorPolicy.Reason}) - not retrying. Check the model/key in Settings.");
                        OnFinishedThings(true);
                        return;
                    }

                    if (string.IsNullOrEmpty(translationResponse))
                    {
                        if (attempt < maxRetries)
                        {
                            Log($"Translation returned empty response. Retrying (attempt {attempt + 1}/{maxRetries})...");
                            continue;
                        }
                        Log($"Translation failed with {currentService} - empty response");
                        OnFinishedThings(true);
                        return;
                    }

                    _translationStopwatch.Stop();
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Log($"Translation took {_translationStopwatch.ElapsedMilliseconds} ms");
                    }

                    if (ProcessTranslatedJSON(translationResponse))
                        break;

                    if (attempt < maxRetries)
                    {
                        Log($"Translation produced unusable response. Retrying (attempt {attempt + 1}/{maxRetries})...");
                        _translationStopwatch.Restart();
                    }
                    else if (maxRetries > 1)
                    {
                        Log($"Translation failed after {maxRetries} attempts.");
                    }
                }
                
                // Record when translation completed for cooldown mechanism
                _lastTranslationTime = DateTime.Now;
              
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

        //!Process the finished translation into text blocks and the chatbox
        bool ProcessTranslatedJSON(string translationResponse)
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
                if (currentService == "ChatGPT" || currentService == "llama.cpp"
                    || currentService == "Anthropic" || currentService == "OpenRouter"
                    || currentService == "ClaudeCli" || currentService == "CodexCli" || currentService == "GeminiCli")
                {
                    // ChatGPT-style envelope: {"translated_text": "...", "original_text": "...", "detected_language": "..."}
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
                                return true;
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
                                            return true;
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
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in ProcessTranslatedJSON: {ex.Message}");
            }
            return false;
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
            
            string playOrder = ConfigManager.Instance.GetTtsPlayOrder();
            var sortedTextObjects = AudioPlaybackManager.SortTextObjectsByPlayOrder(_textObjects.ToList(), playOrder);
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
