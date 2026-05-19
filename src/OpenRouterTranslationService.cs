using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    /// <summary>
    /// Translation via OpenRouter (https://openrouter.ai/api/v1/chat/completions).
    /// OpenAI-compatible chat-completions API with Bearer auth. Honors the shared
    /// "Enable Thinking Mode" checkbox via OpenRouter's unified `reasoning` parameter.
    /// </summary>
    public class OpenRouterTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string ApiEndpoint = "https://openrouter.ai/api/v1/chat/completions";

        public async Task<string?> TranslateAsync(string jsonData, string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string apiKey = ConfigManager.Instance.GetOpenRouterApiKey();
                string model = ConfigManager.Instance.GetOpenRouterModel();

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Console.WriteLine("OpenRouter API key is missing. Please set it in the settings.");
                    ErrorPopupManager.ShowError(
                        "OpenRouter API key is missing.\n\nPlease set it in Settings > Translation.",
                        "OpenRouter Translation Error");
                    return null;
                }

                JsonElement inputJson = JsonSerializer.Deserialize<JsonElement>(jsonData);

                int maxTokens = ConfigManager.Instance.GetChatGptMaxCompletionTokens();
                bool thinkingEnabled = ConfigManager.Instance.GetThinkingEnabled();

                var messages = new List<Dictionary<string, string>>
                {
                    new Dictionary<string, string> { { "role", "system" }, { "content", prompt } },
                    new Dictionary<string, string> { { "role", "user" }, { "content", "Here is the input JSON:\n\n" + jsonData } }
                };

                var requestBody = new Dictionary<string, object>
                {
                    { "model", model },
                    { "messages", messages },
                    { "max_tokens", maxTokens }
                };

                // Only opt INTO reasoning when the user enabled thinking. Do NOT send
                // reasoning:{enabled:false} - some models (e.g. Gemini 3.x Pro) reject
                // "reasoning is mandatory and cannot be disabled" with a hard 400.
                if (thinkingEnabled)
                {
                    requestBody["reasoning"] = new Dictionary<string, object> { { "enabled", true } };
                }

                string requestJson = JsonSerializer.Serialize(requestBody);

                LogManager.Instance.LogLlmRequest(prompt, jsonData, "POST", ApiEndpoint, requestJson);

                Console.WriteLine($"Sending request to OpenRouter API ({model})");
                var stopwatch = Stopwatch.StartNew();

                var response = await RetryHelper.SendWithRetryAsync(
                    ct =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
                        request.Headers.Add("Authorization", $"Bearer {apiKey}");
                        // Optional OpenRouter attribution headers
                        request.Headers.Add("HTTP-Referer", "https://github.com/SethRobinson/UGTLive");
                        request.Headers.Add("X-Title", "UGTLive");
                        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        return _httpClient.SendAsync(request, ct);
                    },
                    cancellationToken,
                    maxRetries: 3,
                    baseDelayMs: 10000,
                    onRetry: (attempt, status) => Console.WriteLine($"[OpenRouter] Retry {attempt} after HTTP {(int)status}"));

                string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"OpenRouter response complete: {stopwatch.Elapsed.TotalSeconds:F1}s, {responseContent.Length} chars");
                    LogManager.Instance.LogLlmReply(responseContent);

                    string translatedText = ParseResponse(responseContent);
                    if (string.IsNullOrEmpty(translatedText))
                    {
                        Console.WriteLine("Failed to extract translated text from OpenRouter response");
                        return null;
                    }

                    return TranslationResponseFormatter.Format(translatedText, jsonData, inputJson);
                }

                Console.WriteLine($"Error calling OpenRouter API: {response.StatusCode}");
                Console.WriteLine($"Response: {responseContent}");

                if (TranslationErrorPolicy.IsNonRetryableStatus(response.StatusCode))
                    TranslationErrorPolicy.SignalNonRetryable($"OpenRouter HTTP {(int)response.StatusCode} ({response.StatusCode})");

                string detailedMessage = $"The OpenRouter API returned an error.\n\nStatus: {response.StatusCode}";
                try
                {
                    if (!string.IsNullOrWhiteSpace(responseContent))
                    {
                        using JsonDocument errorDoc = JsonDocument.Parse(responseContent);
                        if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement) &&
                            errorElement.TryGetProperty("message", out JsonElement messageElement))
                        {
                            detailedMessage += $"\n\nError: {messageElement.GetString()}";
                        }
                        else
                        {
                            detailedMessage += $"\n\nResponse: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                        }
                    }
                }
                catch (JsonException)
                {
                    detailedMessage += $"\n\nResponse: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    detailedMessage += "\n\nPlease check your API key in settings.";
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    detailedMessage += "\n\nRate limited. Please try again later.";
                }

                ErrorPopupManager.ShowError(detailedMessage, "OpenRouter Translation Error");
                return null;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("OpenRouter translation was cancelled");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error connecting to OpenRouter API: {ex.Message}");
                ErrorPopupManager.ShowError(
                    $"Failed to connect to OpenRouter API.\n\nError: {ex.Message}\n\nPlease check:\n" +
                    "1. Your internet connection\n" +
                    "2. Your API key is correct in settings\n" +
                    "3. Your firewall/antivirus isn't blocking the connection",
                    "OpenRouter Connection Error");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OpenRouterTranslationService.TranslateAsync: {ex.Message}");
                ErrorPopupManager.ShowError(
                    $"An unexpected error occurred with OpenRouter translation.\n\nError: {ex.Message}",
                    "OpenRouter Translation Error");
                return null;
            }
        }

        private string ParseResponse(string responseContent)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) &&
                    choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing OpenRouter response: {ex.Message}");
            }
            return "";
        }
    }
}
