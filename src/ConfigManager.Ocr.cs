using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class ConfigManager
    {
        // Get ChatBox font family
        public string GetChatBoxFontFamily()
        {
            return GetValue(CHATBOX_FONT_FAMILY, "Segoe UI");
        }
        
        // Get ChatBox font size
        public double GetChatBoxFontSize()
        {
            string value = GetValue(CHATBOX_FONT_SIZE, "14");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double fontSize) && fontSize > 0)
            {
                return fontSize;
            }
            return 14; // Default font size
        }
        
        // Get ChatBox font color
        public System.Windows.Media.Color GetChatBoxFontColor()
        {
            string value = GetValue(CHATBOX_FONT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing ChatBox font color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }
        
        // Get ChatBox background color
        public System.Windows.Media.Color GetChatBoxBackgroundColor()
        {
            string value = GetValue(CHATBOX_BACKGROUND_COLOR, "#80000000"); // Default: Semi-transparent black
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing ChatBox background color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Color.FromArgb(128, 0, 0, 0);
        }
        
        // Get ChatBox window opacity (0.1 to 1.0)
        public double GetChatBoxWindowOpacity()
        {
            string value = GetValue(CHATBOX_WINDOW_OPACITY, "1.0"); // Default: 100% opacity
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity))
            {
                // Ensure minimum 10% opacity so window never disappears completely
                return Math.Max(0.1, Math.Min(1.0, opacity));
            }
            return 1.0; // Default opacity
        }
        
        // Get ChatBox background opacity (0.0 to 1.0)
        public double GetChatBoxBackgroundOpacity()
        {
            string value = GetValue(CHATBOX_BACKGROUND_OPACITY, "0.5"); // Default: 50% opacity
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) && opacity >= 0 && opacity <= 1)
            {
                return opacity;
            }
            return 0.5; // Default opacity
        }
        
        // Get Original Text color
        public System.Windows.Media.Color GetOriginalTextColor()
        {
            string value = GetValue(CHATBOX_ORIGINAL_TEXT_COLOR, "#FFF8E0A0"); // Default: Light gold
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Original Text color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.LightGoldenrodYellow;
        }
        
        // Get Translated Text color
        public System.Windows.Media.Color GetTranslatedTextColor()
        {
            string value = GetValue(CHATBOX_TRANSLATED_TEXT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Translated Text color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }
        
        // Get ChatBox history size
        public int GetChatBoxHistorySize()
        {
            string value = GetValue(CHATBOX_LINES_OF_HISTORY, "20"); // Default: 20 entries
            if (int.TryParse(value, out int historySize) && historySize > 0)
            {
                return historySize;
            }
            return 20; // Default history size
        }
        
        // Get min ChatBox text size
        public int GetChatBoxMinTextSize()
        {
            string value = GetValue(CHATBOX_MIN_TEXT_SIZE, "2"); // Default: 2 characters
            if (int.TryParse(value, out int minSize) && minSize >= 0)
            {
                return minSize;
            }
            return 2; // Default min size
        }
        
        // Set min ChatBox text size
        public void SetChatBoxMinTextSize(int value)
        {
            if (value >= 0)
            {
                _configValues[CHATBOX_MIN_TEXT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Min ChatBox text size set to: {value}");
            }
        }

        // Get/Set minimum letter confidence (Global - Legacy/Default)
        public double GetMinLetterConfidence()
        {
            string value = GetValue(MIN_LETTER_CONFIDENCE, "0.1"); // Default: 0.1 (10%)
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            return 0.1; // Default: 0.1 (10%)
        }
        
        // Get minimum letter confidence for specific provider
        public double GetMinLetterConfidence(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return GetMinLetterConfidence();

            // Clean provider name for key (e.g. "EasyOCR" -> "easyocr", "Windows OCR" -> "windows_ocr")
            string keySuffix = provider.ToLower().Replace(" ", "_");
            string key = MIN_LETTER_CONFIDENCE_PREFIX + keySuffix;
            
            // Determine default value based on provider
            string defaultValue;
            if (keySuffix == "google_vision")
            {
                defaultValue = "0.7";
            }
            else
            {
                // For other providers, default value depends on legacy global setting
                defaultValue = GetValue(MIN_LETTER_CONFIDENCE, "0.1");
            }
            
            string value = GetValue(key, defaultValue);
            
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            
            if (keySuffix == "google_vision") return 0.7;
            return 0.1;
        }

        public void SetMinLetterConfidence(double value)
        {
            if (value >= 0 && value <= 1)
            {
                _configValues[MIN_LETTER_CONFIDENCE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum letter confidence set to: {value}");
            }
            else
            {
                Console.WriteLine($"Invalid minimum letter confidence: {value}. Must be between 0 and 1.");
            }
        }
        
        public void SetMinLetterConfidence(string provider, double value)
        {
            if (string.IsNullOrEmpty(provider)) return;
            
            if (value >= 0 && value <= 1)
            {
                string keySuffix = provider.ToLower().Replace(" ", "_");
                string key = MIN_LETTER_CONFIDENCE_PREFIX + keySuffix;
                
                _configValues[key] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum letter confidence for {provider} set to: {value}");
            }
        }
        
        // Get/Set minimum line confidence (Global - Legacy/Default)
        public double GetMinLineConfidence()
        {
            string value = GetValue(MIN_LINE_CONFIDENCE, "0.2"); // Default: 0.2 (20%)
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            return 0.2; // Default: 0.2 (20%)
        }

        // Get minimum line confidence for specific provider
        public double GetMinLineConfidence(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return GetMinLineConfidence();

            string keySuffix = provider.ToLower().Replace(" ", "_");
            string key = MIN_LINE_CONFIDENCE_PREFIX + keySuffix;
            
            // Determine default value based on provider
            string defaultValue;
            if (keySuffix == "google_vision")
            {
                defaultValue = "0.7";
            }
            else
            {
                // Default to global setting for others
                defaultValue = GetValue(MIN_LINE_CONFIDENCE, "0.2");
            }
            
            string value = GetValue(key, defaultValue);
            
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            
            if (keySuffix == "google_vision") return 0.7;
            return 0.2;
        }
        
        public void SetMinLineConfidence(double value)
        {
            if (value >= 0 && value <= 1)
            {
                _configValues[MIN_LINE_CONFIDENCE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum line confidence set to: {value}");
            }
            else
            {
                Console.WriteLine($"Invalid minimum line confidence: {value}. Must be between 0 and 1.");
            }
        }

        public void SetMinLineConfidence(string provider, double value)
        {
            if (string.IsNullOrEmpty(provider)) return;

            if (value >= 0 && value <= 1)
            {
                string keySuffix = provider.ToLower().Replace(" ", "_");
                string key = MIN_LINE_CONFIDENCE_PREFIX + keySuffix;

                _configValues[key] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum line confidence for {provider} set to: {value}");
            }
        }

        // Get/Set: Glue docTR lines into paragraphs
        // Get/Set: Manga OCR minimum region width
        public int GetMangaOcrMinRegionWidth()
        {
            string value = GetValue(MANGA_OCR_MIN_REGION_WIDTH, "10");
            if (int.TryParse(value, out int width) && width >= 0)
            {
                return width;
            }
            return 10; // Default: 10 pixels
        }

        public void SetMangaOcrMinRegionWidth(int width)
        {
            if (width >= 0)
            {
                _configValues[MANGA_OCR_MIN_REGION_WIDTH] = width.ToString();
                SaveConfig();
                Console.WriteLine($"Manga OCR minimum region width set to: {width}");
            }
            else
            {
                Console.WriteLine($"Invalid Manga OCR minimum region width: {width}. Must be non-negative.");
            }
        }

        // Get/Set: Manga OCR minimum region height
        public int GetMangaOcrMinRegionHeight()
        {
            string value = GetValue(MANGA_OCR_MIN_REGION_HEIGHT, "10");
            if (int.TryParse(value, out int height) && height >= 0)
            {
                return height;
            }
            return 10; // Default: 10 pixels
        }

        public void SetMangaOcrMinRegionHeight(int height)
        {
            if (height >= 0)
            {
                _configValues[MANGA_OCR_MIN_REGION_HEIGHT] = height.ToString();
                SaveConfig();
                Console.WriteLine($"Manga OCR minimum region height set to: {height}");
            }
            else
            {
                Console.WriteLine($"Invalid Manga OCR minimum region height: {height}. Must be non-negative.");
            }
        }

        // Get/Set: Manga OCR overlap allowed percentage
        public double GetMangaOcrOverlapAllowedPercent()
        {
            string value = GetValue(MANGA_OCR_OVERLAP_ALLOWED_PERCENT, "90");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) && percent >= 0 && percent <= 100)
            {
                return percent;
            }
            return 90.0; // Default: 90%
        }

        public void SetMangaOcrOverlapAllowedPercent(double percent)
        {
            if (percent >= 0 && percent <= 100)
            {
                _configValues[MANGA_OCR_OVERLAP_ALLOWED_PERCENT] = percent.ToString("F1", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Manga OCR overlap allowed percent set to: {percent:F1}%");
            }
            else
            {
                Console.WriteLine($"Invalid Manga OCR overlap allowed percent: {percent}. Must be between 0 and 100.");
            }
        }

        // Get/Set: Manga OCR YOLO confidence threshold
        public double GetMangaOcrYoloConfidence()
        {
            string value = GetValue(MANGA_OCR_YOLO_CONFIDENCE, "0.60");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) && confidence >= 0.0 && confidence <= 1.0)
            {
                return confidence;
            }
            return 0.60; // Default: 0.60 (raised from 0.25 to reduce false positives like tree bark)
        }

        public void SetMangaOcrYoloConfidence(double confidence)
        {
            if (confidence >= 0.0 && confidence <= 1.0)
            {
                _configValues[MANGA_OCR_YOLO_CONFIDENCE] = confidence.ToString("F2", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Manga OCR YOLO confidence threshold set to: {confidence:F2}");
            }
            else
            {
                Console.WriteLine($"Invalid YOLO confidence threshold: {confidence}. Must be between 0.0 and 1.0.");
            }
        }

        public bool GetPaddleOcrUseAngleCls()
        {
            string value = GetValue(PADDLE_OCR_USE_ANGLE_CLS, "false");
            return bool.TryParse(value, out bool result) && result;
        }

        public void SetPaddleOcrUseAngleCls(bool enabled)
        {
            _configValues[PADDLE_OCR_USE_ANGLE_CLS] = enabled.ToString();
            SaveConfig();
        }

        // Get/Set: OCR processing mode (Deprecated/Removed)
        // Logic now handled automatically by UniversalBlockDetector
        
        // Get all ignore phrases as a list of tuples (phrase, exactMatch)

        //OPTIMIZE:  Why is the AI doing all this work over and over?  Should be caching the results
        public List<(string Phrase, bool ExactMatch)> GetIgnorePhrases()
        {
            List<(string, bool)> result = new List<(string, bool)>();
            string value = GetValue(IGNORE_PHRASES, "");
            
            if (!string.IsNullOrEmpty(value))
            {
                // Fix the format if it contains the key prefix (from old format)
                if (value.StartsWith(IGNORE_PHRASES + "|"))
                {
                    value = value.Substring((IGNORE_PHRASES + "|").Length);
                }
                
                // Format should be: phrase1|True|phrase2|False
                string[] parts = value.Split('|');
                
                // Process in pairs
                for (int i = 0; i < parts.Length - 1; i += 2)
                {
                    if (i + 1 < parts.Length)
                    {
                        string phrase = parts[i];
                        bool exactMatch = bool.TryParse(parts[i + 1], out bool match) && match;
                        
                        if (!string.IsNullOrEmpty(phrase))
                        {
                            result.Add((phrase, exactMatch));
                            //Console.WriteLine($"Loaded ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                        }
                    }
                }
            }
            
            return result;
        }
        
        // Save all ignore phrases
        public void SaveIgnorePhrases(List<(string Phrase, bool ExactMatch)> phrases)
        {
            StringBuilder sb = new StringBuilder();
            
            foreach (var (phrase, exactMatch) in phrases)
            {
                if (!string.IsNullOrEmpty(phrase))
                {
                    sb.Append(phrase);
                    sb.Append('|');
                    sb.Append(exactMatch.ToString());
                    sb.Append('|');
                }
            }
            
            _configValues[IGNORE_PHRASES] = sb.ToString();
            SaveConfig();
            Console.WriteLine($"Saved {phrases.Count} ignore phrases: {sb.ToString()}");
        }
        
        // Add a single ignore phrase
        public void AddIgnorePhrase(string phrase, bool exactMatch)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            
            // Check if the phrase already exists
            if (!phrases.Any(p => p.Phrase == phrase))
            {
                phrases.Add((phrase, exactMatch));
                SaveIgnorePhrases(phrases);
                Console.WriteLine($"Added ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
            }
        }
        
        // Remove a single ignore phrase
        public void RemoveIgnorePhrase(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            var originalCount = phrases.Count;
            
            phrases.RemoveAll(p => p.Phrase == phrase);
            
            if (phrases.Count < originalCount)
            {
                SaveIgnorePhrases(phrases);
                Console.WriteLine($"Removed ignore phrase: '{phrase}'");
            }
        }
        
        // Update exact match setting for a phrase
        public void UpdateIgnorePhraseExactMatch(string phrase, bool exactMatch)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            
            for (int i = 0; i < phrases.Count; i++)
            {
                if (phrases[i].Phrase == phrase)
                {
                    phrases[i] = (phrase, exactMatch);
                    SaveIgnorePhrases(phrases);
                    Console.WriteLine($"Updated ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                    break;
                }
            }
        }
        public string GetGoogleTranslateApiKey()
        {
            return GetValue(GOOGLE_TRANSLATE_API_KEY, "");
        }

        public void SetGoogleTranslateApiKey(string apiKey)
        {
            _configValues[GOOGLE_TRANSLATE_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google Translate API key updated");
        }

        public string GetGoogleVisionApiKey()
        {
            return GetValue(GOOGLE_VISION_API_KEY, "");
        }

        public void SetGoogleVisionApiKey(string apiKey)
        {
            _configValues[GOOGLE_VISION_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google Vision API key updated");
        }

        public double GetGoogleVisionHorizontalGlue()
        {
            string value = GetValue(GOOGLE_VISION_HORIZONTAL_GLUE, "1.5");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 1.5; // Default: 1.5 character widths
        }

        public void SetGoogleVisionHorizontalGlue(double value)
        {
            _configValues[GOOGLE_VISION_HORIZONTAL_GLUE] = value.ToString();
            SaveConfig();
            Console.WriteLine($"Google Vision horizontal glue updated to {value}");
        }

        public double GetGoogleVisionVerticalGlue()
        {
            string value = GetValue(GOOGLE_VISION_VERTICAL_GLUE, "0.5");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 0.5; // Default: 0.5 character heights
        }

        public void SetGoogleVisionVerticalGlue(double value)
        {
            _configValues[GOOGLE_VISION_VERTICAL_GLUE] = value.ToString();
            SaveConfig();
            Console.WriteLine($"Google Vision vertical glue updated to {value}");
        }

        public bool GetGoogleVisionKeepLinefeeds()
        {
            return GetBoolValue(GOOGLE_VISION_KEEP_LINEFEEDS, true); // Default to true
        }

        public void SetGoogleVisionKeepLinefeeds(bool value)
        {
            SetBoolValue(GOOGLE_VISION_KEEP_LINEFEEDS, value);
            SaveConfig();
            Console.WriteLine($"Google Vision keep linefeeds set to: {value}");
        }

        // Per-OCR settings methods
        // These allow storing horizontal glue, vertical glue, keep linefeeds, and leave translation onscreen settings per OCR method
        
        // Helper method to normalize OCR method names for config keys
        private string NormalizeOcrMethodName(string ocrMethod)
        {
            return ocrMethod.Replace(" ", "_").ToLower();
        }
        
        // Horizontal Glue (per-OCR)
        public double GetHorizontalGlue(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HORIZONTAL_GLUE_PREFIX + normalizedMethod;
            string value = GetValue(key, "1.0"); // Default: 2.0 character widths
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 1.0;
        }
        
        public void SetHorizontalGlue(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HORIZONTAL_GLUE_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} horizontal glue updated to {value}");
        }
        
        // Vertical Glue (per-OCR)
        public double GetVerticalGlue(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_PREFIX + normalizedMethod;
            string value = GetValue(key, "1.0"); // Default: 2.0 line heights
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 1.0;
        }
        
        public void SetVerticalGlue(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} vertical glue updated to {value}");
        }
        
        // Vertical Glue Overlap (per-OCR)
        public double GetVerticalGlueOverlap(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_OVERLAP_PREFIX + normalizedMethod;
            string value = GetValue(key, "20.0"); // Default: 20%
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 20.0;
        }
        
        public void SetVerticalGlueOverlap(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_OVERLAP_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} vertical glue overlap updated to {value}");
        }
        
        // Height Similarity (per-OCR)
        public double GetHeightSimilarity(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HEIGHT_SIMILARITY_PREFIX + normalizedMethod;
            
            // Windows OCR returns word-level results (not character-level), so it needs a lower default
            // to allow more height variation between words
            string defaultValue = normalizedMethod == "windows_ocr" ? "10.0" : "50.0";
            
            string value = GetValue(key, defaultValue);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            
            // Fallback defaults
            return normalizedMethod == "windows_ocr" ? 10.0 : 50.0;
        }
        
        public void SetHeightSimilarity(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HEIGHT_SIMILARITY_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} height similarity updated to {value}");
        }
        
        // Keep Linefeeds (per-OCR)
        public bool GetKeepLinefeeds(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = KEEP_LINEFEEDS_PREFIX + normalizedMethod;
            return GetBoolValue(key, true); // Default to true
        }
        
        public void SetKeepLinefeeds(string ocrMethod, bool value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = KEEP_LINEFEEDS_PREFIX + normalizedMethod;
            SetBoolValue(key, value);
            SaveConfig();
            Console.WriteLine($"{ocrMethod} keep linefeeds set to: {value}");
        }
        
        // Leave Translation Onscreen (per-OCR)
        public bool GetLeaveTranslationOnscreen(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = LEAVE_TRANSLATION_ONSCREEN_PREFIX + normalizedMethod;
            
            // Default is true for all OCR methods except MangaOCR
            bool defaultValue = !ocrMethod.Equals("MangaOCR", StringComparison.OrdinalIgnoreCase);
            
            return GetBoolValue(key, defaultValue);
        }
        
        public void SetLeaveTranslationOnscreen(string ocrMethod, bool value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = LEAVE_TRANSLATION_ONSCREEN_PREFIX + normalizedMethod;
            SetBoolValue(key, value);
            SaveConfig();
            Console.WriteLine($"{ocrMethod} leave translation onscreen set to: {value}");
        }

        // Get/Set OCR window position and size
        public double GetOcrWindowLeft()
        {
            string value = GetValue(OCR_WINDOW_LEFT, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double left))
            {
                return left;
            }
            return double.NaN;
        }

        public void SetOcrWindowLeft(double left)
        {
            _configValues[OCR_WINDOW_LEFT] = left.ToString();
            SaveConfig();
        }

        public double GetOcrWindowTop()
        {
            string value = GetValue(OCR_WINDOW_TOP, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
            {
                return top;
            }
            return double.NaN;
        }

        public void SetOcrWindowTop(double top)
        {
            _configValues[OCR_WINDOW_TOP] = top.ToString();
            SaveConfig();
        }

        public double GetOcrWindowWidth()
        {
            string value = GetValue(OCR_WINDOW_WIDTH, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
            {
                return width;
            }
            return double.NaN;
        }

        public void SetOcrWindowWidth(double width)
        {
            _configValues[OCR_WINDOW_WIDTH] = width.ToString();
            SaveConfig();
        }

        public double GetOcrWindowHeight()
        {
            string value = GetValue(OCR_WINDOW_HEIGHT, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                return height;
            }
            return double.NaN;
        }

        public void SetOcrWindowHeight(double height)
        {
            _configValues[OCR_WINDOW_HEIGHT] = height.ToString();
            SaveConfig();
        }
        
        // Batch set OCR window position (saves only once)
        public void SetOcrWindowPosition(double left, double top)
        {
            _configValues[OCR_WINDOW_LEFT] = left.ToString();
            _configValues[OCR_WINDOW_TOP] = top.ToString();
            SaveConfig();
        }
        
        // Batch set OCR window size (saves only once)
        public void SetOcrWindowSize(double width, double height)
        {
            _configValues[OCR_WINDOW_WIDTH] = width.ToString();
            _configValues[OCR_WINDOW_HEIGHT] = height.ToString();
            SaveConfig();
        }
        
        // Batch set OCR window position and size (saves only once)
        public void SetOcrWindowBounds(double left, double top, double width, double height)
        {
            _configValues[OCR_WINDOW_LEFT] = left.ToString();
            _configValues[OCR_WINDOW_TOP] = top.ToString();
            _configValues[OCR_WINDOW_WIDTH] = width.ToString();
            _configValues[OCR_WINDOW_HEIGHT] = height.ToString();
            SaveConfig();
        }
        
        // Toolbar offset from main window's top-right corner
        public double GetToolbarOffsetX()
        {
            string value = GetValue(TOOLBAR_OFFSET_X, "5");
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : 5;
        }

        public double GetToolbarOffsetY()
        {
            string value = GetValue(TOOLBAR_OFFSET_Y, "0");
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ? y : 0;
        }

        public void SetToolbarOffset(double offsetX, double offsetY)
        {
            _configValues[TOOLBAR_OFFSET_X] = offsetX.ToString();
            _configValues[TOOLBAR_OFFSET_Y] = offsetY.ToString();
            SaveConfig();
        }

        // Get/Set ChatBox window position and size
        public double GetChatBoxWindowLeft()
        {
            string value = GetValue(CHATBOX_WINDOW_LEFT, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double left))
            {
                return left;
            }
            return double.NaN;
        }

        public double GetChatBoxWindowTop()
        {
            string value = GetValue(CHATBOX_WINDOW_TOP, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
            {
                return top;
            }
            return double.NaN;
        }

        public double GetChatBoxWindowWidth()
        {
            string value = GetValue(CHATBOX_WINDOW_WIDTH, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
            {
                return width;
            }
            return double.NaN;
        }

        public double GetChatBoxWindowHeight()
        {
            string value = GetValue(CHATBOX_WINDOW_HEIGHT, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                return height;
            }
            return double.NaN;
        }

        public bool GetChatBoxWindowWasActive()
        {
            return GetBoolValue(CHATBOX_WINDOW_WAS_ACTIVE, false);
        }

        // Batch set ChatBox window bounds and active state (saves only once)
        public void SetChatBoxWindowState(double left, double top, double width, double height, bool wasActive)
        {
            _configValues[CHATBOX_WINDOW_LEFT] = left.ToString();
            _configValues[CHATBOX_WINDOW_TOP] = top.ToString();
            _configValues[CHATBOX_WINDOW_WIDTH] = width.ToString();
            _configValues[CHATBOX_WINDOW_HEIGHT] = height.ToString();
            _configValues[CHATBOX_WINDOW_WAS_ACTIVE] = wasActive.ToString().ToLower();
            SaveConfig();
        }

        // Get/Set Monitor window position and size
        public double GetMonitorWindowLeft()
        {
            string value = GetValue(MONITOR_WINDOW_LEFT, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double left))
            {
                return left;
            }
            return double.NaN;
        }

        public double GetMonitorWindowTop()
        {
            string value = GetValue(MONITOR_WINDOW_TOP, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
            {
                return top;
            }
            return double.NaN;
        }

        public double GetMonitorWindowWidth()
        {
            string value = GetValue(MONITOR_WINDOW_WIDTH, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
            {
                return width;
            }
            return double.NaN;
        }

        public double GetMonitorWindowHeight()
        {
            string value = GetValue(MONITOR_WINDOW_HEIGHT, "");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                return height;
            }
            return double.NaN;
        }

        public bool GetMonitorWindowWasActive()
        {
            return GetBoolValue(MONITOR_WINDOW_WAS_ACTIVE, false);
        }

        // Batch set Monitor window bounds and active state (saves only once)
        public void SetMonitorWindowState(double left, double top, double width, double height, bool wasActive)
        {
            _configValues[MONITOR_WINDOW_LEFT] = left.ToString();
            _configValues[MONITOR_WINDOW_TOP] = top.ToString();
            _configValues[MONITOR_WINDOW_WIDTH] = width.ToString();
            _configValues[MONITOR_WINDOW_HEIGHT] = height.ToString();
            _configValues[MONITOR_WINDOW_WAS_ACTIVE] = wasActive.ToString().ToLower();
            SaveConfig();
        }

        // Lesson feature methods
        
        // Get/Set Lesson Prompt Template
        // The template should contain {0} as a placeholder for the text to learn
        public string GetLessonPromptTemplate()
        {
            string defaultValue = "Create a comprehensive lesson to help me learn about this Japanese text and its translation: \"{0}\"\n\nPlease include:\n1. A detailed breakdown table with columns for: Japanese text, Reading (furigana), Literal meaning, and Grammar notes\n2. Key vocabulary with example sentences\n3. Cultural or contextual notes if relevant";
            return GetValue(LESSON_PROMPT_TEMPLATE, defaultValue);
        }
        
        public void SetLessonPromptTemplate(string template)
        {
            if (!string.IsNullOrWhiteSpace(template))
            {
                // Trim leading and trailing newlines/whitespace to prevent accumulation
                _configValues[LESSON_PROMPT_TEMPLATE] = template.TrimStart('\r', '\n').TrimEnd('\r', '\n', ' ', '\t');
                SaveConfig();
                Console.WriteLine("Lesson prompt template updated");
            }
        }
        
        // Get/Set Lesson URL Template
        // The template should contain {0} as a placeholder for the URL-encoded prompt
        public string GetLessonUrlTemplate()
        {
            string defaultValue = "https://chat.openai.com/?q={0}";
            return GetValue(LESSON_URL_TEMPLATE, defaultValue);
        }
        
        public void SetLessonUrlTemplate(string urlTemplate)
        {
            if (!string.IsNullOrWhiteSpace(urlTemplate))
            {
                // Trim leading and trailing newlines/whitespace to prevent accumulation
                _configValues[LESSON_URL_TEMPLATE] = urlTemplate.TrimStart('\r', '\n').TrimEnd('\r', '\n', ' ', '\t');
                SaveConfig();
                Console.WriteLine("Lesson URL template updated");
            }
        }
        
        // Service AutoStart preferences
        public bool GetServiceAutoStart(string serviceName)
        {
            string key = $"service_{serviceName}_autostart";
            string value = GetValue(key, "false");
            return value.ToLower() == "true";
        }
        
        public void SetServiceAutoStart(string serviceName, bool autoStart)
        {
            string key = $"service_{serviceName}_autostart";
            _configValues[key] = autoStart ? "true" : "false";
            SaveConfig();
        }

        // Screenshot saving settings
        public string GetScreenshotFilename()
        {
            return GetValue(SCREENSHOT_FILENAME, "screenshot_{DATE}_{TIME}_");
        }

        public void SetScreenshotFilename(string filename)
        {
            _configValues[SCREENSHOT_FILENAME] = filename;
            SaveConfig();
        }

        public string GetScreenshotFolder()
        {
            return GetValue(SCREENSHOT_FOLDER, "output/screenshots");
        }

        public void SetScreenshotFolder(string folder)
        {
            _configValues[SCREENSHOT_FOLDER] = folder;
            SaveConfig();
        }

        public string GetScreenshotType()
        {
            return GetValue(SCREENSHOT_TYPE, "Both");
        }

        public void SetScreenshotType(string type)
        {
            _configValues[SCREENSHOT_TYPE] = type;
            SaveConfig();
        }
    }
}
