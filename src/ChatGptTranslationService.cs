using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    public class ChatGptTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _configFilePath;
        
        public ChatGptTranslationService()
        {
            // Set the base address for OpenAI API
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WPFScreenCapture");
            
            // Get the configuration file path
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = System.IO.Path.Combine(appDirectory, "chatgpt_config.txt");
            
            // Ensure the config file exists
            if (!System.IO.File.Exists(_configFilePath))
            {
                CreateDefaultConfigFile();
            }
        }
        
        private void CreateDefaultConfigFile()
        {
            try
            {
                string defaultPrompt = "You are a translator. Translate the text I'll provide into English. Keep it simple and conversational.";
                string content = $"<llm_prompt_multi_start>\n{defaultPrompt}\n<llm_prompt_multi_end>";
                System.IO.File.WriteAllText(_configFilePath, content);
                Console.WriteLine("Created default ChatGPT config file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating default ChatGPT config file: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the model requires the Responses API instead of Chat Completions API
        /// </summary>
        private bool RequiresResponsesApi(string model)
        {
            // GPT-5.2 Pro requires the Responses API (/v1/responses)
            return model.Equals("gpt-5.2-pro", StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Check if the model supports reasoning effort at all (GPT-5 series only)
        /// </summary>
        private bool SupportsReasoningEffort(string model)
        {
            // Only GPT-5 series and later support reasoning_effort parameter
            // GPT-4.1 and older models do not support it
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Check if the model supports 'none' reasoning effort (GPT-5.1 and GPT-5.2 only)
        /// </summary>
        private bool SupportsNoneReasoningEffort(string model)
        {
            // Only GPT-5.1 and GPT-5.2 series support 'none' reasoning effort
            return model.StartsWith("gpt-5.1", StringComparison.OrdinalIgnoreCase) ||
                   model.StartsWith("gpt-5.2", StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Get the appropriate reasoning effort value based on model and thinking setting
        /// Returns null if the model doesn't support reasoning effort
        /// </summary>
        private string? GetReasoningEffort(string model, bool thinkingEnabled)
        {
            // Check if model supports reasoning effort at all
            if (!SupportsReasoningEffort(model))
            {
                return null; // GPT-4.1 and older don't support this parameter
            }
            
            if (thinkingEnabled)
            {
                // When thinking is enabled, use medium for all models
                return "medium";
            }
            else
            {
                // When thinking is disabled:
                // - GPT-5.1 and GPT-5.2 support 'none'
                // - GPT-5, GPT-5 Mini, GPT-5 Nano support 'low', 'medium', 'high' (not 'none')
                return SupportsNoneReasoningEffort(model) ? "none" : "low";
            }
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Get API key and model from config
                string apiKey = ConfigManager.Instance.GetChatGptApiKey();
                string model = ConfigManager.Instance.GetChatGptModel();
                
                // Validate we have an API key
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Console.WriteLine("ChatGPT API key is missing. Please set it in the settings.");
                    return null;
                }
                
                // Parse the input JSON
                JsonElement inputJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
                
                // Get custom prompt from config
                string customPrompt = ConfigManager.Instance.GetServicePrompt("ChatGPT");
                
                // Get max completion tokens from config
                int maxCompletionTokens = ConfigManager.Instance.GetChatGptMaxCompletionTokens();
                bool thinkingEnabled = ConfigManager.Instance.GetChatGptThinkingEnabled();
                
                // Get the appropriate reasoning effort based on model and thinking setting
                // Returns null for models that don't support reasoning effort (GPT-4.1 and older)
                string? reasoningEffort = GetReasoningEffort(model, thinkingEnabled);

                // Determine which API to use based on model
                bool useResponsesApi = RequiresResponsesApi(model);
                
                string apiEndpoint;
                string requestJson;
                
                if (useResponsesApi)
                {
                    // Use the Responses API for GPT-5.2 Pro
                    apiEndpoint = "https://api.openai.com/v1/responses";
                    
                    // Build input text combining prompt and data
                    string inputText = "";
                    if (!string.IsNullOrWhiteSpace(customPrompt) && !customPrompt.Contains("translator"))
                    {
                        inputText = customPrompt + "\n\n";
                    }
                    inputText += "Here is the input JSON:\n\n" + jsonData;
                    
                    // Create Responses API request body
                    var requestBody = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "input", inputText },
                        { "max_output_tokens", maxCompletionTokens }
                    };
                    
                    // Add reasoning config only for models that support it
                    if (reasoningEffort != null)
                    {
                        requestBody.Add("reasoning", new Dictionary<string, string> { { "effort", reasoningEffort } });
                    }
                    
                    requestJson = JsonSerializer.Serialize(requestBody);
                    Console.WriteLine($"Using Responses API for model: {model}, reasoning effort: {reasoningEffort ?? "not supported"}");
                }
                else
                {
                    // Use the Chat Completions API for other models
                    apiEndpoint = "https://api.openai.com/v1/chat/completions";
                    
                    // Build messages array for ChatGPT API
                    var messages = new List<Dictionary<string, string>>();
                    
                    // Use the exact prompt format as specified
                    StringBuilder systemPrompt = new StringBuilder();
         
                    // Add any custom instructions from the config file
                    if (!string.IsNullOrWhiteSpace(customPrompt) && !customPrompt.Contains("translator"))
                    {
                         systemPrompt.AppendLine(customPrompt);
                    }
                    
                    messages.Add(new Dictionary<string, string> 
                    {
                        { "role", "system" },
                        { "content", systemPrompt.ToString() }
                    });
                    
                    // Add the text to translate as the user message
                    messages.Add(new Dictionary<string, string>
                    {
                        { "role", "user" },
                        { "content", "Here is the input JSON:\n\n" + jsonData }
                    });
                    
                    // Create request body
                    var requestBody = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "messages", messages },
                        { "max_completion_tokens", maxCompletionTokens }
                    };
                    
                    // Add reasoning_effort only for models that support it (GPT-5 series)
                    if (reasoningEffort != null)
                    {
                        requestBody.Add("reasoning_effort", reasoningEffort);
                    }
                    
                    requestJson = JsonSerializer.Serialize(requestBody);
                    Console.WriteLine($"Using Chat Completions API for model: {model}, reasoning effort: {reasoningEffort ?? "not supported"}");
                }
                
                // Set up HTTP request
                var request = new HttpRequestMessage(HttpMethod.Post, apiEndpoint);
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                
                // Send request to OpenAI API
                var response = await _httpClient.SendAsync(request, cancellationToken);
                string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // Check if request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Log raw response before any processing
                    LogManager.Instance.LogLlmReply(responseContent);
                    
                    try
                    {
                        string translatedText;
                        
                        if (useResponsesApi)
                        {
                            // Parse Responses API response format
                            translatedText = ParseResponsesApiResponse(responseContent);
                        }
                        else
                        {
                            // Parse Chat Completions API response format
                            translatedText = ParseChatCompletionsResponse(responseContent);
                        }
                        
                        if (string.IsNullOrEmpty(translatedText))
                        {
                            Console.WriteLine("Failed to extract translated text from response");
                            return null;
                        }
                        
                        // Process the translated text and return formatted output
                        return ProcessTranslatedText(translatedText, jsonData, inputJson);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing ChatGPT response: {ex.Message}");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine($"Error calling ChatGPT API: {response.StatusCode}");
                    Console.WriteLine($"Response: {responseContent}");
                    
                    string errorMessage = $"ChatGPT API error: {response.StatusCode}";
                    string detailedMessage = $"The ChatGPT API returned an error.\n\nStatus: {response.StatusCode}";
                    
                    // Try to parse error details from response
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(responseContent))
                        {
                            using JsonDocument errorDoc = JsonDocument.Parse(responseContent);
                            if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                            {
                                if (errorElement.TryGetProperty("message", out JsonElement messageElement))
                                {
                                    string apiErrorMessage = messageElement.GetString() ?? "";
                                    detailedMessage += $"\n\nError: {apiErrorMessage}";
                                }
                                
                                if (errorElement.TryGetProperty("type", out JsonElement typeElement))
                                {
                                    string errorType = typeElement.GetString() ?? "";
                                    detailedMessage += $"\n\nType: {errorType}";
                                }
                            }
                            else
                            {
                                detailedMessage += $"\n\nResponse: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse as JSON, include raw response
                        if (!string.IsNullOrWhiteSpace(responseContent))
                        {
                            detailedMessage += $"\n\nResponse: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                        }
                    }
                    
                    // Add helpful hints based on status code
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        detailedMessage += "\n\nPlease check your API key in settings.";
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || 
                             response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        detailedMessage += "\n\nThe service may be temporarily unavailable or rate limited. Please try again later.";
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        detailedMessage += "\n\nPlease check your request format and settings.";
                    }
                    
                    ErrorPopupManager.ShowError(detailedMessage, "ChatGPT Translation Error");
                }
                
                return null;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("ChatGPT translation was cancelled");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error connecting to ChatGPT API: {ex.Message}");
                
                string errorMessage = $"Failed to connect to ChatGPT API.\n\nError: {ex.Message}\n\nPlease check:\n" +
                    "1. Your internet connection\n" +
                    "2. Your API key is correct in settings\n" +
                    "3. Your firewall/antivirus isn't blocking the connection";
                
                ErrorPopupManager.ShowError(errorMessage, "ChatGPT Connection Error");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ChatGptTranslationService.TranslateAsync: {ex.Message}");
                
                string errorMessage = $"An unexpected error occurred with ChatGPT translation.\n\nError: {ex.Message}";
                ErrorPopupManager.ShowError(errorMessage, "ChatGPT Translation Error");
                return null;
            }
        }

        /// <summary>
        /// Parse response from the Chat Completions API (/v1/chat/completions)
        /// </summary>
        private string ParseChatCompletionsResponse(string responseContent)
        {
            var responseObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
            if (responseObj != null && responseObj.TryGetValue("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                return firstChoice.GetProperty("message").GetProperty("content").GetString() ?? "";
            }
            return "";
        }

        /// <summary>
        /// Parse response from the Responses API (/v1/responses)
        /// </summary>
        private string ParseResponsesApiResponse(string responseContent)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                
                // The Responses API returns output in the "output" array
                if (root.TryGetProperty("output", out JsonElement output) && output.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in output.EnumerateArray())
                    {
                        // Look for message type output
                        if (item.TryGetProperty("type", out JsonElement typeElement) && 
                            typeElement.GetString() == "message")
                        {
                            if (item.TryGetProperty("content", out JsonElement content) && 
                                content.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var contentItem in content.EnumerateArray())
                                {
                                    if (contentItem.TryGetProperty("type", out JsonElement contentType) &&
                                        contentType.GetString() == "output_text")
                                    {
                                        if (contentItem.TryGetProperty("text", out JsonElement text))
                                        {
                                            return text.GetString() ?? "";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Fallback: try to get output_text directly if it's a simpler response format
                if (root.TryGetProperty("output_text", out JsonElement outputText))
                {
                    return outputText.GetString() ?? "";
                }
                
                Console.WriteLine($"Could not parse Responses API response: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Responses API response: {ex.Message}");
            }
            return "";
        }

        /// <summary>
        /// Process the translated text and format the output
        /// </summary>
        private string? ProcessTranslatedText(string translatedText, string jsonData, JsonElement inputJson)
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
                    var translatedJson = JsonSerializer.Deserialize<JsonElement>(translatedText);
                    
                    // Check if this is a game JSON translation with text_blocks
                    if (translatedJson.TryGetProperty("text_blocks", out _))
                    {
                        try 
                        {
                            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                            string debugFilePath = System.IO.Path.Combine(appDirectory, "chatgpt_translation_debug.txt");
                            System.IO.File.WriteAllText(debugFilePath, translatedText);
                            Console.WriteLine($"Debug translation saved to {debugFilePath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to write debug file: {ex.Message}");
                        }
                        
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
}