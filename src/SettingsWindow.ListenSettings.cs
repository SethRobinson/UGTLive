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
using NAudio.CoreAudioApi;
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

                openAiDualSessionCheckBox.IsChecked = ConfigManager.Instance.IsOpenAIDualSessionEnabled();
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

        private void OpenAiDualSessionCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                bool isEnabled = openAiDualSessionCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetOpenAIDualSessionEnabled(isEnabled);
                Console.WriteLine($"OpenAI dual-session mode set to: {isEnabled}");
                restartListenIfActive();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating OpenAI dual-session setting: {ex.Message}");
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
                UpdateSpeechDetectionApplicability();
                restartListenIfActive();
            }
        }

        // "Speech Detection" (server_vad silence gap) only governs the
        // transcription-only paths. OpenAI Translation uses the continuous
        // gpt-realtime-translate stream, which has no turn detection, so the
        // control is greyed out there for clarity.
        private void UpdateSpeechDetectionApplicability()
        {
            bool applicable = !ConfigManager.Instance.IsOpenAITranslationEnabled();
            if (vadEagernessComboBox != null)
            {
                vadEagernessComboBox.IsEnabled = applicable;
                // Grey the dropdown's own text when it doesn't apply.
                vadEagernessComboBox.Opacity = applicable ? 1.0 : 0.45;
            }
            // Leave the "Speech Detection:" label at normal brightness.
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

        // Populate the loopback combo with active render (playback) devices.
        private void LoadLoopbackDevices()
        {
            try
            {
                if (loopbackDeviceComboBox == null) return;
                loopbackDeviceComboBox.Items.Clear();
                loopbackDeviceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "Default playback device",
                    Tag = ""
                });

                string currentId = ConfigManager.Instance.GetListenLoopbackDeviceId();
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var item = new ComboBoxItem { Content = device.FriendlyName, Tag = device.ID };
                    loopbackDeviceComboBox.Items.Add(item);
                    if (device.ID == currentId) loopbackDeviceComboBox.SelectedItem = item;
                    device.Dispose();
                }

                if (loopbackDeviceComboBox.SelectedItem == null)
                    loopbackDeviceComboBox.SelectedIndex = 0; // Default playback device
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading loopback devices: {ex.Message}");
            }
        }

        // Select the saved capture mode and show the matching device dropdown.
        private void InitListenCaptureMode()
        {
            try
            {
                if (listenCaptureModeComboBox == null) return;
                string mode = ConfigManager.Instance.GetListenCaptureMode();
                foreach (ComboBoxItem item in listenCaptureModeComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), mode, StringComparison.OrdinalIgnoreCase))
                    {
                        listenCaptureModeComboBox.SelectedItem = item;
                        break;
                    }
                }
                if (listenCaptureModeComboBox.SelectedItem == null)
                    listenCaptureModeComboBox.SelectedIndex = 0;
                UpdateCaptureModeVisibility();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing listen capture mode: {ex.Message}");
            }
        }

        private void UpdateCaptureModeVisibility()
        {
            bool loopback = (listenCaptureModeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "loopback";
            if (inputDeviceComboBox != null)
                inputDeviceComboBox.Visibility = loopback ? Visibility.Collapsed : Visibility.Visible;
            if (loopbackDeviceComboBox != null)
                loopbackDeviceComboBox.Visibility = loopback ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ListenCaptureModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (listenCaptureModeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string mode = selectedItem.Tag?.ToString() ?? "microphone";
                ConfigManager.Instance.SetListenCaptureMode(mode);
                Console.WriteLine($"Listen capture mode set to: {mode}");
                UpdateCaptureModeVisibility();
                restartListenIfActive();
            }
        }

        private void LoopbackDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (loopbackDeviceComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string deviceId = selectedItem.Tag?.ToString() ?? "";
                ConfigManager.Instance.SetListenLoopbackDeviceId(deviceId);
                Console.WriteLine($"Loopback device set to: {selectedItem.Content}");
                restartListenIfActive();
            }
        }

        private void VadEagernessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (vadEagernessComboBox.SelectedItem is ComboBoxItem selectedItem &&
                int.TryParse(selectedItem.Tag?.ToString(), out int silenceMs))
            {
                ConfigManager.Instance.SetOpenAiSilenceDurationMs(silenceMs);
                Console.WriteLine($"server_vad silence duration set to: {silenceMs}ms");
                restartListenIfActive();
            }
        }

        private void OpenAiRealtimeApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/account/api-keys");
        }
    }
}
