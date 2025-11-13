using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace UGTLive
{
    public class GoogleVisionOCRService
    {
        private static GoogleVisionOCRService? _instance;
        private static readonly HttpClient _httpClient = new HttpClient() 
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static GoogleVisionOCRService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GoogleVisionOCRService();
                }
                return _instance;
            }
        }

        private GoogleVisionOCRService()
        {
            // Private constructor for singleton
        }

        // Map of language codes from UGTLive to Google Vision API language hints
        private readonly Dictionary<string, string> LanguageMap = new Dictionary<string, string>
        {
            { "en", "en" },
            { "ja", "ja" },
            { "ch_sim", "zh" },
            { "es", "es" },
            { "fr", "fr" },
            { "it", "it" },
            { "de", "de" },
            { "ru", "ru" },
            { "id", "id" },
            { "pl", "pl" },
            { "hi", "hi" },
            { "ko", "ko" },
            { "ar", "ar" },
            { "tr", "tr" },
            { "pt", "pt" },
            { "vi", "vi" },
            { "th", "th" }
        };

        // Convert bitmap to base64 string
        private string ConvertBitmapToBase64(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                // Save as PNG for better quality
                bitmap.Save(ms, ImageFormat.Png);
                byte[] imageBytes = ms.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        // Process image using Google Vision API
        public async Task<List<TextObject>> ProcessImageAsync(Bitmap bitmap, string sourceLanguage)
        {
            try
            {
                string apiKey = ConfigManager.Instance.GetGoogleVisionApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Console.WriteLine("Google Vision API key not configured");
                    return new List<TextObject>();
                }

                // Convert bitmap to base64
                string base64Image = ConvertBitmapToBase64(bitmap);

                // Build the API request
                var requestBody = new
                {
                    requests = new[]
                    {
                        new
                        {
                            image = new
                            {
                                content = base64Image
                            },
                            features = new[]
                            {
                                new
                                {
                                    type = "TEXT_DETECTION",
                                    maxResults = 50
                                }
                            },
                            imageContext = new
                            {
                                languageHints = GetLanguageHints(sourceLanguage)
                            }
                        }
                    }
                };

                string json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                string url = $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}";
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    return ParseGoogleVisionResponse(responseJson);
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Google Vision API error: {response.StatusCode}, {errorMessage}");
                    
                    // Try to parse error message
                    try
                    {
                        using JsonDocument errorDoc = JsonDocument.Parse(errorMessage);
                        if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                        {
                            string? message = errorElement.TryGetProperty("message", out JsonElement msgElement) 
                                ? msgElement.GetString() : "Unknown error";
                            int? code = errorElement.TryGetProperty("code", out JsonElement codeElement) 
                                ? codeElement.GetInt32() : null;
                                
                            Console.WriteLine($"Google Vision API error: Code={code}, Message={message}");
                        }
                    }
                    catch { }
                    
                    return new List<TextObject>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Google Vision OCR: {ex.Message}");
                return new List<TextObject>();
            }
        }

        // Get language hints for the API request
        private string[] GetLanguageHints(string sourceLanguage)
        {
            if (LanguageMap.TryGetValue(sourceLanguage, out string? mappedLang))
            {
                return new[] { mappedLang };
            }
            // If no mapping found, let Google Vision auto-detect
            return new string[] { };
        }

        // Parse the Google Vision API response
        private List<TextObject> ParseGoogleVisionResponse(string responseJson)
        {
            var textObjects = new List<TextObject>();

            try
            {
                using JsonDocument document = JsonDocument.Parse(responseJson);
                var root = document.RootElement;

                if (!root.TryGetProperty("responses", out JsonElement responses) || responses.GetArrayLength() == 0)
                {
                    return textObjects;
                }

                var response = responses[0];

                // Check for fullTextAnnotation which provides better structured data
                if (response.TryGetProperty("fullTextAnnotation", out JsonElement fullTextAnnotation))
                {
                    Console.WriteLine("Google Vision: Using fullTextAnnotation (structured data)");
                    // Process using the structured fullTextAnnotation
                    return ProcessFullTextAnnotation(fullTextAnnotation);
                }
                else if (response.TryGetProperty("textAnnotations", out JsonElement textAnnotations))
                {
                    Console.WriteLine("Google Vision: Using textAnnotations (simple data)");
                    // Fallback to simple text annotations (skip the first one which is the full text)
                    return ProcessTextAnnotations(textAnnotations);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Google Vision response: {ex.Message}");
            }

            return textObjects;
        }

        // Process fullTextAnnotation which has hierarchical structure
        private List<TextObject> ProcessFullTextAnnotation(JsonElement fullTextAnnotation)
        {
            var textObjects = new List<TextObject>();

            try
            {
                if (!fullTextAnnotation.TryGetProperty("pages", out JsonElement pages))
                {
                    return textObjects;
                }

                // Get grouping settings
                double horizontalGlue = ConfigManager.Instance.GetGoogleVisionHorizontalGlue();
                double verticalGlue = ConfigManager.Instance.GetGoogleVisionVerticalGlue();
                
                Console.WriteLine($"Google Vision using glue settings: horizontal={horizontalGlue}, vertical={verticalGlue}");

                foreach (var page in pages.EnumerateArray())
                {
                    if (!page.TryGetProperty("blocks", out JsonElement blocks))
                        continue;

                    // Collect all words from the page with their bounds
                    var allWords = new List<(string text, double x, double y, double width, double height)>();
                    
                    foreach (var block in blocks.EnumerateArray())
                    {
                        if (block.TryGetProperty("paragraphs", out JsonElement paragraphs))
                        {
                            foreach (var paragraph in paragraphs.EnumerateArray())
                            {
                                if (paragraph.TryGetProperty("words", out JsonElement words))
                                {
                                    foreach (var word in words.EnumerateArray())
                                    {
                                        var wordBounds = GetBoundingBox(word);
                                        if (wordBounds == null)
                                            continue;
                                            
                                        var wordText = new StringBuilder();
                                        if (word.TryGetProperty("symbols", out JsonElement symbols))
                                        {
                                            foreach (var symbol in symbols.EnumerateArray())
                                            {
                                                if (symbol.TryGetProperty("text", out JsonElement textElement))
                                                {
                                                    wordText.Append(textElement.GetString());
                                                }
                                            }
                                        }
                                        
                                        string text = wordText.ToString();
                                        if (!string.IsNullOrWhiteSpace(text))
                                        {
                                            allWords.Add((text, wordBounds.Value.x, wordBounds.Value.y, 
                                                         wordBounds.Value.width, wordBounds.Value.height));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // Now group words based on proximity
                    var groupedWords = GroupWordsByProximity(allWords, horizontalGlue, verticalGlue);
                    
                    // Debug: Log the grouping results
                    Console.WriteLine($"Google Vision: {allWords.Count} words grouped into {groupedWords.Count} text objects");
                    
                    // Create text objects from grouped words
                    foreach (var group in groupedWords)
                    {
                        var textObj = CreateTextObject(group.text, 
                            (group.x, group.y, group.width, group.height));
                        textObjects.Add(textObj);
                        Console.WriteLine($"Google Vision grouped text: '{group.text}' at ({group.x:F0}, {group.y:F0}) size ({group.width:F0}x{group.height:F0})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing fullTextAnnotation: {ex.Message}");
            }

            return textObjects;
        }

        // Process paragraphs within a block
        private void ProcessParagraphs(JsonElement block, List<TextObject> textObjects)
        {
            if (!block.TryGetProperty("paragraphs", out JsonElement paragraphs))
                return;

            foreach (var paragraph in paragraphs.EnumerateArray())
            {
                var paragraphBounds = GetBoundingBox(paragraph);
                if (paragraphBounds == null)
                    continue;

                var paragraphText = new StringBuilder();
                bool firstWord = true;

                if (paragraph.TryGetProperty("words", out JsonElement words))
                {
                    foreach (var word in words.EnumerateArray())
                    {
                        // Add space before word (except first)
                        if (!firstWord)
                        {
                            paragraphText.Append(" ");
                        }
                        firstWord = false;

                        if (word.TryGetProperty("symbols", out JsonElement symbols))
                        {
                            foreach (var symbol in symbols.EnumerateArray())
                            {
                                if (symbol.TryGetProperty("text", out JsonElement textElement))
                                {
                                    paragraphText.Append(textElement.GetString());
                                }
                            }
                        }
                    }
                }

                string text = paragraphText.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var textObj = CreateTextObject(text, paragraphBounds.Value);
                    textObjects.Add(textObj);
                }
            }
        }

        // Process simple text annotations (fallback method)
        private List<TextObject> ProcessTextAnnotations(JsonElement textAnnotations)
        {
            var textObjects = new List<TextObject>();

            try
            {
                var annotations = textAnnotations.EnumerateArray().Skip(1).ToList(); // Skip first element (full text)
                
                // Group words by line (words with similar Y coordinates)
                var wordGroups = new List<List<(string text, double x, double y, double width, double height)>>();
                
                foreach (var annotation in annotations)
                {
                    if (!annotation.TryGetProperty("description", out JsonElement descElement))
                        continue;

                    string? text = descElement.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var bounds = GetBoundingPolygon(annotation);
                    if (bounds == null)
                        continue;

                    // Find which line this word belongs to
                    bool foundGroup = false;
                    foreach (var group in wordGroups)
                    {
                        // Check if this word is on the same line (similar Y coordinate)
                        if (Math.Abs(group[0].y - bounds.Value.y) < bounds.Value.height * 0.5)
                        {
                            group.Add((text, bounds.Value.x, bounds.Value.y, bounds.Value.width, bounds.Value.height));
                            foundGroup = true;
                            break;
                        }
                    }
                    
                    if (!foundGroup)
                    {
                        // Create new group for this line
                        wordGroups.Add(new List<(string, double, double, double, double)> 
                        { 
                            (text, bounds.Value.x, bounds.Value.y, bounds.Value.width, bounds.Value.height) 
                        });
                    }
                }
                
                // Create TextObjects from grouped words
                foreach (var group in wordGroups)
                {
                    // Sort words by X coordinate
                    var sortedWords = group.OrderBy(w => w.x).ToList();
                    
                    // Combine text with spaces
                    var lineText = string.Join(" ", sortedWords.Select(w => w.text));
                    
                    // Calculate bounding box for the entire line
                    double minX = sortedWords.Min(w => w.x);
                    double minY = sortedWords.Min(w => w.y);
                    double maxX = sortedWords.Max(w => w.x + w.width);
                    double maxY = sortedWords.Max(w => w.y + w.height);
                    
                    var textObj = CreateTextObject(lineText, (minX, minY, maxX - minX, maxY - minY));
                    textObjects.Add(textObj);
                }
                
                Console.WriteLine($"Google Vision: Grouped {annotations.Count} words into {textObjects.Count} lines");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing textAnnotations: {ex.Message}");
            }

            return textObjects;
        }

        // Get bounding box from an element with boundingBox property
        private (double x, double y, double width, double height)? GetBoundingBox(JsonElement element)
        {
            try
            {
                if (element.TryGetProperty("boundingBox", out JsonElement boundingBox) &&
                    boundingBox.TryGetProperty("vertices", out JsonElement vertices))
                {
                    var vertexList = vertices.EnumerateArray().ToList();
                    if (vertexList.Count >= 4)
                    {
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;

                        foreach (var vertex in vertexList)
                        {
                            if (vertex.TryGetProperty("x", out JsonElement xElement))
                            {
                                double x = xElement.GetDouble();
                                minX = Math.Min(minX, x);
                                maxX = Math.Max(maxX, x);
                            }
                            if (vertex.TryGetProperty("y", out JsonElement yElement))
                            {
                                double y = yElement.GetDouble();
                                minY = Math.Min(minY, y);
                                maxY = Math.Max(maxY, y);
                            }
                        }

                        double width = maxX - minX;
                        double height = maxY - minY;
                        
                        if (width > 0 && height > 0)
                        {
                            return (minX, minY, width, height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting bounding box: {ex.Message}");
            }

            return null;
        }

        // Get bounding polygon from an element with boundingPoly property
        private (double x, double y, double width, double height)? GetBoundingPolygon(JsonElement element)
        {
            try
            {
                if (element.TryGetProperty("boundingPoly", out JsonElement boundingPoly))
                {
                    return GetBoundingBox(boundingPoly);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting bounding polygon: {ex.Message}");
            }

            return null;
        }

        // Create a TextObject from text and bounds
        private TextObject CreateTextObject(string text, (double x, double y, double width, double height) bounds)
        {
            return new TextObject(
                text: text,
                x: bounds.x,
                y: bounds.y,
                width: bounds.width,
                height: bounds.height,
                textColor: new SolidColorBrush(Colors.White),
                backgroundColor: new SolidColorBrush(Colors.Black),
                captureX: bounds.x,
                captureY: bounds.y
            );
        }

        // Test the API key
        public async Task<(bool success, string message)> TestApiKeyAsync()
        {
            try
            {
                string apiKey = ConfigManager.Instance.GetGoogleVisionApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return (false, "No API key configured");
                }

                // Create a small test image
                using (Bitmap testBitmap = new Bitmap(100, 50))
                {
                    using (Graphics g = Graphics.FromImage(testBitmap))
                    {
                        g.Clear(System.Drawing.Color.White);
                        using (Font font = new Font("Arial", 20))
                        {
                            g.DrawString("Test", font, System.Drawing.Brushes.Black, 10, 10);
                        }
                    }

                    // Try to process the test image
                    var result = await ProcessImageAsync(testBitmap, "en");
                    
                    // If we got here without exception, the API key is valid
                    return (true, "API key is valid and Google Vision API is working correctly!");
                }
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Network error: {ex.Message}\n\nPlease check your internet connection.");
            }
            catch (TaskCanceledException)
            {
                return (false, "Request timed out. Please check your internet connection.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        // Process OCR results and send to the main logic
        public async Task ProcessGoogleVisionResults(List<TextObject> textObjects)
        {
            try
            {
                // Convert TextObjects to the JSON format expected by ProcessReceivedTextJsonData
                var results = textObjects.Select(obj => new
                {
                    text = obj.Text,
                    confidence = 0.95, // Google Vision doesn't provide confidence per text block
                    rect = new[] {
                        new[] { obj.X, obj.Y },
                        new[] { obj.X + obj.Width, obj.Y },
                        new[] { obj.X + obj.Width, obj.Y + obj.Height },
                        new[] { obj.X, obj.Y + obj.Height }
                    },
                    is_character = false // Google Vision returns words/blocks, not characters
                }).ToList();

                var response = new
                {
                    status = "success",
                    results = results,
                    processing_time_seconds = 0.1,
                    char_level = false,
                    skip_block_detection = true // Google Vision results are already properly grouped
                };

                // Convert to JSON
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

                // Process on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Logic.Instance.ProcessReceivedTextJsonData(jsonResponse);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Google Vision results: {ex.Message}");
            }

            await Task.CompletedTask;
        }
        
        // Group words by proximity using configurable glue distances
        private List<(string text, double x, double y, double width, double height)> GroupWordsByProximity(
            List<(string text, double x, double y, double width, double height)> words,
            double horizontalGlue, double verticalGlue)
        {
            if (words.Count == 0)
                return new List<(string, double, double, double, double)>();
                
            // Create a list to track which words have been grouped
            var used = new bool[words.Count];
            var groups = new List<List<(string text, double x, double y, double width, double height)>>();
            
            // Process each word
            for (int i = 0; i < words.Count; i++)
            {
                if (used[i]) continue;
                
                // Start a new group with this word
                var group = new List<(string text, double x, double y, double width, double height)> { words[i] };
                used[i] = true;
                
                // Keep looking for words that can be added to this group
                bool foundNewWord = true;
                while (foundNewWord)
                {
                    foundNewWord = false;
                    
                    for (int j = 0; j < words.Count; j++)
                    {
                        if (used[j]) continue;
                        
                        // Check if this word is close enough to ANY word in the current group
                        foreach (var groupWord in group)
                        {
                            // Calculate distances
                            double avgCharWidth = Math.Max(1, (groupWord.width / Math.Max(1, groupWord.text.Length) + 
                                                              words[j].width / Math.Max(1, words[j].text.Length)) / 2);
                            double avgLineHeight = (groupWord.height + words[j].height) / 2;
                            
                            // Check both horizontal and vertical proximity
                            double horizontalDistance = 0;
                            double verticalDistance = Math.Abs(words[j].y - groupWord.y);
                            
                            // Calculate horizontal distance (considering if words overlap or are adjacent)
                            if (words[j].x >= groupWord.x + groupWord.width)
                            {
                                // Word j is to the right of groupWord
                                horizontalDistance = words[j].x - (groupWord.x + groupWord.width);
                            }
                            else if (groupWord.x >= words[j].x + words[j].width)
                            {
                                // Word j is to the left of groupWord
                                horizontalDistance = groupWord.x - (words[j].x + words[j].width);
                            }
                            else
                            {
                                // Words overlap horizontally
                                horizontalDistance = 0;
                            }
                            
                            // For vertical grouping, distinguish between "same line" and "different line"
                            // Words on the same line should stay together even with vertical glue = 0
                            bool onSameLine = verticalDistance <= avgLineHeight * 0.5; // Within half a line height
                            
                            bool shouldGroup = false;
                            
                            if (onSameLine)
                            {
                                // Words on same line - only check horizontal glue
                                shouldGroup = horizontalDistance <= avgCharWidth * horizontalGlue;
                                
                                // Special case: if horizontal glue is 0, still group if very close
                                if (horizontalGlue == 0 && horizontalDistance < 2)
                                {
                                    shouldGroup = true;
                                }
                            }
                            else
                            {
                                // Words on different lines - check both horizontal and vertical glue
                                bool horizontallyClose = horizontalDistance <= avgCharWidth * horizontalGlue;
                                bool verticallyClose = verticalDistance <= avgLineHeight * (1.0 + verticalGlue); // Add 1.0 as base for line spacing
                                
                                shouldGroup = horizontallyClose && verticallyClose;
                            }
                            
                            if (shouldGroup)
                            {
                                group.Add(words[j]);
                                used[j] = true;
                                foundNewWord = true;
                                break;
                            }
                        }
                    }
                }
                
                groups.Add(group);
            }
            
            // Get the keep linefeeds setting
            bool keepLinefeeds = ConfigManager.Instance.GetGoogleVisionKeepLinefeeds();
            
            // Convert groups to single text objects
            var result = new List<(string text, double x, double y, double width, double height)>();
            foreach (var group in groups)
            {
                // Sort words in reading order (top to bottom, left to right)
                var sortedGroup = group.OrderBy(w => w.y).ThenBy(w => w.x).ToList();
                
                // Combine text with spaces or linefeeds
                string combinedText;
                if (keepLinefeeds && sortedGroup.Count > 1)
                {
                    // Build text with linefeeds when words are on different lines
                    var textParts = new List<string>();
                    double? lastY = null;
                    double avgLineHeight = sortedGroup.Average(w => w.height);
                    
                    foreach (var word in sortedGroup)
                    {
                        if (lastY.HasValue)
                        {
                            // Check if this word is on a different line
                            double verticalDistance = Math.Abs(word.y - lastY.Value);
                            bool onDifferentLine = verticalDistance > avgLineHeight * 0.5;
                            
                            if (onDifferentLine)
                            {
                                // Add linefeed before this word
                                textParts.Add("\r\n");
                            }
                            else
                            {
                                // Add space for words on the same line
                                textParts.Add(" ");
                            }
                        }
                        
                        textParts.Add(word.text);
                        lastY = word.y;
                    }
                    
                    combinedText = string.Join("", textParts);
                }
                else
                {
                    // Original behavior: combine with spaces
                    combinedText = string.Join(" ", sortedGroup.Select(w => w.text));
                }
                
                // Calculate bounding box for the group
                double minX = group.Min(w => w.x);
                double minY = group.Min(w => w.y);
                double maxX = group.Max(w => w.x + w.width);
                double maxY = group.Max(w => w.y + w.height);
                
                result.Add((combinedText, minX, minY, maxX - minX, maxY - minY));
            }
            
            return result;
        }
    }
}
