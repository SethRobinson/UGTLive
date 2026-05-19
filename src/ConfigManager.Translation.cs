using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class ConfigManager
    {
        // Get Gemini API key
        public string GetGeminiApiKey()
        {
            return GetValue(GEMINI_API_KEY);
        }
        
        // Set Gemini API key
        public void SetGeminiApiKey(string apiKey)
        {
            _configValues[GEMINI_API_KEY] = apiKey;
            SaveConfig();
        }
        
        // Get/Set Ollama URL
        public string GetOllamaUrl()
        {
            return GetValue(OLLAMA_URL, "http://localhost");
        }
        
        public void SetOllamaUrl(string url)
        {
            _configValues[OLLAMA_URL] = url;
            SaveConfig();
        }
        
        // Get/Set Ollama Port
        public string GetOllamaPort()
        {
            return GetValue(OLLAMA_PORT, "11434");
        }
        
        public void SetOllamaPort(string port)
        {
            _configValues[OLLAMA_PORT] = port;
            SaveConfig();
        }
        
        // Get/Set Ollama Model
        public string GetOllamaModel()
        {
            return GetValue(OLLAMA_MODEL, "llama3"); // Default to llama3
        }
        
        public void SetOllamaModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[OLLAMA_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Ollama model set to: {model}");
            }
        }
        
        // Get/Set Ollama Thinking Mode
        public bool GetOllamaThinkingMode()
        {
            string value = GetValue(OLLAMA_THINKING_MODE, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetOllamaThinkingMode(bool enabled)
        {
            _configValues[OLLAMA_THINKING_MODE] = enabled ? "true" : "false";
            SaveConfig();
        }
        
        // Unified thinking enabled for all services
        public bool GetThinkingEnabled()
        {
            string value = GetValue(THINKING_ENABLED, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetThinkingEnabled(bool enabled)
        {
            _configValues[THINKING_ENABLED] = enabled ? "true" : "false";
            SaveConfig();
        }
        
        // Get the full Ollama API endpoint
        public string GetOllamaApiEndpoint()
        {
            string url = GetOllamaUrl();
            string port = GetOllamaPort();
            
            // Ensure URL doesn't end with a slash
            if (url.EndsWith("/"))
            {
                url = url.Substring(0, url.Length - 1);
            }
            
            return $"{url}:{port}/api/chat";
        }
        
        // Get/Set llama.cpp URL
        public string GetLlamaCppUrl()
        {
            return GetValue(LLAMACPP_URL, "http://localhost");
        }
        
        public void SetLlamaCppUrl(string url)
        {
            _configValues[LLAMACPP_URL] = url;
            SaveConfig();
        }
        
        // Get/Set llama.cpp Port
        public string GetLlamaCppPort()
        {
            return GetValue(LLAMACPP_PORT, "8080");
        }
        
        public void SetLlamaCppPort(string port)
        {
            _configValues[LLAMACPP_PORT] = port;
            SaveConfig();
        }
        
        // Get/Set llama.cpp Model
        public string GetLlamaCppModel()
        {
            return GetValue(LLAMACPP_MODEL, "");
        }
        
        public void SetLlamaCppModel(string model)
        {
            _configValues[LLAMACPP_MODEL] = model;
            SaveConfig();
        }
        
        // Get/Set llama.cpp Thinking Mode
        public bool GetLlamaCppThinkingMode()
        {
            string value = GetValue(LLAMACPP_THINKING_MODE, "true");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetLlamaCppThinkingMode(bool enabled)
        {
            _configValues[LLAMACPP_THINKING_MODE] = enabled ? "true" : "false";
            SaveConfig();
        }
        
        // Get the full llama.cpp API endpoint
        public string GetLlamaCppApiEndpoint()
        {
            string url = GetLlamaCppUrl();
            string port = GetLlamaCppPort();
            
            // Ensure URL doesn't end with a slash
            if (url.EndsWith("/"))
            {
                url = url.Substring(0, url.Length - 1);
            }
            
            return $"{url}:{port}/v1/chat/completions";
        }
        
        public void ResetAllPromptsToDefault()
        {
            string defaultPrompt = GetDefaultPrompt("");
            
            string[] services = { "Gemini", "Ollama", "ChatGPT", "llama.cpp",
                "Anthropic", "OpenRouter", "ClaudeCli", "CodexCli", "GeminiCli" };
            foreach (string service in services)
            {
                SaveServicePrompt(service, defaultPrompt);
                Console.WriteLine($"Reset {service} prompt to default");
            }
        }
        
        // LAST_PROMPT_CHANGE_VERSION: bump this only when the default prompts actually change.
        private const double LAST_PROMPT_CHANGE_VERSION = 1.24;
        
        public void MigratePromptsIfNeeded()
        {
            string lastVersion = GetValue(LAST_PROMPT_UPGRADE_VERSION, "0");
            if (!double.TryParse(lastVersion, NumberStyles.Float, 
                CultureInfo.InvariantCulture, out double lastPromptVersion))
            {
                lastPromptVersion = 0;
            }
            
            if (lastPromptVersion >= LAST_PROMPT_CHANGE_VERSION)
            {
                return;
            }
            
            if (_isNewConfig)
            {
                // Fresh install — prompts are already the latest defaults, just stamp the version
                SetValue(LAST_PROMPT_UPGRADE_VERSION, 
                    LAST_PROMPT_CHANGE_VERSION.ToString(CultureInfo.InvariantCulture));
                SaveConfig();
                return;
            }
            
            var result = MessageBox.Show(
                "The default LLM translation prompts have been improved in this version " +
                "(fixed a backwards example and unsubstituted language placeholders that caused " +
                "some models to return untranslated text).\n\n" +
                "Would you like to reset ALL service prompts to the new defaults?\n\n" +
                "Choose 'Yes' if you haven't customized your prompts (recommended).\n" +
                "Choose 'No' to keep your current prompts.",
                "Prompt Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                ResetAllPromptsToDefault();
                Console.WriteLine("User chose to reset all prompts to defaults");
            }
            else
            {
                Console.WriteLine("User chose to keep existing prompts");
            }
            
            SetValue(LAST_PROMPT_UPGRADE_VERSION, 
                LAST_PROMPT_CHANGE_VERSION.ToString(CultureInfo.InvariantCulture));
            SaveConfig();
        }
        
        // Get LLM Prompt from the current translation service
        public string GetLlmPrompt()
        {
            return GetServicePrompt(_currentTranslationService);
        }
        
        // Get prompt for specific translation service
        public string GetServicePrompt(string service)
        {
            // Google Translate doesn't use prompts
            if (service == "Google Translate")
            {
                return "";
            }
            
            string filePath = _llmPromptConfigFilePath;

            try
            {
                if (File.Exists(filePath))
                {
                    string content = File.ReadAllText(filePath);
                    
                    // Extract prompt text using regex
                    string pattern = @"<llm_prompt_multi_start>(.*?)<llm_prompt_multi_end>";
                    Match match = Regex.Match(content, pattern, RegexOptions.Singleline);
                    
                    if (match.Success && match.Groups.Count >= 2)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }
                
                // Return default prompt if file doesn't exist or prompt not found
                return "You are a translator. Translate the text I'll provide into English. Keep it simple and conversational.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading service prompt: {ex.Message}");
                return "Error loading prompt";
            }
        }
        
        // Get default prompt for translation service
        public string GetDefaultPrompt(string service)
        {
            // Google Translate doesn't use prompts
            if (service == "Google Translate")
            {
                return "";
            }
            
            // All services use the same default prompt
            // NOTE: {SOURCE_LANG} and {TARGET_LANG} are replaced at runtime by Logic.cs with actual language names
            return @"Your task is to translate the {SOURCE_LANG} text in the following JSON data to {TARGET_LANG} and output a new JSON in a specific format.  This is text from OCR of a screenshot from a video game, so please try to infer the context and which parts are menu or dialog. It might also be a webpage or manga, so just do your best.

CRITICAL OUTPUT FORMAT REQUIREMENTS:

* Output ONLY the resulting JSON data with NO extra text, explanations, markdown code blocks, or formatting.
* The output JSON must have the exact same structure as the input JSON: a source_language, target_language, and a text_blocks array.
* Each element in the text_blocks array must include: id, text (TRANSLATED), and rect (the bounding box).
* The ""text"" field in the OUTPUT must contain the TRANSLATED text in {TARGET_LANG}. Do NOT create new fields like ""english_text"", ""japanese_text"", ""translated_text"", etc.
* Keep the same field names as the input - just replace the text content with its translation.
* If ""previous_context"" data exists in the input JSON, use it to better understand context, but do NOT include it in your output.
* Do NOT return the ""previous_context"" or ""game_info"" parameters in your output - those are input-only.
* If the text looks like multiple options for the player to choose from, add a newline after each one so they aren't mushed together.

EXAMPLE:
Input text_block: {""id"": ""text_0"", ""text"": ""<some text in {SOURCE_LANG}>"", ""rect"": {...}}
Output text_block: {""id"": ""text_0"", ""text"": ""<translated text in {TARGET_LANG}>"", ""rect"": {...}}

Here is the input JSON:";
        }
        
        // Save prompt for specific translation service
        public bool SaveServicePrompt(string service, string prompt)
        {
            // Google Translate doesn't use prompts
            if (service == "Google Translate")
            {
                return true;
            }
            
            string filePath = _llmPromptConfigFilePath;

            try
            {
                string content = $"<llm_prompt_multi_start>\n{prompt}\n<llm_prompt_multi_end>";
                File.WriteAllText(filePath, content);
                Console.WriteLine($"Saved {service} prompt ({prompt.Length} chars)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving service prompt: {ex.Message}");
                return false;
            }
        }
        
        // Check if auto sizing text blocks is enabled
        public bool IsAutoSizeTextBlocksEnabled()
        {
            string value = GetValue(AUTO_SIZE_TEXT_BLOCKS, "true");
            return value.ToLower() == "true";
        }
        
        // Check if auto translate is enabled
        public bool IsAutoTranslateEnabled()
        {
            string value = GetValue(AUTO_TRANSLATE_ENABLED, "false");
            return value.ToLower() == "true";
        }
        
        // Set auto translate enabled
        public void SetAutoTranslateEnabled(bool enabled)
        {
            _configValues[AUTO_TRANSLATE_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Auto translate enabled: {enabled}");
        }
        
        // Check if pause OCR while translating is enabled
        public bool IsPauseOcrWhileTranslatingEnabled()
        {
            string value = GetValue(PAUSE_OCR_WHILE_TRANSLATING, "true");
            return value.ToLower() == "true";
        }
        
        // Set pause OCR while translating enabled
        public void SetPauseOcrWhileTranslatingEnabled(bool enabled)
        {
            _configValues[PAUSE_OCR_WHILE_TRANSLATING] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Pause OCR while translating enabled: {enabled}");
        }

        // Check if cloud OCR color correction is enabled
        public bool IsCloudOcrColorCorrectionEnabled()
        {
            string value = GetValue(ENABLE_CLOUD_OCR_COLOR_CORRECTION, "false");
            return value.ToLower() == "true";
        }

        // Set cloud OCR color correction enabled
        public void SetCloudOcrColorCorrectionEnabled(bool enabled)
        {
            _configValues[ENABLE_CLOUD_OCR_COLOR_CORRECTION] = enabled.ToString().ToLower();
            SaveConfig();
        }
        
        // Get/Set translation context settings
        public int GetMaxContextPieces()
        {
            string value = GetValue(MAX_CONTEXT_PIECES, "3"); // Default: 3 pieces
            if (int.TryParse(value, out int maxContextPieces) && maxContextPieces >= 0)
            {
                return maxContextPieces;
            }
            return 3; // Default: 3 context pieces
        }
        
        public void SetMaxContextPieces(int value)
        {
            if (value >= 0)
            {
                _configValues[MAX_CONTEXT_PIECES] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Max context pieces set to: {value}");
            }
        }
        
        public int GetMinContextSize()
        {
            string value = GetValue(MIN_CONTEXT_SIZE, "20"); // Default: 20 characters
            if (int.TryParse(value, out int minContextSize) && minContextSize >= 0)
            {
                return minContextSize;
            }
            return 20; // Default: 20 characters
        }
        
        public void SetMinContextSize(int value)
        {
            if (value >= 0)
            {
                _configValues[MIN_CONTEXT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Min context size set to: {value}");
            }
        }
        
        public int GetMaxTranslationRetries()
        {
            string value = GetValue(MAX_TRANSLATION_RETRIES, "5");
            if (int.TryParse(value, out int retries) && retries >= 0)
                return retries;
            return 5;
        }

        public void SetMaxTranslationRetries(int value)
        {
            if (value >= 0)
            {
                _configValues[MAX_TRANSLATION_RETRIES] = value.ToString();
                SaveConfig();
            }
        }

        // Get/Set game info
        public string GetGameInfo()
        {
            return GetValue(GAME_INFO, "");
        }
        
        public void SetGameInfo(string gameInfo)
        {
            _configValues[GAME_INFO] = gameInfo;
            SaveConfig();
            Console.WriteLine($"Game info set to: {gameInfo}");
        }
        
        // Get/Set minimum text fragment size
        public int GetMinTextFragmentSize()
        {
            string value = GetValue(MIN_TEXT_FRAGMENT_SIZE, "2"); // Default: 2 characters
            if (int.TryParse(value, out int minSize) && minSize >= 0)
            {
                return minSize;
            }
            return 2; // Default: 2 characters
        }
        
        public void SetMinTextFragmentSize(int value)
        {
            if (value >= 0)
            {
                _configValues[MIN_TEXT_FRAGMENT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum text fragment size set to: {value}");
            }
        }
        
        // Get/Set source language
        public string GetSourceLanguage()
        {
            return GetValue(SOURCE_LANGUAGE, "ja"); // Default to Japanese
        }
        
        public void SetSourceLanguage(string language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _configValues[SOURCE_LANGUAGE] = language;
                SaveConfig();
                Console.WriteLine($"Source language set to: {language}");
            }
        }
        
        // Get/Set target language
        public string GetTargetLanguage()
        {
            return GetValue(TARGET_LANGUAGE, "en"); // Default to English
        }
        
        public void SetTargetLanguage(string language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _configValues[TARGET_LANGUAGE] = language;
                SaveConfig();
                Console.WriteLine($"Target language set to: {language}");
            }
        }
        
        // ChatGPT methods
        
        // Get/Set ChatGPT API key
        public string GetChatGptApiKey()
        {
            return GetValue(CHATGPT_API_KEY, "");
        }
        
        public void SetChatGptApiKey(string apiKey)
        {
            _configValues[CHATGPT_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("ChatGPT API key updated");
        }
        
        // Get/Set ChatGPT model
        public string GetChatGptModel()
        {
            return GetValue(CHATGPT_MODEL, "gpt-5.4-nano"); // Default to GPT-5.4 Nano
        }
        
        public void SetChatGptModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[CHATGPT_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"ChatGPT model set to: {model}");
            }
        }
        
        // Get/Set ChatGPT max completion tokens
        public int GetChatGptMaxCompletionTokens()
        {
            string value = GetValue(CHATGPT_MAX_COMPLETION_TOKENS, "32768");
            if (int.TryParse(value, out int tokens) && tokens > 0)
            {
                return tokens;
            }
            return 32768; // Default: 32768 tokens
        }
        
        public void SetChatGptMaxCompletionTokens(int tokens)
        {
            if (tokens > 0)
            {
                _configValues[CHATGPT_MAX_COMPLETION_TOKENS] = tokens.ToString();
                SaveConfig();
                Console.WriteLine($"ChatGPT max completion tokens set to: {tokens}");
            }
            else
            {
                Console.WriteLine($"Invalid ChatGPT max completion tokens: {tokens}. Must be greater than 0.");
            }
        }
        
        // Get ChatGPT thinking enabled
        public bool GetChatGptThinkingEnabled()
        {
            string value = GetValue(CHATGPT_THINKING_ENABLED, "false");
            return value.ToLower() == "true";
        }
        
        // Set ChatGPT thinking enabled
        public void SetChatGptThinkingEnabled(bool enabled)
        {
            _configValues[CHATGPT_THINKING_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"ChatGPT thinking set to: {enabled}");
        }
        
        // Gemini methods
        
        // Get Gemini model
        public string GetGeminiModel()
        {
            return GetValue(GEMINI_MODEL, "gemini-2.5-flash"); // Default to Gemini 2.5 Flash
        }
        
        // Set Gemini model
        public void SetGeminiModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[GEMINI_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Gemini model set to: {model}");
            }
        }
        
        // Get Gemini thinking enabled
        public bool GetGeminiThinkingEnabled()
        {
            string value = GetValue(GEMINI_THINKING_ENABLED, "false");
            return value.ToLower() == "true";
        }
        
        // Set Gemini thinking enabled
        public void SetGeminiThinkingEnabled(bool enabled)
        {
            _configValues[GEMINI_THINKING_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Gemini thinking set to: {enabled}");
        }

        // Anthropic Claude (direct API) methods

        public string GetAnthropicApiKey()
        {
            return GetValue(ANTHROPIC_API_KEY, "");
        }

        public void SetAnthropicApiKey(string apiKey)
        {
            _configValues[ANTHROPIC_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Anthropic API key updated");
        }

        public string GetAnthropicModel()
        {
            return GetValue(ANTHROPIC_MODEL, "claude-sonnet-4-6");
        }

        public void SetAnthropicModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[ANTHROPIC_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Anthropic model set to: {model}");
            }
        }

        public int GetAnthropicMaxTokens()
        {
            string value = GetValue(ANTHROPIC_MAX_TOKENS, "32768");
            if (int.TryParse(value, out int tokens) && tokens > 0)
            {
                return tokens;
            }
            return 32768;
        }

        public void SetAnthropicMaxTokens(int tokens)
        {
            if (tokens > 0)
            {
                _configValues[ANTHROPIC_MAX_TOKENS] = tokens.ToString();
                SaveConfig();
                Console.WriteLine($"Anthropic max tokens set to: {tokens}");
            }
        }

        // OpenRouter methods

        public string GetOpenRouterApiKey()
        {
            return GetValue(OPENROUTER_API_KEY, "");
        }

        public void SetOpenRouterApiKey(string apiKey)
        {
            _configValues[OPENROUTER_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("OpenRouter API key updated");
        }

        public string GetOpenRouterModel()
        {
            return GetValue(OPENROUTER_MODEL, "openai/gpt-5.4-mini");
        }

        public void SetOpenRouterModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[OPENROUTER_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"OpenRouter model set to: {model}");
            }
        }

        // CLI / subscription provider methods (command path + model per provider)

        public string GetClaudeCliCommand()
        {
            return GetValue(CLAUDE_CLI_COMMAND, "claude");
        }

        public void SetClaudeCliCommand(string command)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                _configValues[CLAUDE_CLI_COMMAND] = command;
                SaveConfig();
            }
        }

        public string GetClaudeCliModel()
        {
            return GetValue(CLAUDE_CLI_MODEL, "sonnet");
        }

        public void SetClaudeCliModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[CLAUDE_CLI_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Claude CLI model set to: {model}");
            }
        }

        public string GetCodexCliCommand()
        {
            return GetValue(CODEX_CLI_COMMAND, "codex");
        }

        public void SetCodexCliCommand(string command)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                _configValues[CODEX_CLI_COMMAND] = command;
                SaveConfig();
            }
        }

        public string GetCodexCliModel()
        {
            return GetValue(CODEX_CLI_MODEL, "gpt-5.4");
        }

        public void SetCodexCliModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[CODEX_CLI_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Codex CLI model set to: {model}");
            }
        }

        public string GetGeminiCliCommand()
        {
            return GetValue(GEMINI_CLI_COMMAND, "gemini");
        }

        public void SetGeminiCliCommand(string command)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                _configValues[GEMINI_CLI_COMMAND] = command;
                SaveConfig();
            }
        }

        public bool GetCliWarmPoolEnabled()
        {
            return GetValue(CLI_WARM_POOL_ENABLED, "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void SetCliWarmPoolEnabled(bool enabled)
        {
            _configValues[CLI_WARM_POOL_ENABLED] = enabled ? "true" : "false";
            SaveConfig();
        }

        public int GetCliWarmPoolSize()
        {
            string v = GetValue(CLI_WARM_POOL_SIZE, "1");
            return int.TryParse(v, out int n) && n >= 1 ? n : 1;
        }

        public void SetCliWarmPoolSize(int size)
        {
            _configValues[CLI_WARM_POOL_SIZE] = Math.Max(1, size).ToString();
            SaveConfig();
        }

        public string GetGeminiCliModel()
        {
            return GetValue(GEMINI_CLI_MODEL, "gemini-2.5-flash");
        }

        public void SetGeminiCliModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _configValues[GEMINI_CLI_MODEL] = model;
                SaveConfig();
                Console.WriteLine($"Gemini CLI model set to: {model}");
            }
        }
    }
}
