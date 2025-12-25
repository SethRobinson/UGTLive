using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    public class LlamaCppTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient;
        
        // Static constructor to initialize HttpClient once
        static LlamaCppTranslationService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WPFScreenCapture");
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for thinking models
        }
        
        public LlamaCppTranslationService()
        {
            // HttpClient is initialized in static constructor
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Reset translation status
                TranslationStatus.Reset();
                
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
                
                // Get model and thinking mode settings
                string model = ConfigManager.Instance.GetLlamaCppModel();
                bool thinkingModeEnabled = ConfigManager.Instance.GetLlamaCppThinkingMode();
                
                // Detect model type for thinking mode parameters
                bool isGlmModel = !string.IsNullOrEmpty(model) && 
                    model.Contains("glm", StringComparison.OrdinalIgnoreCase);
                bool isDeepSeekModel = !string.IsNullOrEmpty(model) && 
                    model.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
                bool isThinkingModel = (isGlmModel || isDeepSeekModel) && thinkingModeEnabled;
                
                // Create request body (OpenAI-compatible format) with streaming enabled
                var requestBody = new Dictionary<string, object>
                {
                    { "messages", messages },
                    { "temperature", 0.1 },  // Low temperature for more deterministic output
                    { "max_tokens", 2048 },  // Reasonable limit for translation
                    { "stream", true }       // Enable streaming for progress updates
                };
                
                // Add model if specified (required in router mode)
                if (!string.IsNullOrWhiteSpace(model))
                {
                    requestBody["model"] = model;
                    Console.WriteLine($"Using llama.cpp model: {model}");
                }
                
                // Add thinking mode parameters based on model type
                if (isGlmModel)
                {
                    // GLM models have thinking enabled by default
                    // Only add parameter if we want to disable it
                    if (!thinkingModeEnabled)
                    {
                        requestBody["chat_template_kwargs"] = new Dictionary<string, object>
                        {
                            { "enable_thinking", false }
                        };
                        Console.WriteLine("GLM model: Thinking mode disabled");
                    }
                    else
                    {
                        Console.WriteLine("GLM model: Thinking mode enabled (default)");
                    }
                }
                else if (isDeepSeekModel)
                {
                    // DeepSeek models have thinking disabled by default
                    // Only add parameter if we want to enable it
                    if (thinkingModeEnabled)
                    {
                        requestBody["thinking"] = new Dictionary<string, object>
                        {
                            { "type", "enabled" }
                        };
                        Console.WriteLine("DeepSeek model: Thinking mode enabled");
                    }
                    else
                    {
                        Console.WriteLine("DeepSeek model: Thinking mode disabled (default)");
                    }
                }
                
                // Serialize the request body
                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string requestJson = JsonSerializer.Serialize(requestBody, jsonOptions);
                
                // Set up HTTP request
                var request = new HttpRequestMessage(HttpMethod.Post, llamaCppEndpoint);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                
                // Start streaming - signal that we're in streaming mode
                TranslationStatus.StartStreaming(isThinkingModel);
                
                // Send request with streaming - read headers immediately, don't wait for full response
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    TranslationStatus.StopStreaming();
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"Error calling llama.cpp API: {response.StatusCode}");
                    Console.WriteLine($"Response: {errorContent}");
                    
                    string detailedMessage = $"The llama.cpp server returned an error.\n\nStatus: {response.StatusCode}";
                    
                    if (!string.IsNullOrWhiteSpace(errorContent))
                    {
                        detailedMessage += $"\n\nResponse: {errorContent.Substring(0, Math.Min(200, errorContent.Length))}";
                    }
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || 
                        response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                        response.StatusCode == System.Net.HttpStatusCode.BadGateway)
                    {
                        detailedMessage += "\n\nHint: Make sure llama.cpp server is running. Start it with: llama-server -m model.gguf --port 8080";
                    }
                    
                    ErrorPopupManager.ShowError(detailedMessage, "llama.cpp Translation Error");
                    return null;
                }
                
                // Read the streaming response
                string translatedText = await ReadStreamingResponseAsync(response, isThinkingModel, cancellationToken);
                
                TranslationStatus.StopStreaming();
                
                if (string.IsNullOrEmpty(translatedText))
                {
                    Console.WriteLine("llama.cpp returned empty response");
                    return null;
                }
                
                // Log the response
                LogManager.Instance.LogLlmReply(translatedText);
                
                // Log the extracted translation
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    if (translatedText.Length > 100)
                    {
                        Console.WriteLine($"llama.cpp translation extracted: {translatedText.Substring(0, 100)}...");
                    }
                    else
                    {
                        Console.WriteLine($"llama.cpp translation extracted: {translatedText}");
                    }
                }
                
                // Clean up and format the response
                return FormatResponse(translatedText, jsonData, inputJson);
            }
            catch (OperationCanceledException)
            {
                TranslationStatus.StopStreaming();
                Console.WriteLine("llama.cpp translation was cancelled");
                return null;
            }
            catch (HttpRequestException ex)
            {
                TranslationStatus.StopStreaming();
                Console.WriteLine($"Error connecting to llama.cpp server: {ex.Message}");
                Console.WriteLine("Hint: Make sure llama.cpp server is running. Start it with: llama-server -m model.gguf --port 8080");
                
                string errorMessage = $"Failed to connect to llama.cpp server.\n\nError: {ex.Message}\n\nPlease check:\n" +
                    "1. The llama.cpp server is running\n" +
                    "2. The server URL in settings is correct\n" +
                    "3. Your firewall/antivirus isn't blocking the connection\n\n" +
                    "Start the server with: llama-server -m model.gguf --port 8080";
                
                ErrorPopupManager.ShowError(errorMessage, "llama.cpp Connection Error");
                return null;
            }
            catch (Exception ex)
            {
                TranslationStatus.StopStreaming();
                Console.WriteLine($"Error in LlamaCppTranslationService.TranslateAsync: {ex.Message}");
                
                string errorMessage = $"An unexpected error occurred with llama.cpp translation.\n\nError: {ex.Message}";
                ErrorPopupManager.ShowError(errorMessage, "llama.cpp Translation Error");
                return null;
            }
        }
        
        /// <summary>
        /// Read the SSE streaming response and accumulate content
        /// </summary>
        private async Task<string> ReadStreamingResponseAsync(HttpResponseMessage response, bool isThinkingModel, CancellationToken cancellationToken)
        {
            var contentBuilder = new StringBuilder();
            var thinkingBuilder = new StringBuilder();
            bool isInThinkingPhase = isThinkingModel; // Start in thinking phase for thinking models
            
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // SSE format: lines starting with "data: " contain JSON
                if (line.StartsWith("data: "))
                {
                    string jsonPart = line.Substring(6); // Remove "data: " prefix
                    
                    // Check for stream end marker
                    if (jsonPart == "[DONE]")
                    {
                        break;
                    }
                    
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonPart);
                        var root = doc.RootElement;
                        
                        // OpenAI-compatible streaming format:
                        // {"choices":[{"delta":{"content":"text"},"index":0}]}
                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            
                            if (firstChoice.TryGetProperty("delta", out var delta))
                            {
                                // Check for reasoning_content (DeepSeek thinking mode)
                                if (delta.TryGetProperty("reasoning_content", out var reasoningContent))
                                {
                                    string? reasoning = reasoningContent.GetString();
                                    if (!string.IsNullOrEmpty(reasoning))
                                    {
                                        thinkingBuilder.Append(reasoning);
                                        TranslationStatus.IncrementTokenCount();
                                        TranslationStatus.IsThinking = true;
                                    }
                                }
                                
                                // Check for regular content
                                if (delta.TryGetProperty("content", out var content))
                                {
                                    string? text = content.GetString();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        // For GLM models, content before </think> is thinking content
                                        if (isThinkingModel && isInThinkingPhase)
                                        {
                                            if (text.Contains("</think>"))
                                            {
                                                // Split at </think> - everything before is thinking, after is content
                                                int thinkEndIndex = text.IndexOf("</think>");
                                                string thinkPart = text.Substring(0, thinkEndIndex);
                                                string contentPart = text.Substring(thinkEndIndex + 8); // Skip "</think>"
                                                
                                                thinkingBuilder.Append(thinkPart);
                                                contentBuilder.Append(contentPart);
                                                isInThinkingPhase = false;
                                                TranslationStatus.IsThinking = false;
                                            }
                                            else if (text.Contains("<think>"))
                                            {
                                                // Start of thinking block
                                                int thinkStartIndex = text.IndexOf("<think>");
                                                string beforeThink = text.Substring(0, thinkStartIndex);
                                                string afterThink = text.Substring(thinkStartIndex + 7); // Skip "<think>"
                                                
                                                contentBuilder.Append(beforeThink);
                                                thinkingBuilder.Append(afterThink);
                                                TranslationStatus.IsThinking = true;
                                            }
                                            else
                                            {
                                                // Still in thinking phase, but no tags - treat as content
                                                // (thinking phase only active if we see <think> tag)
                                                contentBuilder.Append(text);
                                                TranslationStatus.IsThinking = false;
                                                isInThinkingPhase = false;
                                            }
                                        }
                                        else
                                        {
                                            // Check if we're entering a thinking block
                                            if (text.Contains("<think>"))
                                            {
                                                int thinkStartIndex = text.IndexOf("<think>");
                                                string beforeThink = text.Substring(0, thinkStartIndex);
                                                string afterThink = text.Substring(thinkStartIndex + 7);
                                                
                                                contentBuilder.Append(beforeThink);
                                                thinkingBuilder.Append(afterThink);
                                                isInThinkingPhase = true;
                                                TranslationStatus.IsThinking = true;
                                            }
                                            else
                                            {
                                                contentBuilder.Append(text);
                                            }
                                        }
                                        
                                        TranslationStatus.IncrementTokenCount();
                                    }
                                }
                            }
                            
                            // Check finish_reason to know when we're done
                            if (firstChoice.TryGetProperty("finish_reason", out var finishReason) && 
                                finishReason.ValueKind != JsonValueKind.Null)
                            {
                                string? reason = finishReason.GetString();
                                if (reason == "stop" || reason == "length")
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        // Skip malformed JSON chunks
                        Console.WriteLine($"Failed to parse streaming chunk: {ex.Message}");
                    }
                }
            }
            
            // Log thinking content if any
            if (thinkingBuilder.Length > 0 && ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"llama.cpp thinking content ({thinkingBuilder.Length} chars): {thinkingBuilder.ToString().Substring(0, Math.Min(200, thinkingBuilder.Length))}...");
            }
            
            return contentBuilder.ToString();
        }
        
        /// <summary>
        /// Format the response to match expected output format
        /// </summary>
        private string? FormatResponse(string translatedText, string jsonData, JsonElement inputJson)
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
                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = false
                    };
                    translatedText = JsonSerializer.Serialize(tempJson, options);
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
                    
                    // Check if this is a game JSON translation with text_blocks
                    if (translatedJson.TryGetProperty("text_blocks", out _))
                    {
                        // For game JSON format, we need to match the format that the other translation services use
                        var outputJson = new Dictionary<string, object>
                        {
                            { "translated_text", translatedText },
                            { "original_text", jsonData },
                            { "detected_language", inputJson.GetProperty("source_language").GetString() ?? "ja" }
                        };
                        
                        return JsonSerializer.Serialize(outputJson);
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
                        
                        return JsonSerializer.Serialize(compatibilityOutput);
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
            
            return JsonSerializer.Serialize(formattedOutput);
        }
    }
}
