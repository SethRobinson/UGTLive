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
        // Text-to-Speech methods
        
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
        
        // Qwen3-TTS methods

        public string GetQwen3TtsUrl()
        {
            return GetValue(QWEN3_TTS_URL, "http://127.0.0.1");
        }

        public string GetQwen3TtsPort()
        {
            return GetValue(QWEN3_TTS_PORT, "5004");
        }

        public string GetQwen3TtsVoice()
        {
            return GetValue(QWEN3_TTS_VOICE, "ono_anna");
        }

        public void SetQwen3TtsVoice(string voice)
        {
            if (!string.IsNullOrWhiteSpace(voice))
            {
                _configValues[QWEN3_TTS_VOICE] = voice;
                SaveConfig();
                Console.WriteLine($"Qwen3-TTS voice set to: {voice}");
            }
        }
        
        private void migratePageReadingTtsDefaults()
        {
            // If the user never explicitly chose a Page Reading TTS service, the config file
            // may still contain the old hardcoded default "Google Cloud TTS". Clear it so the
            // getter falls through to the main TTS service (GetTtsService()).
            string googleApiKey = GetValue(GOOGLE_TTS_API_KEY, "");
            bool googleKeyIsPlaceholder = string.IsNullOrWhiteSpace(googleApiKey) || googleApiKey.Contains("<your");

            if (_configValues.TryGetValue(TTS_SOURCE_SERVICE, out string? srcService)
                && srcService == "Google Cloud TTS" && googleKeyIsPlaceholder)
            {
                _configValues.Remove(TTS_SOURCE_SERVICE);
                Console.WriteLine("Migrated tts_source_service: cleared stale default so it follows the main TTS service");
            }

            if (_configValues.TryGetValue(TTS_TARGET_SERVICE, out string? tgtService)
                && tgtService == "Google Cloud TTS" && googleKeyIsPlaceholder)
            {
                _configValues.Remove(TTS_TARGET_SERVICE);
                Console.WriteLine("Migrated tts_target_service: cleared stale default so it follows the main TTS service");
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
            return GetValue(TTS_SOURCE_VOICE, getDefaultVoiceForService(GetTtsSourceService()));
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
            return GetValue(TTS_TARGET_VOICE, getDefaultVoiceForService(GetTtsTargetService()));
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
        
        private string getDefaultVoiceForService(string service)
        {
            return service switch
            {
                "Qwen3-TTS" => GetQwen3TtsVoice(),
                "Google Cloud TTS" => GetGoogleTtsVoice(),
                _ => GetElevenLabsVoice()
            };
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

        public bool GetTtsAlwaysGenerateNewAudio()
        {
            return GetBoolValue(TTS_ALWAYS_GENERATE_NEW_AUDIO, false);
        }

        public void SetTtsAlwaysGenerateNewAudio(bool enabled)
        {
            _configValues[TTS_ALWAYS_GENERATE_NEW_AUDIO] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"TTS always generate new audio: {enabled}");
        }

        // Get/Set TTS Vertical Overlap Threshold (in pixels)
        public double GetTtsVerticalOverlapThreshold()
        {
            string value = GetValue(TTS_VERTICAL_OVERLAP_THRESHOLD, "120");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold))
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
        
        public int GetTtsMinCharsForTts()
        {
            string value = GetValue(TTS_MIN_CHARS_FOR_TTS, "1");
            if (int.TryParse(value, out int minChars) && minChars >= 1)
            {
                return minChars;
            }
            return 1;
        }

        public void SetTtsMinCharsForTts(int minChars)
        {
            if (minChars < 1)
            {
                minChars = 1;
            }
            _configValues[TTS_MIN_CHARS_FOR_TTS] = minChars.ToString();
            SaveConfig();
            Console.WriteLine($"TTS minimum characters for TTS set to: {minChars}");
        }

        public bool IsTextBelowTtsMinChars(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;
            int minChars = GetTtsMinCharsForTts();
            int nonPunctuationCount = 0;
            foreach (char c in text)
            {
                if (!char.IsPunctuation(c) && !char.IsWhiteSpace(c) && !char.IsSymbol(c)
                    && c != '〜' && c != 'ー' && c != '．')
                    nonPunctuationCount++;
            }
            return nonPunctuationCount < minChars;
        }
    }
}
