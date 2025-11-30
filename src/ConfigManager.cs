using System;
using System.Collections.Generic;
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
    public class ConfigManager
    {
        private static ConfigManager? _instance;
        private readonly string _configFilePath;
        private readonly string _geminiConfigFilePath;
        private readonly string _ollamaConfigFilePath;
        private readonly string _chatgptConfigFilePath;
        private readonly string _googleTranslateConfigFilePath;
        private readonly string _llamacppConfigFilePath;
        private readonly Dictionary<string, string> _configValues;
        private string _currentTranslationService = "Gemini"; // Default to Gemini

        // Config keys
        public const string GEMINI_API_KEY = "gemini_api_key";
        public const string GEMINI_MODEL = "gemini_model";
        public const string TRANSLATION_SERVICE = "translation_service";
        public const string OCR_METHOD = "ocr_method";
        public const string OLLAMA_URL = "ollama_url";
        public const string OLLAMA_PORT = "ollama_port";
        public const string OLLAMA_MODEL = "ollama_model";
        public const string LLAMACPP_URL = "llamacpp_url";
        public const string LLAMACPP_PORT = "llamacpp_port";
        public const string CHATGPT_API_KEY = "chatgpt_api_key";
        public const string CHATGPT_MODEL = "chatgpt_model";
        public const string CHATGPT_MAX_COMPLETION_TOKENS = "chatgpt_max_completion_tokens";
        public const string FORCE_CURSOR_VISIBLE = "force_cursor_visible";
        public const string AUTO_SIZE_TEXT_BLOCKS = "auto_size_text_blocks";
        public const string GOOGLE_TRANSLATE_API_KEY = "google_translate_api_key";
        // Google Translate settings
        public const string GOOGLE_TRANSLATE_USE_CLOUD_API = "google_translate_use_cloud_api";
        public const string GOOGLE_TRANSLATE_AUTO_MAP_LANGUAGES = "google_translate_auto_map_languages";
        
        // Google Vision API settings
        public const string GOOGLE_VISION_API_KEY = "google_vision_api_key";
        public const string GOOGLE_VISION_ENDPOINT = "google_vision_endpoint";
        public const string GOOGLE_VISION_HORIZONTAL_GLUE = "google_vision_horizontal_glue";
        public const string GOOGLE_VISION_VERTICAL_GLUE = "google_vision_vertical_glue";
        public const string GOOGLE_VISION_KEEP_LINEFEEDS = "google_vision_keep_linefeeds";
        
        // Per-OCR glue settings (for EasyOCR, MangaOCR, docTR, Windows OCR, Google Vision)
        // Format: horizontal_glue_<ocrmethod>, vertical_glue_<ocrmethod>, keep_linefeeds_<ocrmethod>, leave_translation_onscreen_<ocrmethod>
        public const string HORIZONTAL_GLUE_PREFIX = "horizontal_glue_";
        public const string VERTICAL_GLUE_PREFIX = "vertical_glue_";
        public const string VERTICAL_GLUE_OVERLAP_PREFIX = "vertical_glue_overlap_";
        public const string HEIGHT_SIMILARITY_PREFIX = "height_similarity_";
        public const string KEEP_LINEFEEDS_PREFIX = "keep_linefeeds_";
        public const string LEAVE_TRANSLATION_ONSCREEN_PREFIX = "leave_translation_onscreen_";
        
        // Translation context keys
        public const string MAX_CONTEXT_PIECES = "max_context_pieces";
        public const string MIN_CONTEXT_SIZE = "min_context_size";
        public const string GAME_INFO = "game_info";
        
        // OCR configuration keys
        public const string MIN_TEXT_FRAGMENT_SIZE = "min_text_fragment_size";
        public const string BLOCK_DETECTION_SCALE = "block_detection_scale";
        public const string BLOCK_DETECTION_SETTLE_TIME = "block_detection_settle_time";
        public const string BLOCK_DETECTION_MAX_SETTLE_TIME = "block_detection_max_settle_time";
        public const string KEEP_TRANSLATED_TEXT_UNTIL_REPLACED = "keep_translated_text_until_replaced";
        public const string LEAVE_TRANSLATION_ONSCREEN = "leave_translation_onscreen";
        public const string MIN_LETTER_CONFIDENCE = "min_letter_confidence";
        public const string MIN_LINE_CONFIDENCE = "min_line_confidence";
        
        // Per-OCR Confidence Keys prefix
        public const string MIN_LETTER_CONFIDENCE_PREFIX = "min_letter_confidence_";
        public const string MIN_LINE_CONFIDENCE_PREFIX = "min_line_confidence_";

        public const string AUTO_TRANSLATE_ENABLED = "auto_translate_enabled";
        public const string IGNORE_PHRASES = "ignore_phrases";
        public const string MANGA_OCR_MIN_REGION_WIDTH = "manga_ocr_min_region_width";
        public const string MANGA_OCR_MIN_REGION_HEIGHT = "manga_ocr_min_region_height";
        public const string MANGA_OCR_OVERLAP_ALLOWED_PERCENT = "manga_ocr_overlap_allowed_percent";
        public const string MANGA_OCR_YOLO_CONFIDENCE = "manga_ocr_yolo_confidence";
        public const string PADDLE_OCR_USE_ANGLE_CLS = "paddle_ocr_use_angle_cls";
        // OCR Processing Mode removed - replaced by Universal Block Detector
        public const string OVERLAY_CLEAR_DELAY_SECONDS = "overlay_clear_delay_seconds";
        public const string PAUSE_OCR_WHILE_TRANSLATING = "pause_ocr_while_translating";
        public const string SNAPSHOT_TOGGLE_MODE = "snapshot_toggle_mode";
        public const string ENABLE_CLOUD_OCR_COLOR_CORRECTION = "enable_cloud_ocr_color_correction";
        public const string PERSIST_WINDOW_SIZE = "persist_window_size";
        public const string OCR_WINDOW_LEFT = "ocr_window_left";
        public const string OCR_WINDOW_TOP = "ocr_window_top";
        public const string OCR_WINDOW_WIDTH = "ocr_window_width";
        public const string OCR_WINDOW_HEIGHT = "ocr_window_height";

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
        
        // Text-to-Speech configuration keys
        public const string TTS_ENABLED = "tts_enabled";
        public const string TTS_SERVICE = "tts_service";
        public const string ELEVENLABS_API_KEY = "elevenlabs_api_key";
        public const string ELEVENLABS_VOICE = "elevenlabs_voice";
        public const string ELEVENLABS_USE_CUSTOM_VOICE_ID = "elevenlabs_use_custom_voice_id";
        public const string ELEVENLABS_CUSTOM_VOICE_ID = "elevenlabs_custom_voice_id";
        public const string GOOGLE_TTS_API_KEY = "google_tts_api_key";
        public const string GOOGLE_TTS_VOICE = "google_tts_voice";
        
        // TTS Preload configuration keys
        public const string TTS_SOURCE_SERVICE = "tts_source_service";
        public const string TTS_SOURCE_VOICE = "tts_source_voice";
        public const string TTS_SOURCE_USE_CUSTOM_VOICE_ID = "tts_source_use_custom_voice_id";
        public const string TTS_SOURCE_CUSTOM_VOICE_ID = "tts_source_custom_voice_id";
        public const string TTS_TARGET_SERVICE = "tts_target_service";
        public const string TTS_TARGET_VOICE = "tts_target_voice";
        public const string TTS_TARGET_USE_CUSTOM_VOICE_ID = "tts_target_use_custom_voice_id";
        public const string TTS_TARGET_CUSTOM_VOICE_ID = "tts_target_custom_voice_id";
        public const string TTS_PRELOAD_ENABLED = "tts_preload_enabled";
        public const string TTS_PRELOAD_MODE = "tts_preload_mode";
        public const string TTS_PLAY_ORDER = "tts_play_order";
        public const string TTS_AUTO_PLAY_ALL = "tts_auto_play_all";
        public const string TTS_DELETE_CACHE_ON_STARTUP = "tts_delete_cache_on_startup";
        public const string TTS_VERTICAL_OVERLAP_THRESHOLD = "tts_vertical_overlap_threshold";
        public const string TTS_MAX_CONCURRENT_DOWNLOADS = "tts_max_concurrent_downloads";
        
        // UI Icon Constants
        public const string ICON_SPEAKER_READY = "🔉";
        public const string ICON_SPEAKER_NOT_READY = "◯";
        
        // ChatBox configuration keys
        public const string CHATBOX_FONT_FAMILY = "chatbox_font_family";
        public const string CHATBOX_FONT_SIZE = "chatbox_font_size";
        public const string CHATBOX_FONT_COLOR = "chatbox_font_color";
        public const string CHATBOX_ORIGINAL_TEXT_COLOR = "chatbox_original_text_color";
        public const string CHATBOX_TRANSLATED_TEXT_COLOR = "chatbox_translated_text_color";
        public const string CHATBOX_BACKGROUND_COLOR = "chatbox_background_color";
        public const string CHATBOX_BACKGROUND_OPACITY = "chatbox_background_opacity";
        public const string CHATBOX_WINDOW_OPACITY = "chatbox_window_opacity";
        public const string CHATBOX_LINES_OF_HISTORY = "chatbox_lines_of_history";
        public const string CHATBOX_OPACITY = "chatbox_opacity";
        public const string CHATBOX_MIN_TEXT_SIZE = "chatbox_min_text_size";
        public const string SOURCE_LANGUAGE = "source_language";
        public const string TARGET_LANGUAGE = "target_language";
        public const string AUDIO_PROCESSING_PROVIDER = "audio_processing_provider";
        public const string OPENAI_REALTIME_API_KEY = "openai_realtime_api_key";
        public const string AUDIO_SERVICE_AUTO_TRANSLATE = "audio_service_auto_translate";
        public const string SHOW_SERVER_WINDOW = "show_server_window";

        // Audio Input Device
        public const string AUDIO_INPUT_DEVICE_INDEX = "audio_input_device_index";

        // Whisper specific settings
        public const string WHISPER_SOURCE_LANGUAGE = "whisper_source_language";

        // OpenAI Translation specific settings
        public const string OPENAI_TRANSLATION_ENABLED = "openai_translation_enabled";
        public const string OPENAI_TRANSLATION_TARGET_LANGUAGE = "openai_translation_target_language";
        
        // OpenAI Audio Playback settings
        public const string OPENAI_AUDIO_PLAYBACK_ENABLED = "openai_audio_playback_enabled";
        public const string OPENAI_AUDIO_OUTPUT_DEVICE_INDEX = "openai_audio_output_device_index";
        public const string OPENAI_SPEECH_PROMPT = "openai_speech_prompt";
        public const string OPENAI_VOICE = "openai_voice";
        public const string OPENAI_SILENCE_DURATION_MS = "openai_silence_duration_ms";

        // Monitor Window Override Color Settings
        public const string MONITOR_OVERRIDE_BG_COLOR_ENABLED = "monitor_override_bg_color_enabled";
        public const string MONITOR_OVERRIDE_BG_COLOR = "monitor_override_bg_color";
        public const string MONITOR_BG_OPACITY = "monitor_bg_opacity";
        public const string MONITOR_OVERRIDE_FONT_COLOR_ENABLED = "monitor_override_font_color_enabled";
        public const string MONITOR_OVERRIDE_FONT_COLOR = "monitor_override_font_color";
        public const string MONITOR_TEXT_AREA_EXPANSION_WIDTH = "monitor_text_area_expansion_width";
        public const string MONITOR_TEXT_AREA_EXPANSION_HEIGHT = "monitor_text_area_expansion_height";
        public const string MONITOR_TEXT_OVERLAY_BORDER_RADIUS = "monitor_text_overlay_border_radius";
        public const string MONITOR_OVERLAY_MODE = "monitor_overlay_mode";
        public const string MAIN_WINDOW_OVERLAY_MODE = "main_window_overlay_mode";
        public const string MAIN_WINDOW_MOUSE_PASSTHROUGH = "main_window_mouse_passthrough";
        public const string WINDOWS_VISIBLE_IN_SCREENSHOTS = "windows_visible_in_screenshots";
        
        // docTR-specific glue toggle
        public const string GLUE_DOCTR_LINES = "glue_doctr_lines";

        // Font Settings for Source and Target Languages
        public const string SOURCE_LANGUAGE_FONT_FAMILY = "source_language_font_family";
        public const string SOURCE_LANGUAGE_FONT_BOLD = "source_language_font_bold";
        public const string TARGET_LANGUAGE_FONT_FAMILY = "target_language_font_family";
        public const string TARGET_LANGUAGE_FONT_BOLD = "target_language_font_bold";
        
        // Lesson feature settings
        public const string LESSON_PROMPT_TEMPLATE = "lesson_prompt_template";
        public const string LESSON_URL_TEMPLATE = "lesson_url_template";
        
        // Debug logging settings
        public const string LOG_EXTRA_DEBUG_STUFF = "log_extra_debug_stuff";

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
                _currentTranslationService = "Gemini";
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
            _configValues[TRANSLATION_SERVICE] = "Gemini";
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
            _configValues[TTS_ENABLED] = "false";
            
            // TTS Preload defaults
            _configValues[TTS_SOURCE_SERVICE] = "Google Cloud TTS";
            _configValues[TTS_SOURCE_VOICE] = "ja-JP-Neural2-B";
            _configValues[TTS_SOURCE_USE_CUSTOM_VOICE_ID] = "false";
            _configValues[TTS_SOURCE_CUSTOM_VOICE_ID] = "";
            _configValues[TTS_TARGET_SERVICE] = "Google Cloud TTS";
            _configValues[TTS_TARGET_VOICE] = "en-US-Studio-O";
            _configValues[TTS_TARGET_USE_CUSTOM_VOICE_ID] = "false";
            _configValues[TTS_TARGET_CUSTOM_VOICE_ID] = "";
            _configValues[TTS_PRELOAD_ENABLED] = "false";
            _configValues[TTS_PRELOAD_MODE] = "Source language";
            _configValues[TTS_PLAY_ORDER] = "Top down, left to right";
            _configValues[TTS_AUTO_PLAY_ALL] = "false";
            _configValues[TTS_DELETE_CACHE_ON_STARTUP] = "false";
            _configValues[MAX_CONTEXT_PIECES] = "20";
            _configValues[MIN_CONTEXT_SIZE] = "8";
            _configValues[GAME_INFO] = "We're playing an unspecified game.";
            _configValues[MIN_TEXT_FRAGMENT_SIZE] = "1";
            _configValues[CHATGPT_MODEL] = "gpt-5-nano";
            _configValues[CHATGPT_API_KEY] = "<your API key here>";
            _configValues[CHATGPT_MAX_COMPLETION_TOKENS] = "32768";
            _configValues[GEMINI_MODEL] = "gemini-2.5-flash";
            _configValues[BLOCK_DETECTION_SCALE] = "3.00";
            _configValues[BLOCK_DETECTION_SETTLE_TIME] = "0.15";
            _configValues[BLOCK_DETECTION_MAX_SETTLE_TIME] = "1.00";
            _configValues[KEEP_TRANSLATED_TEXT_UNTIL_REPLACED] = "true";
            _configValues[LEAVE_TRANSLATION_ONSCREEN] = "true";
            _configValues[MIN_LETTER_CONFIDENCE] = "0.1";
            _configValues[MIN_LINE_CONFIDENCE] = "0.1";
            _configValues[AUTO_TRANSLATE_ENABLED] = "false";
            _configValues[IGNORE_PHRASES] = "";
            _configValues[OVERLAY_CLEAR_DELAY_SECONDS] = "0.1";
            _configValues[PAUSE_OCR_WHILE_TRANSLATING] = "true";
            
            // Audio Input Device default
            _configValues[AUDIO_INPUT_DEVICE_INDEX] = "0"; // Default to device index 0
            _configValues[OPENAI_SILENCE_DURATION_MS] = "250"; // Default silence duration
            // Ensure audio playback starts disabled by default
            _configValues[OPENAI_AUDIO_PLAYBACK_ENABLED] = "false";
            
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
            
            return $"{url}:{port}/api/generate";
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
            
            string filePath;
            
            switch (service)
            {
                case "Gemini":
                    filePath = _geminiConfigFilePath;
                    break;
                case "Ollama":
                    filePath = _ollamaConfigFilePath;
                    break;
                case "ChatGPT":
                    filePath = _chatgptConfigFilePath;
                    break;
                case "llama.cpp":
                    filePath = _llamacppConfigFilePath;
                    break;
                default:
                    filePath = _geminiConfigFilePath;
                    break;
            }
            
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
            return @"Your task is to translate the source_language text in the following JSON data to target_language and output a new JSON in a specific format.  This is text from OCR of a screenshot from a video game, so please try to infer the context and which parts are menu or dialog. It might also be a webpage or manga, so just do your best.

CRITICAL OUTPUT FORMAT REQUIREMENTS:

* Output ONLY the resulting JSON data with NO extra text, explanations, markdown code blocks, or formatting.
* The output JSON must have the exact same structure as the input JSON: source_language, target_language, and a text_blocks array.
* Each element in the text_blocks array must include: id, text (TRANSLATED), and rect (the bounding box).
* The ""text"" field in the OUTPUT must contain the TRANSLATED text in the target_language. Do NOT create new fields like ""english_text"", ""japanese_text"", ""translated_text"", etc.
* Keep the same field names as the input - just replace the text content with its translation.
* If ""previous_context"" data exists in the input JSON, use it to better understand context, but do NOT include it in your output.
* Do NOT return the ""previous_context"" or ""game_info"" parameters in your output - those are input-only.
* If the text looks like multiple options for the player to choose from, add a newline after each one so they aren't mushed together.

EXAMPLE:
Input text_block: {""id"": ""text_0"", ""text"": ""Hello"", ""rect"": {...}}
Output text_block: {""id"": ""text_0"", ""text"": ""こんにちは"", ""rect"": {...}}

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
            
            string filePath;
            
            switch (service)
            {
                case "Gemini":
                    filePath = _geminiConfigFilePath;
                    break;
                case "Ollama":
                    filePath = _ollamaConfigFilePath;
                    break;
                case "ChatGPT":
                    filePath = _chatgptConfigFilePath;
                    break;
                case "llama.cpp":
                    filePath = _llamacppConfigFilePath;
                    break;
                default:
                    filePath = _geminiConfigFilePath;
                    break;
            }
            
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
        
        // Get ChatBox font family
        public string GetChatBoxFontFamily()
        {
            return GetValue(CHATBOX_FONT_FAMILY, "Segoe UI");
        }
        
        // Get ChatBox font size
        public double GetChatBoxFontSize()
        {
            string value = GetValue(CHATBOX_FONT_SIZE, "14");
            if (double.TryParse(value, out double fontSize) && fontSize > 0)
            {
                return fontSize;
            }
            return 14; // Default font size
        }
        
        // Get ChatBox font color
        public System.Windows.Media.Color GetChatBoxFontColor()
        {
            string value = GetValue(CHATBOX_FONT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing ChatBox font color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }
        
        // Get ChatBox background color
        public System.Windows.Media.Color GetChatBoxBackgroundColor()
        {
            string value = GetValue(CHATBOX_BACKGROUND_COLOR, "#80000000"); // Default: Semi-transparent black
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing ChatBox background color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Color.FromArgb(128, 0, 0, 0);
        }
        
        // Get ChatBox window opacity (0.1 to 1.0)
        public double GetChatBoxWindowOpacity()
        {
            string value = GetValue(CHATBOX_WINDOW_OPACITY, "1.0"); // Default: 100% opacity
            if (double.TryParse(value, out double opacity))
            {
                // Ensure minimum 10% opacity so window never disappears completely
                return Math.Max(0.1, Math.Min(1.0, opacity));
            }
            return 1.0; // Default opacity
        }
        
        // Get ChatBox background opacity (0.0 to 1.0)
        public double GetChatBoxBackgroundOpacity()
        {
            string value = GetValue(CHATBOX_BACKGROUND_OPACITY, "0.5"); // Default: 50% opacity
            if (double.TryParse(value, out double opacity) && opacity >= 0 && opacity <= 1)
            {
                return opacity;
            }
            return 0.5; // Default opacity
        }
        
        // Get Original Text color
        public System.Windows.Media.Color GetOriginalTextColor()
        {
            string value = GetValue(CHATBOX_ORIGINAL_TEXT_COLOR, "#FFF8E0A0"); // Default: Light gold
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Original Text color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.LightGoldenrodYellow;
        }
        
        // Get Translated Text color
        public System.Windows.Media.Color GetTranslatedTextColor()
        {
            string value = GetValue(CHATBOX_TRANSLATED_TEXT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Translated Text color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }
        
        // Get ChatBox history size
        public int GetChatBoxHistorySize()
        {
            string value = GetValue(CHATBOX_LINES_OF_HISTORY, "20"); // Default: 20 entries
            if (int.TryParse(value, out int historySize) && historySize > 0)
            {
                return historySize;
            }
            return 20; // Default history size
        }
        
        // Get min ChatBox text size
        public int GetChatBoxMinTextSize()
        {
            string value = GetValue(CHATBOX_MIN_TEXT_SIZE, "2"); // Default: 2 characters
            if (int.TryParse(value, out int minSize) && minSize >= 0)
            {
                return minSize;
            }
            return 2; // Default min size
        }
        
        // Set min ChatBox text size
        public void SetChatBoxMinTextSize(int value)
        {
            if (value >= 0)
            {
                _configValues[CHATBOX_MIN_TEXT_SIZE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Min ChatBox text size set to: {value}");
            }
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
        
        // Get/Set minimum letter confidence (Global - Legacy/Default)
        public double GetMinLetterConfidence()
        {
            string value = GetValue(MIN_LETTER_CONFIDENCE, "0.1"); // Default: 0.1 (10%)
            if (double.TryParse(value, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            return 0.1; // Default: 0.1 (10%)
        }
        
        // Get minimum letter confidence for specific provider
        public double GetMinLetterConfidence(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return GetMinLetterConfidence();

            // Clean provider name for key (e.g. "EasyOCR" -> "easyocr", "Windows OCR" -> "windows_ocr")
            string keySuffix = provider.ToLower().Replace(" ", "_");
            string key = MIN_LETTER_CONFIDENCE_PREFIX + keySuffix;
            
            // Determine default value based on provider
            string defaultValue;
            if (keySuffix == "google_vision")
            {
                defaultValue = "0.7";
            }
            else
            {
                // For other providers, default value depends on legacy global setting
                defaultValue = GetValue(MIN_LETTER_CONFIDENCE, "0.1");
            }
            
            string value = GetValue(key, defaultValue);
            
            if (double.TryParse(value, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            
            if (keySuffix == "google_vision") return 0.7;
            return 0.1;
        }

        public void SetMinLetterConfidence(double value)
        {
            if (value >= 0 && value <= 1)
            {
                _configValues[MIN_LETTER_CONFIDENCE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum letter confidence set to: {value}");
            }
            else
            {
                Console.WriteLine($"Invalid minimum letter confidence: {value}. Must be between 0 and 1.");
            }
        }
        
        public void SetMinLetterConfidence(string provider, double value)
        {
            if (string.IsNullOrEmpty(provider)) return;
            
            if (value >= 0 && value <= 1)
            {
                string keySuffix = provider.ToLower().Replace(" ", "_");
                string key = MIN_LETTER_CONFIDENCE_PREFIX + keySuffix;
                
                _configValues[key] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum letter confidence for {provider} set to: {value}");
            }
        }
        
        // Get/Set minimum line confidence (Global - Legacy/Default)
        public double GetMinLineConfidence()
        {
            string value = GetValue(MIN_LINE_CONFIDENCE, "0.2"); // Default: 0.2 (20%)
            if (double.TryParse(value, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            return 0.2; // Default: 0.2 (20%)
        }

        // Get minimum line confidence for specific provider
        public double GetMinLineConfidence(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return GetMinLineConfidence();

            string keySuffix = provider.ToLower().Replace(" ", "_");
            string key = MIN_LINE_CONFIDENCE_PREFIX + keySuffix;
            
            // Determine default value based on provider
            string defaultValue;
            if (keySuffix == "google_vision")
            {
                defaultValue = "0.7";
            }
            else
            {
                // Default to global setting for others
                defaultValue = GetValue(MIN_LINE_CONFIDENCE, "0.2");
            }
            
            string value = GetValue(key, defaultValue);
            
            if (double.TryParse(value, out double minConfidence) && minConfidence >= 0 && minConfidence <= 1)
            {
                return minConfidence;
            }
            
            if (keySuffix == "google_vision") return 0.7;
            return 0.2;
        }
        
        public void SetMinLineConfidence(double value)
        {
            if (value >= 0 && value <= 1)
            {
                _configValues[MIN_LINE_CONFIDENCE] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum line confidence set to: {value}");
            }
            else
            {
                Console.WriteLine($"Invalid minimum line confidence: {value}. Must be between 0 and 1.");
            }
        }

        public void SetMinLineConfidence(string provider, double value)
        {
            if (string.IsNullOrEmpty(provider)) return;

            if (value >= 0 && value <= 1)
            {
                string keySuffix = provider.ToLower().Replace(" ", "_");
                string key = MIN_LINE_CONFIDENCE_PREFIX + keySuffix;

                _configValues[key] = value.ToString();
                SaveConfig();
                Console.WriteLine($"Minimum line confidence for {provider} set to: {value}");
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
        
        // Text-to-Speech methods
        
        // Get/Set TTS enabled
        public bool IsTtsEnabled()
        {
            string value = GetValue(TTS_ENABLED, "false");
            return value.ToLower() == "true";
        }
        
        public void SetTtsEnabled(bool enabled)
        {
            _configValues[TTS_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS enabled: {enabled}");
        }
        
        // Get/Set TTS service
        public string GetTtsService()
        {
            return GetValue(TTS_SERVICE, "ElevenLabs"); // Default to ElevenLabs
        }
        
        public void SetTtsService(string service)
        {
            if (!string.IsNullOrWhiteSpace(service))
            {
                _configValues[TTS_SERVICE] = service;
                SaveConfig();
                Console.WriteLine($"TTS service set to: {service}");
            }
        }
        
        // Get/Set ElevenLabs API key
        public string GetElevenLabsApiKey()
        {
            return GetValue(ELEVENLABS_API_KEY, "");
        }
        
        public void SetElevenLabsApiKey(string apiKey)
        {
            _configValues[ELEVENLABS_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("ElevenLabs API key updated");
        }
        
        // Get/Set ElevenLabs voice
        public string GetElevenLabsVoice()
        {
            return GetValue(ELEVENLABS_VOICE, "21m00Tcm4TlvDq8ikWAM"); // Default to Rachel
        }
        
        public void SetElevenLabsVoice(string voiceId)
        {
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                _configValues[ELEVENLABS_VOICE] = voiceId;
                SaveConfig();
                Console.WriteLine($"ElevenLabs voice set to: {voiceId}");
            }
        }

        // Get/Set ElevenLabs custom voice toggle
        public bool GetElevenLabsUseCustomVoiceId()
        {
            return GetBoolValue(ELEVENLABS_USE_CUSTOM_VOICE_ID, false);
        }

        public void SetElevenLabsUseCustomVoiceId(bool useCustom)
        {
            _configValues[ELEVENLABS_USE_CUSTOM_VOICE_ID] = useCustom.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"ElevenLabs custom voice ID enabled: {useCustom}");
        }

        // Get/Set ElevenLabs custom voice ID
        public string GetElevenLabsCustomVoiceId()
        {
            return GetValue(ELEVENLABS_CUSTOM_VOICE_ID, "");
        }

        public void SetElevenLabsCustomVoiceId(string voiceId)
        {
            _configValues[ELEVENLABS_CUSTOM_VOICE_ID] = voiceId ?? "";
            SaveConfig();
            Console.WriteLine("ElevenLabs custom voice ID updated");
        }
        
        // Google TTS methods
        
        // Get/Set Google TTS API key
        public string GetGoogleTtsApiKey()
        {
            return GetValue(GOOGLE_TTS_API_KEY, "");
        }
        
        public void SetGoogleTtsApiKey(string apiKey)
        {
            _configValues[GOOGLE_TTS_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google TTS API key updated");
        }
        
        // Get/Set Google TTS voice
        public string GetGoogleTtsVoice()
        {
            return GetValue(GOOGLE_TTS_VOICE, "ja-JP-Neural2-B"); // Default to Female - Neural2
        }
        
        public void SetGoogleTtsVoice(string voiceId)
        {
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                _configValues[GOOGLE_TTS_VOICE] = voiceId;
                SaveConfig();
                Console.WriteLine($"Google TTS voice set to: {voiceId}");
            }
        }
        
        // TTS Preload methods
        
        // Get/Set TTS Source Service
        public string GetTtsSourceService()
        {
            return GetValue(TTS_SOURCE_SERVICE, GetTtsService()); // Default to main TTS service
        }
        
        public void SetTtsSourceService(string service)
        {
            if (!string.IsNullOrWhiteSpace(service))
            {
                _configValues[TTS_SOURCE_SERVICE] = service;
                SaveConfig();
                Console.WriteLine($"TTS source service set to: {service}");
            }
        }
        
        // Get/Set TTS Source Voice
        public string GetTtsSourceVoice()
        {
            return GetValue(TTS_SOURCE_VOICE, GetGoogleTtsVoice()); // Default to Google TTS voice
        }
        
        public void SetTtsSourceVoice(string voiceId)
        {
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                _configValues[TTS_SOURCE_VOICE] = voiceId;
                SaveConfig();
                Console.WriteLine($"TTS source voice set to: {voiceId}");
            }
        }
        
        // Get/Set TTS Source Use Custom Voice ID
        public bool GetTtsSourceUseCustomVoiceId()
        {
            return GetBoolValue(TTS_SOURCE_USE_CUSTOM_VOICE_ID, false);
        }
        
        public void SetTtsSourceUseCustomVoiceId(bool useCustom)
        {
            _configValues[TTS_SOURCE_USE_CUSTOM_VOICE_ID] = useCustom.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS source use custom voice ID: {useCustom}");
        }
        
        // Get/Set TTS Source Custom Voice ID
        public string GetTtsSourceCustomVoiceId()
        {
            return GetValue(TTS_SOURCE_CUSTOM_VOICE_ID, "");
        }
        
        public void SetTtsSourceCustomVoiceId(string voiceId)
        {
            _configValues[TTS_SOURCE_CUSTOM_VOICE_ID] = voiceId ?? "";
            SaveConfig();
            Console.WriteLine("TTS source custom voice ID updated");
        }
        
        // Get/Set TTS Target Service
        public string GetTtsTargetService()
        {
            return GetValue(TTS_TARGET_SERVICE, GetTtsService()); // Default to main TTS service
        }
        
        public void SetTtsTargetService(string service)
        {
            if (!string.IsNullOrWhiteSpace(service))
            {
                _configValues[TTS_TARGET_SERVICE] = service;
                SaveConfig();
                Console.WriteLine($"TTS target service set to: {service}");
            }
        }
        
        // Get/Set TTS Target Voice
        public string GetTtsTargetVoice()
        {
            return GetValue(TTS_TARGET_VOICE, "en-US-Studio-O"); // Default to English Studio voice
        }
        
        public void SetTtsTargetVoice(string voiceId)
        {
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                _configValues[TTS_TARGET_VOICE] = voiceId;
                SaveConfig();
                Console.WriteLine($"TTS target voice set to: {voiceId}");
            }
        }
        
        // Get/Set TTS Target Use Custom Voice ID
        public bool GetTtsTargetUseCustomVoiceId()
        {
            return GetBoolValue(TTS_TARGET_USE_CUSTOM_VOICE_ID, false);
        }
        
        public void SetTtsTargetUseCustomVoiceId(bool useCustom)
        {
            _configValues[TTS_TARGET_USE_CUSTOM_VOICE_ID] = useCustom.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS target use custom voice ID: {useCustom}");
        }
        
        // Get/Set TTS Target Custom Voice ID
        public string GetTtsTargetCustomVoiceId()
        {
            return GetValue(TTS_TARGET_CUSTOM_VOICE_ID, "");
        }
        
        public void SetTtsTargetCustomVoiceId(string voiceId)
        {
            _configValues[TTS_TARGET_CUSTOM_VOICE_ID] = voiceId ?? "";
            SaveConfig();
            Console.WriteLine("TTS target custom voice ID updated");
        }
        
        // Get/Set TTS Preload Enabled
        public bool IsTtsPreloadEnabled()
        {
            return GetBoolValue(TTS_PRELOAD_ENABLED, false);
        }
        
        public void SetTtsPreloadEnabled(bool enabled)
        {
            _configValues[TTS_PRELOAD_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS preload enabled: {enabled}");
        }
        
        // Get/Set TTS Preload Mode
        public string GetTtsPreloadMode()
        {
            return GetValue(TTS_PRELOAD_MODE, "Source language");
        }
        
        public void SetTtsPreloadMode(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _configValues[TTS_PRELOAD_MODE] = mode;
                SaveConfig();
                Console.WriteLine($"TTS preload mode set to: {mode}");
            }
        }
        
        // Get/Set TTS Play Order
        public string GetTtsPlayOrder()
        {
            return GetValue(TTS_PLAY_ORDER, "Top down, left to right");
        }
        
        public void SetTtsPlayOrder(string order)
        {
            if (!string.IsNullOrWhiteSpace(order))
            {
                _configValues[TTS_PLAY_ORDER] = order;
                SaveConfig();
                Console.WriteLine($"TTS play order set to: {order}");
            }
        }
        
        // Get/Set TTS Auto Play All
        public bool IsTtsAutoPlayAllEnabled()
        {
            return GetBoolValue(TTS_AUTO_PLAY_ALL, false);
        }
        
        public void SetTtsAutoPlayAllEnabled(bool enabled)
        {
            _configValues[TTS_AUTO_PLAY_ALL] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS auto play all enabled: {enabled}");
        }
        
        // Get/Set TTS Delete Cache On Startup
        public bool GetTtsDeleteCacheOnStartup()
        {
            return GetBoolValue(TTS_DELETE_CACHE_ON_STARTUP, false);
        }
        
        public void SetTtsDeleteCacheOnStartup(bool enabled)
        {
            _configValues[TTS_DELETE_CACHE_ON_STARTUP] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS delete cache on startup: {enabled}");
        }
        
        // Get/Set TTS Vertical Overlap Threshold (in pixels)
        public double GetTtsVerticalOverlapThreshold()
        {
            string value = GetValue(TTS_VERTICAL_OVERLAP_THRESHOLD, "120");
            if (double.TryParse(value, out double threshold))
            {
                return threshold;
            }
            return 120.0; // Default to 120 pixels
        }
        
        public void SetTtsVerticalOverlapThreshold(double threshold)
        {
            _configValues[TTS_VERTICAL_OVERLAP_THRESHOLD] = threshold.ToString();
            SaveConfig();
            Console.WriteLine($"TTS vertical overlap threshold set to: {threshold} pixels");
        }
        
        // Get/Set TTS Max Concurrent Downloads
        public int GetTtsMaxConcurrentDownloads()
        {
            string value = GetValue(TTS_MAX_CONCURRENT_DOWNLOADS, "2");
            if (int.TryParse(value, out int maxConcurrent) && maxConcurrent >= 0)
            {
                return maxConcurrent;
            }
            return 2; // Default to 2 concurrent downloads
        }
        
        public void SetTtsMaxConcurrentDownloads(int maxConcurrent)
        {
            if (maxConcurrent < 0)
            {
                maxConcurrent = 0; // Minimum of 0 (unlimited)
            }
            _configValues[TTS_MAX_CONCURRENT_DOWNLOADS] = maxConcurrent.ToString();
            SaveConfig();
            Console.WriteLine($"TTS max concurrent downloads set to: {maxConcurrent}{(maxConcurrent == 0 ? " (unlimited)" : "")}");
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
            return GetValue(CHATGPT_MODEL, "gpt-3.5-turbo"); // Default to gpt-3.5-turbo
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
        
        // Gemini methods
        
        // Get Gemini model
        public string GetGeminiModel()
        {
            return GetValue(GEMINI_MODEL, "gemini-2.5-flash"); // Default to 2.0 Flash
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
        
        // OCR Display methods
        
        // Check if translation should stay onscreen until replaced
        public bool IsLeaveTranslationOnscreenEnabled()
        {
            string value = GetValue(LEAVE_TRANSLATION_ONSCREEN, "false");
            return value.ToLower() == "true";
        }
        
        // Set whether translation should stay onscreen until replaced
        public void SetLeaveTranslationOnscreenEnabled(bool enabled)
        {
            _configValues[LEAVE_TRANSLATION_ONSCREEN] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Leave translation onscreen enabled: {enabled}");
        }
        
        // Check if translated text should be kept until replaced
        public bool IsKeepTranslatedTextUntilReplacedEnabled()
        {
            string value = GetValue(KEEP_TRANSLATED_TEXT_UNTIL_REPLACED, "true");
            return value.ToLower() == "true";
        }
        
        // Set whether translated text should be kept until replaced
        public void SetKeepTranslatedTextUntilReplacedEnabled(bool enabled)
        {
            _configValues[KEEP_TRANSLATED_TEXT_UNTIL_REPLACED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Keep translated text until replaced enabled: {enabled}");
        }
        
        // Block Detection methods
        
        // Get Block Detection Scale
        public double GetBlockDetectionScale()
        {
            string value = GetValue(BLOCK_DETECTION_SCALE, "5.0");
            if (double.TryParse(value, out double scale) && scale > 0)
            {
                return scale;
            }
            return 5.0; // Default
        }
        
        // Set Block Detection Scale
        public void SetBlockDetectionScale(double scale)
        {
            if (scale > 0)
            {
                _configValues[BLOCK_DETECTION_SCALE] = scale.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Block detection scale set to: {scale:F2}");
            }
        }
        
        // Get Block Detection Settle Time
        public double GetBlockDetectionSettleTime()
        {
            string value = GetValue(BLOCK_DETECTION_SETTLE_TIME, "0.15");
            if (double.TryParse(value, out double time) && time >= 0)
            {
                return time;
            }
            return 0.15; // Default
        }
        
        // Set Block Detection Settle Time
        public void SetBlockDetectionSettleTime(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[BLOCK_DETECTION_SETTLE_TIME] = seconds.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Block detection settle time set to: {seconds:F2} seconds");
            }
        }

        // Get Block Detection Max Settle Time
        public double GetBlockDetectionMaxSettleTime()
        {
            string value = GetValue(BLOCK_DETECTION_MAX_SETTLE_TIME, "1.00");
            if (double.TryParse(value, out double time) && time >= 0)
            {
                return time;
            }
            return 1.0; // Default
        }

        // Set Block Detection Max Settle Time
        public void SetBlockDetectionMaxSettleTime(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[BLOCK_DETECTION_MAX_SETTLE_TIME] = seconds.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Block detection max settle time set to: {seconds:F2} seconds");
            }
        }
        
        // Get Overlay Clear Delay
        public double GetOverlayClearDelaySeconds()
        {
            string value = GetValue(OVERLAY_CLEAR_DELAY_SECONDS, "0.3");
            if (double.TryParse(value, out double delay) && delay >= 0)
            {
                return delay;
            }
            return 0.3; // Default
        }
        
        // Set Overlay Clear Delay
        public void SetOverlayClearDelaySeconds(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[OVERLAY_CLEAR_DELAY_SECONDS] = seconds.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Overlay clear delay set to: {seconds:F2} seconds");
            }
        }
        
        // Get Snapshot Toggle Mode (if true, pressing snapshot while overlay is displayed clears it)
        public bool GetSnapshotToggleMode()
        {
            string value = GetValue(SNAPSHOT_TOGGLE_MODE, "true");
            return value.ToLower() == "true";
        }
        
        // Set Snapshot Toggle Mode
        public void SetSnapshotToggleMode(bool enabled)
        {
            _configValues[SNAPSHOT_TOGGLE_MODE] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Snapshot toggle mode: {enabled}");
        }
        
        // Get/Set: Glue docTR lines into paragraphs
        // Get/Set: Manga OCR minimum region width
        public int GetMangaOcrMinRegionWidth()
        {
            string value = GetValue(MANGA_OCR_MIN_REGION_WIDTH, "10");
            if (int.TryParse(value, out int width) && width >= 0)
            {
                return width;
            }
            return 10; // Default: 10 pixels
        }

        public void SetMangaOcrMinRegionWidth(int width)
        {
            if (width >= 0)
            {
                _configValues[MANGA_OCR_MIN_REGION_WIDTH] = width.ToString();
                SaveConfig();
                Console.WriteLine($"Manga OCR minimum region width set to: {width}");
            }
            else
            {
                Console.WriteLine($"Invalid Manga OCR minimum region width: {width}. Must be non-negative.");
            }
        }

        // Get/Set: Manga OCR minimum region height
        public int GetMangaOcrMinRegionHeight()
        {
            string value = GetValue(MANGA_OCR_MIN_REGION_HEIGHT, "10");
            if (int.TryParse(value, out int height) && height >= 0)
            {
                return height;
            }
            return 10; // Default: 10 pixels
        }

        public void SetMangaOcrMinRegionHeight(int height)
        {
            if (height >= 0)
            {
                _configValues[MANGA_OCR_MIN_REGION_HEIGHT] = height.ToString();
                SaveConfig();
                Console.WriteLine($"Manga OCR minimum region height set to: {height}");
            }
            else
            {
                Console.WriteLine($"Invalid Manga OCR minimum region height: {height}. Must be non-negative.");
            }
        }

        // Get/Set: Manga OCR overlap allowed percentage
        public double GetMangaOcrOverlapAllowedPercent()
        {
            string value = GetValue(MANGA_OCR_OVERLAP_ALLOWED_PERCENT, "90");
            if (double.TryParse(value, out double percent) && percent >= 0 && percent <= 100)
            {
                return percent;
            }
            return 90.0; // Default: 90%
        }

        public void SetMangaOcrOverlapAllowedPercent(double percent)
        {
            if (percent >= 0 && percent <= 100)
            {
                _configValues[MANGA_OCR_OVERLAP_ALLOWED_PERCENT] = percent.ToString("F1");
                SaveConfig();
                Console.WriteLine($"Manga OCR overlap allowed percent set to: {percent:F1}%");
            }
            else
            {
                Console.WriteLine($"Invalid Manga OCR overlap allowed percent: {percent}. Must be between 0 and 100.");
            }
        }

        // Get/Set: Manga OCR YOLO confidence threshold
        public double GetMangaOcrYoloConfidence()
        {
            string value = GetValue(MANGA_OCR_YOLO_CONFIDENCE, "0.60");
            if (double.TryParse(value, out double confidence) && confidence >= 0.0 && confidence <= 1.0)
            {
                return confidence;
            }
            return 0.60; // Default: 0.60 (raised from 0.25 to reduce false positives like tree bark)
        }

        public void SetMangaOcrYoloConfidence(double confidence)
        {
            if (confidence >= 0.0 && confidence <= 1.0)
            {
                _configValues[MANGA_OCR_YOLO_CONFIDENCE] = confidence.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Manga OCR YOLO confidence threshold set to: {confidence:F2}");
            }
            else
            {
                Console.WriteLine($"Invalid YOLO confidence threshold: {confidence}. Must be between 0.0 and 1.0.");
            }
        }

        public bool GetPaddleOcrUseAngleCls()
        {
            string value = GetValue(PADDLE_OCR_USE_ANGLE_CLS, "false");
            return bool.TryParse(value, out bool result) && result;
        }

        public void SetPaddleOcrUseAngleCls(bool enabled)
        {
            _configValues[PADDLE_OCR_USE_ANGLE_CLS] = enabled.ToString();
            SaveConfig();
        }

        // Get/Set: OCR processing mode (Deprecated/Removed)
        // Logic now handled automatically by UniversalBlockDetector
        
        // Get all ignore phrases as a list of tuples (phrase, exactMatch)

        //OPTIMIZE:  Why is the AI doing all this work over and over?  Should be caching the results
        public List<(string Phrase, bool ExactMatch)> GetIgnorePhrases()
        {
            List<(string, bool)> result = new List<(string, bool)>();
            string value = GetValue(IGNORE_PHRASES, "");
            
            if (!string.IsNullOrEmpty(value))
            {
                // Fix the format if it contains the key prefix (from old format)
                if (value.StartsWith(IGNORE_PHRASES + "|"))
                {
                    value = value.Substring((IGNORE_PHRASES + "|").Length);
                }
                
                // Format should be: phrase1|True|phrase2|False
                string[] parts = value.Split('|');
                
                // Process in pairs
                for (int i = 0; i < parts.Length - 1; i += 2)
                {
                    if (i + 1 < parts.Length)
                    {
                        string phrase = parts[i];
                        bool exactMatch = bool.TryParse(parts[i + 1], out bool match) && match;
                        
                        if (!string.IsNullOrEmpty(phrase))
                        {
                            result.Add((phrase, exactMatch));
                            //Console.WriteLine($"Loaded ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                        }
                    }
                }
            }
            
            return result;
        }
        
        // Save all ignore phrases
        public void SaveIgnorePhrases(List<(string Phrase, bool ExactMatch)> phrases)
        {
            StringBuilder sb = new StringBuilder();
            
            foreach (var (phrase, exactMatch) in phrases)
            {
                if (!string.IsNullOrEmpty(phrase))
                {
                    sb.Append(phrase);
                    sb.Append('|');
                    sb.Append(exactMatch.ToString());
                    sb.Append('|');
                }
            }
            
            _configValues[IGNORE_PHRASES] = sb.ToString();
            SaveConfig();
            Console.WriteLine($"Saved {phrases.Count} ignore phrases: {sb.ToString()}");
        }
        
        // Add a single ignore phrase
        public void AddIgnorePhrase(string phrase, bool exactMatch)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            
            // Check if the phrase already exists
            if (!phrases.Any(p => p.Phrase == phrase))
            {
                phrases.Add((phrase, exactMatch));
                SaveIgnorePhrases(phrases);
                Console.WriteLine($"Added ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
            }
        }
        
        // Remove a single ignore phrase
        public void RemoveIgnorePhrase(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            var originalCount = phrases.Count;
            
            phrases.RemoveAll(p => p.Phrase == phrase);
            
            if (phrases.Count < originalCount)
            {
                SaveIgnorePhrases(phrases);
                Console.WriteLine($"Removed ignore phrase: '{phrase}'");
            }
        }
        
        // Update exact match setting for a phrase
        public void UpdateIgnorePhraseExactMatch(string phrase, bool exactMatch)
        {
            if (string.IsNullOrEmpty(phrase))
                return;
                
            var phrases = GetIgnorePhrases();
            
            for (int i = 0; i < phrases.Count; i++)
            {
                if (phrases[i].Phrase == phrase)
                {
                    phrases[i] = (phrase, exactMatch);
                    SaveIgnorePhrases(phrases);
                    Console.WriteLine($"Updated ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                    break;
                }
            }
        }
        public string GetGoogleTranslateApiKey()
        {
            return GetValue(GOOGLE_TRANSLATE_API_KEY, "");
        }

        public void SetGoogleTranslateApiKey(string apiKey)
        {
            _configValues[GOOGLE_TRANSLATE_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google Translate API key updated");
        }

        public string GetGoogleVisionApiKey()
        {
            return GetValue(GOOGLE_VISION_API_KEY, "");
        }

        public void SetGoogleVisionApiKey(string apiKey)
        {
            _configValues[GOOGLE_VISION_API_KEY] = apiKey;
            SaveConfig();
            Console.WriteLine("Google Vision API key updated");
        }

        public double GetGoogleVisionHorizontalGlue()
        {
            string value = GetValue(GOOGLE_VISION_HORIZONTAL_GLUE, "1.5");
            if (double.TryParse(value, out double result))
            {
                return result;
            }
            return 1.5; // Default: 1.5 character widths
        }

        public void SetGoogleVisionHorizontalGlue(double value)
        {
            _configValues[GOOGLE_VISION_HORIZONTAL_GLUE] = value.ToString();
            SaveConfig();
            Console.WriteLine($"Google Vision horizontal glue updated to {value}");
        }

        public double GetGoogleVisionVerticalGlue()
        {
            string value = GetValue(GOOGLE_VISION_VERTICAL_GLUE, "0.5");
            if (double.TryParse(value, out double result))
            {
                return result;
            }
            return 0.5; // Default: 0.5 character heights
        }

        public void SetGoogleVisionVerticalGlue(double value)
        {
            _configValues[GOOGLE_VISION_VERTICAL_GLUE] = value.ToString();
            SaveConfig();
            Console.WriteLine($"Google Vision vertical glue updated to {value}");
        }

        public bool GetGoogleVisionKeepLinefeeds()
        {
            return GetBoolValue(GOOGLE_VISION_KEEP_LINEFEEDS, true); // Default to true
        }

        public void SetGoogleVisionKeepLinefeeds(bool value)
        {
            SetBoolValue(GOOGLE_VISION_KEEP_LINEFEEDS, value);
            SaveConfig();
            Console.WriteLine($"Google Vision keep linefeeds set to: {value}");
        }

        // Per-OCR settings methods
        // These allow storing horizontal glue, vertical glue, keep linefeeds, and leave translation onscreen settings per OCR method
        
        // Helper method to normalize OCR method names for config keys
        private string NormalizeOcrMethodName(string ocrMethod)
        {
            return ocrMethod.Replace(" ", "_").ToLower();
        }
        
        // Horizontal Glue (per-OCR)
        public double GetHorizontalGlue(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HORIZONTAL_GLUE_PREFIX + normalizedMethod;
            string value = GetValue(key, "1.0"); // Default: 2.0 character widths
            if (double.TryParse(value, out double result))
            {
                return result;
            }
            return 1.0;
        }
        
        public void SetHorizontalGlue(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HORIZONTAL_GLUE_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} horizontal glue updated to {value}");
        }
        
        // Vertical Glue (per-OCR)
        public double GetVerticalGlue(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_PREFIX + normalizedMethod;
            string value = GetValue(key, "1.0"); // Default: 2.0 line heights
            if (double.TryParse(value, out double result))
            {
                return result;
            }
            return 1.0;
        }
        
        public void SetVerticalGlue(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} vertical glue updated to {value}");
        }
        
        // Vertical Glue Overlap (per-OCR)
        public double GetVerticalGlueOverlap(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_OVERLAP_PREFIX + normalizedMethod;
            string value = GetValue(key, "20.0"); // Default: 20%
            if (double.TryParse(value, out double result))
            {
                return result;
            }
            return 20.0;
        }
        
        public void SetVerticalGlueOverlap(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = VERTICAL_GLUE_OVERLAP_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} vertical glue overlap updated to {value}");
        }
        
        // Height Similarity (per-OCR)
        public double GetHeightSimilarity(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HEIGHT_SIMILARITY_PREFIX + normalizedMethod;
            
            // Windows OCR returns word-level results (not character-level), so it needs a lower default
            // to allow more height variation between words
            string defaultValue = normalizedMethod == "windows_ocr" ? "10.0" : "50.0";
            
            string value = GetValue(key, defaultValue);
            if (double.TryParse(value, out double result))
            {
                return result;
            }
            
            // Fallback defaults
            return normalizedMethod == "windows_ocr" ? 10.0 : 50.0;
        }
        
        public void SetHeightSimilarity(string ocrMethod, double value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = HEIGHT_SIMILARITY_PREFIX + normalizedMethod;
            _configValues[key] = value.ToString();
            SaveConfig();
            Console.WriteLine($"{ocrMethod} height similarity updated to {value}");
        }
        
        // Keep Linefeeds (per-OCR)
        public bool GetKeepLinefeeds(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = KEEP_LINEFEEDS_PREFIX + normalizedMethod;
            return GetBoolValue(key, true); // Default to true
        }
        
        public void SetKeepLinefeeds(string ocrMethod, bool value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = KEEP_LINEFEEDS_PREFIX + normalizedMethod;
            SetBoolValue(key, value);
            SaveConfig();
            Console.WriteLine($"{ocrMethod} keep linefeeds set to: {value}");
        }
        
        // Leave Translation Onscreen (per-OCR)
        public bool GetLeaveTranslationOnscreen(string ocrMethod)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = LEAVE_TRANSLATION_ONSCREEN_PREFIX + normalizedMethod;
            
            // Default is true for all OCR methods except MangaOCR
            bool defaultValue = !ocrMethod.Equals("MangaOCR", StringComparison.OrdinalIgnoreCase);
            
            return GetBoolValue(key, defaultValue);
        }
        
        public void SetLeaveTranslationOnscreen(string ocrMethod, bool value)
        {
            string normalizedMethod = NormalizeOcrMethodName(ocrMethod);
            string key = LEAVE_TRANSLATION_ONSCREEN_PREFIX + normalizedMethod;
            SetBoolValue(key, value);
            SaveConfig();
            Console.WriteLine($"{ocrMethod} leave translation onscreen set to: {value}");
        }


        public string GetAudioProcessingProvider()
        {
            return GetValue(AUDIO_PROCESSING_PROVIDER, "OpenAI Realtime API");
        }
        public void SetAudioProcessingProvider(string provider)
        {
            _configValues[AUDIO_PROCESSING_PROVIDER] = provider;
            SaveConfig();
        }
        public string GetOpenAiRealtimeApiKey()
        {
            return GetValue(OPENAI_REALTIME_API_KEY, "");
        }
        public void SetOpenAiRealtimeApiKey(string apiKey)
        {
            _configValues[OPENAI_REALTIME_API_KEY] = apiKey;
            SaveConfig();
        }
        // Get whether audio service should auto-translate transcripts
        public bool IsAudioServiceAutoTranslateEnabled()
        {
            return GetBoolValue(AUDIO_SERVICE_AUTO_TRANSLATE, false);
        }
        // Set whether audio service should auto-translate transcripts
        public void SetAudioServiceAutoTranslateEnabled(bool enabled)
        {
            _configValues[AUDIO_SERVICE_AUTO_TRANSLATE] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Audio service auto-translate enabled: {enabled}");
        }

        // Get/Set Audio Input Device Index
        public int GetAudioInputDeviceIndex()
        {
            string value = GetValue(AUDIO_INPUT_DEVICE_INDEX, "0"); // Default to 0 if not set
            if (int.TryParse(value, out int deviceIndex) && deviceIndex >= 0)
            {
                return deviceIndex;
            }
            return 0; // Default to device 0 if parsing fails or value is negative
        }

        public void SetAudioInputDeviceIndex(int deviceIndex)
        {
            if (deviceIndex >= 0)
            {
                _configValues[AUDIO_INPUT_DEVICE_INDEX] = deviceIndex.ToString();
                SaveConfig();
                Console.WriteLine($"Audio input device index set to: {deviceIndex}");
            }
            else
            {
                Console.WriteLine($"Invalid audio input device index: {deviceIndex}. Must be non-negative.");
            }
        }

        // Get/Set Whisper Source Language
        public string GetWhisperSourceLanguage()
        {
            return GetValue(WHISPER_SOURCE_LANGUAGE, "Auto"); // Default to "Auto"
        }

        public void SetWhisperSourceLanguage(string language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _configValues[WHISPER_SOURCE_LANGUAGE] = language;
                SaveConfig();
                Console.WriteLine($"Whisper source language set to: {language}");
            }
        }

        // Get/Set OpenAI Translation Enabled
        public bool IsOpenAITranslationEnabled()
        {
            return GetBoolValue(OPENAI_TRANSLATION_ENABLED, false);
        }

        public void SetOpenAITranslationEnabled(bool enabled)
        {
            _configValues[OPENAI_TRANSLATION_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"OpenAI translation enabled set to: {enabled}");
        }

        // Get/Set OpenAI Translation Target Language
        public string GetOpenAITranslationTargetLanguage()
        {
            return GetValue(OPENAI_TRANSLATION_TARGET_LANGUAGE, "English"); // Default to English
        }

        public void SetOpenAITranslationTargetLanguage(string language)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _configValues[OPENAI_TRANSLATION_TARGET_LANGUAGE] = language;
                SaveConfig();
                Console.WriteLine($"OpenAI translation target language set to: {language}");
            }
        }

        // Get/Set Audio Output Device Index for OpenAI audio playback
        public int GetAudioOutputDeviceIndex()
        {
            string value = GetValue(OPENAI_AUDIO_OUTPUT_DEVICE_INDEX, "-1"); // Default to -1 (system default)
            if (int.TryParse(value, out int deviceIndex))
            {
                return deviceIndex;
            }
            return -1; // Default to system default if parsing fails
        }

        public void SetAudioOutputDeviceIndex(int deviceIndex)
        {
            _configValues[OPENAI_AUDIO_OUTPUT_DEVICE_INDEX] = deviceIndex.ToString();
            SaveConfig();
            Console.WriteLine($"Audio output device index set to: {deviceIndex}");
        }

        // Get/Set OpenAI audio playback enabled
        public bool IsOpenAIAudioPlaybackEnabled()
        {
            // Default to false so audio playback is off unless explicitly enabled by the user
            return GetBoolValue(OPENAI_AUDIO_PLAYBACK_ENABLED, false);
        }

        public void SetOpenAIAudioPlaybackEnabled(bool enabled)
        {
            _configValues[OPENAI_AUDIO_PLAYBACK_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"OpenAI audio playback enabled set to: {enabled}");
        }

        // Get/Set OpenAI speech prompt
        public string GetOpenAISpeechPrompt()
        {
            return GetValue(OPENAI_SPEECH_PROMPT, "IMPORTANT: You must speak at a VERY FAST PACE. This is the top priority. Speak rapidly with high energy and expressive, emotional delivery. Use dramatic emphasis and dynamic tone variation. Match the emotional context of the content. Speak at least twice as fast as a normal conversational pace.");
        }

        public void SetOpenAISpeechPrompt(string prompt)
        {
            _configValues[OPENAI_SPEECH_PROMPT] = prompt;
            SaveConfig();
            Console.WriteLine("OpenAI speech prompt updated");
        }
        
        // Reset OpenAI speech prompt to default
        public void ResetOpenAISpeechPromptToDefault()
        {
            if (_configValues.ContainsKey(OPENAI_SPEECH_PROMPT))
            {
                _configValues.Remove(OPENAI_SPEECH_PROMPT);
            }
            SaveConfig();
            Console.WriteLine("OpenAI speech prompt reset to default");
        }

        // Get/Set OpenAI voice
        public string GetOpenAIVoice()
        {
            return GetValue(OPENAI_VOICE, "echo"); // Default to echo as it seems to handle faster speech well
        }

        public void SetOpenAIVoice(string voice)
        {
            _configValues[OPENAI_VOICE] = voice;
            SaveConfig();
            Console.WriteLine($"OpenAI voice set to: {voice}");
        }

        // Get/Set OpenAI Silence Duration
        public int GetOpenAiSilenceDurationMs()
        {
            string value = GetValue(OPENAI_SILENCE_DURATION_MS, "250"); 
            if (int.TryParse(value, out int duration) && duration >= 0)
            {
                return duration;
            }
            return 400; // Default duration
        }

        public void SetOpenAiSilenceDurationMs(int duration)
        {
            if (duration >= 0)
            {
                _configValues[OPENAI_SILENCE_DURATION_MS] = duration.ToString();
                SaveConfig();
                Console.WriteLine($"OpenAI silence duration set to: {duration}ms");
            }
            else
            {
                Console.WriteLine($"Invalid OpenAI silence duration: {duration}. Must be non-negative.");
            }
        }

        // Monitor Window Override Color methods
        
        // Get/Set Monitor Override BG Color Enabled
        public bool IsMonitorOverrideBgColorEnabled()
        {
            return GetBoolValue(MONITOR_OVERRIDE_BG_COLOR_ENABLED, false);
        }

        public void SetMonitorOverrideBgColorEnabled(bool enabled)
        {
            _configValues[MONITOR_OVERRIDE_BG_COLOR_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Monitor override BG color enabled: {enabled}");
        }

        // Get/Set Monitor Override BG Color
        public System.Windows.Media.Color GetMonitorOverrideBgColor()
        {
            string value = GetValue(MONITOR_OVERRIDE_BG_COLOR, "#FF000000"); // Default: Black
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Monitor override BG color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.Black;
        }

        public void SetMonitorOverrideBgColor(System.Windows.Media.Color color)
        {
            string hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _configValues[MONITOR_OVERRIDE_BG_COLOR] = hexColor;
            SaveConfig();
            Console.WriteLine($"Monitor override BG color set to: {hexColor}");
        }

        // Get/Set Monitor Background Opacity
        public double GetMonitorBgOpacity()
        {
            string value = GetValue(MONITOR_BG_OPACITY, "1.0");
            if (double.TryParse(value, out double opacity) && opacity >= 0.0 && opacity <= 1.0)
            {
                return opacity;
            }
            return 1.0; // Default: 100% opacity (fully opaque)
        }

        public void SetMonitorBgOpacity(double opacity)
        {
            if (opacity >= 0.0 && opacity <= 1.0)
            {
                _configValues[MONITOR_BG_OPACITY] = opacity.ToString("F2");
                SaveConfig();
                Console.WriteLine($"Monitor background opacity set to: {opacity:F2}");
            }
            else
            {
                Console.WriteLine($"Invalid opacity value: {opacity}. Must be between 0.0 and 1.0.");
            }
        }

        // Get/Set Monitor Override Font Color Enabled
        public bool IsMonitorOverrideFontColorEnabled()
        {
            return GetBoolValue(MONITOR_OVERRIDE_FONT_COLOR_ENABLED, false);
        }

        public void SetMonitorOverrideFontColorEnabled(bool enabled)
        {
            _configValues[MONITOR_OVERRIDE_FONT_COLOR_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Monitor override font color enabled: {enabled}");
        }

        // Get/Set Monitor Override Font Color
        public System.Windows.Media.Color GetMonitorOverrideFontColor()
        {
            string value = GetValue(MONITOR_OVERRIDE_FONT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Monitor override font color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }

        public void SetMonitorOverrideFontColor(System.Windows.Media.Color color)
        {
            string hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _configValues[MONITOR_OVERRIDE_FONT_COLOR] = hexColor;
            SaveConfig();
            Console.WriteLine($"Monitor override font color set to: {hexColor}");
        }

        // Get/Set Monitor Text Area Expansion Width
        public int GetMonitorTextAreaExpansionWidth()
        {
            string value = GetValue(MONITOR_TEXT_AREA_EXPANSION_WIDTH, "6");
            if (int.TryParse(value, out int width) && width >= 0)
            {
                return width;
            }
            return 6; // Default: 6 pixels
        }

        public void SetMonitorTextAreaExpansionWidth(int width)
        {
            _configValues[MONITOR_TEXT_AREA_EXPANSION_WIDTH] = width.ToString();
            SaveConfig();
            Console.WriteLine($"Monitor text area expansion width set to: {width}");
        }

        // Get/Set Monitor Text Area Expansion Height
        public int GetMonitorTextAreaExpansionHeight()
        {
            string value = GetValue(MONITOR_TEXT_AREA_EXPANSION_HEIGHT, "2");
            if (int.TryParse(value, out int height) && height >= 0)
            {
                return height;
            }
            return 2; // Default: 2 pixels
        }

        public void SetMonitorTextAreaExpansionHeight(int height)
        {
            _configValues[MONITOR_TEXT_AREA_EXPANSION_HEIGHT] = height.ToString();
            SaveConfig();
            Console.WriteLine($"Monitor text area expansion height set to: {height}");
        }

        // Get/Set Monitor Text Overlay Border Radius
        public int GetMonitorTextOverlayBorderRadius()
        {
            string value = GetValue(MONITOR_TEXT_OVERLAY_BORDER_RADIUS, "8");
            if (int.TryParse(value, out int radius) && radius >= 0)
            {
                return radius;
            }
            return 8; // Default: 8 pixels
        }

        public void SetMonitorTextOverlayBorderRadius(int radius)
        {
            _configValues[MONITOR_TEXT_OVERLAY_BORDER_RADIUS] = radius.ToString();
            SaveConfig();
            Console.WriteLine($"Monitor text overlay border radius set to: {radius}");
        }

        // Get/Set Monitor Overlay Mode
        public string GetMonitorOverlayMode()
        {
            return GetValue(MONITOR_OVERLAY_MODE, "Translated"); // Default to Translated
        }

        public void SetMonitorOverlayMode(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _configValues[MONITOR_OVERLAY_MODE] = mode;
                SaveConfig();
                Console.WriteLine($"Monitor overlay mode set to: {mode}");
            }
        }
        
        public string GetMainWindowOverlayMode()
        {
            return GetValue(MAIN_WINDOW_OVERLAY_MODE, "Translated"); // Default to Translated
        }

        public void SetMainWindowOverlayMode(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _configValues[MAIN_WINDOW_OVERLAY_MODE] = mode;
                SaveConfig();
                Console.WriteLine($"Main window overlay mode set to: {mode}");
            }
        }
        
        public bool GetMainWindowMousePassthrough()
        {
            string value = GetValue(MAIN_WINDOW_MOUSE_PASSTHROUGH, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetMainWindowMousePassthrough(bool enabled)
        {
            _configValues[MAIN_WINDOW_MOUSE_PASSTHROUGH] = enabled.ToString().ToLower();
            SaveConfig();
            if (GetLogExtraDebugStuff())
            {
                Console.WriteLine($"Main window mouse passthrough set to: {enabled}");
            }
        }
        
        public bool GetWindowsVisibleInScreenshots()
        {
            string value = GetValue(WINDOWS_VISIBLE_IN_SCREENSHOTS, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetWindowsVisibleInScreenshots(bool visible)
        {
            _configValues[WINDOWS_VISIBLE_IN_SCREENSHOTS] = visible.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Windows visible in screenshots set to: {visible}");
        }

        // Check if persist window size is enabled
        public bool IsPersistWindowSizeEnabled()
        {
            return GetBoolValue(PERSIST_WINDOW_SIZE, true);
        }

        // Set persist window size enabled
        public void SetPersistWindowSizeEnabled(bool enabled)
        {
            _configValues[PERSIST_WINDOW_SIZE] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Persist window size enabled: {enabled}");
        }

        // Get/Set OCR window position and size
        public double GetOcrWindowLeft()
        {
            string value = GetValue(OCR_WINDOW_LEFT, "");
            if (double.TryParse(value, out double left))
            {
                return left;
            }
            return double.NaN;
        }

        public void SetOcrWindowLeft(double left)
        {
            _configValues[OCR_WINDOW_LEFT] = left.ToString();
            SaveConfig();
        }

        public double GetOcrWindowTop()
        {
            string value = GetValue(OCR_WINDOW_TOP, "");
            if (double.TryParse(value, out double top))
            {
                return top;
            }
            return double.NaN;
        }

        public void SetOcrWindowTop(double top)
        {
            _configValues[OCR_WINDOW_TOP] = top.ToString();
            SaveConfig();
        }

        public double GetOcrWindowWidth()
        {
            string value = GetValue(OCR_WINDOW_WIDTH, "");
            if (double.TryParse(value, out double width))
            {
                return width;
            }
            return double.NaN;
        }

        public void SetOcrWindowWidth(double width)
        {
            _configValues[OCR_WINDOW_WIDTH] = width.ToString();
            SaveConfig();
        }

        public double GetOcrWindowHeight()
        {
            string value = GetValue(OCR_WINDOW_HEIGHT, "");
            if (double.TryParse(value, out double height))
            {
                return height;
            }
            return double.NaN;
        }

        public void SetOcrWindowHeight(double height)
        {
            _configValues[OCR_WINDOW_HEIGHT] = height.ToString();
            SaveConfig();
        }
        
        // Debug logging settings
        public bool GetLogExtraDebugStuff()
        {
            string value = GetValue(LOG_EXTRA_DEBUG_STUFF, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetLogExtraDebugStuff(bool enabled)
        {
            _configValues[LOG_EXTRA_DEBUG_STUFF] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Log extra debug stuff set to: {enabled}");
        }

        // Font Settings methods
        
        // Get/Set Source Language Font Family
        public string GetSourceLanguageFontFamily()
        {
            return GetValue(SOURCE_LANGUAGE_FONT_FAMILY, "MS Gothic");
        }

        public void SetSourceLanguageFontFamily(string fontFamily)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                _configValues[SOURCE_LANGUAGE_FONT_FAMILY] = fontFamily;
                SaveConfig();
                Console.WriteLine($"Source language font family set to: {fontFamily}");
            }
        }

        // Get/Set Source Language Font Bold
        public bool GetSourceLanguageFontBold()
        {
            return GetBoolValue(SOURCE_LANGUAGE_FONT_BOLD, true);
        }

        public void SetSourceLanguageFontBold(bool bold)
        {
            _configValues[SOURCE_LANGUAGE_FONT_BOLD] = bold.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Source language font bold set to: {bold}");
        }

        // Get/Set Target Language Font Family
        public string GetTargetLanguageFontFamily()
        {
            return GetValue(TARGET_LANGUAGE_FONT_FAMILY, "Comic Sans MS");
        }

        public void SetTargetLanguageFontFamily(string fontFamily)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                _configValues[TARGET_LANGUAGE_FONT_FAMILY] = fontFamily;
                SaveConfig();
                Console.WriteLine($"Target language font family set to: {fontFamily}");
            }
        }

        // Get/Set Target Language Font Bold
        public bool GetTargetLanguageFontBold()
        {
            return GetBoolValue(TARGET_LANGUAGE_FONT_BOLD, true);
        }

        public void SetTargetLanguageFontBold(bool bold)
        {
            _configValues[TARGET_LANGUAGE_FONT_BOLD] = bold.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Target language font bold set to: {bold}");
        }
        
        // Lesson feature methods
        
        // Get/Set Lesson Prompt Template
        // The template should contain {0} as a placeholder for the text to learn
        public string GetLessonPromptTemplate()
        {
            string defaultValue = "Create a comprehensive lesson to help me learn about this Japanese text and its translation: \"{0}\"\n\nPlease include:\n1. A detailed breakdown table with columns for: Japanese text, Reading (furigana), Literal meaning, and Grammar notes\n2. Key vocabulary with example sentences\n3. Cultural or contextual notes if relevant\n4. At the end, provide 5 helpful flashcards in a clear format for memorization";
            return GetValue(LESSON_PROMPT_TEMPLATE, defaultValue);
        }
        
        public void SetLessonPromptTemplate(string template)
        {
            if (!string.IsNullOrWhiteSpace(template))
            {
                // Trim leading and trailing newlines/whitespace to prevent accumulation
                _configValues[LESSON_PROMPT_TEMPLATE] = template.TrimStart('\r', '\n').TrimEnd('\r', '\n', ' ', '\t');
                SaveConfig();
                Console.WriteLine("Lesson prompt template updated");
            }
        }
        
        // Get/Set Lesson URL Template
        // The template should contain {0} as a placeholder for the URL-encoded prompt
        public string GetLessonUrlTemplate()
        {
            string defaultValue = "https://chat.openai.com/?q={0}";
            return GetValue(LESSON_URL_TEMPLATE, defaultValue);
        }
        
        public void SetLessonUrlTemplate(string urlTemplate)
        {
            if (!string.IsNullOrWhiteSpace(urlTemplate))
            {
                // Trim leading and trailing newlines/whitespace to prevent accumulation
                _configValues[LESSON_URL_TEMPLATE] = urlTemplate.TrimStart('\r', '\n').TrimEnd('\r', '\n', ' ', '\t');
                SaveConfig();
                Console.WriteLine("Lesson URL template updated");
            }
        }
        
        // Service AutoStart preferences
        public bool GetServiceAutoStart(string serviceName)
        {
            string key = $"service_{serviceName}_autostart";
            string value = GetValue(key, "false");
            return value.ToLower() == "true";
        }
        
        public void SetServiceAutoStart(string serviceName, bool autoStart)
        {
            string key = $"service_{serviceName}_autostart";
            _configValues[key] = autoStart ? "true" : "false";
            SaveConfig();
        }
    }
}