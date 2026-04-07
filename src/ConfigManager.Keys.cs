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
        // Config keys
        public const string GEMINI_API_KEY = "gemini_api_key";
        public const string GEMINI_MODEL = "gemini_model";
        public const string GEMINI_THINKING_ENABLED = "gemini_thinking_enabled";
        public const string TRANSLATION_SERVICE = "translation_service";
        public const string OCR_METHOD = "ocr_method";
        public const string OLLAMA_URL = "ollama_url";
        public const string OLLAMA_PORT = "ollama_port";
        public const string OLLAMA_MODEL = "ollama_model";
        public const string OLLAMA_THINKING_MODE = "ollama_thinking_mode"; // Legacy, migrated to THINKING_ENABLED
        public const string THINKING_ENABLED = "thinking_enabled";
        public const string LLAMACPP_URL = "llamacpp_url";
        public const string LLAMACPP_PORT = "llamacpp_port";
        public const string LLAMACPP_MODEL = "llamacpp_model";
        public const string LLAMACPP_THINKING_MODE = "llamacpp_thinking_mode";
        public const string CHATGPT_API_KEY = "chatgpt_api_key";
        public const string CHATGPT_MODEL = "chatgpt_model";
        public const string CHATGPT_MAX_COMPLETION_TOKENS = "chatgpt_max_completion_tokens";
        public const string CHATGPT_THINKING_ENABLED = "chatgpt_thinking_enabled";
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
        public const string MAX_TRANSLATION_RETRIES = "max_translation_retries";
        public const string GAME_INFO = "game_info";
        
        // OCR configuration keys
        public const string MIN_TEXT_FRAGMENT_SIZE = "min_text_fragment_size";
        public const string BLOCK_DETECTION_SCALE = "block_detection_scale";
        public const string BLOCK_DETECTION_SETTLE_TIME = "block_detection_settle_time";
        public const string BLOCK_DETECTION_MAX_SETTLE_TIME = "block_detection_max_settle_time";
        public const string COOLDOWN_HASH_COMPARE_LENGTH = "cooldown_hash_compare_length";
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
        public const string PLAY_COMPLETION_SOUND = "play_completion_sound";
        public const string OCR_WINDOW_LEFT = "ocr_window_left";
        public const string OCR_WINDOW_TOP = "ocr_window_top";
        public const string OCR_WINDOW_WIDTH = "ocr_window_width";
        public const string OCR_WINDOW_HEIGHT = "ocr_window_height";
        
        // ChatBox window persistence
        public const string CHATBOX_WINDOW_LEFT = "chatbox_window_left";
        public const string CHATBOX_WINDOW_TOP = "chatbox_window_top";
        public const string CHATBOX_WINDOW_WIDTH = "chatbox_window_width";
        public const string CHATBOX_WINDOW_HEIGHT = "chatbox_window_height";
        public const string CHATBOX_WINDOW_WAS_ACTIVE = "chatbox_window_was_active";
        
        // Monitor window persistence
        public const string MONITOR_WINDOW_LEFT = "monitor_window_left";
        public const string MONITOR_WINDOW_TOP = "monitor_window_top";
        public const string MONITOR_WINDOW_WIDTH = "monitor_window_width";
        public const string MONITOR_WINDOW_HEIGHT = "monitor_window_height";
        public const string MONITOR_WINDOW_WAS_ACTIVE = "monitor_window_was_active";

        // Text-to-Speech configuration keys
        public const string TTS_SERVICE = "tts_service";
        public const string ELEVENLABS_API_KEY = "elevenlabs_api_key";
        public const string ELEVENLABS_VOICE = "elevenlabs_voice";
        public const string ELEVENLABS_USE_CUSTOM_VOICE_ID = "elevenlabs_use_custom_voice_id";
        public const string ELEVENLABS_CUSTOM_VOICE_ID = "elevenlabs_custom_voice_id";
        public const string GOOGLE_TTS_API_KEY = "google_tts_api_key";
        public const string GOOGLE_TTS_VOICE = "google_tts_voice";
        public const string QWEN3_TTS_URL = "qwen3_tts_url";
        public const string QWEN3_TTS_PORT = "qwen3_tts_port";
        public const string QWEN3_TTS_VOICE = "qwen3_tts_voice";
        
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
        public const string TTS_ALWAYS_GENERATE_NEW_AUDIO = "tts_always_generate_new_audio";
        public const string TTS_MIN_CHARS_FOR_TTS = "tts_min_chars_for_tts";

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
        public const string OPENAI_LISTEN_TEXT_PROMPT = "openai_listen_text_prompt";
        public const string OPENAI_LISTEN_SPOKEN_PROMPT = "openai_listen_spoken_prompt";
        public const string OPENAI_VOICE = "openai_voice";
        public const string OPENAI_SILENCE_DURATION_MS = "openai_silence_duration_ms";
        public const string OPENAI_SEMANTIC_VAD_EAGERNESS = "openai_semantic_vad_eagerness";
        public const string OPENAI_TRANSCRIPTION_MODEL = "openai_transcription_model";
        public const string OPENAI_NOISE_REDUCTION = "openai_noise_reduction";

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

        // Text overlay alignment defaults
        public const string TEXT_OVERLAY_HORIZONTAL_ALIGNMENT = "text_overlay_horizontal_alignment";
        public const string TEXT_OVERLAY_VERTICAL_ALIGNMENT = "text_overlay_vertical_alignment";

        // Edit mode
        public const string EDIT_MODE_ENABLED = "edit_mode_enabled";

        // Floating toolbar position (offset from main window's top-right corner)
        public const string TOOLBAR_OFFSET_X = "toolbar_offset_x";
        public const string TOOLBAR_OFFSET_Y = "toolbar_offset_y";

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
        
        // Prompt upgrade tracking
        public const string LAST_PROMPT_UPGRADE_VERSION = "last_prompt_upgrade_version";
        
        // Hotkey upgrade tracking
        public const string LAST_HOTKEY_UPGRADE_VERSION = "last_hotkey_upgrade_version";
        
        // Screenshot saving settings
        public const string SCREENSHOT_FILENAME = "screenshot_filename";
        public const string SCREENSHOT_FOLDER = "screenshot_folder";
        public const string SCREENSHOT_TYPE = "screenshot_type";

        // Batch converter settings
        public const string BATCH_JPEG_QUALITY = "batch_jpeg_quality";

        // Debug logging settings
        public const string LOG_EXTRA_DEBUG_STUFF = "log_extra_debug_stuff";
    }
}
