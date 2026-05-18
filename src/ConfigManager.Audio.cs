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

        // Listen capture mode: "microphone" or "loopback"
        public string GetListenCaptureMode()
        {
            return GetValue(LISTEN_CAPTURE_MODE, "microphone");
        }

        public void SetListenCaptureMode(string mode)
        {
            _configValues[LISTEN_CAPTURE_MODE] = mode;
            SaveConfig();
            Console.WriteLine($"Listen capture mode set to: {mode}");
        }

        // WASAPI render-device ID to loopback-capture (empty = default device)
        public string GetListenLoopbackDeviceId()
        {
            return GetValue(LISTEN_LOOPBACK_DEVICE_ID, "");
        }

        public void SetListenLoopbackDeviceId(string deviceId)
        {
            _configValues[LISTEN_LOOPBACK_DEVICE_ID] = deviceId ?? "";
            SaveConfig();
            Console.WriteLine($"Listen loopback device ID set to: {deviceId}");
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

        // Get/Set OpenAI Realtime Translation Model (gpt-realtime-translate streaming speech translation)
        public string GetOpenAITranslateModel()
        {
            return GetValue(OPENAI_TRANSLATE_MODEL, "gpt-realtime-translate");
        }

        public void SetOpenAITranslateModel(string model)
        {
            _configValues[OPENAI_TRANSLATE_MODEL] = model;
            SaveConfig();
            Console.WriteLine($"OpenAI translate model set to: {model}");
        }

        // Transcription model is locked to gpt-realtime-whisper — the latest
        // natively-streaming, low-latency STT. The older gpt-4o-transcribe /
        // whisper-1 models were removed; this is intentionally not configurable.
        public string GetOpenAITranscriptionModel()
        {
            return "gpt-realtime-whisper";
        }

        // Get/Set OpenAI Noise Reduction (near_field, far_field, or none)
        public string GetOpenAINoiseReduction()
        {
            return GetValue(OPENAI_NOISE_REDUCTION, "near_field");
        }

        public void SetOpenAINoiseReduction(string noiseReduction)
        {
            _configValues[OPENAI_NOISE_REDUCTION] = noiseReduction;
            SaveConfig();
            Console.WriteLine($"OpenAI noise reduction set to: {noiseReduction}");
        }
    }
}
