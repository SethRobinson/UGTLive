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
            
            // Line grouping thresholds
            public double BaseLineVerticalGap = 5.0;         // Vertical gap between lines to consider as paragraph
            public double BaseLineFontSizeTolerance = 5.0;    // Max font height difference for lines in same paragraph
            
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
        
        public void SetBaseLineVerticalGap(double value)
        {
            if (value < 0)
            {
            //    Console.WriteLine("Line vertical gap must be positive");
                return;
            }
            _config.BaseLineVerticalGap = value;
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
        
        #endregion
        
        #region Main Processing Method
        
        /// <summary>
        /// Process OCR results to identify and group text into natural reading blocks
        /// </summary>
        public JsonElement ProcessResults(JsonElement resultsElement)
        {
            // Early validation
            if (resultsElement.ValueKind != JsonValueKind.Array || resultsElement.GetArrayLength() == 0)
                return resultsElement;
                
            try
            {
                // Sync configuration from ConfigManager
                // We map the "Google Vision" glue settings to our internal thresholds
                // This allows the user to control the glue behavior for all OCR methods
                double horizontalGlue = ConfigManager.Instance.GetGoogleVisionHorizontalGlue();
                double verticalGlue = ConfigManager.Instance.GetGoogleVisionVerticalGlue();
                
                // Update internal config
                SetBaseWordHorizontalGap(horizontalGlue);
                SetBaseLineVerticalGap(verticalGlue);

                bool keepLinefeeds = ConfigManager.Instance.GetGoogleVisionKeepLinefeeds();
                
                // PHASE 1: Extract all text elements (Chars, Words, Lines)
                var allElements = ExtractTextElements(resultsElement);
                
                // Get minimum confidence thresholds
                double minLetterConfidence = ConfigManager.Instance.GetMinLetterConfidence();
                
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
                
                // Filter out low confidence lines
                double minLineConfidence = ConfigManager.Instance.GetMinLineConfidence();
                lines = lines.Where(l => l.Confidence >= minLineConfidence).ToList();
                
                // PHASE 4: Group lines into paragraphs (Vertical Glue)
                var paragraphs = GroupLinesIntoParagraphs(lines, keepLinefeeds);
                
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
                        
                        if (horizontalGap <= horizontalGapThreshold)
                        {
                            currentWord.Text += character.Text;
                            
                            double right = Math.Max(currentWord.Bounds.X + currentWord.Bounds.Width, 
                                               character.Bounds.X + character.Bounds.Width);
                            double bottom = Math.Max(currentWord.Bounds.Y + currentWord.Bounds.Height, 
                                               character.Bounds.Y + character.Bounds.Height);
                                               
                            currentWord.Bounds.Width = right - currentWord.Bounds.X;
                            currentWord.Bounds.Height = bottom - currentWord.Bounds.Y;
                            
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
                    
                    if (Math.Abs(seg.CenterY - bucketY) < (bucketHeight * 0.5))
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

                        // Check horizontal gap
                        double gap = seg.Bounds.X - (prev.Bounds.X + prev.Bounds.Width);
                        
                        // If gap is small, add to line. If large, start new line.
                        if (gap <= horizontalGapThreshold)
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
        /// Group lines into paragraphs based on spacing, indentation, and font size
        /// </summary>
        private List<TextElement> GroupLinesIntoParagraphs(List<TextElement> lines, bool keepLinefeeds)
        {
            if (lines.Count == 0)
                return new List<TextElement>();
                
            // Get config values
            // The values from ConfigManager are "factors" (e.g., 1.5 line heights)
            // We will multiply them by the actual line height during processing
            double lineVerticalGapFactor = _config.BaseLineVerticalGap;
            double fontSizeTolerance = 5.0; // Can be fixed or configurable
            double indentationThreshold = 20.0; // Can be fixed or configurable
            
            // Sort lines by Y position
            var sortedLines = lines.OrderBy(l => l.Bounds.Y).ToList();
            var paragraphs = new List<TextElement>();
            TextElement? currentParagraph = null;
            
            foreach (var line in sortedLines)
            {
                if (currentParagraph == null)
                {
                    // Start a new paragraph with this line
                    currentParagraph = new TextElement
                    {
                        ElementType = ElementType.Paragraph,
                        Bounds = line.Bounds.Clone(),
                        Children = new List<TextElement> { line },
                        Text = line.Text,
                        Confidence = line.Confidence,
                        TextOrientation = line.TextOrientation
                    };
                }
                else
                {
                    bool startNewParagraph = false;
                    
                    // Get the last line in the paragraph to properly calculate gaps
                    var lastLine = currentParagraph.Children.Last();
                    
                    // Calculate horizontal overlap ratio to ensure lines belong to the same column/bubble
                    double lastLeft = lastLine.Bounds.X;
                    double lastRight = lastLine.Bounds.X + lastLine.Bounds.Width;
                    double currLeft = line.Bounds.X;
                    double currRight = line.Bounds.X + line.Bounds.Width;
                    double overlapWidth = Math.Max(0, Math.Min(lastRight, currRight) - Math.Max(lastLeft, currLeft));
                    double minWidth = Math.Max(1.0, Math.Min(lastLine.Bounds.Width, line.Bounds.Width));
                    double horizontalOverlapRatio = overlapWidth / minWidth;
                    
                    // Require a minimum horizontal overlap so we don't glue distant columns/bubbles
                    // If vertical glue is very high (user wants to force merge), we can relax this check
                    // But for now, let's keep it reasonable to avoid merging unrelated columns
                    double minHorizontalOverlapRequired = 0.20; // Reduced from 0.35 to allow more aggressive gluing if aligned slightly off
                    
                    // Calculate vertical distance between line centers
                    double lastLineCenterY = lastLine.Bounds.Y + (lastLine.Bounds.Height * 0.5);
                    double currentLineCenterY = line.Bounds.Y + (line.Bounds.Height * 0.5);
                    double centerDistance = currentLineCenterY - lastLineCenterY;
                    
                    // Calculate expected line height (average of the two lines)
                    double averageHeight = (lastLine.Bounds.Height + line.Bounds.Height) * 0.5;
                    
                    // Calculate normal spacing (approximate distance from center to center for standard text)
                    // Typically line spacing is ~1.2x font size (height)
                    // So center distance for single spaced text is roughly 1.0 to 1.2 * height
                    // We subtract a "standard" amount to get the "excess" gap
                    double standardCenterDistance = averageHeight * 1.0; 
                    
                    // The "gap" is how much EXTRA space there is beyond standard closely packed lines
                    // But simply: User setting "300.0" means "Glue lines if they are within 300 line heights"
                    // So we should check if centerDistance <= (averageHeight * lineVerticalGapFactor)
                    // However, lineVerticalGapFactor is likely "gap between bottom of one and top of other" or "center to center"?
                    // The tooltip says "Vertical glue distance (in line heights)".
                    // Let's interpret it as "Max allowed center-to-center distance in line heights".
                    // If lines are tightly packed, center distance is ~1.0 line height.
                    // If factor is 0.5, it might be too small if we use center distance.
                    // Let's assume the user means "gap between lines".
                    // Gap = (Line2.Top - Line1.Bottom).
                    // But Rects might overlap or be tight.
                    // Let's use the geometric gap:
                    double geometricGap = Math.Max(0, line.Bounds.Y - (lastLine.Bounds.Y + lastLine.Bounds.Height));
                    
                    // Threshold in pixels
                    double maxAllowedGapPixels = averageHeight * lineVerticalGapFactor;
                    
                    // Check horizontal overlap
                    if (horizontalOverlapRatio < minHorizontalOverlapRequired)
                    {
                        startNewParagraph = true;
                    }
                    
                    // Check vertical gap
                    // If the actual gap is larger than the allowed threshold, break.
                    if (geometricGap > maxAllowedGapPixels)
                    {
                        startNewParagraph = true;
                    }
                    
                    // Removed the secondary "centerDistance" check that was forcing breaks based on hardcoded multipliers.
                    // Now we strictly obey the configured Vertical Glue.
                    
                    // Check indentation (only if we haven't already decided to break)
                    // If indentation is massive, maybe break? But user wants glue.
                    // Let's keep indentation check but maybe scale it or allow it to be skipped if glue is huge?
                    // For now, standard indentation check:
                    double indentation = line.Bounds.X - lastLine.Bounds.X;
                    if (!startNewParagraph && Math.Abs(indentation) > indentationThreshold)
                    {
                        // If the user set a huge vertical glue, they probably don't care about indentation splitting blocks.
                        // Let's disable indentation check if vertical glue is "aggressive" (> 3.0 lines)
                        if (lineVerticalGapFactor < 3.0)
                        {
                            startNewParagraph = true;
                        }
                    }
                    
                    // Check font size consistency
                    double fontSizeDiff = Math.Abs(line.Bounds.Height - lastLine.Bounds.Height);
                    if (!startNewParagraph && fontSizeDiff > fontSizeTolerance)
                    {
                        // Similarly, if aggressively gluing, ignore font size differences
                        if (lineVerticalGapFactor < 3.0)
                        {
                            startNewParagraph = true;
                        }
                    }
                    
                    if (startNewParagraph)
                    {
                        paragraphs.Add(currentParagraph);
                        
                        currentParagraph = new TextElement
                        {
                            ElementType = ElementType.Paragraph,
                            Bounds = line.Bounds.Clone(),
                            Children = new List<TextElement> { line },
                            Text = line.Text,
                            Confidence = line.Confidence,
                            TextOrientation = line.TextOrientation
                        };
                    }
                    else
                    {
                        currentParagraph.Children.Add(line);

                        if (keepLinefeeds)
                        {
                            currentParagraph.Text += "\n";
                        }

                        string sourceLangForParagraphs = ConfigManager.Instance.GetSourceLanguage();
                        bool isEastAsianLangForParagraphs = sourceLangForParagraphs == "ja" || 
                                                          sourceLangForParagraphs == "ch_sim" || 
                                                          sourceLangForParagraphs == "ch_tra" || 
                                                          sourceLangForParagraphs == "ko";
                                                  
                        if (!keepLinefeeds && !isEastAsianLangForParagraphs && 
                            !currentParagraph.Text.EndsWith(" ") && 
                            !currentParagraph.Text.EndsWith("\n"))
                        {
                            currentParagraph.Text += " ";
                        }
                        
                        currentParagraph.Text += line.Text;

                        double right = Math.Max(currentParagraph.Bounds.X + currentParagraph.Bounds.Width, 
                                          line.Bounds.X + line.Bounds.Width);
                        double bottom = Math.Max(currentParagraph.Bounds.Y + currentParagraph.Bounds.Height, 
                                          line.Bounds.Y + line.Bounds.Height);
                                          
                        currentParagraph.Bounds.Width = right - currentParagraph.Bounds.X;
                        currentParagraph.Bounds.Height = bottom - currentParagraph.Bounds.Y;
                    }
                }
            }
            
            if (currentParagraph != null)
            {
                paragraphs.Add(currentParagraph);
            }
            
            return paragraphs;
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
                            if (el.OriginalItem.ValueKind != JsonValueKind.Undefined)
                            {
                                if (el.OriginalItem.TryGetProperty("foreground_color", out JsonElement fg)) foregroundColors.Add((fg, el.Confidence));
                                if (el.OriginalItem.TryGetProperty("background_color", out JsonElement bg)) backgroundColors.Add((bg, el.Confidence));
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