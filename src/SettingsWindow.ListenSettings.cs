using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Diagnostics;
using System.Collections.ObjectModel;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using MessageBox = System.Windows.MessageBox;
using NAudio.Wave;
using System.Collections.Generic;
using System.Windows.Forms;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace UGTLive
{
    public partial class SettingsWindow
    {
        private void OpenAiRealtimeApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ConfigManager.Instance.SetOpenAiRealtimeApiKey(openAiRealtimeApiKeyPasswordBox.Password.Trim());
        }

        // Method to load audio input devices into the ComboBox
        private void LoadAudioInputDevices()
        {
            try
            {
                // Store the currently selected device index
                int currentDeviceIndex = ConfigManager.Instance.GetAudioInputDeviceIndex();
                
                // Clear previous items
                inputDeviceComboBox.Items.Clear();
                
                // Get the number of available input devices
                int deviceCount = WaveInEvent.DeviceCount;
                
                // Add a ComboBoxItem for each input device
                for (int i = 0; i < deviceCount; i++)
                {
                    var deviceCapabilities = WaveInEvent.GetCapabilities(i);
                    var item = new ComboBoxItem
                    {
                        Content = deviceCapabilities.ProductName,
                        Tag = i
                    };
                    inputDeviceComboBox.Items.Add(item);
                    
                    // Select this item if it matches the currently selected device
                    if (i == currentDeviceIndex)
                    {
                        inputDeviceComboBox.SelectedItem = item;
                    }
                }
                
                // If no device was selected, default to the first one
                if (inputDeviceComboBox.SelectedIndex < 0 && inputDeviceComboBox.Items.Count > 0)
                {
                    inputDeviceComboBox.SelectedIndex = 0;
                }
                
                // Load output devices too
                LoadAudioOutputDevices();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading audio input devices: {ex.Message}");
            }
        }

        // Load audio output devices
        private void LoadAudioOutputDevices()
        {
            try
            {
                // Store the currently selected device index
                int currentDeviceIndex = ConfigManager.Instance.GetAudioOutputDeviceIndex();
                
                // Clear previous items
                outputDeviceComboBox.Items.Clear();
                
                // Add system default option
                var defaultItem = new ComboBoxItem
                {
                    Content = "System Default",
                    Tag = -1
                };
                outputDeviceComboBox.Items.Add(defaultItem);
                
                // Get the number of available output devices
                int deviceCount = WaveOut.DeviceCount;
                
                // Add a ComboBoxItem for each output device
                for (int i = 0; i < deviceCount; i++)
                {
                    var deviceCapabilities = WaveOut.GetCapabilities(i);
                    var item = new ComboBoxItem
                    {
                        Content = deviceCapabilities.ProductName,
                        Tag = i
                    };
                    outputDeviceComboBox.Items.Add(item);
                    
                    // Select this item if it matches the currently selected device
                    if (i == currentDeviceIndex)
                    {
                        outputDeviceComboBox.SelectedItem = item;
                    }
                }
                
                // If current device is -1 (default), select the default option
                if (currentDeviceIndex == -1)
                {
                    outputDeviceComboBox.SelectedItem = defaultItem;
                }
                // If no device was selected, default to system default
                else if (outputDeviceComboBox.SelectedIndex < 0)
                {
                    outputDeviceComboBox.SelectedItem = defaultItem;
                }
                
                // Enable or disable output device controls based on audio playback setting
                bool playbackEnabled = ConfigManager.Instance.IsOpenAIAudioPlaybackEnabled();
                openAiAudioPlaybackCheckBox.IsChecked = playbackEnabled;
                outputDeviceComboBox.IsEnabled = playbackEnabled;
                outputDeviceLabel.IsEnabled = playbackEnabled;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading audio output devices: {ex.Message}");
            }
        }
        
        private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (outputDeviceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    int deviceIndex = (int)selectedItem.Tag;
                    ConfigManager.Instance.SetAudioOutputDeviceIndex(deviceIndex);
                    
                    Console.WriteLine($"Audio output device set to: {selectedItem.Content} (Index: {deviceIndex})");
                    restartListenIfActive();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating audio output device: {ex.Message}");
            }
        }
        
        private void OpenAiAudioPlaybackCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                bool isEnabled = openAiAudioPlaybackCheckBox.IsChecked ?? true;
                
                // Update UI
                outputDeviceComboBox.IsEnabled = isEnabled;
                outputDeviceLabel.IsEnabled = isEnabled;
                
                // Save to config
                ConfigManager.Instance.SetOpenAIAudioPlaybackEnabled(isEnabled);
                Console.WriteLine($"OpenAI audio playback enabled set to: {isEnabled}");
                restartListenIfActive();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating OpenAI audio playback setting: {ex.Message}");
            }
        }

        // Whisper Source Language changed
        private void WhisperSourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            if (whisperSourceLanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string languageCode = selectedItem.Tag?.ToString() ?? "Auto";
                ConfigManager.Instance.SetWhisperSourceLanguage(languageCode);
                Console.WriteLine($"Whisper source language set to: {languageCode}");
                updateAutoGeneratedPromptPreview();
                restartListenIfActive();
            }
        }

        // Event handler for audio translation type dropdown
        private void AudioTranslationTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (audioTranslationTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "none";
                
                // Update the configuration based on selection
                switch (tag)
                {
                    case "none":
                        // No translation
                        ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(false);
                        ConfigManager.Instance.SetOpenAITranslationEnabled(false);
                        break;
                    case "openai":
                        // OpenAI translation
                        ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(false);
                        ConfigManager.Instance.SetOpenAITranslationEnabled(true);
                        break;
                    case "google":
                        // Google Translate
                        ConfigManager.Instance.SetAudioServiceAutoTranslateEnabled(true);
                        ConfigManager.Instance.SetOpenAITranslationEnabled(false);
                        break;
                }
                
                Console.WriteLine($"Audio translation type set to: {tag}");
                restartListenIfActive();
            }
        }

        private void TranscriptionModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (transcriptionModelComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string model = selectedItem.Tag?.ToString() ?? "gpt-4o-transcribe";
                ConfigManager.Instance.SetOpenAITranscriptionModel(model);
                Console.WriteLine($"Transcription model set to: {model}");
                restartListenIfActive();
            }
        }

        private void NoiseReductionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (noiseReductionComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string noiseReduction = selectedItem.Tag?.ToString() ?? "near_field";
                ConfigManager.Instance.SetOpenAINoiseReduction(noiseReduction);
                Console.WriteLine($"Noise reduction set to: {noiseReduction}");
                restartListenIfActive();
            }
        }

        // Event handler for input device selection change
        private void InputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || inputDeviceComboBox == null || inputDeviceComboBox.SelectedItem == null)
                return;

            if (inputDeviceComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is int selectedIndex)
            {
                if (selectedIndex >= 0) // Ensure it's a valid device index, not the error tag
                {
                    ConfigManager.Instance.SetAudioInputDeviceIndex(selectedIndex);
                    Console.WriteLine($"Audio input device changed to: {selectedItem.Content} (Index: {selectedIndex})");
                    restartListenIfActive();
                }
            }
        }

        private void VadEagernessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (vadEagernessComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string eagerness)
            {
                ConfigManager.Instance.SetSemanticVadEagerness(eagerness);
                Console.WriteLine($"Semantic VAD eagerness set to: {eagerness}");
                restartListenIfActive();
            }
        }

        private void ListenTextPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            string prompt = listenTextPromptTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(prompt))
            {
                ConfigManager.Instance.SetListenTextPrompt(prompt);
                restartListenIfActive();
            }
        }

        private void ResetListenTextPromptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            ConfigManager.Instance.ResetListenTextPromptToDefault();
            listenTextPromptTextBox.Text = ConfigManager.Instance.GetListenTextPrompt();
            restartListenIfActive();
        }

        private void ListenSpokenPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            string prompt = listenSpokenPromptTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(prompt))
            {
                ConfigManager.Instance.SetListenSpokenPrompt(prompt);
                restartListenIfActive();
            }
        }

        private void ResetListenSpokenPromptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            ConfigManager.Instance.ResetListenSpokenPromptToDefault();
            listenSpokenPromptTextBox.Text = ConfigManager.Instance.GetListenSpokenPrompt();
            restartListenIfActive();
        }

        private void updateAutoGeneratedPromptPreview()
        {
            string sourceLanguage = ConfigManager.Instance.GetWhisperSourceLanguage();
            string targetLanguage = ConfigManager.Instance.GetTargetLanguage();

            bool isSourceSpecified = !string.IsNullOrEmpty(sourceLanguage) &&
                                     !sourceLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase);

            string langDirective = isSourceSpecified
                ? $"Translate from {sourceLanguage} to {targetLanguage}."
                : $"Translate from the detected language to {targetLanguage}.";

            autoGeneratedPromptPreview.Text = langDirective;
        }

        // Handle OpenAI voice selection change
        private void OpenAiVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (openAiVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Tag?.ToString() ?? "echo";
                    ConfigManager.Instance.SetOpenAIVoice(voiceId);
                    Console.WriteLine($"OpenAI voice set to: {selectedItem.Content} (ID: {voiceId})");
                    restartListenIfActive();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating OpenAI voice: {ex.Message}");
            }
        }

        private void OpenAiRealtimeApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/account/api-keys");
        }
    }
}
