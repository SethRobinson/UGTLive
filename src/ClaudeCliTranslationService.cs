using System;
using System.Text.Json;

namespace UGTLive
{
    /// <summary>
    /// Translation via the Claude Code CLI in headless mode
    /// (`claude -p --output-format json`). Uses the user's Claude subscription
    /// login - no API key. NOTE: --bare is intentionally NOT used (it would skip
    /// OAuth/keychain and force API-key auth).
    /// </summary>
    public class ClaudeCliTranslationService : CliTranslationServiceBase
    {
        protected override string ProviderName => "Claude CLI";

        protected override string SetupHint =>
            "Requires Claude Code installed and logged in once (run `claude` and sign in " +
            "with your Pro/Max subscription). Set the command path in Settings if it is not on PATH.";

        protected override string GetCommand() => ConfigManager.Instance.GetClaudeCliCommand();

        protected override string BuildArguments(bool thinkingEnabled)
        {
            string model = ConfigManager.Instance.GetClaudeCliModel();
            // Startup trims that still keep OAuth/subscription auth (NOT --bare, which
            // would force an API key): no tools, no skills, no MCP, no session file.
            // --effort honors the shared thinking checkbox (low when off = fastest).
            string effort = thinkingEnabled ? "high" : "low";
            return $"-p --output-format json --model {model} --effort {effort} " +
                   "--tools \"\" --disable-slash-commands --strict-mcp-config " +
                   "--no-session-persistence --setting-sources user";
        }

        protected override string ExtractText(string stdout)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("result", out JsonElement result))
                {
                    return result.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not parse Claude CLI JSON ({ex.Message}); using raw output");
            }
            return stdout.Trim();
        }
    }
}
