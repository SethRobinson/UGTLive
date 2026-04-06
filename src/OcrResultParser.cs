using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace UGTLive
{
    /// <summary>
    /// Pure parser that converts OCR JSON into TextObject instances without touching
    /// any global state (Logic._textObjects, MonitorWindow, etc.).
    /// Used by both the live pipeline (Logic.DisplayOcrResults) and the batch converter.
    /// </summary>
    public static class OcrResultParser
    {
        /// <summary>
        /// Parses an OCR JSON string (with status/results or status/texts) through the
        /// standard filter + block detection pipeline, then creates TextObjects.
        /// Returns an independent list with no side effects.
        /// </summary>
        public static List<TextObject> ParseOcrJsonToTextObjects(string ocrJson)
        {
            var textObjects = new List<TextObject>();

            try
            {
                var options = new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                };

                using JsonDocument doc = JsonDocument.Parse(ocrJson, options);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("status", out JsonElement statusElement))
                    return textObjects;

                string status = statusElement.GetString() ?? "unknown";
                if (status != "success")
                    return textObjects;

                JsonElement resultsElement;
                bool hasResults = root.TryGetProperty("results", out resultsElement);
                if (!hasResults && root.TryGetProperty("texts", out JsonElement textsElement))
                {
                    resultsElement = textsElement;
                    hasResults = true;
                }

                if (!hasResults)
                    return textObjects;

                string ocrMethod = ConfigManager.Instance.GetOcrMethod();
                JsonElement filtered = Logic.Instance.FilterLowConfidenceCharactersStatic(resultsElement, ocrMethod);

                bool skipBlockDetection = false;
                if (root.TryGetProperty("skip_block_detection", out JsonElement skipEl))
                    skipBlockDetection = skipEl.GetBoolean();

                JsonElement processed = skipBlockDetection
                    ? filtered
                    : UniversalBlockDetector.Instance.ProcessResults(filtered, ocrMethod);

                processed = Logic.Instance.FilterIgnoredPhrasesStatic(processed);

                textObjects = ParseResultsToTextObjects(processed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OcrResultParser] Error parsing OCR JSON: {ex.Message}");
            }

            return textObjects;
        }

        /// <summary>
        /// Parses a results JsonElement array into TextObjects.
        /// This is the pure data extraction without any UI or global state.
        /// </summary>
        public static List<TextObject> ParseResultsToTextObjects(JsonElement resultsElement)
        {
            var textObjects = new List<TextObject>();

            if (resultsElement.ValueKind != JsonValueKind.Array)
                return textObjects;

            int minTextFragmentSize = ConfigManager.Instance.GetMinTextFragmentSize();
            int expansionWidth = ConfigManager.Instance.GetMonitorTextAreaExpansionWidth();
            int expansionHeight = ConfigManager.Instance.GetMonitorTextAreaExpansionHeight();
            int idCounter = 0;

            for (int i = 0; i < resultsElement.GetArrayLength(); i++)
            {
                JsonElement item = resultsElement[i];

                if (!item.TryGetProperty("text", out JsonElement textElement) ||
                    !item.TryGetProperty("confidence", out JsonElement confElement))
                    continue;

                string text = textElement.GetString() ?? "";
                if (text.Length < minTextFragmentSize)
                    continue;

                string textOrientation = "horizontal";
                if (item.TryGetProperty("text_orientation", out JsonElement orientEl))
                    textOrientation = orientEl.GetString() ?? "horizontal";

                double confidence = 1.0;
                if (confElement.ValueKind != JsonValueKind.Null)
                    confidence = confElement.GetDouble();

                double x = 0, y = 0, width = 0, height = 0;
                JsonElement boxElement;
                bool hasBox = item.TryGetProperty("rect", out boxElement);
                if (!hasBox)
                    hasBox = item.TryGetProperty("vertices", out boxElement);

                if (hasBox && boxElement.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;

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

                        x = minX;
                        y = minY;
                        width = maxX - minX;
                        height = maxY - minY;

                        double expansionWidthHalf = expansionWidth / 2.0;
                        x = minX - expansionWidthHalf;
                        width = width + expansionWidth;

                        double expansionHeightHalf = expansionHeight / 2.0;
                        y = minY - expansionHeightHalf;
                        height = height + expansionHeight;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OcrResultParser] Error parsing rect: {ex.Message}");
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                Color? foregroundColor = null;
                Color? backgroundColor = null;

                if (item.TryGetProperty("foreground_color", out JsonElement fgEl))
                    foregroundColor = ParseColorFromJson(fgEl);
                if (item.TryGetProperty("background_color", out JsonElement bgEl))
                    backgroundColor = ParseColorFromJson(bgEl);

                int fontSize = 16;
                if (height > 0)
                    fontSize = Math.Max(10, Math.Min(36, (int)(height * 0.9)));

                SolidColorBrush textColor = foregroundColor.HasValue
                    ? new SolidColorBrush(foregroundColor.Value)
                    : new SolidColorBrush(Colors.White);
                SolidColorBrush bgColor = backgroundColor.HasValue
                    ? new SolidColorBrush(backgroundColor.Value)
                    : new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

                var textObject = new TextObject(
                    text, x, y, width, height,
                    textColor, bgColor,
                    x, y,
                    textOrientation);
                textObject.Confidence = confidence;
                textObject.ID = $"text_{idCounter}";
                textObject.SetFontSize(fontSize);
                idCounter++;

                textObjects.Add(textObject);
            }

            return textObjects;
        }

        private static Color? ParseColorFromJson(JsonElement colorElement)
        {
            try
            {
                if (colorElement.TryGetProperty("rgb", out JsonElement rgbElement) &&
                    rgbElement.ValueKind == JsonValueKind.Array &&
                    rgbElement.GetArrayLength() >= 3)
                {
                    int r, g, b;
                    if (rgbElement[0].TryGetInt32(out int rInt)) r = rInt;
                    else r = (int)Math.Round(rgbElement[0].GetDouble());

                    if (rgbElement[1].TryGetInt32(out int gInt)) g = gInt;
                    else g = (int)Math.Round(rgbElement[1].GetDouble());

                    if (rgbElement[2].TryGetInt32(out int bInt)) b = bInt;
                    else b = (int)Math.Round(rgbElement[2].GetDouble());

                    r = Math.Max(0, Math.Min(255, r));
                    g = Math.Max(0, Math.Min(255, g));
                    b = Math.Max(0, Math.Min(255, b));

                    return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
                }

                if (colorElement.TryGetProperty("hex", out JsonElement hexElement))
                {
                    string hex = hexElement.GetString() ?? "";
                    if (!string.IsNullOrEmpty(hex) && hex.StartsWith("#") && hex.Length == 7)
                    {
                        int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                        int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                        int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                        return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
