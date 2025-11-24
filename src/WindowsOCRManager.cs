using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;
using System.IO;
using System.Text.Json;

using Application = System.Windows.Application;

namespace UGTLive
{
    class WindowsOCRManager
    {
        private static WindowsOCRManager? _instance;

        public static WindowsOCRManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowsOCRManager();
                }
                return _instance;
            }
        }

        // Map of language codes to Windows language tags
        private readonly Dictionary<string, string> LanguageMap = new Dictionary<string, string>
        {
            { "en", "en-US" },
            { "ja", "ja-JP" },
            { "ch_sim", "zh-CN" },
            { "es", "es-ES" },
            { "fr", "fr-FR" },
            { "it", "it-IT" },
            { "de", "de-DE" },
            { "ru", "ru-RU" },
            { "id", "id-ID" },
            { "pl", "pl-PL" },
            { "hi", "hi-IN" },
            { "ko", "ko-KR" },
            { "ar", "ar-SA" },
            { "tr", "tr-TR" },
            { "nl", "nl-NL" }
        };

        // Convert a System.Drawing.Bitmap to a Windows.Graphics.Imaging.SoftwareBitmap
        public async Task<SoftwareBitmap> ConvertBitmapToSoftwareBitmapAsync(System.Drawing.Bitmap bitmap)
        {
            try
            {
                // Convert the bitmap to a SoftwareBitmap
                using (var enhancedBitmap = OptimizeImageForOcr(bitmap))
                {
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        // Using BMP because it's faster and doesn't require compression/decompression
                        enhancedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                        memoryStream.Position = 0;
                        
                        using (var randomAccessStream = memoryStream.AsRandomAccessStream())
                        {
                            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                            return await decoder.GetSoftwareBitmapAsync(
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Premultiplied);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting bitmap to SoftwareBitmap: {ex.Message}");
                throw; // Rethrow to be handled by caller
            }
        }

        // Optimize the image for OCR by applying a sharpening filter and adjusting brightness and contrast
        private System.Drawing.Bitmap OptimizeImageForOcr(System.Drawing.Bitmap source)
        {
            // Create a new bitmap to hold the optimized image
            var result = new System.Drawing.Bitmap(source.Width, source.Height);
            
            try
            {
                // Create a graphics object for drawing on the result bitmap
                using (var graphics = System.Drawing.Graphics.FromImage(result))
                {
                    // Set the graphics object to use high-quality interpolation and smoothing
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    
                    // Create a color matrix to adjust brightness and contrast
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        // Increase contrast and brightness
                        float contrast = 1.2f;
                        // Increase brightness
                        float brightness = 0.02f;
                        
                        // Create a color matrix to adjust brightness and contrast
                        float[][] colorMatrix = {
                            new float[] {contrast, 0, 0, 0, 0},
                            new float[] {0, contrast, 0, 0, 0},
                            new float[] {0, 0, contrast, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {brightness, brightness, brightness, 0, 1}
                        };
                        
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(colorMatrix));
                        
                        // Increase gamma to make the image brighter
                        attributes.SetGamma(1.1f);
                        
                        // Draw the source bitmap onto the result bitmap, applying the color matrix and gamma correction
                        graphics.DrawImage(
                            source,
                            new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                            0, 0, source.Width, source.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attributes);
                    }
                }
                               
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Image optimization failed: {ex.Message}");
                result.Dispose();
                return new System.Drawing.Bitmap(source);
            }
        }

        // Get OCR engine for the specified language
        private OcrEngine GetOcrEngine(string languageCode)
        {
            // Convert language code to Windows language tag
            if (LanguageMap.TryGetValue(languageCode, out string? languageTag))
            {
                // Check if the language is available for OCR
                if (OcrEngine.IsLanguageSupported(new Language(languageTag)))
                {
                    return OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                }
                else
                {
                    Console.WriteLine($"Language {languageTag} not supported for Windows OCR, using user profile languages");
                    return OcrEngine.TryCreateFromUserProfileLanguages();
                }
            }
            else
            {
                // Fallback to user profile languages
                Console.WriteLine($"No mapping found for language {languageCode}, using user profile languages");
                return OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }
        
      
        
        // Get OCR lines directly from a bitmap
        public async Task<List<Windows.Media.Ocr.OcrLine>> GetOcrLinesFromBitmapAsync(System.Drawing.Bitmap bitmap, string languageCode = "en")
        {
            try
            {
                // Convert the bitmap to a SoftwareBitmap
                SoftwareBitmap softwareBitmap = await ConvertBitmapToSoftwareBitmapAsync(bitmap);
                
                // Get the OCR engine
                OcrEngine ocrEngine = GetOcrEngine(languageCode);
                
                // Perform OCR
                var result = await ocrEngine.RecognizeAsync(softwareBitmap);
                
                // Return the lines directly
                return result.Lines.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Windows OCR error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<Windows.Media.Ocr.OcrLine>();
            }
        }

        // Process Windows OCR results
        public async Task ProcessWindowsOcrResults(List<Windows.Media.Ocr.OcrLine> textLines, System.Drawing.Bitmap bitmap, string languageCode = "en")
        {
            try
            {
                // Disable immediate color correction here - moved to Logic.DisplayOcrResults
                // Check if color correction is enabled
                // bool colorCorrectionEnabled = ConfigManager.Instance.IsCloudOcrColorCorrectionEnabled();

                // Create a JSON response similar to what EasyOCR would return
                var results = new List<object>();
                
                foreach (var line in textLines)
                {
                    // Skip empty lines
                    if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    foreach (var word in line.Words)
                    {
                        string wordText = word.Text;
                        
                        // Skip empty words
                        if (string.IsNullOrWhiteSpace(wordText))
                            continue;
                        
                        var wordRect = word.BoundingRect;
                        
                        // Calculate box coordinates (polygon points)
                        var box = new[] {
                            new[] { (double)wordRect.X, (double)wordRect.Y },
                            new[] { (double)(wordRect.X + wordRect.Width), (double)wordRect.Y },
                            new[] { (double)(wordRect.X + wordRect.Width), (double)(wordRect.Y + wordRect.Height) },
                            new[] { (double)wordRect.X, (double)(wordRect.Y + wordRect.Height) }
                        };
                        
                        object? backgroundColor = null;
                        object? foregroundColor = null;

                        // Perform color correction later in Logic.DisplayOcrResults
                        /*
                        if (colorCorrectionEnabled)
                        {
                            try
                            {
                                // Crop the word from the original bitmap
                                // Ensure coordinates are within bounds
                                int x = Math.Max(0, (int)wordRect.X);
                                int y = Math.Max(0, (int)wordRect.Y);
                                int w = Math.Min((int)wordRect.Width, bitmap.Width - x);
                                int h = Math.Min((int)wordRect.Height, bitmap.Height - y);

                                if (w > 0 && h > 0)
                                {
                                    using (var crop = bitmap.Clone(new System.Drawing.Rectangle(x, y, w, h), bitmap.PixelFormat))
                                    {
                                        var colorInfo = await Logic.Instance.GetColorAnalysisAsync(crop);
                                        if (colorInfo.HasValue)
                                        {
                                            if (colorInfo.Value.TryGetProperty("background_color", out var bgProp))
                                                backgroundColor = bgProp;
                                            if (colorInfo.Value.TryGetProperty("foreground_color", out var fgProp))
                                                foregroundColor = fgProp;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Color correction failed for word '{wordText}': {ex.Message}");
                            }
                        }
                        */
                        
                        // Add the word to results
                        results.Add(new
                        {
                            text = wordText,
                            confidence = 0.9, // Windows OCR doesn't provide confidence
                            rect = box,
                            is_character = false,
                            background_color = backgroundColor,
                            foreground_color = foregroundColor
                        });
                    }
                }

                // Create a JSON response
                var response = new
                {
                    status = "success",
                    results = results,
                    processing_time_seconds = 0.1,
                    char_level = false // Indicate this is NOT character-level data
                };

                // Convert to JSON
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

                // Process the JSON response on the UI thread to handle STA requirements
                Application.Current.Dispatcher.Invoke((Action)(() => {
                    Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Windows OCR results: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

    }
}