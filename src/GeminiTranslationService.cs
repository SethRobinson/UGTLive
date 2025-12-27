using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    public class GeminiTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        /// <summary>
        /// Check if the model is a Gemini 3.x model
        /// </summary>
        private bool IsGemini3Model(string model)
        {
            return model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Check if the model supports thinking configuration
        /// </summary>
        private bool SupportsThinking(string model)
        {
            // Gemini 2.5 and 3.x models support thinking
            return model.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase) ||
                   model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Translate text using the Gemini API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <param name="cancellationToken">Cancellation token to cancel the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        public async Task<string?> TranslateAsync(string jsonData, string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                string apiKey = ConfigManager.Instance.GetGeminiApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("Gemini API key not configured");
                    return null;
                }

                // Get model from config
                string model = ConfigManager.Instance.GetGeminiModel();
                bool thinkingEnabled = ConfigManager.Instance.GetGeminiThinkingEnabled();
                
                // Build the generationConfig
                var generationConfig = new Dictionary<string, object>
                {
                    { "response_mime_type", "application/json" }
                };
                
                // Add thinkingConfig for models that support it
                if (SupportsThinking(model))
                {
                    var thinkingConfig = new Dictionary<string, object>();
                    
                    if (IsGemini3Model(model))
                    {
                        // Gemini 3.x uses thinkingLevel: "low" or "high"
                        thinkingConfig["thinkingLevel"] = thinkingEnabled ? "high" : "low";
                    }
                    else
                    {
                        // Gemini 2.5 uses thinkingBudget: 0 (disabled) or -1 (dynamic/enabled)
                        thinkingConfig["thinkingBudget"] = thinkingEnabled ? -1 : 0;
                    }
                    
                    generationConfig["thinkingConfig"] = thinkingConfig;
                    Console.WriteLine($"Gemini thinking config: model={model}, enabled={thinkingEnabled}");
                }
                
                // Build the request content
                var requestContent = new Dictionary<string, object>
                {
                    { "contents", new[]
                        {
                            new Dictionary<string, object>
                            {
                                { "parts", new[]
                                    {
                                        new Dictionary<string, string>
                                        {
                                            { "text", $"{prompt}\n{jsonData}" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    { "generationConfig", generationConfig }
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string requestJson = JsonSerializer.Serialize(requestContent, jsonOptions);
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                HttpResponseMessage response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // Log the raw Gemini response before returning it
                    LogManager.Instance.LogLlmReply(jsonResponse);
                    
                    return jsonResponse;
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Gemini API error: {response.StatusCode}, {errorMessage}");
                    
                    // Try to parse the error message from JSON if possible
                    try
                    {
                        using JsonDocument errorDoc = JsonDocument.Parse(errorMessage);
                        if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                        {
                            string detailedError = "";
                            
                            // Extract error message
                            if (errorElement.TryGetProperty("message", out JsonElement messageElement))
                            {
                                detailedError = messageElement.GetString() ?? "";
                            }
                            
                            // Write error to file
                            System.IO.File.WriteAllText("gemini_last_error.txt", $"Gemini API error: {detailedError}\n\nResponse code: {response.StatusCode}\nFull response: {errorMessage}");
                            
                            // Show error message to user
                            ErrorPopupManager.ShowError(
                                $"Gemini API error: {detailedError}\n\nPlease check your API key and settings.",
                                "Gemini Translation Error");
                            
                            return null;
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse as JSON, just use the raw message
                    }
                    
                    // Write error to file
                    System.IO.File.WriteAllText("gemini_last_error.txt", $"Gemini API error: {response.StatusCode}\n\nFull response: {errorMessage}");
                    
                    // Show general error if JSON parsing failed
                    ErrorPopupManager.ShowError(
                        $"Gemini API error: {response.StatusCode}\n{errorMessage}\n\nPlease check your API key and settings.",
                        "Gemini Translation Error");
                    
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Gemini translation was cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation API error: {ex.Message}");
                
                // Write error to file
                System.IO.File.WriteAllText("gemini_last_error.txt", $"Gemini API error: {ex.Message}\n\nStack trace: {ex.StackTrace}");
                
                // Show error message to user
                string errorMessage = $"Gemini API error: {ex.Message}";
                
                if (ex is HttpRequestException)
                {
                    errorMessage += "\n\nFailed to connect to Gemini API.\n\nPlease check:\n" +
                        "1. Your internet connection\n" +
                        "2. Your API key is correct in settings\n" +
                        "3. Your firewall/antivirus isn't blocking the connection";
                }
                else
                {
                    errorMessage += "\n\nPlease check your network connection and API key.";
                }
                
                ErrorPopupManager.ShowError(errorMessage, "Gemini Translation Error");
                
                return null;
            }
        }
    }
}