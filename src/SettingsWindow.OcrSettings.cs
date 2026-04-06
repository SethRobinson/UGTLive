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
        // OCR settings
        private void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            Console.WriteLine($"SettingsWindow.OcrMethodComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
            if (_isInitializing)
            {
                Console.WriteLine("Skipping OCR method change during initialization");
                return;
            }
            
            if (sender is ComboBox comboBox)
            {
                // Get internal ID from Tag property
                string? ocrMethod = (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                
                if (!string.IsNullOrEmpty(ocrMethod))
                {
                    Console.WriteLine($"SettingsWindow OCR method changed to: '{ocrMethod}'");
                    
                    // Update MonitorWindow OCR method
                    if (MonitorWindow.Instance.ocrMethodComboBox != null)
                    {
                        // Find and select the matching item by Tag (internal ID)
                        foreach (ComboBoxItem item in MonitorWindow.Instance.ocrMethodComboBox.Items)
                        {
                            if (string.Equals(item.Tag?.ToString(), ocrMethod, StringComparison.OrdinalIgnoreCase))
                            {
                                MonitorWindow.Instance.ocrMethodComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                    
                    // Set OCR method in MainWindow
                    MainWindow.Instance.SetOcrMethod(ocrMethod);
                    
                    // Only save to config if not during initialization
                    if (!_isInitializing)
                    {
                        Console.WriteLine($"SettingsWindow: Saving OCR method '{ocrMethod}'");
                        ConfigManager.Instance.SetOcrMethod(ocrMethod);
                    }
                    else
                    {
                        Console.WriteLine($"SettingsWindow: Skipping save during initialization for OCR method '{ocrMethod}'");
                    }
                    
                    // Update OCR-specific settings visibility
                    UpdateOcrSpecificSettings(ocrMethod);
                    
                    // Delete old OCR reply file to prevent users from seeing stale data from a different OCR method
                    DeleteLastOcrReplyFile();
                    
                    // Reset the OCR hash to force a fresh comparison after changing OCR method
                    Logic.Instance.ResetHash();
                    
                    // Clear any existing text objects
                    Logic.Instance.ClearAllTextObjects();
                    
                    // Force OCR to run again
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    
                    // Refresh overlays
                    MonitorWindow.Instance.RefreshOverlays();
                }
            }
        }
        
        private void MangaOcrMinWidthTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(mangaOcrMinWidthTextBox.Text, out int width) && width >= 0)
                {
                    ConfigManager.Instance.SetMangaOcrMinRegionWidth(width);
                    Console.WriteLine($"Manga OCR minimum region width set to: {width}");
                    
                    // Force refresh to apply immediately
                    Logic.Instance.ResetHash();
                    Logic.Instance.ClearAllTextObjects();
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    MonitorWindow.Instance.RefreshOverlays();
                }
                else
                {
                    // Reset to current config value if invalid
                    mangaOcrMinWidthTextBox.Text = ConfigManager.Instance.GetMangaOcrMinRegionWidth().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Manga OCR minimum region width: {ex.Message}");
            }
        }
        
        private void MangaOcrMinHeightTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(mangaOcrMinHeightTextBox.Text, out int height) && height >= 0)
                {
                    ConfigManager.Instance.SetMangaOcrMinRegionHeight(height);
                    Console.WriteLine($"Manga OCR minimum region height set to: {height}");
                    
                    // Force refresh to apply immediately
                    Logic.Instance.ResetHash();
                    Logic.Instance.ClearAllTextObjects();
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    MonitorWindow.Instance.RefreshOverlays();
                }
                else
                {
                    // Reset to current config value if invalid
                    mangaOcrMinHeightTextBox.Text = ConfigManager.Instance.GetMangaOcrMinRegionHeight().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Manga OCR minimum region height: {ex.Message}");
            }
        }
        
        private void MangaOcrOverlapTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (double.TryParse(mangaOcrOverlapTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) && percent >= 0 && percent <= 100)
                {
                    ConfigManager.Instance.SetMangaOcrOverlapAllowedPercent(percent);
                    Console.WriteLine($"Manga OCR overlap allowed percent set to: {percent:F1}%");
                    
                    // Force refresh to apply immediately
                    Logic.Instance.ResetHash();
                    Logic.Instance.ClearAllTextObjects();
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    MonitorWindow.Instance.RefreshOverlays();
                }
                else
                {
                    // Reset to current config value if invalid
                    mangaOcrOverlapTextBox.Text = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent().ToString("F1", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Manga OCR overlap allowed percent: {ex.Message}");
            }
        }
        
        private void MangaOcrYoloConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (double.TryParse(mangaOcrYoloConfidenceTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) && confidence >= 0.0 && confidence <= 1.0)
                {
                    ConfigManager.Instance.SetMangaOcrYoloConfidence(confidence);
                    Console.WriteLine($"Manga OCR YOLO confidence threshold set to: {confidence:F2}");
                    
                    // Force refresh to apply immediately
                    Logic.Instance.ResetHash();
                    Logic.Instance.ClearAllTextObjects();
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    MonitorWindow.Instance.RefreshOverlays();
                }
                else
                {
                    // Reset to current config value if invalid
                    mangaOcrYoloConfidenceTextBox.Text = ConfigManager.Instance.GetMangaOcrYoloConfidence().ToString("F2", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Manga OCR YOLO confidence: {ex.Message}");
            }
        }

        // Paddle OCR Angle Classification
        private void PaddleOcrAngleClsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            bool enabled = paddleOcrAngleClsCheckBox.IsChecked == true;
            ConfigManager.Instance.SetPaddleOcrUseAngleCls(enabled);
            
            // Reset OCR
            Logic.Instance.ResetHash();
            MainWindow.Instance.SetOCRCheckIsWanted(true);
        }
        
        // Update OCR-specific settings visibility
        private void UpdateOcrSpecificSettings(string selectedOcr)
        {
            try
            {
                bool isGoogleVisionSelected = string.Equals(selectedOcr, "Google Vision", StringComparison.OrdinalIgnoreCase);
                bool isMangaOcrSelected = string.Equals(selectedOcr, "MangaOCR", StringComparison.OrdinalIgnoreCase);
                bool isPaddleOcrSelected = string.Equals(selectedOcr, "PaddleOCR", StringComparison.OrdinalIgnoreCase);
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"UpdateOcrSpecificSettings: Selected='{selectedOcr}', IsPaddle={isPaddleOcrSelected}");
                }
                
                bool isEasyOcrSelected = string.Equals(selectedOcr, "EasyOCR", StringComparison.OrdinalIgnoreCase);
                bool isDocTrSelected = string.Equals(selectedOcr, "docTR", StringComparison.OrdinalIgnoreCase);

                // Confidence settings are only useful for EasyOCR, docTR, and Google Vision
                bool showConfidenceSettings = isEasyOcrSelected || isDocTrSelected || isGoogleVisionSelected || isPaddleOcrSelected;
                
                // EasyOCR, PaddleOCR and docTR don't use character-level confidence (or at least we don't use it), so hide that specific setting
                // Google Vision DOES support character-level confidence (word-level)
                bool showLetterConfidence = showConfidenceSettings && !isEasyOcrSelected && !isDocTrSelected && !isPaddleOcrSelected;
                
                // Only EasyOCR and PaddleOCR use line-level confidence
                // Google Vision uses letter/word confidence, docTR uses letter confidence
                bool showLineConfidence = isEasyOcrSelected || isPaddleOcrSelected;

                if (minLetterConfidenceLabel != null)
                    minLetterConfidenceLabel.Visibility = showLetterConfidence ? Visibility.Visible : Visibility.Collapsed;
                if (minLetterConfidenceTextBox != null)
                {
                    minLetterConfidenceTextBox.Visibility = showLetterConfidence ? Visibility.Visible : Visibility.Collapsed;
                    if (showLetterConfidence)
                    {
                        minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence(selectedOcr).ToString();
                    }
                }

                if (minLineConfidenceLabel != null)
                    minLineConfidenceLabel.Visibility = showLineConfidence ? Visibility.Visible : Visibility.Collapsed;
                if (minLineConfidenceTextBox != null)
                {
                    minLineConfidenceTextBox.Visibility = showLineConfidence ? Visibility.Visible : Visibility.Collapsed;
                    if (showLineConfidence)
                    {
                        minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence(selectedOcr).ToString();
                    }
                }

                // Glue settings are available for all OCRs EXCEPT MangaOCR (which has its own logic/model)
                bool shouldShowGlueSettings = !isMangaOcrSelected;
                
                // Show/hide PaddleOCR settings
                if (paddleOcrAngleClsLabel != null)
                    paddleOcrAngleClsLabel.Visibility = isPaddleOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (paddleOcrAngleClsCheckBox != null)
                    paddleOcrAngleClsCheckBox.Visibility = isPaddleOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                
                if (isPaddleOcrSelected && paddleOcrAngleClsCheckBox != null)
                {
                    paddleOcrAngleClsCheckBox.IsChecked = ConfigManager.Instance.GetPaddleOcrUseAngleCls();
                }

                // Color Correction is only for Windows OCR, Google Cloud Vision, and PaddleOCR
                bool isWindowsOcrSelected = string.Equals(selectedOcr, "Windows OCR", StringComparison.OrdinalIgnoreCase);
                bool showColorCorrection = isGoogleVisionSelected || isWindowsOcrSelected || isPaddleOcrSelected;
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Color Correction Visibility: {showColorCorrection} (Paddle={isPaddleOcrSelected}, Windows={isWindowsOcrSelected}, Google={isGoogleVisionSelected})");
                }
                
                if (cloudOcrColorCorrectionLabel != null)
                    cloudOcrColorCorrectionLabel.Visibility = showColorCorrection ? Visibility.Visible : Visibility.Collapsed;
                if (cloudOcrColorCorrectionCheckBox != null)
                    cloudOcrColorCorrectionCheckBox.Visibility = showColorCorrection ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Manga OCR-specific settings
                if (mangaOcrMinWidthLabel != null)
                    mangaOcrMinWidthLabel.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrMinWidthTextBox != null)
                    mangaOcrMinWidthTextBox.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrMinHeightLabel != null)
                    mangaOcrMinHeightLabel.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrMinHeightTextBox != null)
                    mangaOcrMinHeightTextBox.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrOverlapLabel != null)
                    mangaOcrOverlapLabel.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrOverlapTextBox != null)
                    mangaOcrOverlapTextBox.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrYoloConfidenceLabel != null)
                    mangaOcrYoloConfidenceLabel.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (mangaOcrYoloConfidenceTextBox != null)
                    mangaOcrYoloConfidenceTextBox.Visibility = isMangaOcrSelected ? Visibility.Visible : Visibility.Collapsed;

                // Show/hide Google Vision-specific settings
                if (googleVisionApiKeyLabel != null)
                    googleVisionApiKeyLabel.Visibility = isGoogleVisionSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleVisionApiKeyGrid != null)
                    googleVisionApiKeyGrid.Visibility = isGoogleVisionSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Text Grouping settings (Universal)
                Visibility glueVisibility = shouldShowGlueSettings ? Visibility.Visible : Visibility.Collapsed;

                if (googleVisionGroupingLabel != null)
                    googleVisionGroupingLabel.Visibility = glueVisibility;
                if (googleVisionHeightSimilarityLabel != null)
                    googleVisionHeightSimilarityLabel.Visibility = glueVisibility;
                if (googleVisionHeightSimilarityGrid != null)
                    googleVisionHeightSimilarityGrid.Visibility = glueVisibility;
                if (googleVisionHorizontalGlueLabel != null)
                    googleVisionHorizontalGlueLabel.Visibility = glueVisibility;
                if (googleVisionHorizontalGlueGrid != null)
                    googleVisionHorizontalGlueGrid.Visibility = glueVisibility;
                if (googleVisionVerticalGlueLabel != null)
                    googleVisionVerticalGlueLabel.Visibility = glueVisibility;
                if (googleVisionVerticalGlueGrid != null)
                    googleVisionVerticalGlueGrid.Visibility = glueVisibility;
                if (googleVisionVerticalGlueOverlapLabel != null)
                    googleVisionVerticalGlueOverlapLabel.Visibility = glueVisibility;
                if (googleVisionVerticalGlueOverlapGrid != null)
                    googleVisionVerticalGlueOverlapGrid.Visibility = glueVisibility;
                if (googleVisionKeepLinefeedsLabel != null)
                    googleVisionKeepLinefeedsLabel.Visibility = glueVisibility;
                if (googleVisionKeepLinefeedsCheckBox != null)
googleVisionKeepLinefeedsCheckBox.Visibility = glueVisibility;

                // Load Text Grouping settings if shown
                if (shouldShowGlueSettings)
                {
                    if (googleVisionHeightSimilarityTextBox != null)
                    {
                        googleVisionHeightSimilarityTextBox.Text = ConfigManager.Instance.GetHeightSimilarity(selectedOcr).ToString("F1", CultureInfo.InvariantCulture);
                    }
                    
                    if (googleVisionHorizontalGlueTextBox != null)
                    {
                        googleVisionHorizontalGlueTextBox.Text = ConfigManager.Instance.GetHorizontalGlue(selectedOcr).ToString("F2", CultureInfo.InvariantCulture);
                    }
                    
                    if (googleVisionVerticalGlueTextBox != null)
                    {
                        googleVisionVerticalGlueTextBox.Text = ConfigManager.Instance.GetVerticalGlue(selectedOcr).ToString("F2", CultureInfo.InvariantCulture);
                    }
                    
                    if (googleVisionVerticalGlueOverlapTextBox != null)
                    {
                        googleVisionVerticalGlueOverlapTextBox.Text = ConfigManager.Instance.GetVerticalGlueOverlap(selectedOcr).ToString("F1", CultureInfo.InvariantCulture);
                    }
                    
                    if (googleVisionKeepLinefeedsCheckBox != null)
                    {
                        googleVisionKeepLinefeedsCheckBox.IsChecked = ConfigManager.Instance.GetKeepLinefeeds(selectedOcr);
                    }
                }
                
                // Load leave translation onscreen setting for this OCR method
                leaveTranslationOnscreenCheckBox.IsChecked = ConfigManager.Instance.GetLeaveTranslationOnscreen(selectedOcr);

                // Load Google Vision API Key only if GV
                if (isGoogleVisionSelected)
                {
                    if (googleVisionApiKeyPasswordBox != null)
                    {
                        googleVisionApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleVisionApiKey();
                    }
                }

                // Load Manga OCR settings if it's being shown
                if (isMangaOcrSelected)
                {
                    if (mangaOcrMinWidthTextBox != null)
                    {
                        mangaOcrMinWidthTextBox.Text = ConfigManager.Instance.GetMangaOcrMinRegionWidth().ToString();
                    }
                    
                    if (mangaOcrMinHeightTextBox != null)
                    {
                        mangaOcrMinHeightTextBox.Text = ConfigManager.Instance.GetMangaOcrMinRegionHeight().ToString();
                    }
                    
                    if (mangaOcrOverlapTextBox != null)
                    {
                        mangaOcrOverlapTextBox.Text = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent().ToString("F1", CultureInfo.InvariantCulture);
                    }
                    
                    if (mangaOcrYoloConfidenceTextBox != null)
                    {
                        mangaOcrYoloConfidenceTextBox.Text = ConfigManager.Instance.GetMangaOcrYoloConfidence().ToString("F2", CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating OCR-specific settings visibility: {ex.Message}");
            }
        }

        // Google Vision API Key password changed
        private void GoogleVisionApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            string apiKey = googleVisionApiKeyPasswordBox.Password;
            ConfigManager.Instance.SetGoogleVisionApiKey(apiKey);
            Console.WriteLine("Google Vision API key updated");
        }

        // Google Vision API Key help button click
        private void GoogleVisionApiKeyHelpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new GoogleVisionSetupDialog();
                dialog.Owner = this;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open setup guide: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Google Vision API link click
        private void GoogleVisionApiLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://cloud.google.com/vision",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open link: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Google Vision Test API Key button click
        private async void GoogleVisionTestApiKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button and show progress
                googleVisionTestApiKeyButton.IsEnabled = false;
                googleVisionTestProgressBar.Visibility = Visibility.Visible;
                googleVisionTestResultText.Text = "Testing...";
                googleVisionTestResultText.Foreground = new SolidColorBrush(Colors.Gray);

                // Test the API key
                var (success, message) = await GoogleVisionOCRService.Instance.TestApiKeyAsync();

                // Update UI with result
                googleVisionTestResultText.Text = message;
                googleVisionTestResultText.Foreground = success 
                    ? new SolidColorBrush(Colors.Green) 
                    : new SolidColorBrush(Colors.Red);

                if (!success)
                {
                    // If the message contains specific error types, provide additional help
                    if (message.Contains("API key not configured"))
                    {
                        googleVisionTestResultText.Text = "Please enter your API key first";
                    }
                    else if (message.Contains("403") || message.Contains("permission", StringComparison.OrdinalIgnoreCase))
                    {
                        googleVisionTestResultText.Text = "API key invalid or Vision API not enabled. Click 'How to get API key' for help.";
                    }
                    else if (message.Contains("quota", StringComparison.OrdinalIgnoreCase))
                    {
                        googleVisionTestResultText.Text = "Quota exceeded. Check your Google Cloud Console for usage limits.";
                    }
                }
            }
            catch (Exception ex)
            {
                googleVisionTestResultText.Text = $"Test failed: {ex.Message}";
                googleVisionTestResultText.Foreground = new SolidColorBrush(Colors.Red);
            }
            finally
            {
                // Re-enable button and hide progress
                googleVisionTestApiKeyButton.IsEnabled = true;
                googleVisionTestProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // Google Vision Horizontal Glue text changed
        private void GoogleVisionHorizontalGlueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            if (double.TryParse(googleVisionHorizontalGlueTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                // Clamp to range (-2000 to 2000)
                value = Math.Max(-2000.0, Math.Min(2000.0, value));
                googleVisionHorizontalGlueTextBox.Text = value.ToString("F2", CultureInfo.InvariantCulture);
                
                ConfigManager.Instance.SetHorizontalGlue(currentOcr, value);
                Console.WriteLine($"{currentOcr} horizontal glue set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionHorizontalGlueTextBox.Text = ConfigManager.Instance.GetHorizontalGlue(currentOcr).ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        // Google Vision Vertical Glue text changed
        private void GoogleVisionVerticalGlueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            if (double.TryParse(googleVisionVerticalGlueTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                // Clamp to range (-2000 to 2000)
                value = Math.Max(-2000.0, Math.Min(2000.0, value));
                googleVisionVerticalGlueTextBox.Text = value.ToString("F2", CultureInfo.InvariantCulture);
                
                ConfigManager.Instance.SetVerticalGlue(currentOcr, value);
                Console.WriteLine($"{currentOcr} vertical glue set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionVerticalGlueTextBox.Text = ConfigManager.Instance.GetVerticalGlue(currentOcr).ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        // Google Vision Vertical Glue Overlap text changed
        private void GoogleVisionVerticalGlueOverlapTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            if (double.TryParse(googleVisionVerticalGlueOverlapTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                // Clamp to range (0 to 100)
                value = Math.Max(0, Math.Min(100.0, value));
                googleVisionVerticalGlueOverlapTextBox.Text = value.ToString("F1", CultureInfo.InvariantCulture);
                
                ConfigManager.Instance.SetVerticalGlueOverlap(currentOcr, value);
                Console.WriteLine($"{currentOcr} vertical glue overlap set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionVerticalGlueOverlapTextBox.Text = ConfigManager.Instance.GetVerticalGlueOverlap(currentOcr).ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        // Height Similarity text changed
        private void GoogleVisionHeightSimilarityTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            if (double.TryParse(googleVisionHeightSimilarityTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                // Clamp to range (0 to 100)
                value = Math.Max(0, Math.Min(100.0, value));
                googleVisionHeightSimilarityTextBox.Text = value.ToString("F1", CultureInfo.InvariantCulture);
                
                ConfigManager.Instance.SetHeightSimilarity(currentOcr, value);
                Console.WriteLine($"{currentOcr} height similarity set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionHeightSimilarityTextBox.Text = ConfigManager.Instance.GetHeightSimilarity(currentOcr).ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        private void GoogleVisionKeepLinefeedsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            bool isChecked = googleVisionKeepLinefeedsCheckBox.IsChecked ?? true;
            ConfigManager.Instance.SetKeepLinefeeds(currentOcr, isChecked);
            Console.WriteLine($"{currentOcr} keep linefeeds set to {isChecked}");
            
            // Force refresh
            Logic.Instance.ResetHash();
            MainWindow.Instance.SetOCRCheckIsWanted(true);
        }
    }
}
