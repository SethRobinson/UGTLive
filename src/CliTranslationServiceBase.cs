using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    /// <summary>
    /// Base class for translation providers that shell out to an installed CLI
    /// (Claude Code, Codex, Gemini) in headless mode. These reuse the user's
    /// interactive subscription login, so no API key is stored or sent.
    /// </summary>
    public abstract class CliTranslationServiceBase : ITranslationService
    {
        /// <summary>Human-readable provider name for log/error messages.</summary>
        protected abstract string ProviderName { get; }

        /// <summary>The configured command/executable (e.g. "claude").</summary>
        protected abstract string GetCommand();

        /// <summary>CLI arguments (flags only - the prompt is piped via stdin).</summary>
        protected abstract string BuildArguments(bool thinkingEnabled);

        /// <summary>Hint shown when the CLI can't be found / fails to launch.</summary>
        protected abstract string SetupHint { get; }

        /// <summary>Extract the model's answer text from the CLI's stdout.</summary>
        protected abstract string ExtractText(string stdout);

        /// <summary>Command line (command + args) for the current config. Used for prewarming.</summary>
        public (string command, string arguments) GetCommandLine()
        {
            bool thinkingEnabled = ConfigManager.Instance.GetThinkingEnabled();
            return (GetCommand(), BuildArguments(thinkingEnabled));
        }

        public async Task<string?> TranslateAsync(string jsonData, string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                JsonElement inputJson = JsonSerializer.Deserialize<JsonElement>(jsonData);
                bool thinkingEnabled = ConfigManager.Instance.GetThinkingEnabled();

                string command = GetCommand();
                string arguments = BuildArguments(thinkingEnabled);
                string stdinPayload = prompt + "\n\nHere is the input JSON:\n\n" + jsonData;
                string commandLine = $"{command} {arguments}";

                LogManager.Instance.LogLlmRequest(prompt, jsonData, "CLI", commandLine, stdinPayload);

                var totalSw = Stopwatch.StartNew();
                WarmCliProcess warm = CliWarmPool.Acquire(command, arguments);
                Console.WriteLine($"Invoking {ProviderName} ({(warm.Cold ? "COLD spawn" : "WARM pool hit")}): {commandLine}");

                var process = warm.Process;

                // 3-minute cap - cold Node start plus model latency can be slow.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(180));

                var modelSw = Stopwatch.StartNew();
                try
                {
                    // Write under the timeout token: if the CLI bailed instantly
                    // (e.g. Gemini exit 42 on a parked process) the pipe write must
                    // not hang forever.
                    try
                    {
                        await process.StandardInput.WriteAsync(stdinPayload.AsMemory(), timeoutCts.Token);
                        process.StandardInput.Close();
                    }
                    catch (Exception we) when (we is not OperationCanceledException)
                    {
                        Console.WriteLine($"{ProviderName}: stdin write failed ({we.Message}); process likely exited early.");
                    }

                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    warm.Kill();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"{ProviderName} translation was cancelled");
                        return null;
                    }
                    ErrorPopupManager.ShowError(
                        $"The {ProviderName} CLI timed out after 180 seconds.\n\n" +
                        "Try a faster model or check that the CLI is logged in.",
                        $"{ProviderName} Timeout");
                    return null;
                }

                string stdout = await warm.StdoutTask;
                string stderr = await warm.StderrTask;
                modelSw.Stop();
                totalSw.Stop();

                int exitCode = process.ExitCode;
                try { process.Dispose(); } catch { }

                if (exitCode != 0)
                {
                    Console.WriteLine($"{ProviderName} CLI exited with code {exitCode}. Stderr: {stderr}");
                    string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    ErrorPopupManager.ShowError(
                        $"The {ProviderName} CLI returned an error (exit code {exitCode}).\n\n" +
                        $"{Truncate(detail, 400)}\n\n{SetupHint}",
                        $"{ProviderName} Error");
                    return null;
                }

                Console.WriteLine($"{ProviderName} response: total={totalSw.Elapsed.TotalSeconds:F1}s " +
                    $"(post-stdin={modelSw.Elapsed.TotalSeconds:F1}s), {(warm.Cold ? "cold" : "warm")}, {stdout.Length} chars");
                LogManager.Instance.LogLlmReply(stdout);

                string translatedText = ExtractText(stdout);
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    Console.WriteLine($"Failed to extract text from {ProviderName} CLI output");
                    ErrorPopupManager.ShowError(
                        $"The {ProviderName} CLI returned no usable text.\n\n" +
                        $"Output: {Truncate(stdout, 300)}\n\n{SetupHint}",
                        $"{ProviderName} Error");
                    return null;
                }

                return TranslationResponseFormatter.Format(translatedText, jsonData, inputJson);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"{ProviderName} translation was cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in {ProviderName} CLI translation: {ex.Message}");
                ErrorPopupManager.ShowError(
                    $"An unexpected error occurred with {ProviderName} translation.\n\nError: {ex.Message}\n\n{SetupHint}",
                    $"{ProviderName} Error");
                return null;
            }
        }

        private static readonly Regex _ansiRegex = new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = _ansiRegex.Replace(s, "").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
