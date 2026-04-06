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
                    string overlapStr = overlapPercent.ToString(CultureInfo.InvariantCulture);
                    string yoloStr = yoloConfidence.ToString(CultureInfo.InvariantCulture);
                    url += $"&min_region_width={minWidth}&min_region_height={minHeight}&overlap_allowed_percent={overlapStr}&yolo_confidence={yoloStr}";
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
                    string errorBody = "";
                    try
                    {
                        errorBody = await response.Content.ReadAsStringAsync();
                    }
                    catch { }

                    if (!string.IsNullOrWhiteSpace(errorBody))
                    {
                        try
                        {
                            using (JsonDocument errorDoc = JsonDocument.Parse(errorBody))
                            {
                                var errorRoot = errorDoc.RootElement;
                                string errorMsg = errorRoot.TryGetProperty("message", out var msgEl)
                                    ? msgEl.GetString() ?? errorBody
                                    : errorBody;
                                string errorType = errorRoot.TryGetProperty("error_type", out var typeEl)
                                    ? typeEl.GetString() ?? ""
                                    : "";
                                string detail = !string.IsNullOrEmpty(errorType)
                                    ? $"{errorType}: {errorMsg}"
                                    : errorMsg;
                                Log($"{serviceName} HTTP {(int)response.StatusCode}: {detail}");
                            }
                        }
                        catch (JsonException)
                        {
                            Log($"{serviceName} HTTP {(int)response.StatusCode}: {errorBody}");
                        }
                    }
                    else
                    {
                        Log($"{serviceName} HTTP request failed: {response.StatusCode}");
                    }

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
        /// Maps internal language codes to service-specific language codes.
        /// Python OCR services handle their own library-specific language code conversion,
        /// so we just pass through the code unchanged.
        /// </summary>
        private string MapLanguageForService(string language)
        {
            // Pass through - Python services handle their own mapping
            return language;
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
                        
                        Log("WARNING: EasyOCR service is required for color correction but is not running.");
                        
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
                else
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        string errorBody = "";
                        try
                        {
                            errorBody = await response.Content.ReadAsStringAsync();
                        }
                        catch { }

                        if (!string.IsNullOrWhiteSpace(errorBody))
                        {
                            Log($"Color analysis HTTP {(int)response.StatusCode}: {errorBody}");
                        }
                        else
                        {
                            Log($"Color analysis HTTP request failed: {response.StatusCode}");
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
    }
}
