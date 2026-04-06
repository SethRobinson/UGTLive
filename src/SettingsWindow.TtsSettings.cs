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
        private void UpdateTtsServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isElevenLabsSelected = selectedService == "ElevenLabs";
                bool isGoogleTtsSelected = selectedService == "Google Cloud TTS";
                bool isQwen3TtsSelected = selectedService == "Qwen3-TTS";
                
                // Make sure the window is fully loaded and controls are initialized
                if (elevenLabsApiKeyLabel == null || elevenLabsApiKeyGrid == null || 
                    elevenLabsApiKeyHelpText == null || elevenLabsVoiceLabel == null || 
                    elevenLabsVoiceComboBox == null || googleTtsApiKeyLabel == null || 
                    googleTtsApiKeyGrid == null || googleTtsVoiceLabel == null || 
                    googleTtsVoiceComboBox == null || elevenLabsCustomVoiceLabel == null || 
                    elevenLabsCustomVoiceGrid == null)
                {
                    Console.WriteLine("TTS UI elements not initialized yet. Skipping visibility update.");
                    return;
                }
                
                // Show/hide ElevenLabs-specific settings
                elevenLabsApiKeyLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyGrid.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyHelpText.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsCustomVoiceLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsCustomVoiceGrid.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsVoiceLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsVoiceComboBox.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Google TTS-specific settings
                googleTtsApiKeyLabel.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsApiKeyGrid.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsVoiceLabel.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTtsVoiceComboBox.Visibility = isGoogleTtsSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Qwen3-TTS-specific settings
                if (qwen3TtsVoiceLabel != null && qwen3TtsVoiceComboBox != null)
                {
                    qwen3TtsVoiceLabel.Visibility = isQwen3TtsSelected ? Visibility.Visible : Visibility.Collapsed;
                    qwen3TtsVoiceComboBox.Visibility = isQwen3TtsSelected ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Load service-specific settings if they're being shown
                if (isElevenLabsSelected)
                {
                    elevenLabsApiKeyPasswordBox.Password = ConfigManager.Instance.GetElevenLabsApiKey();
                    
                    // Set selected voice
                    string voiceId = ConfigManager.Instance.GetElevenLabsVoice();
                    foreach (ComboBoxItem item in elevenLabsVoiceComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                        {
                            elevenLabsVoiceComboBox.SelectedItem = item;
                            break;
                        }
                    }

                    // Update custom voice UI state
                    bool useCustom = ConfigManager.Instance.GetElevenLabsUseCustomVoiceId();
                    if (elevenLabsCustomVoiceCheckBox != null)
                    {
                        elevenLabsCustomVoiceCheckBox.IsChecked = useCustom;
                    }
                    if (elevenLabsCustomVoiceIdTextBox != null)
                    {
                        elevenLabsCustomVoiceIdTextBox.Text = ConfigManager.Instance.GetElevenLabsCustomVoiceId();
                        elevenLabsCustomVoiceIdTextBox.IsEnabled = useCustom;
                    }
                    elevenLabsVoiceComboBox.IsEnabled = !useCustom;
                    elevenLabsVoiceLabel.IsEnabled = !useCustom;
                }
                else if (isGoogleTtsSelected)
                {
                    googleTtsApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTtsApiKey();
                    
                    // Set selected voice
                    string voiceId = ConfigManager.Instance.GetGoogleTtsVoice();
                    foreach (ComboBoxItem item in googleTtsVoiceComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                        {
                            googleTtsVoiceComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                else if (isQwen3TtsSelected)
                {
                    if (qwen3TtsVoiceComboBox != null)
                    {
                        string voiceId = ConfigManager.Instance.GetQwen3TtsVoice();
                        foreach (ComboBoxItem item in qwen3TtsVoiceComboBox.Items)
                        {
                            if (string.Equals(item.Tag?.ToString(), voiceId, StringComparison.OrdinalIgnoreCase))
                            {
                                qwen3TtsVoiceComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS service-specific settings: {ex.Message}");
            }
        }
        
        private void ElevenLabsApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://elevenlabs.io/app/developers/api-keys");
        }
        
        private void GoogleTtsApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://cloud.google.com/text-to-speech");
        }
        
        // Text-to-Speech settings handlers
        
        private void TtsServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (ttsServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string service = selectedItem.Content.ToString() ?? "ElevenLabs";
                    ConfigManager.Instance.SetTtsService(service);
                    Console.WriteLine($"TTS service set to: {service}");
                    
                    // Update UI for the selected service
                    UpdateTtsServiceSpecificSettings(service);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS service: {ex.Message}");
            }
        }
        
        private void GoogleTtsApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                string apiKey = googleTtsApiKeyPasswordBox.Password.Trim();
                ConfigManager.Instance.SetGoogleTtsApiKey(apiKey);
                Console.WriteLine("Google TTS API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google TTS API key: {ex.Message}");
            }
        }
        
        private void GoogleTtsVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (googleTtsVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Tag?.ToString() ?? "ja-JP-Neural2-B"; // Default to Female A
                    ConfigManager.Instance.SetGoogleTtsVoice(voiceId);
                    Console.WriteLine($"Google TTS voice set to: {selectedItem.Content} (ID: {voiceId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google TTS voice: {ex.Message}");
            }
        }
        
        // Qwen3-TTS event handlers

        private void Qwen3TtsVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                if (qwen3TtsVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Tag?.ToString() ?? "ono_anna";
                    ConfigManager.Instance.SetQwen3TtsVoice(voiceId);
                    Console.WriteLine($"Qwen3-TTS voice set to: {selectedItem.Content} (ID: {voiceId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Qwen3-TTS voice: {ex.Message}");
            }
        }

        // Audio Preload Settings
        
        private void LoadAudioPreloadSettings()
        {
            try
            {
                // Detach event handlers
                if (ttsPreloadEnabledCheckBox != null)
                {
                    ttsPreloadEnabledCheckBox.Checked -= TtsPreloadEnabledCheckBox_CheckedChanged;
                    ttsPreloadEnabledCheckBox.Unchecked -= TtsPreloadEnabledCheckBox_CheckedChanged;
                }
                if (ttsPreloadModeComboBox != null)
                {
                    ttsPreloadModeComboBox.SelectionChanged -= TtsPreloadModeComboBox_SelectionChanged;
                }
                if (ttsPlayOrderComboBox != null)
                {
                    ttsPlayOrderComboBox.SelectionChanged -= TtsPlayOrderComboBox_SelectionChanged;
                }
                if (ttsVerticalOverlapTextBox != null)
                {
                    ttsVerticalOverlapTextBox.TextChanged -= TtsVerticalOverlapTextBox_TextChanged;
                }
                if (ttsAutoPlayAllCheckBox != null)
                {
                    ttsAutoPlayAllCheckBox.Checked -= TtsAutoPlayAllCheckBox_CheckedChanged;
                    ttsAutoPlayAllCheckBox.Unchecked -= TtsAutoPlayAllCheckBox_CheckedChanged;
                }
                if (ttsDeleteCacheOnStartupCheckBox != null)
                {
                    ttsDeleteCacheOnStartupCheckBox.Checked -= TtsDeleteCacheOnStartupCheckBox_CheckedChanged;
                    ttsDeleteCacheOnStartupCheckBox.Unchecked -= TtsDeleteCacheOnStartupCheckBox_CheckedChanged;
                }
                if (ttsAlwaysGenerateNewAudioCheckBox != null)
                {
                    ttsAlwaysGenerateNewAudioCheckBox.Checked -= TtsAlwaysGenerateNewAudioCheckBox_CheckedChanged;
                    ttsAlwaysGenerateNewAudioCheckBox.Unchecked -= TtsAlwaysGenerateNewAudioCheckBox_CheckedChanged;
                }
                if (ttsMaxConcurrentDownloadsTextBox != null)
                {
                    ttsMaxConcurrentDownloadsTextBox.LostFocus -= TtsMaxConcurrentDownloadsTextBox_LostFocus;
                }
                if (ttsMinCharsTextBox != null)
                {
                    ttsMinCharsTextBox.LostFocus -= TtsMinCharsTextBox_LostFocus;
                }

                // Load preload enabled checkbox
                if (ttsPreloadEnabledCheckBox != null)
                {
                    bool preloadEnabled = ConfigManager.Instance.IsTtsPreloadEnabled();
                    ttsPreloadEnabledCheckBox.IsChecked = preloadEnabled;
                }
                
                // Load preload mode
                string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                if (ttsPreloadModeComboBox != null)
                {
                    foreach (ComboBoxItem item in ttsPreloadModeComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), preloadMode, StringComparison.OrdinalIgnoreCase))
                        {
                            ttsPreloadModeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Load play order
                string playOrder = ConfigManager.Instance.GetTtsPlayOrder();
                if (ttsPlayOrderComboBox != null)
                {
                    foreach (ComboBoxItem item in ttsPlayOrderComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), playOrder, StringComparison.OrdinalIgnoreCase))
                        {
                            ttsPlayOrderComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Load vertical overlap threshold
                if (ttsVerticalOverlapTextBox != null)
                {
                    double threshold = ConfigManager.Instance.GetTtsVerticalOverlapThreshold();
                    _lastVerticalOverlapValue = threshold;
                    ttsVerticalOverlapTextBox.Text = threshold.ToString();
                }
                
                // Load auto play all
                if (ttsAutoPlayAllCheckBox != null)
                {
                    bool autoPlayAll = ConfigManager.Instance.IsTtsAutoPlayAllEnabled();
                    _lastAutoPlayAllValue = autoPlayAll;
                    ttsAutoPlayAllCheckBox.IsChecked = autoPlayAll;
                }
                
                // Load delete cache on startup
                if (ttsDeleteCacheOnStartupCheckBox != null)
                {
                    bool deleteCache = ConfigManager.Instance.GetTtsDeleteCacheOnStartup();
                    _lastDeleteCacheValue = deleteCache;
                    ttsDeleteCacheOnStartupCheckBox.IsChecked = deleteCache;
                }
                
                // Load always generate new audio
                if (ttsAlwaysGenerateNewAudioCheckBox != null)
                {
                    ttsAlwaysGenerateNewAudioCheckBox.IsChecked = ConfigManager.Instance.GetTtsAlwaysGenerateNewAudio();
                }
                
                // Load max concurrent downloads
                if (ttsMaxConcurrentDownloadsTextBox != null)
                {
                    int maxConcurrent = ConfigManager.Instance.GetTtsMaxConcurrentDownloads();
                    _lastMaxConcurrentDownloadsValue = maxConcurrent;
                    ttsMaxConcurrentDownloadsTextBox.Text = maxConcurrent.ToString();
                }

                // Load minimum characters for TTS
                if (ttsMinCharsTextBox != null)
                {
                    int minChars = ConfigManager.Instance.GetTtsMinCharsForTts();
                    _lastMinCharsValue = minChars;
                    ttsMinCharsTextBox.Text = minChars.ToString();
                }
                
                // Show effective TTS service on the source/target buttons
                updatePageReadingTtsButtonLabels();
                
                // Re-attach event handlers
                if (ttsPreloadEnabledCheckBox != null)
                {
                    ttsPreloadEnabledCheckBox.Checked += TtsPreloadEnabledCheckBox_CheckedChanged;
                    ttsPreloadEnabledCheckBox.Unchecked += TtsPreloadEnabledCheckBox_CheckedChanged;
                }
                if (ttsPreloadModeComboBox != null)
                {
                    ttsPreloadModeComboBox.SelectionChanged += TtsPreloadModeComboBox_SelectionChanged;
                }
                if (ttsPlayOrderComboBox != null)
                {
                    ttsPlayOrderComboBox.SelectionChanged += TtsPlayOrderComboBox_SelectionChanged;
                }
                if (ttsVerticalOverlapTextBox != null)
                {
                    ttsVerticalOverlapTextBox.TextChanged += TtsVerticalOverlapTextBox_TextChanged;
                }
                if (ttsAutoPlayAllCheckBox != null)
                {
                    ttsAutoPlayAllCheckBox.Checked += TtsAutoPlayAllCheckBox_CheckedChanged;
                    ttsAutoPlayAllCheckBox.Unchecked += TtsAutoPlayAllCheckBox_CheckedChanged;
                }
                if (ttsDeleteCacheOnStartupCheckBox != null)
                {
                    ttsDeleteCacheOnStartupCheckBox.Checked += TtsDeleteCacheOnStartupCheckBox_CheckedChanged;
                    ttsDeleteCacheOnStartupCheckBox.Unchecked += TtsDeleteCacheOnStartupCheckBox_CheckedChanged;
                }
                if (ttsAlwaysGenerateNewAudioCheckBox != null)
                {
                    ttsAlwaysGenerateNewAudioCheckBox.Checked += TtsAlwaysGenerateNewAudioCheckBox_CheckedChanged;
                    ttsAlwaysGenerateNewAudioCheckBox.Unchecked += TtsAlwaysGenerateNewAudioCheckBox_CheckedChanged;
                }
                if (ttsMaxConcurrentDownloadsTextBox != null)
                {
                    ttsMaxConcurrentDownloadsTextBox.LostFocus += TtsMaxConcurrentDownloadsTextBox_LostFocus;
                }
                if (ttsMinCharsTextBox != null)
                {
                    ttsMinCharsTextBox.LostFocus += TtsMinCharsTextBox_LostFocus;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading audio preload settings: {ex.Message}");
            }
        }
        
        private void TtsPreloadEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                
                bool isEnabled = ttsPreloadEnabledCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetTtsPreloadEnabled(isEnabled);
                Console.WriteLine($"TTS preload enabled: {isEnabled}");
                
                // Cancel any in-progress preloads and retrigger OCR
                AudioPreloadService.Instance.CancelAllPreloads();
                Logic.Instance.ResetHash();
                Logic.Instance.ClearAllTextObjects();
                MainWindow.Instance.SetOCRCheckIsWanted(true); // Force OCR check immediately
                Console.WriteLine("OCR retriggered due to TTS preload enabled change");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS preload enabled: {ex.Message}");
            }
        }
        
        private void TtsPreloadModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                    
                if (ttsPreloadModeComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string mode = selectedItem.Content.ToString() ?? "Source language";
                    string previousMode = ConfigManager.Instance.GetTtsPreloadMode();
                    ConfigManager.Instance.SetTtsPreloadMode(mode);
                    Console.WriteLine($"TTS preload mode set to: {mode}");
                    
                    // Don't clear cache - just retrigger OCR if mode changed
                    if (mode != previousMode)
                    {
                        // Cancel any in-progress preloads
                        AudioPreloadService.Instance.CancelAllPreloads();
                        
                        // Retrigger OCR to start preloading with new mode
                        Logic.Instance.ResetHash();
                        Logic.Instance.ClearAllTextObjects();
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                        Console.WriteLine("OCR retriggered due to TTS preload mode change");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS preload mode: {ex.Message}");
            }
        }
        
        private void TtsPlayOrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                    
                if (ttsPlayOrderComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string order = selectedItem.Content.ToString() ?? "Top down, left to right";
                    ConfigManager.Instance.SetTtsPlayOrder(order);
                    Console.WriteLine($"TTS play order set to: {order}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS play order: {ex.Message}");
            }
        }
        
        private static double _lastVerticalOverlapValue = -1;
        
        private void TtsVerticalOverlapTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                
                if (ttsVerticalOverlapTextBox == null)
                    return;
                
                string text = ttsVerticalOverlapTextBox.Text;
                
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold))
                {
                    if (threshold >= 0)
                    {
                        // Only save if the value actually changed
                        if (Math.Abs(threshold - _lastVerticalOverlapValue) > 0.001)
                        {
                            _lastVerticalOverlapValue = threshold;
                            ConfigManager.Instance.SetTtsVerticalOverlapThreshold(threshold);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS vertical overlap threshold: {ex.Message}");
            }
        }
        
        private static bool _lastAutoPlayAllValue = false;
        
        private void TtsAutoPlayAllCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                
                bool isEnabled = ttsAutoPlayAllCheckBox.IsChecked ?? false;
                
                // Only save if the value actually changed
                if (isEnabled != _lastAutoPlayAllValue)
                {
                    _lastAutoPlayAllValue = isEnabled;
                    ConfigManager.Instance.SetTtsAutoPlayAllEnabled(isEnabled);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS auto play all: {ex.Message}");
            }
        }
        
        private static bool _lastDeleteCacheValue = false;
        
        private void TtsDeleteCacheOnStartupCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                
                bool isEnabled = ttsDeleteCacheOnStartupCheckBox.IsChecked ?? false;
                
                // Only save if the value actually changed
                if (isEnabled != _lastDeleteCacheValue)
                {
                    _lastDeleteCacheValue = isEnabled;
                    ConfigManager.Instance.SetTtsDeleteCacheOnStartup(isEnabled);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS delete cache on startup: {ex.Message}");
            }
        }
        
        private void TtsAlwaysGenerateNewAudioCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                bool isEnabled = ttsAlwaysGenerateNewAudioCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetTtsAlwaysGenerateNewAudio(isEnabled);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS always generate new audio: {ex.Message}");
            }
        }
        
        private static int _lastMaxConcurrentDownloadsValue = -1;
        
        private void TtsMaxConcurrentDownloadsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                
                if (ttsMaxConcurrentDownloadsTextBox == null)
                    return;
                
                string text = ttsMaxConcurrentDownloadsTextBox.Text;
                
                if (int.TryParse(text, out int maxConcurrent))
                {
                    // Allow 0 (unlimited) or any positive value
                    if (maxConcurrent < 0)
                    {
                        maxConcurrent = 0;
                        ttsMaxConcurrentDownloadsTextBox.Text = "0";
                    }
                    
                    // Only save if the value actually changed
                    if (maxConcurrent != _lastMaxConcurrentDownloadsValue)
                    {
                        _lastMaxConcurrentDownloadsValue = maxConcurrent;
                        ConfigManager.Instance.SetTtsMaxConcurrentDownloads(maxConcurrent);
                        
                        // Update the AudioPreloadService's concurrency limit
                        AudioPreloadService.Instance.UpdateConcurrencyLimit();
                    }
                }
                else
                {
                    // Invalid input, reset to last valid value or default
                    int currentValue = ConfigManager.Instance.GetTtsMaxConcurrentDownloads();
                    ttsMaxConcurrentDownloadsTextBox.Text = currentValue.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS max concurrent downloads: {ex.Message}");
            }
        }
        
        private static int _lastMinCharsValue = -1;

        private void TtsMinCharsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                if (ttsMinCharsTextBox == null)
                    return;

                string text = ttsMinCharsTextBox.Text;

                if (int.TryParse(text, out int minChars))
                {
                    if (minChars < 1)
                    {
                        minChars = 1;
                        ttsMinCharsTextBox.Text = "1";
                    }

                    if (minChars != _lastMinCharsValue)
                    {
                        _lastMinCharsValue = minChars;
                        ConfigManager.Instance.SetTtsMinCharsForTts(minChars);

                        MonitorWindow.Instance?.RefreshOverlays();
                        MainWindow.Instance?.RefreshMainWindowOverlays();
                    }
                }
                else
                {
                    int currentValue = ConfigManager.Instance.GetTtsMinCharsForTts();
                    ttsMinCharsTextBox.Text = currentValue.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS minimum characters: {ex.Message}");
            }
        }

        private void SetSourceTtsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentService = ConfigManager.Instance.GetTtsSourceService();
                string currentVoice = ConfigManager.Instance.GetTtsSourceVoice();
                bool useCustom = ConfigManager.Instance.GetTtsSourceUseCustomVoiceId();
                string customVoiceId = ConfigManager.Instance.GetTtsSourceCustomVoiceId();
                
                var dialog = new TtsVoiceSelectorDialog(currentService, currentVoice, useCustom, customVoiceId);
                dialog.Owner = this; // Make it modal to SettingsWindow
                if (dialog.ShowDialog() == true)
                {
                    // Check if voice actually changed
                    bool voiceChanged = dialog.SelectedService != currentService || 
                                       dialog.SelectedVoice != currentVoice ||
                                       dialog.UseCustomVoiceId != useCustom ||
                                       (dialog.UseCustomVoiceId && dialog.CustomVoiceId != customVoiceId);
                    
                    ConfigManager.Instance.SetTtsSourceService(dialog.SelectedService);
                    ConfigManager.Instance.SetTtsSourceVoice(dialog.SelectedVoice);
                    ConfigManager.Instance.SetTtsSourceUseCustomVoiceId(dialog.UseCustomVoiceId);
                    ConfigManager.Instance.SetTtsSourceCustomVoiceId(dialog.CustomVoiceId ?? "");
                    Console.WriteLine($"Source TTS set to: {dialog.SelectedService} / {dialog.SelectedVoice} (Custom: {dialog.UseCustomVoiceId})");
                    
                    updatePageReadingTtsButtonLabels();
                    adjustConcurrencyForLocalServices();
                    
                    // Clear audio cache and retrigger OCR if voice changed
                    if (voiceChanged)
                    {
                        AudioPreloadService.Instance.ClearAudioCache();
                        Console.WriteLine("Audio cache cleared due to source TTS voice change");
                        
                        // Retrigger OCR to regenerate audio with new voice
                        Logic.Instance.ResetHash();
                        Logic.Instance.ClearAllTextObjects();
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                        Console.WriteLine("OCR retriggered due to source TTS voice change");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting source TTS: {ex.Message}");
                MessageBox.Show($"Error setting source TTS: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SetTargetTtsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentService = ConfigManager.Instance.GetTtsTargetService();
                string currentVoice = ConfigManager.Instance.GetTtsTargetVoice();
                bool useCustom = ConfigManager.Instance.GetTtsTargetUseCustomVoiceId();
                string customVoiceId = ConfigManager.Instance.GetTtsTargetCustomVoiceId();
                
                var dialog = new TtsVoiceSelectorDialog(currentService, currentVoice, useCustom, customVoiceId);
                dialog.Owner = this; // Make it modal to SettingsWindow
                if (dialog.ShowDialog() == true)
                {
                    // Check if voice actually changed
                    bool voiceChanged = dialog.SelectedService != currentService || 
                                       dialog.SelectedVoice != currentVoice ||
                                       dialog.UseCustomVoiceId != useCustom ||
                                       (dialog.UseCustomVoiceId && dialog.CustomVoiceId != customVoiceId);
                    
                    ConfigManager.Instance.SetTtsTargetService(dialog.SelectedService);
                    ConfigManager.Instance.SetTtsTargetVoice(dialog.SelectedVoice);
                    ConfigManager.Instance.SetTtsTargetUseCustomVoiceId(dialog.UseCustomVoiceId);
                    ConfigManager.Instance.SetTtsTargetCustomVoiceId(dialog.CustomVoiceId ?? "");
                    Console.WriteLine($"Target TTS set to: {dialog.SelectedService} / {dialog.SelectedVoice} (Custom: {dialog.UseCustomVoiceId})");
                    
                    updatePageReadingTtsButtonLabels();
                    adjustConcurrencyForLocalServices();
                    
                    // Clear audio cache and retrigger OCR if voice changed
                    if (voiceChanged)
                    {
                        AudioPreloadService.Instance.ClearAudioCache();
                        Console.WriteLine("Audio cache cleared due to target TTS voice change");
                        
                        // Retrigger OCR to regenerate audio with new voice
                        Logic.Instance.ResetHash();
                        Logic.Instance.ClearAllTextObjects();
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                        Console.WriteLine("OCR retriggered due to target TTS voice change");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting target TTS: {ex.Message}");
                MessageBox.Show($"Error setting target TTS: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void updatePageReadingTtsButtonLabels()
        {
            if (setSourceTtsButton != null)
            {
                setSourceTtsButton.Content = $"Source TTS: {ConfigManager.Instance.GetTtsSourceService()} (click to change)";
            }
            if (setTargetTtsButton != null)
            {
                setTargetTtsButton.Content = $"Target TTS: {ConfigManager.Instance.GetTtsTargetService()} (click to change)";
            }
        }
        
        private void adjustConcurrencyForLocalServices()
        {
            string sourceService = ConfigManager.Instance.GetTtsSourceService();
            string targetService = ConfigManager.Instance.GetTtsTargetService();
            
            bool anyLocal = TtsServiceFactory.IsLocalService(sourceService) || 
                           TtsServiceFactory.IsLocalService(targetService);
            
            if (anyLocal && ttsMaxConcurrentDownloadsTextBox != null)
            {
                int currentMax = ConfigManager.Instance.GetTtsMaxConcurrentDownloads();
                if (currentMax > 1 || currentMax == 0)
                {
                    ConfigManager.Instance.SetTtsMaxConcurrentDownloads(1);
                    _lastMaxConcurrentDownloadsValue = 1;
                    ttsMaxConcurrentDownloadsTextBox.Text = "1";
                    AudioPreloadService.Instance.UpdateConcurrencyLimit();
                    Console.WriteLine("Auto-set max concurrent downloads to 1 (local TTS service selected)");
                }
            }
        }
        
        private void ElevenLabsApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                string apiKey = elevenLabsApiKeyPasswordBox.Password.Trim();
                ConfigManager.Instance.SetElevenLabsApiKey(apiKey);
                Console.WriteLine("ElevenLabs API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs API key: {ex.Message}");
            }
        }
        
        private void ElevenLabsVoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (elevenLabsVoiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string voiceId = selectedItem.Tag?.ToString() ?? "21m00Tcm4TlvDq8ikWAM"; // Default to Rachel
                    ConfigManager.Instance.SetElevenLabsVoice(voiceId);
                    Console.WriteLine($"ElevenLabs voice set to: {selectedItem.Content} (ID: {voiceId})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs voice: {ex.Message}");
            }
        }

        private void ElevenLabsCustomVoiceCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                bool useCustom = elevenLabsCustomVoiceCheckBox.IsChecked == true;
                ConfigManager.Instance.SetElevenLabsUseCustomVoiceId(useCustom);

                // Enable/disable related controls
                if (elevenLabsCustomVoiceIdTextBox != null)
                {
                    elevenLabsCustomVoiceIdTextBox.IsEnabled = useCustom;
                }
                elevenLabsVoiceComboBox.IsEnabled = !useCustom;
                elevenLabsVoiceLabel.IsEnabled = !useCustom;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs custom voice toggle: {ex.Message}");
            }
        }

        private void ElevenLabsCustomVoiceIdTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;

                string customId = elevenLabsCustomVoiceIdTextBox.Text?.Trim() ?? "";
                ConfigManager.Instance.SetElevenLabsCustomVoiceId(customId);
                Console.WriteLine("ElevenLabs custom voice ID updated from UI");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ElevenLabs custom voice ID: {ex.Message}");
            }
        }
    }
}
