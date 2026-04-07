using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    public class LogManager
    {
        private static LogManager? _instance;
        private readonly string _logDirectory;
        private readonly object _fileLock = new object(); // Thread-safe file access
        
        // Log file paths
        private readonly string _ocrResponsePath;
        private readonly string _llmRequestPath;
        private readonly string _llmReplyPath;
        
        // Singleton pattern
        public static LogManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LogManager();
                }
                return _instance;
            }
        }
        
        // Constructor
        private LogManager()
        {
            // Set log directory to be in the application's directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _logDirectory = appDirectory;
            
            // Set log file paths
            _ocrResponsePath = Path.Combine(_logDirectory, "last_ocr_reply_received.txt");
            _llmRequestPath = Path.Combine(_logDirectory, "last_llm_request_sent.txt");
            _llmReplyPath = Path.Combine(_logDirectory, "last_llm_reply_received.txt");
            
            Console.WriteLine($"Log files will be saved in: {_logDirectory}");
        }
        
        // Log OCR response (thread-safe, non-blocking)
        public void LogOcrResponse(string jsonData)
        {
            // Fire-and-forget to avoid blocking
            Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        // Attempt to format the JSON for better readability
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(jsonData);
                            var options = new JsonSerializerOptions 
                            { 
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                            };
                            jsonData = JsonSerializer.Serialize(doc.RootElement, options);
                        }
                        catch
                        {
                            // If formatting fails, use the original JSON
                        }
                        
                        // Write to file
                        File.WriteAllText(_ocrResponsePath, jsonData);
                        //Console.WriteLine($"OCR response logged to {_ocrResponsePath}");
                    }
                    catch (Exception ex)
                    {
                        // Use debug output to avoid potential deadlock with Console.WriteLine
                        System.Diagnostics.Debug.WriteLine($"Error logging OCR response: {ex.Message}");
                    }
                }
            });
        }
        
        // Log LLM request (thread-safe, non-blocking)
        public void LogLlmRequest(string prompt, string jsonData)
        {
            LogLlmRequest(prompt, jsonData, null, null, null);
        }

        /// <summary>
        /// Log LLM request with full HTTP request details. API keys are automatically masked.
        /// </summary>
        public void LogLlmRequest(string prompt, string jsonData, string? httpMethod, string? httpUrl, string? httpBody)
        {
            // Fire-and-forget to avoid blocking
            Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder();

                        // Log the full HTTP request first so it's easy to see
                        if (!string.IsNullOrEmpty(httpMethod) && !string.IsNullOrEmpty(httpUrl))
                        {
                            sb.AppendLine("=== HTTP REQUEST ===");
                            sb.AppendLine($"{httpMethod} {MaskApiKeys(httpUrl)}");
                            sb.AppendLine();

                            if (!string.IsNullOrEmpty(httpBody))
                            {
                                sb.AppendLine("--- Request Body ---");
                                string maskedBody = MaskApiKeys(httpBody);
                                try
                                {
                                    using JsonDocument doc = JsonDocument.Parse(maskedBody);
                                    var options = new JsonSerializerOptions
                                    {
                                        WriteIndented = true,
                                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                    };
                                    sb.AppendLine(JsonSerializer.Serialize(doc.RootElement, options));
                                }
                                catch
                                {
                                    sb.AppendLine(maskedBody);
                                }
                                sb.AppendLine();
                            }
                        }

                        sb.AppendLine("=== LLM PROMPT ===");
                        sb.AppendLine(prompt);
                        sb.AppendLine();
                        sb.AppendLine("=== INPUT JSON ===");
                        
                        // Attempt to format the JSON for better readability
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(jsonData);
                            var options = new JsonSerializerOptions 
                            { 
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                            };
                            sb.AppendLine(JsonSerializer.Serialize(doc.RootElement, options));
                        }
                        catch
                        {
                            sb.AppendLine(jsonData);
                        }
                        
                        File.WriteAllText(_llmRequestPath, sb.ToString());
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"LLM request logged to {_llmRequestPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error logging LLM request: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// Mask API keys and tokens in a string. Keeps first 4 and last 4 chars, replaces the middle with ***.
        /// </summary>
        private static string MaskApiKeys(string input)
        {
            // Mask "key=VALUE" or "key=VALUE" in query strings
            input = Regex.Replace(input, @"([?&]key=)([^&\s]{9,})", m =>
            {
                string key = m.Groups[2].Value;
                return m.Groups[1].Value + key.Substring(0, 4) + "***" + key.Substring(key.Length - 4);
            });

            // Mask "api_key":"VALUE" or "apikey":"VALUE" in JSON bodies
            input = Regex.Replace(input, @"(""(?:api[_-]?key|apikey|authorization|token)""\s*:\s*"")((?:Bearer\s+)?[^""]{9,})("")", m =>
            {
                string val = m.Groups[2].Value;
                string prefix = "";
                if (val.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = "Bearer ";
                    val = val.Substring(7);
                }
                return m.Groups[1].Value + prefix + val.Substring(0, 4) + "***" + val.Substring(val.Length - 4) + m.Groups[3].Value;
            }, RegexOptions.IgnoreCase);

            // Mask Bearer tokens in header-style strings
            input = Regex.Replace(input, @"(Bearer\s+)([^\s""]{9,})", m =>
            {
                string token = m.Groups[2].Value;
                return m.Groups[1].Value + token.Substring(0, 4) + "***" + token.Substring(token.Length - 4);
            });

            return input;
        }
        
        public void LogTranslationError(string sourceFile, int pageNumber, string request, string response)
        {
            Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        string errorDir = Path.Combine(_logDirectory, "output", "errors");
                        Directory.CreateDirectory(errorDir);

                        string safeName = Path.GetFileNameWithoutExtension(sourceFile);
                        foreach (char c in Path.GetInvalidFileNameChars())
                            safeName = safeName.Replace(c, '_');

                        string timestamp = DateTime.Now.ToString("HHmmss");
                        string baseName = $"{safeName}_page{pageNumber}_{timestamp}";

                        string requestPath = Path.Combine(errorDir, $"{baseName}_request.txt");
                        string responsePath = Path.Combine(errorDir, $"{baseName}_response.txt");

                        File.WriteAllText(requestPath, formatJsonSafe(request));
                        File.WriteAllText(responsePath, formatJsonSafe(response));

                        Console.WriteLine($"[BatchConverter] Translation error logged to: {errorDir}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error logging translation error: {ex.Message}");
                    }
                }
            });
        }

        private string formatJsonSafe(string text)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                return JsonSerializer.Serialize(doc.RootElement, options);
            }
            catch
            {
                return text;
            }
        }

        // Log LLM reply (thread-safe, non-blocking)
        public void LogLlmReply(string jsonResponse)
        {
            // Fire-and-forget to avoid blocking
            Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        // Attempt to format the JSON for better readability
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                            var options = new JsonSerializerOptions 
                            { 
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                            };
                            jsonResponse = JsonSerializer.Serialize(doc.RootElement, options);
                        }
                        catch
                        {
                            // If formatting fails, use the original JSON
                        }
                        
                        // Write to file
                        File.WriteAllText(_llmReplyPath, jsonResponse);
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"LLM reply logged to {_llmReplyPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Use debug output to avoid potential deadlock with Console.WriteLine
                        System.Diagnostics.Debug.WriteLine($"Error logging LLM reply: {ex.Message}");
                    }
                }
            });
        }
    }
}