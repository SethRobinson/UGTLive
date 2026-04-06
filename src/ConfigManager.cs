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
        private static ConfigManager? _instance;
        private readonly string _configFilePath;
        private readonly string _geminiConfigFilePath;
        private readonly string _ollamaConfigFilePath;
        private readonly string _chatgptConfigFilePath;
        private readonly string _googleTranslateConfigFilePath;
        private readonly string _llamacppConfigFilePath;
        private readonly Dictionary<string, string> _configValues;
        private string _currentTranslationService = "Google Translate"; // Default to Google Translate
        private bool _isNewConfig = false;
        public bool IsNewConfig => _isNewConfig;

        // Supported OCR methods (internal IDs)
        private static readonly IReadOnlyList<string> _supportedOcrMethods = new List<string>
        {
            "EasyOCR",
            "MangaOCR",
            "PaddleOCR",
            "docTR",
            "Windows OCR",
            "Google Vision"
        };

        // Display names for OCR methods (can be changed without breaking code)
        private static readonly Dictionary<string, string> _ocrMethodDisplayNames = new Dictionary<string, string>
        {
            { "EasyOCR", "EasyOCR (Decent at most languages)" },
            { "MangaOCR", "MangaOCR (Vertical Japanese manga)" },
            { "PaddleOCR", "PaddleOCR (Multi-language)" },
            { "docTR", "docTR (Great at non-asian languages)" },
            { "Windows OCR", "Windows OCR (mid at most languages)" },
            { "Google Vision", "Google Cloud Vision (non-local, costs $)" }
        };

        public static IReadOnlyList<string> SupportedOcrMethods => _supportedOcrMethods;

        public static bool IsSupportedOcrMethod(string method)
        {
            return !string.IsNullOrWhiteSpace(method) && _supportedOcrMethods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));
        }

        // Get display name for an OCR method (returns internal ID if display name not found)
        public static string GetOcrMethodDisplayName(string internalId)
        {
            if (_ocrMethodDisplayNames.TryGetValue(internalId, out string? displayName))
            {
                return displayName;
            }
            return internalId; // Fallback to internal ID if display name not found
        }

        // Singleton instance
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigManager();
                }
                return _instance;
            }
        }

        // Constructor
        private ConfigManager()
        {
            _configValues = new Dictionary<string, string>();
            
            // Set config file paths to be in the application's directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = Path.Combine(appDirectory, "config.txt");
            _geminiConfigFilePath = Path.Combine(appDirectory, "gemini_config.txt");
            _ollamaConfigFilePath = Path.Combine(appDirectory, "ollama_config.txt");
            _chatgptConfigFilePath = Path.Combine(appDirectory, "chatgpt_config.txt");
            _googleTranslateConfigFilePath = Path.Combine(appDirectory, "google_translate_config.txt");
            _llamacppConfigFilePath = Path.Combine(appDirectory, "llamacpp_config.txt");
            
            Console.WriteLine($"Config file path: {_configFilePath}");
            Console.WriteLine($"Gemini config file path: {_geminiConfigFilePath}");
            Console.WriteLine($"Ollama config file path: {_ollamaConfigFilePath}");
            Console.WriteLine($"ChatGPT config file path: {_chatgptConfigFilePath}");
            Console.WriteLine($"Google Translate config file path: {_googleTranslateConfigFilePath}");
            Console.WriteLine($"llama.cpp config file path: {_llamacppConfigFilePath}");
            
            // Load main config values
            LoadConfig();
            
            // Migrate: clear stale Page Reading TTS service defaults so they follow the main TTS service
            migratePageReadingTtsDefaults();
            
            // Force "windows visible in screenshots" to false at startup (dangerous option)
            SetWindowsVisibleInScreenshots(false);
            Console.WriteLine("Forced 'windows visible in screenshots' to false at startup (dangerous option disabled)");
            
            // Load translation service from config
            if (_configValues.TryGetValue(TRANSLATION_SERVICE, out string? service))
            {
                // Normalize service name for backwards compatibility
                if (string.Equals(service, "Llama.cpp", StringComparison.OrdinalIgnoreCase))
                {
                    service = "llama.cpp";
                    _configValues[TRANSLATION_SERVICE] = service;
                    SaveConfig(); // Update the config file with the normalized name
                }
                _currentTranslationService = service;
            }
            else
            {
                // Set default and save it
                _currentTranslationService = "Google Translate";
                _configValues[TRANSLATION_SERVICE] = _currentTranslationService;
                SaveConfig();
            }
            
            // Remove the old "llm_prompt_multi" entry if it exists, as it's now stored in separate files
            if (_configValues.ContainsKey("llm_prompt_multi"))
            {
                Console.WriteLine("Removing unused 'llm_prompt_multi' entry from config");
                _configValues.Remove("llm_prompt_multi");
                SaveConfig();
            }
            
            // Create service-specific config files if they don't exist
            EnsureServiceConfigFilesExist();
            
            // Offer to reset prompts if they've been improved since last upgrade
            MigratePromptsIfNeeded();
        }

        // Get a boolean configuration value
        public bool GetBoolValue(string key, bool defaultValue = false)
        {
            string value = GetValue(key, defaultValue.ToString().ToLower());
            return value.ToLower() == "true";
        }
        
        public void SetBoolValue(string key, bool value)
        {
            SetValue(key, value.ToString().ToLower());
        }
        
        public bool GetShowServerWindow()
        {
            return GetBoolValue(SHOW_SERVER_WINDOW, false);
        }
        
        public void SetShowServerWindow(bool showWindow)
        {
            SetBoolValue(SHOW_SERVER_WINDOW, showWindow);
        }
        

        // Load configuration from file
        private void LoadConfig()
        {
            try
            {
                // Create default config if it doesn't exist
                if (!File.Exists(_configFilePath))
                {
                    Console.WriteLine("Configuration file not found. Creating default configuration.");
                    _isNewConfig = true;
                    CreateDefaultConfig();
                }
                else
                {
                    // Read all content from the config file
                    string content = File.ReadAllText(_configFilePath);
                    
                    // First, process multiline values with tags
                    ProcessMultilineValues(content);
                    
                    // Then process regular single-line values
                    ProcessSingleLineValues(content);
                }

                applyDeprecatedModelMigrations();
                migrateLocaleCorruptedValues();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Configuration Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            // Debug output: dump all loaded config values
            Console.WriteLine("=== All Loaded Config Values ===");
            foreach (var entry in _configValues)
            {
                Console.WriteLine($"  {entry.Key} = {(entry.Key.Contains("api_key") ? "***" : entry.Value)}");
            }
            Console.WriteLine("===============================");
        }

        /// <summary>
        /// Remap model IDs that vendors have shut down or deprecated so existing config files keep working.
        /// </summary>
        private void applyDeprecatedModelMigrations()
        {
            bool changed = false;

            if (_configValues.TryGetValue(GEMINI_MODEL, out string? geminiModel) &&
                string.Equals(geminiModel.Trim(), "gemini-3-pro-preview", StringComparison.OrdinalIgnoreCase))
            {
                _configValues[GEMINI_MODEL] = "gemini-3.1-pro-preview";
                changed = true;
                Console.WriteLine("Config: migrated gemini_model gemini-3-pro-preview -> gemini-3.1-pro-preview");
            }

            if (changed)
            {
                SaveConfig();
            }
        }
        
        private void migrateLocaleCorruptedValues()
        {
            bool needsSave = false;
            var numericWithComma = new Regex(@"^-?\d+,\d+$");

            foreach (var key in _configValues.Keys.ToList())
            {
                string value = _configValues[key];

                if (numericWithComma.IsMatch(value))
                {
                    _configValues[key] = value.Replace(',', '.');
                    Console.WriteLine($"Config migration: fixed locale-corrupted value for '{key}': '{value}' -> '{_configValues[key]}'");
                    needsSave = true;
                }

                if (value == "NaN")
                {
                    _configValues.Remove(key);
                    Console.WriteLine($"Config migration: removed NaN value for '{key}'");
                    needsSave = true;
                }
            }

            if (needsSave)
            {
                SaveConfig();
                Console.WriteLine("Config migration: saved repaired config file");
            }
        }

        public bool GetGoogleTranslateUseCloudApi()
        {
            return GetBoolValue(GOOGLE_TRANSLATE_USE_CLOUD_API, false);
        }

        public void SetGoogleTranslateUseCloudApi(bool useCloudApi)
        {
            _configValues[GOOGLE_TRANSLATE_USE_CLOUD_API] = useCloudApi.ToString();
            SaveConfig();
            Console.WriteLine($"Google Translate Cloud API usage set to: {useCloudApi}");
        }

        // Set/Get Google Translate auto language mapping
        public bool GetGoogleTranslateAutoMapLanguages()
        {
            return GetBoolValue(GOOGLE_TRANSLATE_AUTO_MAP_LANGUAGES, true);
        }

        public void SetGoogleTranslateAutoMapLanguages(bool autoMap)
        {
            _configValues[GOOGLE_TRANSLATE_AUTO_MAP_LANGUAGES] = autoMap.ToString();
            SaveConfig();
            Console.WriteLine($"Google Translate auto language mapping set to: {autoMap}");
        }

        // Create default configuration
        private void CreateDefaultConfig()
        {
            // Set default values based on current config.txt
            _configValues[GEMINI_API_KEY] = "<your API key here>";
            _configValues[AUTO_SIZE_TEXT_BLOCKS] = "true";
            _configValues[CHATBOX_FONT_FAMILY] = "Segoe UI";
            _configValues[CHATBOX_FONT_SIZE] = "15";
            _configValues[CHATBOX_FONT_COLOR] = "#FFFFFFFF";
            _configValues[CHATBOX_BACKGROUND_COLOR] = "#FF000000";
            _configValues[CHATBOX_LINES_OF_HISTORY] = "20";
            _configValues[CHATBOX_OPACITY] = "0";
            _configValues[CHATBOX_ORIGINAL_TEXT_COLOR] = "#FFFAFAD2";
            _configValues[CHATBOX_TRANSLATED_TEXT_COLOR] = "#FFFFFFFF";
            _configValues[CHATBOX_BACKGROUND_OPACITY] = "0.35";
            _configValues[CHATBOX_WINDOW_OPACITY] = "1";
            _configValues[CHATBOX_MIN_TEXT_SIZE] = "2";
            _configValues[TRANSLATION_SERVICE] = "Google Translate";
            _configValues[OLLAMA_URL] = "http://localhost";
            _configValues[OLLAMA_PORT] = "11434";
            _configValues[OCR_METHOD] = "EasyOCR";
            _configValues[OLLAMA_MODEL] = "gemma3:12b";
            _configValues[SOURCE_LANGUAGE] = "ja";
            _configValues[TARGET_LANGUAGE] = "en";
            _configValues[ELEVENLABS_API_KEY] = "<your API key here>";
            _configValues[ELEVENLABS_VOICE] = "21m00Tcm4TlvDq8ikWAM";
            _configValues[ELEVENLABS_USE_CUSTOM_VOICE_ID] = "false";
            _configValues[ELEVENLABS_CUSTOM_VOICE_ID] = "";
            _configValues[TTS_SERVICE] = "Google Cloud TTS";
            _configValues[GOOGLE_TTS_API_KEY] = "<your API key here>";
            _configValues[GOOGLE_TTS_VOICE] = "ja-JP-Neural2-B";
            
            // TTS Preload defaults (TTS_SOURCE_SERVICE and TTS_TARGET_SERVICE are intentionally
            // not set here so they fall through to GetTtsService() as the dynamic default)
            _configValues[TTS_SOURCE_VOICE] = "ja-JP-Neural2-B";
            _configValues[TTS_SOURCE_USE_CUSTOM_VOICE_ID] = "false";
            _configValues[TTS_SOURCE_CUSTOM_VOICE_ID] = "";
            _configValues[TTS_TARGET_VOICE] = "en-US-Studio-O";
            _configValues[TTS_TARGET_USE_CUSTOM_VOICE_ID] = "false";
            _configValues[TTS_TARGET_CUSTOM_VOICE_ID] = "";
            _configValues[TTS_PRELOAD_ENABLED] = "false";
            _configValues[TTS_PRELOAD_MODE] = "Source language";
            _configValues[TTS_PLAY_ORDER] = "Top down, left to right";
            _configValues[TTS_AUTO_PLAY_ALL] = "false";
            _configValues[TTS_DELETE_CACHE_ON_STARTUP] = "false";
            _configValues[TTS_MIN_CHARS_FOR_TTS] = "1";
            _configValues[MAX_CONTEXT_PIECES] = "20";
            _configValues[MIN_CONTEXT_SIZE] = "8";
            _configValues[GAME_INFO] = "We're playing an unspecified game.";
            _configValues[MIN_TEXT_FRAGMENT_SIZE] = "1";
            _configValues[CHATGPT_MODEL] = "gpt-5.4-nano";
            _configValues[CHATGPT_API_KEY] = "<your API key here>";
            _configValues[CHATGPT_MAX_COMPLETION_TOKENS] = "32768";
            _configValues[CHATGPT_THINKING_ENABLED] = "false";
            _configValues[GEMINI_MODEL] = "gemini-2.5-flash";
            _configValues[GEMINI_THINKING_ENABLED] = "false";
            _configValues[BLOCK_DETECTION_SCALE] = "3.00";
            _configValues[BLOCK_DETECTION_SETTLE_TIME] = "0.15";
            _configValues[BLOCK_DETECTION_MAX_SETTLE_TIME] = "1.00";
            _configValues[COOLDOWN_HASH_COMPARE_LENGTH] = "15";
            _configValues[KEEP_TRANSLATED_TEXT_UNTIL_REPLACED] = "true";
            _configValues[LEAVE_TRANSLATION_ONSCREEN] = "true";
            _configValues[MIN_LETTER_CONFIDENCE] = "0.1";
            _configValues[MIN_LINE_CONFIDENCE] = "0.1";
            _configValues[AUTO_TRANSLATE_ENABLED] = "true";
            _configValues[IGNORE_PHRASES] = "";
            _configValues[OVERLAY_CLEAR_DELAY_SECONDS] = "0.1";
            _configValues[PAUSE_OCR_WHILE_TRANSLATING] = "true";
            
            // Audio Input Device default
            _configValues[AUDIO_INPUT_DEVICE_INDEX] = "0"; // Default to device index 0
            _configValues[OPENAI_SILENCE_DURATION_MS] = "250"; // Legacy, kept for backward compat
            _configValues[OPENAI_SEMANTIC_VAD_EAGERNESS] = "auto";
            // Ensure audio playback starts disabled by default
            _configValues[OPENAI_AUDIO_PLAYBACK_ENABLED] = "false";
            _configValues[OPENAI_TRANSCRIPTION_MODEL] = "gpt-4o-transcribe";
            _configValues[OPENAI_NOISE_REDUCTION] = "near_field";
            
            // Monitor Window Override Color defaults
            _configValues[MONITOR_OVERRIDE_BG_COLOR_ENABLED] = "false";
            _configValues[MONITOR_OVERRIDE_BG_COLOR] = "#FF000000"; // Black
            _configValues[MONITOR_BG_OPACITY] = "1.0"; // Default opacity 100% (fully opaque)
            _configValues[MONITOR_OVERRIDE_FONT_COLOR_ENABLED] = "false";
            _configValues[MONITOR_OVERRIDE_FONT_COLOR] = "#FFFFFFFF"; // White
            
            // Font Settings defaults
            _configValues[SOURCE_LANGUAGE_FONT_FAMILY] = "MS Gothic";
            _configValues[SOURCE_LANGUAGE_FONT_BOLD] = "true";
            _configValues[TARGET_LANGUAGE_FONT_FAMILY] = "Comic Sans MS";
            _configValues[TARGET_LANGUAGE_FONT_BOLD] = "true";
            
            // Text Area Size Expansion defaults
            _configValues[MONITOR_TEXT_AREA_EXPANSION_WIDTH] = "6";
            _configValues[MONITOR_TEXT_AREA_EXPANSION_HEIGHT] = "2";
            _configValues[MONITOR_TEXT_OVERLAY_BORDER_RADIUS] = "8";
            
            // Manga OCR minimum region size defaults
            _configValues[MANGA_OCR_MIN_REGION_WIDTH] = "10";
            _configValues[MANGA_OCR_MIN_REGION_HEIGHT] = "10";
            _configValues[MANGA_OCR_OVERLAP_ALLOWED_PERCENT] = "90";
            
            // Save the default configuration
            SaveConfig();
            Console.WriteLine("Default configuration created and saved.");
        }
        
        // Process multiline values enclosed in tags
        private void ProcessMultilineValues(string content)
        {
            try
            {
                // Use regex to find content between tags like <key_start>content<key_end>
                string pattern = @"<(\w+)_start>(.*?)<\1_end>";
                
                // Use RegexOptions.Singleline to make '.' match newlines as well
                var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);
                
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string key = match.Groups[1].Value;
                        string value = match.Groups[2].Value;
                        
                        // Trim leading and trailing newlines/whitespace to prevent accumulation
                        // This preserves intentional newlines within the content but removes leading/trailing ones
                        value = value.TrimStart('\r', '\n').TrimEnd('\r', '\n', ' ', '\t');
                        
                        // Store the value
                        _configValues[key] = value;
                        
                        Console.WriteLine($"Loaded multiline config: {key} ({value.Length} chars)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing multiline values: {ex.Message}");
            }
        }
        
        // Process single-line key-value pairs
        private void ProcessSingleLineValues(string content)
        {
            try
            {
                // Remove sections with multiline tags to avoid parsing them as single-line entries
                content = Regex.Replace(content, @"<\w+_start>.*?<\w+_end>", "", RegexOptions.Singleline);
                
                // Split into lines and process each line
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (string line in lines)
                {
                    // Skip comments and empty lines
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;
                    
                    // Skip lines that are part of multiline tags
                    if (line.Contains("_start>") || line.Contains("_end>"))
                        continue;
                    
                    // Parse config entries in format "key|value|"
                    string[] parts = line.Split('|');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        string key = parts[0].Trim();
                        
                        // Special handling for IGNORE_PHRASES
                        if (key == IGNORE_PHRASES)
                        {
                            // For IGNORE_PHRASES, we need to capture the full line after the key
                            string phraseValue = line.Substring(key.Length + 1);
                            // Remove trailing delimiter if present
                            if (phraseValue.EndsWith("|"))
                                phraseValue = phraseValue.Substring(0, phraseValue.Length - 1);
                                
                            _configValues[key] = phraseValue;
                            if (GetLogExtraDebugStuff())
                            {
                                Console.WriteLine($"Loaded ignore phrases config: {key}");
                            }
                            continue;
                        }
                        
                        // Normal key-value pairs
                        string value = parts[1].Trim();
                        
                        // Only add if not already added by multiline processing
                        if (!_configValues.ContainsKey(key))
                        {
                            _configValues[key] = value;
                            Console.WriteLine($"Loaded config: {key}={value}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing single-line values: {ex.Message}");
            }
        }

        // Save configuration to file
        public void SaveConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Configuration file for WPFScreenCapture");
                sb.AppendLine("# Format for single-line values: key|value|");
                sb.AppendLine("# Format for multiline values: <key_start>multiple lines of content<key_end>");
                sb.AppendLine();
                
                // First add single-line entries
                foreach (var entry in _configValues.Where(e => !ShouldBeMultiline(e.Key)))
                {
                    sb.AppendLine($"{entry.Key}|{entry.Value}|");
                }
                
                // Then add multiline entries
                foreach (var entry in _configValues.Where(e => ShouldBeMultiline(e.Key)))
                {
                    sb.AppendLine();
                    sb.AppendLine($"<{entry.Key}_start>");
                    // Append value directly without adding extra newline to prevent accumulation
                    sb.Append(entry.Value);
                    sb.AppendLine();
                    sb.AppendLine($"<{entry.Key}_end>");
                }
                
                // Write to file
                File.WriteAllText(_configFilePath, sb.ToString());
                
                if (GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Saved config to {_configFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Configuration Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Determine if a key should be stored as multiline
        private bool ShouldBeMultiline(string key)
        {
            // List of keys that should use multiline format
            return key.EndsWith("_multi") || key.EndsWith("_template");
        }

        // Get a configuration value
        public string GetValue(string key, string defaultValue = "")
        {
            if (_configValues.TryGetValue(key, out var value))
            {
                return value;
            }
            
            return defaultValue;
        }

        // Set a configuration value
        public void SetValue(string key, string value)
        {
            _configValues[key] = value;
        }

        // Get current translation service
        public string GetCurrentTranslationService()
        {
            return _currentTranslationService;
        }
        
        // Set current translation service
        public void SetTranslationService(string service)
        {
            if (service == "Gemini" || service == "Ollama" || service == "ChatGPT" || service == "llama.cpp" || service == "Google Translate")
            {
                _currentTranslationService = service;
                _configValues[TRANSLATION_SERVICE] = service;
                SaveConfig();
                Console.WriteLine($"Translation service set to {service}");
            }
            else
            {
                Console.WriteLine($"WARNING: Invalid translation service: '{service}'. Valid options are: Gemini, Ollama, ChatGPT, llama.cpp, Google Translate");
            }
        }
        
        // Get current OCR method
        public string GetOcrMethod()
        {
            string ocrMethod = GetValue(OCR_METHOD, "Windows OCR"); // Default to Windows OCR if not set
            
            // Normalize method name if it's one of the supported methods (handles case differences)
            var match = _supportedOcrMethods.FirstOrDefault(m => string.Equals(m, ocrMethod, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
            
            return ocrMethod;
        }
        
        // Set current OCR method
        public void SetOcrMethod(string method)
        {
            if (GetLogExtraDebugStuff())
            {
                Console.WriteLine($"ConfigManager.SetOcrMethod called with method: {method}");
            }
            if (IsSupportedOcrMethod(method))
            {
                var normalized = _supportedOcrMethods.First(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));
                _configValues[OCR_METHOD] = normalized;
                SaveConfig();
                if (GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"OCR method set to {normalized} and saved to config");
                }
            }
            else
            {
                Console.WriteLine($"WARNING: Invalid OCR method: {method}. Supported methods: {string.Join(", ", _supportedOcrMethods)}");
            }
        }
        
        
        // Create service-specific config files if they don't exist
        private void EnsureServiceConfigFilesExist()
        {
            try
            {
                // Default prompts for each service
                string defaultPrompt = GetDefaultPrompt("");
                
                string defaultGeminiPrompt = defaultPrompt;
                string defaultOllamaPrompt = defaultPrompt;
                string defaultChatGptPrompt = defaultPrompt;
                string defaultLlamaCppPrompt = defaultPrompt;
                
                // Check and create Gemini config file
                if (!File.Exists(_geminiConfigFilePath))
                {
                    string geminiContent = $"<llm_prompt_multi_start>\n{defaultGeminiPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_geminiConfigFilePath, geminiContent);
                    Console.WriteLine("Created default Gemini config file");
                }
                
                // Check and create Ollama config file
                if (!File.Exists(_ollamaConfigFilePath))
                {
                    string ollamaContent = $"<llm_prompt_multi_start>\n{defaultOllamaPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_ollamaConfigFilePath, ollamaContent);
                    Console.WriteLine("Created default Ollama config file");
                }
                
                // Check and create ChatGPT config file
                if (!File.Exists(_chatgptConfigFilePath))
                {
                    string chatgptContent = $"<llm_prompt_multi_start>\n{defaultChatGptPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_chatgptConfigFilePath, chatgptContent);
                    Console.WriteLine("Created default ChatGPT config file");
                }
                
                // Check and create llama.cpp config file
                if (!File.Exists(_llamacppConfigFilePath))
                {
                    string llamacppContent = $"<llm_prompt_multi_start>\n{defaultLlamaCppPrompt}\n<llm_prompt_multi_end>";
                    File.WriteAllText(_llamacppConfigFilePath, llamacppContent);
                    Console.WriteLine("Created default llama.cpp config file");
                }
                
                // Google Translate doesn't use prompts, so no need to create config file
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring service config files: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates if a window rectangle is in a valid position on available screens.
        /// Returns true if at least a minimum portion of the window is visible on some screen.
        /// </summary>
        public static bool IsWindowBoundsValid(double left, double top, double width, double height, double minVisiblePixels = 100, double minSize = 50)
        {
            // Check for NaN or invalid sizes
            if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(width) || double.IsNaN(height))
            {
                return false;
            }

            // Check minimum size
            if (width < minSize || height < minSize)
            {
                return false;
            }

            // Get all screens and check if the window is at least partially visible on any
            var screens = System.Windows.Forms.Screen.AllScreens;
            
            // Create a rectangle for the window
            var windowRect = new System.Drawing.Rectangle((int)left, (int)top, (int)width, (int)height);
            
            foreach (var screen in screens)
            {
                // Check if window intersects with this screen
                var intersection = System.Drawing.Rectangle.Intersect(windowRect, screen.WorkingArea);
                
                // If the intersection has a reasonable size, the window is valid
                if (intersection.Width >= minVisiblePixels && intersection.Height >= minVisiblePixels)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
