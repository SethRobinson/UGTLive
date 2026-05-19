using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UGTLive
{
    /// <summary>
    /// Shared post-processing for LLM translation replies. Mirrors the logic in
    /// ChatGptTranslationService.ProcessTranslatedText so the newer providers
    /// (Anthropic, OpenRouter, and the CLI/subscription services) all return the
    /// exact JSON envelope that Logic expects.
    /// </summary>
    public static class TranslationResponseFormatter
    {
        /// <summary>
        /// Cleans markdown fences / whitespace and wraps the model's text in the
        /// { translated_text, original_text, detected_language } envelope.
        /// </summary>
        public static string? Format(string translatedText, string jsonData, JsonElement inputJson)
        {
            // Clean up the response - sometimes there might be markdown code block markers
            translatedText = translatedText.Trim();
            if (translatedText.StartsWith("```json"))
            {
                translatedText = translatedText.Substring(7);
            }
            else if (translatedText.StartsWith("```"))
            {
                translatedText = translatedText.Substring(3);
            }

            if (translatedText.EndsWith("```"))
            {
                translatedText = translatedText.Substring(0, translatedText.Length - 3);
            }
            translatedText = translatedText.Trim();

            // Clean up escape sequences and newlines in the JSON
            if (translatedText.StartsWith("{") && translatedText.EndsWith("}"))
            {
                if (translatedText.Contains("\r\n"))
                {
                    translatedText = translatedText.Replace("\r\n", " ");
                }

                // Replace nicely formatted JSON with compact JSON for better parsing
                try
                {
                    var tempJson = JsonSerializer.Deserialize<object>(translatedText);
                    var options = new JsonSerializerOptions { WriteIndented = false };
                    translatedText = JsonSerializer.Serialize(tempJson, options);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to normalize JSON format: {ex.Message}");
                }
            }

            string detectedLanguage = "ja";
            try
            {
                if (inputJson.ValueKind == JsonValueKind.Object &&
                    inputJson.TryGetProperty("source_language", out var srcLang))
                {
                    detectedLanguage = srcLang.GetString() ?? "ja";
                }
            }
            catch { }

            var formattedOutput = new Dictionary<string, object>
            {
                { "translated_text", translatedText },
                { "original_text", jsonData },
                { "detected_language", detectedLanguage }
            };

            string output = JsonSerializer.Serialize(formattedOutput);
            Console.WriteLine($"Formatted output: {output.Substring(0, Math.Min(100, output.Length))}...");
            return output;
        }
    }
}
