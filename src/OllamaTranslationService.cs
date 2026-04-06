using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    public class OllamaTranslationService : ITranslationService
    {
        private static readonly HttpClient _httpClient;
        private string _lastPartialContent = "";
        private string _lastPartialThinking = "";

        static OllamaTranslationService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                TranslationStatus.Reset();

                string ollamaEndpoint = ConfigManager.Instance.GetOllamaApiEndpoint();
                string ollamaModel = ConfigManager.Instance.GetOllamaModel();
                bool thinkingModeEnabled = ConfigManager.Instance.GetThinkingEnabled();

                Console.WriteLine($"Sending request to Ollama API at: {ollamaEndpoint}");

                var messages = new List<Dictionary<string, string>>();

                if (thinkingModeEnabled)
                {
                    // When thinking is enabled, move the full instructions into the user
                    // message. A verbose system prompt causes Gemma 4 (and likely other
                    // Ollama models) to skip their thinking phase entirely.
                    messages.Add(new Dictionary<string, string>
                    {
                        { "role", "system" },
                        { "content", "You are a translator." }
                    });
                    messages.Add(new Dictionary<string, string>
                    {
                        { "role", "user" },
                        { "content", prompt + "\n\nHere is the input JSON:\n\n" + jsonData }
                    });
                }
                else
                {
                    messages.Add(new Dictionary<string, string>
                    {
                        { "role", "system" },
                        { "content", prompt }
                    });
                    messages.Add(new Dictionary<string, string>
                    {
                        { "role", "user" },
                        { "content", "Here is the input JSON:\n\n" + jsonData }
                    });
                }

                var requestBody = new Dictionary<string, object>
                {
                    { "model", ollamaModel },
                    { "messages", messages },
                    { "stream", true },
                    { "think", thinkingModeEnabled }
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string requestJson = JsonSerializer.Serialize(requestBody, jsonOptions);

                var request = new HttpRequestMessage(HttpMethod.Post, ollamaEndpoint);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                LogManager.Instance.LogLlmRequest(prompt, jsonData, "POST", ollamaEndpoint, requestJson);

                TranslationStatus.StartStreaming(thinkingModeEnabled);

                var stopwatch = Stopwatch.StartNew();
                var response = await RetryHelper.SendWithRetryAsync(
                    ct => _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct),
                    cancellationToken,
                    maxRetries: 3,
                    baseDelayMs: 10000,
                    onRetry: (attempt, status) => Console.WriteLine($"[Ollama] Retry {attempt} after HTTP {(int)status}"));

                if (!response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    TranslationStatus.StopStreaming();
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"Ollama API error: {response.StatusCode}, {errorContent}");

                    try
                    {
                        using JsonDocument errorDoc = JsonDocument.Parse(errorContent);
                        if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                        {
                            string detailedError = errorElement.GetString() ?? errorContent;
                            ErrorPopupManager.ShowError(
                                $"Ollama error: {detailedError}\n\nPlease check your model name and Ollama settings.",
                                "Ollama Translation Error");
                            return null;
                        }
                    }
                    catch (JsonException)
                    {
                    }

                    ErrorPopupManager.ShowError(
                        $"Ollama API error: {response.StatusCode}\n{errorContent}\n\nPlease check your settings.",
                        "Ollama Translation Error");
                    return null;
                }

                string responseText = await ReadStreamingResponseAsync(response, cancellationToken);
                stopwatch.Stop();

                TranslationStatus.StopStreaming();

                if (string.IsNullOrEmpty(responseText))
                {
                    Console.WriteLine("Ollama returned empty response");
                    return null;
                }

                int thinkingChars = _lastPartialThinking?.Length ?? 0;
                int tokenCount = TranslationStatus.TokenCount;
                double tps = stopwatch.Elapsed.TotalSeconds > 0 ? tokenCount / stopwatch.Elapsed.TotalSeconds : 0;
                Console.WriteLine($"Ollama response complete: {stopwatch.Elapsed.TotalSeconds:F1}s, {tokenCount} tokens ({tps:F1} t/s), {responseText.Length} content chars, {thinkingChars} thinking chars");

                string logText = responseText;
                if (!string.IsNullOrEmpty(_lastPartialThinking))
                {
                    logText = $"[THINKING]\n{_lastPartialThinking}\n\n[CONTENT]\n{responseText}";
                }
                LogManager.Instance.LogLlmReply(logText);

                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    string preview = responseText.Length > 100
                        ? responseText.Substring(0, 100) + "..."
                        : responseText;
                    Console.WriteLine($"Ollama translation extracted: {preview}");
                }

                return FormatResponse(responseText, jsonOptions);
            }
            catch (OperationCanceledException)
            {
                TranslationStatus.StopStreaming();
                Console.WriteLine("Ollama translation was cancelled");

                if (!string.IsNullOrEmpty(_lastPartialContent))
                {
                    string partialLog = _lastPartialContent;
                    if (!string.IsNullOrEmpty(_lastPartialThinking))
                    {
                        partialLog = $"[THINKING]\n{_lastPartialThinking}\n\n[CONTENT]\n{_lastPartialContent}";
                    }
                    Console.WriteLine($"Logging partial Ollama response ({_lastPartialContent.Length} content chars)");
                    LogManager.Instance.LogLlmReply($"[CANCELLED - partial response]\n{partialLog}");
                }

                return null;
            }
            catch (HttpRequestException ex)
            {
                TranslationStatus.StopStreaming();
                Console.WriteLine($"Error connecting to Ollama server: {ex.Message}");

                ErrorPopupManager.ShowError(
                    $"Failed to connect to Ollama server.\n\nError: {ex.Message}\n\nPlease check:\n" +
                    "1. Ollama is running\n" +
                    "2. The server URL in settings is correct\n" +
                    "3. Your firewall/antivirus isn't blocking the connection",
                    "Ollama Connection Error");
                return null;
            }
            catch (Exception ex)
            {
                TranslationStatus.StopStreaming();
                Console.WriteLine($"Ollama API error: {ex.Message}");

                ErrorPopupManager.ShowError(
                    $"Ollama API error: {ex.Message}\n\nPlease check your network connection and Ollama settings.",
                    "Ollama Translation Error");
                return null;
            }
        }

        /// <summary>
        /// Read Ollama's newline-delimited JSON streaming response and accumulate content
        /// </summary>
        private async Task<string> ReadStreamingResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var contentBuilder = new StringBuilder();
            var thinkingBuilder = new StringBuilder();

            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("message", out var message))
                        {
                            if (message.TryGetProperty("thinking", out var thinking))
                            {
                                string? thinkText = thinking.GetString();
                                if (!string.IsNullOrEmpty(thinkText))
                                {
                                    thinkingBuilder.Append(thinkText);
                                    TranslationStatus.IncrementTokenCount();
                                    TranslationStatus.IsThinking = true;
                                }
                            }

                            if (message.TryGetProperty("content", out var content))
                            {
                                string? text = content.GetString();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    contentBuilder.Append(text);
                                    TranslationStatus.IncrementTokenCount();
                                    TranslationStatus.IsThinking = false;
                                }
                            }
                        }

                        if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                            break;
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Failed to parse Ollama streaming chunk: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Ollama streaming cancelled after {contentBuilder.Length} content chars, {thinkingBuilder.Length} thinking chars");
                throw;
            }
            finally
            {
                if (thinkingBuilder.Length > 0 && ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    string preview = thinkingBuilder.Length > 200
                        ? thinkingBuilder.ToString().Substring(0, 200) + "..."
                        : thinkingBuilder.ToString();
                    Console.WriteLine($"Ollama thinking content ({thinkingBuilder.Length} chars): {preview}");
                }

                _lastPartialContent = contentBuilder.ToString();
                _lastPartialThinking = thinkingBuilder.ToString();
            }

            return contentBuilder.ToString();
        }

        /// <summary>
        /// Format the streamed response into the Gemini-compatible structure that Logic.cs expects
        /// </summary>
        private string? FormatResponse(string responseText, JsonSerializerOptions jsonOptions)
        {
            responseText = responseText.Trim();

            if (responseText.StartsWith("```json"))
            {
                int endIndex = responseText.IndexOf("```", 7);
                if (endIndex > 0)
                {
                    responseText = responseText.Substring(7, endIndex - 7).Trim();
                }
            }
            else if (responseText.StartsWith("```"))
            {
                responseText = responseText.Substring(3);
                if (responseText.EndsWith("```"))
                {
                    responseText = responseText.Substring(0, responseText.Length - 3);
                }
                responseText = responseText.Trim();
            }

            if (responseText.EndsWith("```"))
            {
                responseText = responseText.Substring(0, responseText.Length - 3).Trim();
            }

            if (!responseText.StartsWith("{"))
            {
                int jsonStart = responseText.IndexOf('{');
                int jsonEnd = responseText.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    responseText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }
            }

            if (responseText.StartsWith("{") && responseText.EndsWith("}"))
            {
                try
                {
                    var tempJson = JsonSerializer.Deserialize<object>(responseText);
                    var compactOptions = new JsonSerializerOptions { WriteIndented = false };
                    responseText = JsonSerializer.Serialize(tempJson, compactOptions);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to normalize JSON format: {ex.Message}");
                }
            }

            var formattedResponse = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = responseText
                                }
                            }
                        }
                    }
                }
            };

            return JsonSerializer.Serialize(formattedResponse, jsonOptions);
        }

    }
}
