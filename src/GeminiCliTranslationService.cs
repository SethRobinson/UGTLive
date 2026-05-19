using System;
using System.Text.Json;

namespace UGTLive
{
    /// <summary>
    /// Translation via the Gemini CLI in headless mode
    /// (`gemini --output-format json`, prompt piped via stdin). Uses the user's
    /// Google account login - no API key.
    /// </summary>
    public class GeminiCliTranslationService : CliTranslationServiceBase
    {
        protected override string ProviderName => "Gemini CLI";

        protected override string SetupHint =>
            "Requires Gemini CLI installed and logged in once (run `gemini` and sign in " +
            "with your Google account). Set the command path in Settings if it is not on PATH.";

        protected override string GetCommand() => ConfigManager.Instance.GetGeminiCliCommand();

        protected override string BuildArguments(bool thinkingEnabled)
        {
            // --skip-trust: app dir isn't a "trusted folder" (required headless).
            // -e none: load no extensions (big startup win).
            // Gemini CLI exposes no reasoning/thinking flag - model is the only
            // speed lever (thinkingEnabled is a documented no-op here).
            string model = ConfigManager.Instance.GetGeminiCliModel();
            return $"--output-format json --skip-trust -e none -m {model}";
        }

        protected override string ExtractText(string stdout)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("response", out JsonElement response))
                {
                    return response.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not parse Gemini CLI JSON ({ex.Message}); using raw output");
            }
            return stdout.Trim();
        }
    }
}
