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
        // Centralized OCR status tracking
        private DispatcherTimer? _ocrStatusTimer;
        private bool _isOCRActive = false;
        private DateTime _lastOcrFrameTime = DateTime.MinValue;
        private Queue<double> _ocrFrameTimes = new Queue<double>();
        private const int MAX_FPS_SAMPLES = 10;

        internal JsonElement FilterIgnoredPhrasesStatic(JsonElement resultsElement) => FilterIgnoredPhrases(resultsElement);

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

        /// <summary>
        /// Filters out low-confidence text objects from the OCR results.
        /// Different OCR providers return different granularities:
        /// - Line-level (PaddleOCR, EasyOCR): Use line confidence threshold
        /// - Word-level (Windows OCR, Google Vision, docTR): Use letter/word confidence threshold
        /// - Block-level (MangaOCR): No filtering (confidence is null)
        /// </summary>
        internal JsonElement FilterLowConfidenceCharactersStatic(JsonElement resultsElement, string ocrProvider = "") => FilterLowConfidenceCharacters(resultsElement, ocrProvider);

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
             new(" \n\r,.-:;ー・…。、~』!^へ·･" +
                 // Full-width punctuation equivalents (OCR often mixes half/full width)
                 "！？．，：；（）［］｛｝「」『』【】〔〕" +
                 // Additional common full-width characters
                 "～＾｜＼／" +
                 // Additional ellipsis variants (OCR often confuses these)
                 "‥⋯");

        // Normalize characters that OCR frequently confuses for hash comparison
        // This helps prevent re-translations when OCR produces slightly different but semantically identical text
        static readonly Dictionary<char, char> g_hashNormalizationMap = new()
        {
            // Small kana to regular hiragana (OCR often confuses these)
            { 'ゃ', 'や' }, { 'ゅ', 'ゆ' }, { 'ょ', 'よ' },
            { 'ャ', 'や' }, { 'ュ', 'ゆ' }, { 'ョ', 'よ' },  // Small katakana → hiragana
            { 'ぁ', 'あ' }, { 'ぃ', 'い' }, { 'ぅ', 'う' }, { 'ぇ', 'え' }, { 'ぉ', 'お' },
            { 'ァ', 'あ' }, { 'ィ', 'い' }, { 'ゥ', 'う' }, { 'ェ', 'え' }, { 'ォ', 'お' },  // Small katakana → hiragana
            { 'っ', 'つ' }, { 'ッ', 'つ' },  // Both small tsu → hiragana tsu
            { 'ゎ', 'わ' }, { 'ヮ', 'わ' },  // Both small wa → hiragana wa
            
            // Katakana to hiragana normalization (OCR often mixes scripts for same sounds)
            // This handles cases like やツ vs ヤツ being read differently
            { 'ア', 'あ' }, { 'イ', 'い' }, { 'ウ', 'う' }, { 'エ', 'え' }, { 'オ', 'お' },
            { 'カ', 'か' }, { 'キ', 'き' }, { 'ク', 'く' }, { 'ケ', 'け' }, { 'コ', 'こ' },
            { 'サ', 'さ' }, { 'シ', 'し' }, { 'ス', 'す' }, { 'セ', 'せ' }, { 'ソ', 'そ' },
            { 'タ', 'た' }, { 'チ', 'ち' }, { 'ツ', 'つ' }, { 'テ', 'て' }, { 'ト', 'と' },
            { 'ナ', 'な' }, { 'ニ', 'に' }, { 'ヌ', 'ぬ' }, { 'ネ', 'ね' }, { 'ノ', 'の' },
            { 'ハ', 'は' }, { 'ヒ', 'ひ' }, { 'フ', 'ふ' }, { 'ヘ', 'へ' }, { 'ホ', 'ほ' },
            { 'マ', 'ま' }, { 'ミ', 'み' }, { 'ム', 'む' }, { 'メ', 'め' }, { 'モ', 'も' },
            { 'ヤ', 'や' }, { 'ユ', 'ゆ' }, { 'ヨ', 'よ' },
            { 'ラ', 'ら' }, { 'リ', 'り' }, { 'ル', 'る' }, { 'レ', 'れ' }, { 'ロ', 'ろ' },
            { 'ワ', 'わ' }, { 'ヲ', 'を' }, { 'ン', 'ん' },
            // Voiced/semi-voiced variants
            { 'ガ', 'が' }, { 'ギ', 'ぎ' }, { 'グ', 'ぐ' }, { 'ゲ', 'げ' }, { 'ゴ', 'ご' },
            { 'ザ', 'ざ' }, { 'ジ', 'じ' }, { 'ズ', 'ず' }, { 'ゼ', 'ぜ' }, { 'ゾ', 'ぞ' },
            { 'ダ', 'だ' }, { 'ヂ', 'ぢ' }, { 'ヅ', 'づ' }, { 'デ', 'で' }, { 'ド', 'ど' },
            { 'バ', 'ば' }, { 'ビ', 'び' }, { 'ブ', 'ぶ' }, { 'ベ', 'べ' }, { 'ボ', 'ぼ' },
            { 'パ', 'ぱ' }, { 'ピ', 'ぴ' }, { 'プ', 'ぷ' }, { 'ペ', 'ぺ' }, { 'ポ', 'ぽ' },
            
            // Full-width to half-width ASCII normalization (OCR often mixes these)
            { '０', '0' }, { '１', '1' }, { '２', '2' }, { '３', '3' }, { '４', '4' },
            { '５', '5' }, { '６', '6' }, { '７', '7' }, { '８', '8' }, { '９', '9' },
            { 'Ａ', 'A' }, { 'Ｂ', 'B' }, { 'Ｃ', 'C' }, { 'Ｄ', 'D' }, { 'Ｅ', 'E' },
            { 'Ｆ', 'F' }, { 'Ｇ', 'G' }, { 'Ｈ', 'H' }, { 'Ｉ', 'I' }, { 'Ｊ', 'J' },
            { 'Ｋ', 'K' }, { 'Ｌ', 'L' }, { 'Ｍ', 'M' }, { 'Ｎ', 'N' }, { 'Ｏ', 'O' },
            { 'Ｐ', 'P' }, { 'Ｑ', 'Q' }, { 'Ｒ', 'R' }, { 'Ｓ', 'S' }, { 'Ｔ', 'T' },
            { 'Ｕ', 'U' }, { 'Ｖ', 'V' }, { 'Ｗ', 'W' }, { 'Ｘ', 'X' }, { 'Ｙ', 'Y' }, { 'Ｚ', 'Z' },
            { 'ａ', 'a' }, { 'ｂ', 'b' }, { 'ｃ', 'c' }, { 'ｄ', 'd' }, { 'ｅ', 'e' },
            { 'ｆ', 'f' }, { 'ｇ', 'g' }, { 'ｈ', 'h' }, { 'ｉ', 'i' }, { 'ｊ', 'j' },
            { 'ｋ', 'k' }, { 'ｌ', 'l' }, { 'ｍ', 'm' }, { 'ｎ', 'n' }, { 'ｏ', 'o' },
            { 'ｐ', 'p' }, { 'ｑ', 'q' }, { 'ｒ', 'r' }, { 'ｓ', 's' }, { 'ｔ', 't' },
            { 'ｕ', 'u' }, { 'ｖ', 'v' }, { 'ｗ', 'w' }, { 'ｘ', 'x' }, { 'ｙ', 'y' }, { 'ｚ', 'z' },
        };

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
                    // Skip characters we want to strip from hash
                    if (g_charsToStripFromHash.Contains(c))
                    {
                        continue;
                    }
                    
                    // Normalize characters that OCR frequently confuses
                    if (g_hashNormalizationMap.TryGetValue(c, out char normalizedChar))
                    {
                        contentBuilder.Append(normalizedChar);
                    }
                    else
                    {
                        contentBuilder.Append(c);
                    }
                }
            }

            string hash = contentBuilder.ToString();
            //Log($"Generated hash: {hash}");
            return hash;
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
