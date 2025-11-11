using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UGTLive
{
    public class LlamaCppTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public LlamaCppTranslationService()
        {
            // Set the user agent
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WPFScreenCapture");
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt)
        {
            try
            {
                // Get the llama.cpp API endpoint from config
                string llamaCppEndpoint = ConfigManager.Instance.GetLlamaCppApiEndpoint();
                
                Console.WriteLine($"Sending request to llama.cpp API at: {llamaCppEndpoint}");
                
                // Parse the input JSON
                JsonElement inputJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
                
                // Get custom prompt from config
                string customPrompt = ConfigManager.Instance.GetServicePrompt("llama.cpp");
                
                // Build messages array for OpenAI-compatible API
                var messages = new List<Dictionary<string, string>>();
                
                // Add system message with the prompt
                messages.Add(new Dictionary<string, string> 
                {
                    { "role", "system" },
                    { "content", customPrompt }
                });
                
                // Add the text to translate as the user message
                messages.Add(new Dictionary<string, string>
                {
                    { "role", "user" },
                    { "content", "Here is the input JSON:\n\n" + jsonData }
                });
                
                // Create request body (OpenAI-compatible format)
                var requestBody = new Dictionary<string, object>
                {
                    { "messages", messages },
                    { "temperature", 0.1 },  // Low temperature for more deterministic output
                    { "max_tokens", 2048 }   // Reasonable limit for translation
                };
                
                // Serialize the request body
                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string requestJson = JsonSerializer.Serialize(requestBody, jsonOptions);
                
                // Set up HTTP request
                var request = new HttpRequestMessage(HttpMethod.Post, llamaCppEndpoint);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                
                // Send request to llama.cpp API
                var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                
                // Check if request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Log raw response before any processing
                    LogManager.Instance.LogLlmReply(responseContent);
                    
                    // Log to console for debugging (limited to first 500 chars)
                    if (responseContent.Length > 500)
                    {
                        Console.WriteLine($"llama.cpp API response: {responseContent.Substring(0, 500)}...");
                    }
                    else
                    {
                        Console.WriteLine($"llama.cpp API response: {responseContent}");
                    }
                    
                    try
                    {
                        // Parse response (OpenAI format)
                        var responseObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
                        if (responseObj != null && responseObj.TryGetValue("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            string translatedText = firstChoice.GetProperty("message").GetProperty("content").GetString() ?? "";
                            
                            // Log the extracted translation
                            if (translatedText.Length > 100)
                            {
                                Console.WriteLine($"llama.cpp translation extracted: {translatedText.Substring(0, 100)}...");
                            }
                            else
                            {
                                Console.WriteLine($"llama.cpp translation extracted: {translatedText}");
                            }
                            
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
                                    var options = new JsonSerializerOptions 
                                    { 
                                        WriteIndented = false
                                    };
                                    translatedText = JsonSerializer.Serialize(tempJson, options);
                                    Console.WriteLine("Successfully normalized JSON format");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to normalize JSON format: {ex.Message}");
                                }
                            }
                            
                            // Check if the response is in JSON format
                            if (translatedText.StartsWith("{") && translatedText.EndsWith("}"))
                            {
                                try
                                {
                                    // Validate it's proper JSON by parsing it
                                    var translatedJson = JsonSerializer.Deserialize<JsonElement>(translatedText);
                                    
                                    // Log that we got valid JSON
                                    Console.WriteLine("llama.cpp returned valid JSON");
                                    
                                    // Check if this is a game JSON translation with text_blocks
                                    if (translatedJson.TryGetProperty("text_blocks", out _))
                                    {
                                        // For game JSON format, we need to match the format that the other translation services use
                                        Console.WriteLine("This is a game JSON format - wrapping in the standard format");
                                        
                                        var outputJson = new Dictionary<string, object>
                                        {
                                            { "translated_text", translatedText },
                                            { "original_text", jsonData },
                                            { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                                        };
                                        
                                        string finalOutput = JsonSerializer.Serialize(outputJson);
                                        Console.WriteLine($"Final wrapped output: {finalOutput.Substring(0, Math.Min(100, finalOutput.Length))}...");
                                        
                                        return finalOutput;
                                    }
                                    else
                                    {
                                        // For other formats, we'll wrap the result in the standard format
                                        var compatibilityOutput = new Dictionary<string, object>
                                        {
                                            { "translated_text", translatedText },
                                            { "original_text", jsonData },
                                            { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                                        };
                                        
                                        string finalOutput = JsonSerializer.Serialize(compatibilityOutput);
                                        Console.WriteLine($"Final output format: {finalOutput.Substring(0, Math.Min(100, finalOutput.Length))}...");
                                        
                                        return finalOutput;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error parsing JSON response: {ex.Message}");
                                    // Not valid JSON, will handle as plain text below
                                }
                            }
                            
                            // If we got plain text or invalid JSON, wrap it in our format
                            var formattedOutput = new Dictionary<string, object>
                            {
                                { "translated_text", translatedText },
                                { "original_text", jsonData },
                                { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                            };
                            
                            string output = JsonSerializer.Serialize(formattedOutput);
                            Console.WriteLine($"Formatted as plain text, output: {output.Substring(0, Math.Min(100, output.Length))}...");
                            
                            return output;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing llama.cpp response: {ex.Message}");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine($"Error calling llama.cpp API: {response.StatusCode}");
                    Console.WriteLine($"Response: {responseContent}");
                    
                    // Common error: server not running
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || 
                        response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                        response.StatusCode == System.Net.HttpStatusCode.BadGateway)
                    {
                        Console.WriteLine("Hint: Make sure llama.cpp server is running. Start it with: llama-server -m model.gguf --port 8080");
                    }
                }
                
                return null;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error connecting to llama.cpp server: {ex.Message}");
                Console.WriteLine("Hint: Make sure llama.cpp server is running. Start it with: llama-server -m model.gguf --port 8080");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in LlamaCppTranslationService.TranslateAsync: {ex.Message}");
                return null;
            }
        }
    }
}
