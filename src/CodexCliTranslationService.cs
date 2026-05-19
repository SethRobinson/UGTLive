using System;
using System.Text.Json;

namespace UGTLive
{
    /// <summary>
    /// Translation via the OpenAI Codex CLI in headless mode
    /// (`codex exec --json -`). Uses the user's ChatGPT subscription login - no API key.
    /// </summary>
    public class CodexCliTranslationService : CliTranslationServiceBase
    {
        protected override string ProviderName => "Codex CLI";

        protected override string SetupHint =>
            "Requires Codex CLI installed and logged in once (run `codex login` and sign in " +
            "with your ChatGPT subscription). Set the command path in Settings if it is not on PATH.";

        protected override string GetCommand() => ConfigManager.Instance.GetCodexCliCommand();

        protected override string BuildArguments(bool thinkingEnabled)
        {
            string model = ConfigManager.Instance.GetCodexCliModel();
            // model_reasoning_effort honors the thinking checkbox (low = fastest).
            // --ephemeral (no session files) + --skip-git-repo-check trim startup.
            string effort = thinkingEnabled ? "medium" : "low";
            return $"exec --json --ephemeral --skip-git-repo-check " +
                   $"-c model_reasoning_effort={effort} -m {model} -";
        }

        protected override string ExtractText(string stdout)
        {
            // --json emits JSON Lines; the final assistant message is the last
            // event whose item.type == "agent_message".
            string lastAgentMessage = "";
            foreach (string rawLine in stdout.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] != '{')
                {
                    continue;
                }
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("item", out JsonElement item) &&
                        item.TryGetProperty("type", out JsonElement itemType) &&
                        itemType.GetString() == "agent_message" &&
                        item.TryGetProperty("text", out JsonElement text))
                    {
                        lastAgentMessage = text.GetString() ?? lastAgentMessage;
                    }
                    else if (root.TryGetProperty("type", out JsonElement t) &&
                             t.GetString() == "agent_message" &&
                             root.TryGetProperty("text", out JsonElement directText))
                    {
                        lastAgentMessage = directText.GetString() ?? lastAgentMessage;
                    }
                }
                catch
                {
                    // Non-JSON progress line - ignore.
                }
            }

            if (!string.IsNullOrWhiteSpace(lastAgentMessage))
            {
                return lastAgentMessage;
            }

            Console.WriteLine("Could not parse Codex CLI JSONL; using raw output");
            return stdout.Trim();
        }
    }
}
