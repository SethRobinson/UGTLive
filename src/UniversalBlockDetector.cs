using System.Text.Json;
using System.IO;

namespace UGTLive
{
    /// <summary>
    /// Advanced intelligent universal text block detection system
    /// for grouping mixed inputs (Words, Lines) into natural reading units.
    /// </summary>
    public class UniversalBlockDetector
    {
        #region Singleton and Configuration
        
        private static UniversalBlockDetector? _instance;

        // Singleton pattern
        public static UniversalBlockDetector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new UniversalBlockDetector();
                }
                return _instance;
            }
        }
        
        // Block detection power is obtained from BlockDetectionManager - REMOVED
        // private double GetBlockPower() => BlockDetectionManager.Instance.GetBlockDetectionScale();
        
        // Configuration values
        private readonly Config _config = new Config();
        
        // Configuration class to keep all thresholds together
        private class Config
        {
            // Character grouping thresholds (base values before scaling)
            public double BaseCharacterHorizontalGap = 2.0;  // Horizontal gap for letter-to-letter
            public double BaseCharacterVerticalGap = 4.0;     // Vertical alignment tolerance for characters
            
            // Word grouping thresholds
            public double BaseWordHorizontalGap = 5.0;       // Horizontal gap for word-to-word (Increased for better safety)
            public double BaseWordVerticalGap = 8.0;         // Vertical alignment for word-to-word
            
            // Large gap detection
            public double BaseLargeHorizontalGapThreshold = 40.0; // Large horizontal gap that should split text into separate blocks
            
            // Height similarity for gluing (prevents merging text with very different sizes)
            public double HeightSimilarityThreshold = 50.0;   // Percentage (0-100) - text heights must be within this % to glue
            
            // Paragraph detection
            public double BaseIndentation = 20.0;             // Indentation that suggests a new paragraph
            public double BaseParagraphBreakThreshold = 20.0; // Vertical gap suggesting paragraph break
            
            // Get scaled values with current block power - REMOVED
            // public double GetScaledValue(double baseValue, double blockPower) => baseValue * blockPower;
        }
        
        // Public methods to adjust configuration
        public void SetBaseCharacterHorizontalGap(double value)
        {
            if (value < 0) {
                Console.WriteLine("Character horizontal gap must be positive");
                return;
            }
            _config.BaseCharacterHorizontalGap = value;
        }
        
        public void SetBaseCharacterVerticalGap(double value)
        {
            if (value < 0) {
                Console.WriteLine("Character vertical gap must be positive");
                return;
            }
            _config.BaseCharacterVerticalGap = value;
        }

        public void SetBaseWordHorizontalGap(double value)
        {
            if (value < 0)
            {
                Console.WriteLine("Word horizontal gap must be positive");
                return;
            }
            _config.BaseWordHorizontalGap = value;
        }
        
        public void SetHeightSimilarity(double value)
        {
            _config.HeightSimilarityThreshold = value;
        }
        
        /// <summary>
        /// Check if two text elements have similar heights (within threshold percentage)
        /// </summary>
        private bool AreSimilarHeights(TextElement a, TextElement b)
        {
            double threshold = _config.HeightSimilarityThreshold;
            
            // If threshold is 0, allow all heights
            if (threshold <= 0)
                return true;
                
            double heightA = a.Bounds.Height;
            double heightB = b.Bounds.Height;
            
            // Avoid division by zero
            if (heightA <= 0 || heightB <= 0)
                return true;
            
            // Calculate ratio (smaller/larger)
            double ratio = Math.Min(heightA, heightB) / Math.Max(heightA, heightB);
            
            // Convert threshold percentage to ratio
            // 70% threshold means ratio must be >= 0.70
            double minRatio = threshold / 100.0;
            
            return ratio >= minRatio;
        }
        
        #endregion
        
        #region Main Processing Method
        
        /// <summary>
        /// Process OCR results to identify and group text into natural reading blocks
        /// </summary>
        public JsonElement ProcessResults(JsonElement resultsElement, string ocrProvider)
        {
            // Early validation
            if (resultsElement.ValueKind != JsonValueKind.Array || resultsElement.GetArrayLength() == 0)
                return resultsElement;

            // Bypass for MangaOCR which provides its own blocking logic and shouldn't use the glue math
            if (ocrProvider == "MangaOCR")
            {
                return resultsElement;
            }
                
            try
            {
                // Sync configuration from ConfigManager using per-OCR settings
                // This allows the user to control the glue behavior individually for each OCR method
                double horizontalGlue = ConfigManager.Instance.GetHorizontalGlue(ocrProvider);
                double verticalGlue = ConfigManager.Instance.GetVerticalGlue(ocrProvider);
                double verticalGlueOverlap = ConfigManager.Instance.GetVerticalGlueOverlap(ocrProvider);
                double heightSimilarity = ConfigManager.Instance.GetHeightSimilarity(ocrProvider);
                
                // Update internal config
                SetBaseWordHorizontalGap(horizontalGlue);
                SetHeightSimilarity(heightSimilarity);

                bool keepLinefeeds = ConfigManager.Instance.GetKeepLinefeeds(ocrProvider);
                
                // PHASE 1: Extract all text elements (Chars, Words, Lines)
                var allElements = ExtractTextElements(resultsElement);
                
                // Get minimum confidence thresholds using provider-specific settings
                double minLetterConfidence = ConfigManager.Instance.GetMinLetterConfidence(ocrProvider);
                
                // Remove low confidence elements
                allElements.RemoveAll(c => c.Confidence < minLetterConfidence);
                
                // Split into Characters and Segments (Words/Lines)
                var characters = allElements.Where(c => c.IsCharacter && !c.IsProcessed).ToList();
                var segments = allElements.Where(c => !c.IsCharacter || c.IsProcessed).ToList();
                
                // PHASE 2: Group characters into words
                // NOTE: BlockPower was removed, so we'll use a default scale factor of 1.0
                // Or just pass 1.0 if the method still expects it, until we refactor that method too.
                // Actually, we need to refactor GroupCharactersIntoWords as well to remove BlockPower dependency.
                // For now, let's check if we can just remove blockPower argument.
                if (characters.Count > 0)
                {
                    var formedWords = GroupCharactersIntoWords(characters);
                    segments.AddRange(formedWords);
                }
                
                // PHASE 3: Group words/segments into lines (Horizontal Glue)
                // This handles both "Words" -> "Lines" and preserves existing "Lines"
                var lines = GroupSegmentsIntoLines(segments);
                
                // Filter out low confidence lines using provider-specific settings
                double minLineConfidence = ConfigManager.Instance.GetMinLineConfidence(ocrProvider);
                lines = lines.Where(l => l.Confidence >= minLineConfidence).ToList();
                
                // PHASE 4: Group lines into paragraphs (Vertical Glue)
                var paragraphs = GroupLinesIntoParagraphs(lines, keepLinefeeds, verticalGlue, verticalGlueOverlap);
                
                // Create JSON output
                return CreateJsonOutput(paragraphs, new List<TextElement>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in universal block detection: {ex.Message}");
                return resultsElement; // Return original if processing fails
            }
        }
        
        #endregion
        
        #region Element Extraction
        
        /// <summary>
        /// Extract text elements from JSON results
        /// </summary>
        private List<TextElement> ExtractTextElements(JsonElement resultsElement)
        {
            var elements = new List<TextElement>();
            
            for (int i = 0; i < resultsElement.GetArrayLength(); i++)
            {
                JsonElement item = resultsElement[i];
                
                // Skip if missing required properties
                if (!item.TryGetProperty("text", out JsonElement textElement) || 
                    !item.TryGetProperty("confidence", out JsonElement confElement))
                {
                    continue;
                }
                
                // Try to get bounding box
                JsonElement boxElement;
                bool hasBox = item.TryGetProperty("rect", out boxElement);
                if (!hasBox)
                {
                    hasBox = item.TryGetProperty("vertices", out boxElement);
                }
                
                if (!hasBox || boxElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                
                string text = textElement.GetString() ?? "";
                
                double confidence = 1.0;
                if (confElement.ValueKind != JsonValueKind.Null)
                {
                    confidence = confElement.GetDouble();
                    
                    // Normalize 0-100 range to 0-1
                    if (confidence > 1.0)
                    {
                        confidence /= 100.0;
                    }
                }

                bool isCharacter = false; // Default to false (Word/Line)
                if (item.TryGetProperty("is_character", out JsonElement isCharElement))
                {
                    isCharacter = isCharElement.GetBoolean();
                }

                string textOrientation = "unknown";
                if (item.TryGetProperty("text_orientation", out JsonElement textOrientationElement))
                {
                    textOrientation = textOrientationElement.GetString() ?? "unknown";
                }

                // Skip empty text
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                
                // Calculate bounding box from polygon points
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                var points = new List<Point>();
                
                for (int p = 0; p < boxElement.GetArrayLength(); p++)
                {
                    if (boxElement[p].ValueKind == JsonValueKind.Array && boxElement[p].GetArrayLength() >= 2)
                    {
                        double pointX = boxElement[p][0].GetDouble();
                        double pointY = boxElement[p][1].GetDouble();
                        
                        points.Add(new Point(pointX, pointY));
                        
                        minX = Math.Min(minX, pointX);
                        minY = Math.Min(minY, pointY);
                        maxX = Math.Max(maxX, pointX);
                        maxY = Math.Max(maxY, pointY);
                    }
                }
                
                var element = new TextElement
                {
                    Text = text,
                    Confidence = confidence,
                    Bounds = new Rect(minX, minY, maxX - minX, maxY - minY),
                    Points = points,
                    IsCharacter = isCharacter,
                    IsProcessed = !isCharacter,
                    OriginalItem = item,
                    ElementType = isCharacter ? ElementType.Character : ElementType.Word,
                    TextOrientation = textOrientation,
                    CenterY = minY + (maxY - minY) / 2
                };
                
                elements.Add(element);
            }
            
            return elements;
        }
        
        #endregion
        
        #region Character to Word Grouping
        
        private List<TextElement> GroupCharactersIntoWords(List<TextElement> characters)
        {
            if (characters.Count == 0)
                return new List<TextElement>();
                
            // Estimate gap threshold based on average character height (proxy for size)
            // If BlockPower was used to scale this, we now just trust the BaseCharacterHorizontalGap logic.
            // Assuming BaseCharacterHorizontalGap was around 2.0 pixels scaled by block power (~9) -> ~18 pixels.
            // If we use height scaling, 0.5 * Height is reasonable for char gap.
            
            double avgHeight = characters.Average(c => c.Bounds.Height);
            
            // Use a factor relative to height. 
            // Old logic: BaseCharacterHorizontalGap (2.0) * BlockPower (~9) = 18.
            // Avg height is likely 20-30 pixels.
            // So factor is ~0.6 to 0.9 of height.
            
            double horizontalGapThreshold = avgHeight * 0.8; 
            double verticalGapThreshold = avgHeight * 0.5;
            
            string sourceLangForChars = ConfigManager.Instance.GetSourceLanguage();
            bool isEastAsianLangForChars = sourceLangForChars == "ja" || 
                                          sourceLangForChars == "ch_sim" || 
                                          sourceLangForChars == "ch_tra" || 
                                          sourceLangForChars == "ko";
                                      
            if (!isEastAsianLangForChars)
            {
                // Western languages have tighter letter spacing within words, but we want to group them.
                // Actually if we are grouping chars into words, we need to be careful not to merge words.
                // But chars are usually very close.
                // Let's keep the threshold somewhat generous for chars within a word.
                horizontalGapThreshold = Math.Max(5, horizontalGapThreshold * 0.5);
            }
            
            var lines = characters
                .GroupBy(c => Math.Round(c.CenterY / verticalGapThreshold))
                .OrderBy(g => g.Key)
                .ToList();
                
            var words = new List<TextElement>();
            
            foreach (var line in lines)
            {
                var lineCharacters = line.OrderBy(c => c.Bounds.X).ToList();
                TextElement? currentWord = null;
                
                foreach (var character in lineCharacters)
                {
                    if (currentWord == null)
                    {
                        currentWord = new TextElement
                        {
                            Text = character.Text,
                            Confidence = character.Confidence,
                            Bounds = character.Bounds.Clone(),
                            Points = new List<Point>(character.Points),
                            ElementType = ElementType.Word,
                            Children = new List<TextElement> { character },
                            CenterY = character.CenterY,
                            TextOrientation = character.TextOrientation
                        };
                    }
                    else
                    {
                        double horizontalGap = character.Bounds.X - (currentWord.Bounds.X + currentWord.Bounds.Width);
                        
                        // Check both gap distance and height similarity
                        if (horizontalGap <= horizontalGapThreshold && AreSimilarHeights(currentWord, character))
                        {
                            currentWord.Text += character.Text;
                            
                            // Expand bounding box to include the new character in all directions
                            double minX = Math.Min(currentWord.Bounds.X, character.Bounds.X);
                            double minY = Math.Min(currentWord.Bounds.Y, character.Bounds.Y);
                            double maxX = Math.Max(currentWord.Bounds.X + currentWord.Bounds.Width, 
                                               character.Bounds.X + character.Bounds.Width);
                            double maxY = Math.Max(currentWord.Bounds.Y + currentWord.Bounds.Height, 
                                               character.Bounds.Y + character.Bounds.Height);
                                               
                            // Update position and size to properly expand bounding box
                            currentWord.Bounds.X = minX;
                            currentWord.Bounds.Y = minY;
                            currentWord.Bounds.Width = maxX - minX;
                            currentWord.Bounds.Height = maxY - minY;
                            
                            currentWord.Children.Add(character);
                        }
                        else
                        {
                            words.Add(currentWord);
                            
                            currentWord = new TextElement
                            {
                                Text = character.Text,
                                Confidence = character.Confidence,
                                Bounds = character.Bounds.Clone(),
                                Points = new List<Point>(character.Points),
                                ElementType = ElementType.Word,
                                Children = new List<TextElement> { character },
                                CenterY = character.CenterY,
                                TextOrientation = character.TextOrientation
                            };
                        }
                    }
                }
                
                if (currentWord != null)
                {
                    words.Add(currentWord);
                }
            }
            
            return words;
        }
        
        #endregion
        
        #region Segment to Line Grouping
        
        /// <summary>
        /// Group segments (words/blocks) into lines based on vertical proximity and horizontal overlap.
        /// Replaces "Word to Line" and "Line Index" logic with a raw geometric approach.
        /// </summary>
        private List<TextElement> GroupSegmentsIntoLines(List<TextElement> segments)
        {
            if (segments.Count == 0)
                return new List<TextElement>();

            // Use configured threshold directly
            double horizontalGapFactor = _config.BaseWordHorizontalGap;
            
            // For Western languages, use larger word gaps
            string sourceLang = ConfigManager.Instance.GetSourceLanguage();
            bool isEastAsian = sourceLang == "ja" || sourceLang == "ch_sim" || sourceLang == "ch_tra" || sourceLang == "ko";
            
            // If user specifies 1.0 char widths, that means approx 1.0 * height.
            // But previously we had some scaling.
            // Let's trust the user value if they set it. 
            // If East Asian, char width is roughly height.
            // If Western, char width is roughly height/2.
            // If user sets "1.0", they probably mean "1 standard char".
            // Let's use 1.0 * height as a safe baseline for "1 unit".
            // If not East Asian, maybe slightly increase tolerance?
            
            if (!isEastAsian)
            {
                 horizontalGapFactor = Math.Max(horizontalGapFactor, horizontalGapFactor * 1.2);
            }

            // Sort by Y center
            var sortedSegments = segments.OrderBy(s => s.CenterY).ToList();
            var lines = new List<TextElement>();

            // Group into lines by vertical overlap
            // We iterate and check if current segment overlaps vertically with active line groups
            // But a simpler "Bucket" approach works well for OCR text:
            // 1. Buckets based on Y / threshold
            // 2. Or refined line tracking.
            
            // Let's use a simple greedy grouping:
            // Iterate segments. Try to add to existing line groups if Y aligns. If not, start new line.
            // Then sort lines by X and merge internal segments.
            
            var lineBuckets = new List<List<TextElement>>();
            
            foreach (var seg in sortedSegments)
            {
                bool added = false;
                
                // Try to find a matching line bucket
                foreach (var bucket in lineBuckets)
                {
                    // Calculate average Y of bucket
                    double bucketY = bucket.Average(s => s.CenterY);
                    double bucketHeight = bucket.Average(s => s.Bounds.Height);
                    
                    // Check vertical proximity and height similarity with any element in bucket
                    bool verticallyClose = Math.Abs(seg.CenterY - bucketY) < (bucketHeight * 0.5);
                    bool heightSimilar = bucket.Any(b => AreSimilarHeights(seg, b));
                    
                    if (verticallyClose && heightSimilar)
                    {
                        bucket.Add(seg);
                        added = true;
                        break;
                    }
                }
                
                if (!added)
                {
                    lineBuckets.Add(new List<TextElement> { seg });
                }
            }
            
            // Now process each bucket into one or more lines (splitting by large horizontal gaps)
            foreach (var bucket in lineBuckets)
            {
                // Sort by X
                var rowSegments = bucket.OrderBy(s => s.Bounds.X).ToList();
                
                List<TextElement> currentLineSegs = new List<TextElement>();
                TextElement? prev = null;
                
                foreach (var seg in rowSegments)
                {
                    if (prev == null)
                    {
                        currentLineSegs.Add(seg);
                    }
                    else
                    {
                        // Calculate dynamic threshold based on segment height
                        double avgHeight = (prev.Bounds.Height + seg.Bounds.Height) / 2.0;
                        double horizontalGapThreshold = avgHeight * horizontalGapFactor;

                        // Check horizontal gap and height similarity
                        double gap = seg.Bounds.X - (prev.Bounds.X + prev.Bounds.Width);
                        
                        // If gap is small and heights are similar, add to line. If not, start new line.
                        if (gap <= horizontalGapThreshold && AreSimilarHeights(prev, seg))
                        {
                            currentLineSegs.Add(seg);
                        }
                        else
                        {
                            // Create line from current segments
                            lines.Add(CreateLineFromSegments(currentLineSegs, isEastAsian));
                            currentLineSegs = new List<TextElement> { seg };
                        }
                    }
                    prev = seg;
                }
                
                if (currentLineSegs.Count > 0)
                {
                    lines.Add(CreateLineFromSegments(currentLineSegs, isEastAsian));
                }
            }
            
            return lines;
        }
        
        private TextElement CreateLineFromSegments(List<TextElement> segments, bool isEastAsian)
        {
            var first = segments.First();
            var line = new TextElement
            {
                ElementType = ElementType.Line,
                Children = segments.ToList(),
                TextOrientation = first.TextOrientation,
                Confidence = segments.Average(s => s.Confidence)
            };
            
            // Calculate bounds
            double minX = segments.Min(s => s.Bounds.X);
            double minY = segments.Min(s => s.Bounds.Y);
            double maxX = segments.Max(s => s.Bounds.X + s.Bounds.Width);
            double maxY = segments.Max(s => s.Bounds.Y + s.Bounds.Height);
            
            line.Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            line.CenterY = minY + (maxY - minY) / 2;
            
            // Join text
            if (isEastAsian)
            {
                line.Text = string.Join("", segments.Select(s => s.Text));
            }
            else
            {
                line.Text = string.Join(" ", segments.Select(s => s.Text));
            }
            
            return line;
        }

        #endregion
        
        #region Line to Paragraph Grouping
        
        /// <summary>
        /// Group lines into paragraphs based on spacing and horizontal overlap.
        /// Enhanced to support multi-column layouts by tracking active paragraphs.
        /// </summary>
        private List<TextElement> GroupLinesIntoParagraphs(List<TextElement> lines, bool keepLinefeeds, double verticalGlueFactor, double minOverlapPercent)
        {
            if (lines.Count == 0)
                return new List<TextElement>();
                
            // verticalGlueFactor is the multiplier for line heights (e.g., 1.0 means 1.0x line height gap is allowed)
            
            // Sort lines by Y position
            // OrderBy Y, then X for a stable and logical sort
            var sortedLines = lines.OrderBy(l => l.Bounds.Y).ThenBy(l => l.Bounds.X).ToList();
            
            // Use a list of active paragraphs to handle multi-column layouts
            // (e.g. left and right speech bubbles that are vertically interleaved)
            var activeParagraphs = new List<TextElement>();
            var completedParagraphs = new List<TextElement>();
            
            foreach (var line in sortedLines)
            {
                TextElement? bestParagraph = null;
                double bestFitScore = double.MaxValue; // Lower is better
                
                // Try to find a matching paragraph among active ones
                // We iterate backwards to prefer more recent paragraphs (though sorting by Y makes them all recent)
                for (int i = activeParagraphs.Count - 1; i >= 0; i--)
                {
                    var paragraph = activeParagraphs[i];
                    var lastLine = paragraph.Children.Last();
                    
                    // 0. Check Height Similarity FIRST (before other checks)
                    if (!AreSimilarHeights(lastLine, line))
                    {
                        continue; // Heights too different, don't merge
                    }
                    
                    // 1. Calculate Geometric Gap (Vertical Distance)
                    double averageHeight = (lastLine.Bounds.Height + line.Bounds.Height) * 0.5;
                    double geometricGap = Math.Max(0, line.Bounds.Y - (lastLine.Bounds.Y + lastLine.Bounds.Height));
                    double maxAllowedGapPixels = averageHeight * verticalGlueFactor;
                    
                    // If gap is too large, this paragraph is done (for this line, and likely for all future lines)
                    if (geometricGap > maxAllowedGapPixels)
                    {
                        continue;
                    }

                    // 2. Check Horizontal Overlap (Column Alignment)
                    double lastLeft = lastLine.Bounds.X;
                    double lastRight = lastLine.Bounds.X + lastLine.Bounds.Width;
                    double currLeft = line.Bounds.X;
                    double currRight = line.Bounds.X + line.Bounds.Width;
                    
                    double overlapWidth = Math.Max(0, Math.Min(lastRight, currRight) - Math.Max(lastLeft, currLeft));
                    double minWidth = Math.Max(1.0, Math.Min(lastLine.Bounds.Width, line.Bounds.Width));
                    double horizontalOverlapRatio = overlapWidth / minWidth;
                    
                    double minHorizontalOverlapRequired = minOverlapPercent / 100.0;
                    
                    if (horizontalOverlapRatio < minHorizontalOverlapRequired)
                    {
                        continue; // Not aligned column-wise
                    }

                    // If we got here, it's a match!
                    // Calculate a "score" to pick the best one if multiple match (rare)
                    // Score = Gap + Indentation (weighted)
                    double score = geometricGap + Math.Abs(line.Bounds.X - lastLine.Bounds.X);
                    
                    if (score < bestFitScore)
                    {
                        bestFitScore = score;
                        bestParagraph = paragraph;
                    }
                }
                
                if (bestParagraph != null)
                {
                    // Add to existing paragraph
                    bestParagraph.Children.Add(line);
                    
                    // Update Paragraph Text and Bounds immediately (optional but good for debugging)
                    // We'll do a full update at the end or just append text here
                    if (keepLinefeeds)
                    {
                        bestParagraph.Text += "\n";
                    }
                    else
                    {
                        string sourceLang = ConfigManager.Instance.GetSourceLanguage();
                        bool isEastAsian = sourceLang == "ja" || sourceLang == "ch_sim" || sourceLang == "ch_tra" || sourceLang == "ko";
                        if (!isEastAsian && !bestParagraph.Text.EndsWith(" ") && !bestParagraph.Text.EndsWith("\n"))
                        {
                            bestParagraph.Text += " ";
                        }
                    }
                    bestParagraph.Text += line.Text;
                    
                    // Update bounds - expand in all directions to include the new line
                    double minX = Math.Min(bestParagraph.Bounds.X, line.Bounds.X);
                    double minY = Math.Min(bestParagraph.Bounds.Y, line.Bounds.Y);
                    double maxX = Math.Max(bestParagraph.Bounds.X + bestParagraph.Bounds.Width, 
                                      line.Bounds.X + line.Bounds.Width);
                    double maxY = Math.Max(bestParagraph.Bounds.Y + bestParagraph.Bounds.Height, 
                                      line.Bounds.Y + line.Bounds.Height);
                    
                    // Update position and size to properly expand bounding box
                    bestParagraph.Bounds.X = minX;
                    bestParagraph.Bounds.Y = minY;
                    bestParagraph.Bounds.Width = maxX - minX;
                    bestParagraph.Bounds.Height = maxY - minY;
                }
                else
                {
                    // Start new paragraph
                    var newParagraph = new TextElement
                    {
                        ElementType = ElementType.Paragraph,
                        Bounds = line.Bounds.Clone(),
                        Children = new List<TextElement> { line },
                        Text = line.Text,
                        Confidence = line.Confidence,
                        TextOrientation = line.TextOrientation
                    };
                    
                    activeParagraphs.Add(newParagraph);
                }
                
                // Clean up "closed" paragraphs from active list to keep it small
                // A paragraph is closed if the current line is way below it (more than 2x the normal merge threshold).
                // Since lines are sorted by Y, if current line Y is significantly below a paragraph's bottom,
                // no future line will match it either (they'd be even further down).
                
                for (int i = activeParagraphs.Count - 1; i >= 0; i--)
                {
                    var p = activeParagraphs[i];
                    double lastLineHeight = p.Children.Last().Bounds.Height;
                    double closeoutThreshold = lastLineHeight * verticalGlueFactor * 2.0; // 2x the normal merge threshold
                    
                    if (line.Bounds.Y > (p.Bounds.Y + p.Bounds.Height + closeoutThreshold))
                    {
                        completedParagraphs.Add(p);
                        activeParagraphs.RemoveAt(i);
                    }
                }
            }
            
            // Add remaining active paragraphs
            completedParagraphs.AddRange(activeParagraphs);
            
            // Sort paragraphs by Y for clean output
            return completedParagraphs.OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X).ToList();
        }
        
        #endregion
        
        #region Json Output Creation
        
        private void WriteAveragedColor(Utf8JsonWriter writer, List<(JsonElement color, double confidence)> colors)
        {
            if (colors.Count == 0) return;
            
            if (colors.Count == 1)
            {
                colors[0].color.WriteTo(writer);
                return;
            }
            
            // ... (Same implementation as before) ...
            // For brevity assuming it's standard, but I will rewrite it to be safe
            
            double totalWeight = 0;
            double rSum = 0, gSum = 0, bSum = 0;
            string? hexValue = null;
            double totalPercentage = 0;
            
            foreach (var (color, confidence) in colors)
            {
                double weight = Math.Max(0.1, confidence);
                totalWeight += weight;
                
                if (color.TryGetProperty("rgb", out JsonElement rgbElement) && 
                    rgbElement.ValueKind == JsonValueKind.Array && 
                    rgbElement.GetArrayLength() >= 3)
                {
                    double r = rgbElement[0].GetDouble();
                    double g = rgbElement[1].GetDouble();
                    double b = rgbElement[2].GetDouble();
                    
                    rSum += r * weight;
                    gSum += g * weight;
                    bSum += b * weight;
                }
                
                if (hexValue == null && color.TryGetProperty("hex", out JsonElement hexElement))
                {
                    hexValue = hexElement.GetString();
                }
                
                if (color.TryGetProperty("percentage", out JsonElement percElement))
                {
                    totalPercentage += percElement.GetDouble() * weight;
                }
            }
            
            int avgR = (int)Math.Round(Math.Max(0, Math.Min(255, rSum / totalWeight)));
            int avgG = (int)Math.Round(Math.Max(0, Math.Min(255, gSum / totalWeight)));
            int avgB = (int)Math.Round(Math.Max(0, Math.Min(255, bSum / totalWeight)));
            
            if (hexValue == null) hexValue = $"#{avgR:X2}{avgG:X2}{avgB:X2}";
            double avgPercentage = totalPercentage / totalWeight;
            
            writer.WriteStartObject();
            writer.WriteStartArray("rgb");
            writer.WriteNumberValue(avgR);
            writer.WriteNumberValue(avgG);
            writer.WriteNumberValue(avgB);
            writer.WriteEndArray();
            writer.WriteString("hex", hexValue);
            writer.WriteNumber("percentage", avgPercentage);
            writer.WriteEndObject();
        }
        
        private JsonElement CreateJsonOutput(List<TextElement> paragraphs, List<TextElement> nonCharacters)
        {
            int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();
            
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();
                    
                    foreach (var paragraph in paragraphs)
                    {
                        if (paragraph.Text.Length < minTextFragmentSize) continue;
                        
                        writer.WriteStartObject();
                        writer.WriteString("text", paragraph.Text);
                        writer.WriteNumber("confidence", paragraph.Confidence);
                        writer.WriteString("text_orientation", paragraph.TextOrientation);

                        writer.WriteStartArray("rect");
                        writer.WriteStartArray(); writer.WriteNumberValue(paragraph.Bounds.X); writer.WriteNumberValue(paragraph.Bounds.Y); writer.WriteEndArray();
                        writer.WriteStartArray(); writer.WriteNumberValue(paragraph.Bounds.X + paragraph.Bounds.Width); writer.WriteNumberValue(paragraph.Bounds.Y); writer.WriteEndArray();
                        writer.WriteStartArray(); writer.WriteNumberValue(paragraph.Bounds.X + paragraph.Bounds.Width); writer.WriteNumberValue(paragraph.Bounds.Y + paragraph.Bounds.Height); writer.WriteEndArray();
                        writer.WriteStartArray(); writer.WriteNumberValue(paragraph.Bounds.X); writer.WriteNumberValue(paragraph.Bounds.Y + paragraph.Bounds.Height); writer.WriteEndArray();
                        writer.WriteEndArray();
                        
                        // Color aggregation logic (simplified for this rewrite)
                        // Collect colors from children
                         List<(JsonElement color, double confidence)> foregroundColors = new List<(JsonElement, double)>();
                        List<(JsonElement color, double confidence)> backgroundColors = new List<(JsonElement, double)>();
                        
                        void CollectColors(TextElement el)
                        {
                            if (el.OriginalItem.ValueKind != JsonValueKind.Undefined && el.OriginalItem.ValueKind != JsonValueKind.Null)
                            {
                                if (el.OriginalItem.TryGetProperty("foreground_color", out JsonElement fg) && fg.ValueKind != JsonValueKind.Null) 
                                    foregroundColors.Add((fg, el.Confidence));
                                if (el.OriginalItem.TryGetProperty("background_color", out JsonElement bg) && bg.ValueKind != JsonValueKind.Null) 
                                    backgroundColors.Add((bg, el.Confidence));
                            }
                            if (el.Children != null) foreach (var child in el.Children) CollectColors(child);
                        }
                        
                        CollectColors(paragraph);
                        
                        if (foregroundColors.Count > 0)
                        {
                            writer.WritePropertyName("foreground_color");
                            WriteAveragedColor(writer, foregroundColors);
                        }
                        if (backgroundColors.Count > 0)
                        {
                            writer.WritePropertyName("background_color");
                            WriteAveragedColor(writer, backgroundColors);
                        }
                        
                        writer.WriteNumber("line_count", paragraph.Children.Count);
                        writer.WriteString("element_type", "paragraph");
                        writer.WriteEndObject();
                    }
                    
                    foreach (var element in nonCharacters)
                    {
                        if (element.OriginalItem.ValueKind != JsonValueKind.Undefined)
                        {
                            element.OriginalItem.WriteTo(writer);
                        }
                    }
                    
                    writer.WriteEndArray();
                    writer.Flush();
                    
                    stream.Position = 0;
                    using (JsonDocument doc = JsonDocument.Parse(stream))
                    {
                        return doc.RootElement.Clone();
                    }
                }
            }
        }
        
        #endregion
        
        #region Helper Classes
        
        private enum ElementType { Character, Word, Line, Paragraph, Other }
        
        private class Point
        {
            public double X { get; set; }
            public double Y { get; set; }
            public Point(double x, double y) { X = x; Y = y; }
        }
        
        private class Rect
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public Rect(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
            public Rect Clone() => new Rect(X, Y, Width, Height);
        }
        
        private class TextElement
        {
            public string Text { get; set; } = "";
            public double Confidence { get; set; }
            public Rect Bounds { get; set; } = new Rect(0, 0, 0, 0);
            public List<Point> Points { get; set; } = new List<Point>();
            public string TextOrientation { get; set; } = "unknown";
            public ElementType ElementType { get; set; } = ElementType.Other;
            public bool IsCharacter { get; set; }
            public bool IsProcessed { get; set; }
            public int LineIndex { get; set; } = -1;
            public List<TextElement> Children { get; set; } = new List<TextElement>();
            public double CenterY { get; set; }
            public JsonElement OriginalItem { get; set; }
        }
        
        #endregion
    }
}