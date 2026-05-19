using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    /// <summary>
    /// Headless smoke-test for translation providers, so a provider/model can be
    /// validated end-to-end (request -> response -> envelope parsing) without the GUI.
    ///
    /// Usage:
    ///   ugtlive.exe --test-translate --provider GeminiCli [--model gemini-2.5-flash]
    ///               [--thinking] [--ocr-json path] [--text "some text"]
    ///               [--source ja] [--target en]
    ///
    /// Exit code 0 = PASS (usable translation), 1 = FAIL.
    /// </summary>
    public static class TranslationTestHarness
    {
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();

        public static bool IsTestModeRequested(string[] args)
        {
            return Array.Exists(args, a => a == "--test-translate");
        }

        public static int Run(string[] args)
        {
            AllocConsole();
            ErrorPopupManager.SuppressPopups = true;

            // Tee console output to a result file so it can be read back even though
            // this is a GUI-subsystem exe (AllocConsole isn't pipe-capturable).
            string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_translate_result.txt");
            TextWriter? fileWriter = null;
            try { fileWriter = new StreamWriter(resultPath, false) { AutoFlush = true }; } catch { }
            if (fileWriter != null)
            {
                Console.SetOut(new TeeTextWriter(Console.Out, fileWriter));
            }

            try
            {
                // Run off the UI thread - OnStartup has a DispatcherSynchronizationContext
                // installed, so blocking on an awaiting task here would deadlock.
                return Task.Run(() => RunAsync(args)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HARNESS] Unhandled exception: {ex}");
                return 1;
            }
            finally
            {
                fileWriter?.Flush();
                fileWriter?.Dispose();
            }
        }

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _a;
            private readonly TextWriter _b;
            public TeeTextWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
            public override void Write(char value) { _a.Write(value); _b.Write(value); }
            public override void Write(string? value) { _a.Write(value); _b.Write(value); }
            public override void WriteLine(string? value) { _a.WriteLine(value); _b.WriteLine(value); }
        }

        private static string? GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            if (i >= 0 && i + 1 < args.Length) return args[i + 1];
            return null;
        }

        private static async Task<int> RunAsync(string[] args)
        {
            string provider = GetArg(args, "--provider") ?? "GeminiCli";
            string? model = GetArg(args, "--model");
            bool thinking = Array.Exists(args, a => a == "--thinking");
            string source = GetArg(args, "--source") ?? "ja";
            string target = GetArg(args, "--target") ?? "en";
            string? ocrJsonPath = GetArg(args, "--ocr-json");
            string? overrideText = GetArg(args, "--text");
            string? apiKey = GetArg(args, "--api-key");
            string? imagePath = GetArg(args, "--image");
            string? ocrMethod = GetArg(args, "--ocr");
            bool startServices = Array.Exists(args, a => a == "--start-services");
            int runs = int.TryParse(GetArg(args, "--runs"), out int rn) && rn > 0 ? rn : 1;
            bool pool = Array.Exists(args, a => a == "--pool");
            int poolSize = int.TryParse(GetArg(args, "--pool-size"), out int ps) && ps > 0 ? ps : 1;
            bool prewarm = Array.Exists(args, a => a == "--prewarm");
            int warmupDelay = int.TryParse(GetArg(args, "--warmup-delay"), out int wd) && wd >= 0 ? wd : 8;

            CliWarmPool.Configure(pool, poolSize, warmupDelay);

            Console.WriteLine("=====================================================");
            Console.WriteLine($" UGTLive translation smoke-test");
            Console.WriteLine($" Provider : {provider}");
            Console.WriteLine($" Model    : {model ?? "(config default)"}");
            Console.WriteLine($" Thinking : {thinking}");
            Console.WriteLine($" {source} -> {target}");
            if (imagePath != null)
                Console.WriteLine($" Image    : {imagePath}  (OCR: {ocrMethod ?? "(config default)"})");
            Console.WriteLine($" Runs     : {runs}   Warm pool: {(pool ? $"ON (size {poolSize}, settle {warmupDelay}s)" : "OFF")}   Prewarm: {prewarm}");
            Console.WriteLine("=====================================================");

            var cfg = ConfigManager.Instance;

            // Apply test parameters (saved & restored so we don't clobber the user's config)
            string origService = cfg.GetCurrentTranslationService();
            bool origThinking = cfg.GetThinkingEnabled();
            string origSource = cfg.GetSourceLanguage();
            string origTarget = cfg.GetTargetLanguage();
            string origOcr = cfg.GetOcrMethod();
            string? origModel = ApplyModel(cfg, provider, model);
            string? origKey = ApplyApiKey(cfg, provider, apiKey);
            if (ocrMethod != null) cfg.SetOcrMethod(ocrMethod);

            cfg.SetTranslationService(provider);
            cfg.SetThinkingEnabled(thinking);
            cfg.SetSourceLanguage(source);
            cfg.SetTargetLanguage(target);

            try
            {
                string ocrJson;
                if (imagePath != null)
                {
                    ocrJson = await BuildOcrJsonFromImageAsync(imagePath, source, target, startServices);
                    if (ocrJson == null!)
                    {
                        Console.WriteLine("RESULT: FAIL - OCR produced no text blocks from the image");
                        return 1;
                    }
                }
                else
                {
                    ocrJson = BuildOcrJson(ocrJsonPath, overrideText, source, target);
                }
                Console.WriteLine($"Input OCR JSON:\n{ocrJson}\n");

                string prompt = cfg.GetServicePrompt(provider)
                    .Replace("{SOURCE_LANG}", Logic.GetLanguageName(source))
                    .Replace("{TARGET_LANG}", Logic.GetLanguageName(target));

                ITranslationService service = TranslationServiceFactory.CreateService(provider);

                // Optional prewarm: pre-spawn the pool and wait until a process is parked
                if (prewarm && pool && service is CliTranslationServiceBase cliSvc)
                {
                    var (cmd, cargs) = cliSvc.GetCommandLine();
                    Console.WriteLine($"Prewarming pool: {cmd} {cargs}");
                    var warmSw = System.Diagnostics.Stopwatch.StartNew();
                    CliWarmPool.Prewarm(cmd, cargs);
                    while (CliWarmPool.ReadyCount(cmd, cargs) < 1
                           && !CliWarmPool.IsUnpoolable(cmd, cargs)
                           && warmSw.Elapsed.TotalSeconds < 90)
                        await Task.Delay(250);
                    warmSw.Stop();
                    if (CliWarmPool.IsUnpoolable(cmd, cargs))
                        Console.WriteLine($"Prewarm aborted after {warmSw.Elapsed.TotalSeconds:F1}s - this CLI cannot be pre-warmed (exits when parked).");
                    else
                        Console.WriteLine(CliWarmPool.ReadyCount(cmd, cargs) >= 1
                            ? $"Prewarm complete in {warmSw.Elapsed.TotalSeconds:F1}s (process parked & waiting on stdin)"
                            : $"Prewarm timed out after {warmSw.Elapsed.TotalSeconds:F1}s (continuing anyway)");
                }

                var times = new List<double>();
                bool allPass = true;
                string? lastResponse = null;
                for (int run = 1; run <= runs; run++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string? response = await service.TranslateAsync(ocrJson, prompt, CancellationToken.None);
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalSeconds);
                    lastResponse = response;
                    bool ok = ValidateEnvelope(response);
                    allPass &= ok;
                    Console.WriteLine($"  run {run}/{runs}: {sw.Elapsed.TotalSeconds:F1}s  {(ok ? "OK" : "FAIL")}");
                }

                Console.WriteLine($"\n--- Last raw response ---\n{lastResponse ?? "(null)"}\n--- End ---\n");

                if (times.Count > 0)
                {
                    Console.WriteLine("===== TIMING SUMMARY =====");
                    for (int i = 0; i < times.Count; i++)
                        Console.WriteLine($"  run {i + 1}: {times[i]:F1}s{(i == 0 ? "  (first)" : "")}");
                    if (times.Count > 1)
                    {
                        var rest = times.GetRange(1, times.Count - 1);
                        double avg = 0; foreach (var t in rest) avg += t; avg /= rest.Count;
                        double min = rest[0], max = rest[0];
                        foreach (var t in rest) { if (t < min) min = t; if (t > max) max = t; }
                        Console.WriteLine($"  first run        : {times[0]:F1}s");
                        Console.WriteLine($"  subsequent avg   : {avg:F1}s  (min {min:F1}s / max {max:F1}s, n={rest.Count})");
                        Console.WriteLine($"  speedup vs first : {(times[0] / Math.Max(0.01, avg)):F2}x");
                    }
                    Console.WriteLine("==========================");
                }

                Console.WriteLine(allPass
                    ? "RESULT: PASS - usable translation produced"
                    : "RESULT: FAIL - response not usable by ProcessTranslatedJSON");
                return allPass ? 0 : 1;
            }
            finally
            {
                // Restore the user's original settings
                RestoreApiKey(cfg, provider, origKey);
                cfg.SetOcrMethod(origOcr);
                cfg.SetTranslationService(origService);
                cfg.SetThinkingEnabled(origThinking);
                cfg.SetSourceLanguage(origSource);
                cfg.SetTargetLanguage(origTarget);
                RestoreModel(cfg, provider, origModel);
            }
        }

        private static string? ApplyModel(ConfigManager cfg, string provider, string? model)
        {
            if (model == null) return null;
            switch (provider)
            {
                case "Anthropic": { var o = cfg.GetAnthropicModel(); cfg.SetAnthropicModel(model); return o; }
                case "OpenRouter": { var o = cfg.GetOpenRouterModel(); cfg.SetOpenRouterModel(model); return o; }
                case "ClaudeCli": { var o = cfg.GetClaudeCliModel(); cfg.SetClaudeCliModel(model); return o; }
                case "CodexCli": { var o = cfg.GetCodexCliModel(); cfg.SetCodexCliModel(model); return o; }
                case "GeminiCli": { var o = cfg.GetGeminiCliModel(); cfg.SetGeminiCliModel(model); return o; }
                case "ChatGPT": { var o = cfg.GetChatGptModel(); cfg.SetChatGptModel(model); return o; }
                case "Gemini": { var o = cfg.GetGeminiModel(); cfg.SetGeminiModel(model); return o; }
                default: return null;
            }
        }

        private static string? ApplyApiKey(ConfigManager cfg, string provider, string? key)
        {
            if (key == null) return null;
            switch (provider)
            {
                case "Anthropic": { var o = cfg.GetAnthropicApiKey(); cfg.SetAnthropicApiKey(key); return o; }
                case "OpenRouter": { var o = cfg.GetOpenRouterApiKey(); cfg.SetOpenRouterApiKey(key); return o; }
                case "ChatGPT": { var o = cfg.GetChatGptApiKey(); cfg.SetChatGptApiKey(key); return o; }
                case "Gemini": { var o = cfg.GetGeminiApiKey(); cfg.SetGeminiApiKey(key); return o; }
                default: return null;
            }
        }

        private static void RestoreApiKey(ConfigManager cfg, string provider, string? key)
        {
            if (key == null) return;
            switch (provider)
            {
                case "Anthropic": cfg.SetAnthropicApiKey(key); break;
                case "OpenRouter": cfg.SetOpenRouterApiKey(key); break;
                case "ChatGPT": cfg.SetChatGptApiKey(key); break;
                case "Gemini": cfg.SetGeminiApiKey(key); break;
            }
        }

        private static void RestoreModel(ConfigManager cfg, string provider, string? model)
        {
            if (model == null) return;
            switch (provider)
            {
                case "Anthropic": cfg.SetAnthropicModel(model); break;
                case "OpenRouter": cfg.SetOpenRouterModel(model); break;
                case "ClaudeCli": cfg.SetClaudeCliModel(model); break;
                case "CodexCli": cfg.SetCodexCliModel(model); break;
                case "GeminiCli": cfg.SetGeminiCliModel(model); break;
                case "ChatGPT": cfg.SetChatGptModel(model); break;
                case "Gemini": cfg.SetGeminiModel(model); break;
            }
        }

        private static string BuildOcrJson(string? path, string? overrideText, string source, string target)
        {
            if (path != null && File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            var blocks = new List<object>();
            if (overrideText != null)
            {
                blocks.Add(new { id = "text_0", text = overrideText, rect = new { x = 10, y = 10, width = 200, height = 40 } });
            }
            else
            {
                // Built-in Japanese sample
                blocks.Add(new { id = "text_0", text = "こんにちは、元気ですか？", rect = new { x = 10, y = 10, width = 220, height = 40 } });
                blocks.Add(new { id = "text_1", text = "冒険を始めましょう！", rect = new { x = 10, y = 60, width = 220, height = 40 } });
            }

            var ocrData = new
            {
                source_language = source,
                target_language = target,
                text_blocks = blocks,
                previous_context = Array.Empty<object>(),
                game_info = ""
            };

            return JsonSerializer.Serialize(ocrData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        /// <summary>
        /// OCRs a real image with the configured OCR method (mirrors
        /// BatchConverterService.runOcrAsync), then builds the translation input JSON.
        /// Returns null if no text blocks were detected.
        /// </summary>
        private static async Task<string> BuildOcrJsonFromImageAsync(string imagePath, string source, string target, bool startServices)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image not found: {imagePath}");

            string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"OCR method: {ocrMethod}");

            using var bitmap = new Bitmap(imagePath);
            Console.WriteLine($"Loaded image: {bitmap.Width}x{bitmap.Height}");

            string ocrResultJson;
            if (ocrMethod == "Windows OCR")
            {
                var lines = await WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, source);
                ocrResultJson = WindowsOCRManager.Instance.FormatOcrLinesToJson(lines);
            }
            else if (ocrMethod == "Google Vision")
            {
                var tos = await GoogleVisionOCRService.Instance.ProcessImageAsync(bitmap, source);
                ocrResultJson = GoogleVisionOCRService.Instance.FormatResultsToJson(tos);
            }
            else
            {
                PythonServicesManager.Instance.DiscoverServices();
                var svc = PythonServicesManager.Instance.GetServiceByName(ocrMethod);
                if (svc == null)
                    throw new InvalidOperationException($"OCR service '{ocrMethod}' not found under app\\services.");

                bool running = svc.IsRunning || await svc.CheckIsRunningAsync(forceCheck: true);
                if (!running)
                {
                    if (!startServices)
                        throw new InvalidOperationException(
                            $"OCR service '{ocrMethod}' is not running. Re-run with --start-services to launch it, " +
                            "or start the GPU services from the app first.");
                    Console.WriteLine($"Service '{ocrMethod}' not running - starting it (this can take a while on first load)...");
                    running = await svc.StartAsync(showWindow: false);
                    if (!running)
                        throw new InvalidOperationException($"Failed to start OCR service '{ocrMethod}'.");
                    Console.WriteLine($"Service '{ocrMethod}' is up.");
                }
                else
                {
                    Console.WriteLine($"Service '{ocrMethod}' already running.");
                }

                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string? result = await Logic.Instance.ProcessImageWithHttpServiceAsync(imageBytes, ocrMethod, source, suppressErrorUI: true);
                sw.Stop();
                Console.WriteLine($"OCR HTTP call took {sw.Elapsed.TotalSeconds:F1}s");
                if (result == null)
                    throw new InvalidOperationException($"OCR service '{ocrMethod}' returned no result.");
                ocrResultJson = result;
            }

            Console.WriteLine($"--- Raw OCR result ({ocrResultJson?.Length ?? 0} chars) ---");
            Console.WriteLine(ocrResultJson == null ? "(null)"
                : ocrResultJson.Substring(0, Math.Min(2000, ocrResultJson.Length)));
            Console.WriteLine("--- End raw OCR ---");

            var textObjects = OcrResultParser.ParseOcrJsonToTextObjects(ocrResultJson ?? "");
            Console.WriteLine($"OCR detected {textObjects.Count} text block(s):");
            foreach (var t in textObjects)
                Console.WriteLine($"  {t.ID} [{t.X:F0},{t.Y:F0} {t.Width:F0}x{t.Height:F0}]: {t.Text}");

            if (textObjects.Count == 0)
                return null!;

            var blocks = textObjects.Select(t => (object)new
            {
                id = t.ID,
                text = t.Text,
                rect = new { x = t.X, y = t.Y, width = t.Width, height = t.Height }
            }).ToList();

            var ocrData = new
            {
                source_language = source,
                target_language = target,
                text_blocks = blocks,
                previous_context = Array.Empty<object>(),
                game_info = ConfigManager.Instance.GetGameInfo()
            };

            return JsonSerializer.Serialize(ocrData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        /// <summary>
        /// Mirrors the ChatGPT-style branch of Logic.ProcessTranslatedJSON: the
        /// outer envelope must have translated_text whose inner JSON has text_blocks.
        /// </summary>
        private static bool ValidateEnvelope(string? response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(response);
                if (!doc.RootElement.TryGetProperty("translated_text", out var tt))
                {
                    Console.WriteLine("[VALIDATE] missing 'translated_text'");
                    return false;
                }
                string inner = tt.GetString() ?? "";
                if (!inner.TrimStart().StartsWith("{"))
                {
                    Console.WriteLine($"[VALIDATE] translated_text is not JSON: {Trunc(inner)}");
                    return false;
                }
                using JsonDocument innerDoc = JsonDocument.Parse(inner);
                if (!innerDoc.RootElement.TryGetProperty("text_blocks", out var tb) ||
                    tb.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("[VALIDATE] inner JSON missing text_blocks array");
                    return false;
                }
                Console.WriteLine($"[VALIDATE] text_blocks count = {tb.GetArrayLength()}");
                foreach (var b in tb.EnumerateArray())
                {
                    string id = b.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    string txt = b.TryGetProperty("text", out var txEl) ? txEl.GetString() ?? "" : "";
                    Console.WriteLine($"  {id}: {txt}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATE] parse error: {ex.Message}");
                return false;
            }
        }

        private static string Trunc(string s) => s.Length <= 200 ? s : s.Substring(0, 200) + "...";
    }
}
