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
        private void OverrideBgColorButton_Click(object sender, RoutedEventArgs e)
        {
            // Create color dialog
            var colorDialog = new ColorDialog();
            
            // Get current color from config
            Color currentColor = ConfigManager.Instance.GetMonitorOverrideBgColor();
            
            // Set the initial color (ignore alpha, we handle that separately)
            colorDialog.Color = System.Drawing.Color.FromArgb(
                255, 
                currentColor.R, 
                currentColor.G, 
                currentColor.B);
            
            // Show dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Get selected color (fully opaque)
                Color selectedColor = Color.FromArgb(
                    255, 
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);
                
                // Save to config
                ConfigManager.Instance.SetMonitorOverrideBgColor(selectedColor);
                
                // Update UI
                overrideBgColorButton.Background = new SolidColorBrush(selectedColor);
                overrideBgColorText.Text = ColorToHexString(selectedColor);
                
                // Refresh overlays if override is enabled
                if (overrideBgColorCheckBox.IsChecked == true)
                {
                    MonitorWindow.Instance.RefreshOverlays();
                }
                
                // Trigger OCR refresh
                Logic.Instance.ResetHash();
            }
        }

        private void OverrideFontColorButton_Click(object sender, RoutedEventArgs e)
        {
            // Create color dialog
            var colorDialog = new ColorDialog();
            
            // Get current color from config
            Color currentColor = ConfigManager.Instance.GetMonitorOverrideFontColor();
            
            // Set the initial color
            colorDialog.Color = System.Drawing.Color.FromArgb(
                255, 
                currentColor.R, 
                currentColor.G, 
                currentColor.B);
            
            // Show dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Get selected color (fully opaque)
                Color selectedColor = Color.FromArgb(
                    255, 
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);
                
                // Save to config
                ConfigManager.Instance.SetMonitorOverrideFontColor(selectedColor);
                
                // Update UI
                overrideFontColorButton.Background = new SolidColorBrush(selectedColor);
                overrideFontColorText.Text = ColorToHexString(selectedColor);
                
                // Refresh overlays if override is enabled
                if (overrideFontColorCheckBox.IsChecked == true)
                {
                    MonitorWindow.Instance.RefreshOverlays();
                }
                
                // Trigger OCR refresh
                Logic.Instance.ResetHash();
            }
        }

        private void BgOpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Handle click-to-position on slider track
            Slider slider = sender as Slider;
            if (slider == null || slider.ActualWidth == 0) return;
            
            // Get mouse position relative to the slider
            System.Windows.Point position = e.GetPosition(slider);
            
            // Calculate where the thumb currently is
            double currentPercentage = (slider.Value - slider.Minimum) / (slider.Maximum - slider.Minimum);
            double thumbPosition = currentPercentage * slider.ActualWidth;
            
            // Approximate thumb width (WPF default is around 11-15 pixels)
            double thumbWidth = 18;
            double thumbLeft = thumbPosition - thumbWidth / 2;
            double thumbRight = thumbPosition + thumbWidth / 2;
            
            // If click is on the thumb, let default behavior handle dragging
            if (position.X >= thumbLeft && position.X <= thumbRight)
            {
                return; // Don't handle, allow thumb dragging
            }
            
            // Click is on the track, calculate new value based on click position
            double percentage = position.X / slider.ActualWidth;
            double value = slider.Minimum + (percentage * (slider.Maximum - slider.Minimum));
            
            // Clamp to valid range
            value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            
            // Set the value (IsSnapToTickEnabled will snap it to nearest tick)
            slider.Value = value;
            
            // Mark event as handled to prevent default toggle behavior
            e.Handled = true;
        }

        private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
            
            double opacity = bgOpacitySlider.Value;
            ConfigManager.Instance.SetMonitorBgOpacity(opacity);
            bgOpacityText.Text = $"{(int)(opacity * 100)}%";
            Console.WriteLine($"Monitor background opacity set to: {opacity:F2}");
            
            // Force clear HTML cache so overlays regenerate with new opacity
            MonitorWindow.Instance.ClearOverlayCache();
            MainWindow.Instance.ClearMainWindowOverlayCache();
            
            // Refresh overlays to apply changes immediately
            MonitorWindow.Instance.RefreshOverlays();
            MainWindow.Instance.RefreshMainWindowOverlays();
            
            // Trigger OCR refresh
            Logic.Instance.ResetHash();
        }

        private string ColorToHexString(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        // Context settings handlers
        private void MaxContextPiecesTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(maxContextPiecesTextBox.Text, out int maxContextPieces) && maxContextPieces >= 0)
                {
                    ConfigManager.Instance.SetMaxContextPieces(maxContextPieces);
                    Console.WriteLine($"Max context pieces set to: {maxContextPieces}");
                }
                else
                {
                    // Reset to current value from config if invalid
                    maxContextPiecesTextBox.Text = ConfigManager.Instance.GetMaxContextPieces().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating max context pieces: {ex.Message}");
            }
        }
        
        private void MinContextSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(minContextSizeTextBox.Text, out int minContextSize) && minContextSize >= 0)
                {
                    ConfigManager.Instance.SetMinContextSize(minContextSize);
                    Console.WriteLine($"Min context size set to: {minContextSize}");
                }
                else
                {
                    // Reset to current value from config if invalid
                    minContextSizeTextBox.Text = ConfigManager.Instance.GetMinContextSize().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating min context size: {ex.Message}");
            }
        }

        private void MaxTranslationRetriesTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            applyMaxTranslationRetries();
        }

        private void MaxTranslationRetriesTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                applyMaxTranslationRetries();
        }

        private void applyMaxTranslationRetries()
        {
            try
            {
                if (_isInitializing)
                    return;

                if (int.TryParse(maxTranslationRetriesTextBox.Text, out int retries) && retries >= 0)
                {
                    ConfigManager.Instance.SetMaxTranslationRetries(retries);
                    Console.WriteLine($"Max translation retries set to: {retries}");
                }
                else
                {
                    maxTranslationRetriesTextBox.Text = ConfigManager.Instance.GetMaxTranslationRetries().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating max translation retries: {ex.Message}");
            }
        }
        
        private void MinChatBoxTextSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(minChatBoxTextSizeTextBox.Text, out int minChatBoxTextSize) && minChatBoxTextSize >= 0)
                {
                    ConfigManager.Instance.SetChatBoxMinTextSize(minChatBoxTextSize);
                    Console.WriteLine($"Min ChatBox text size set to: {minChatBoxTextSize}");
                }
                else
                {
                    // Reset to current value from config if invalid
                    minChatBoxTextSizeTextBox.Text = ConfigManager.Instance.GetChatBoxMinTextSize().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating min ChatBox text size: {ex.Message}");
            }
        }
        
        private void GameInfoTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                string gameInfo = gameInfoTextBox.Text.Trim();
                ConfigManager.Instance.SetGameInfo(gameInfo);
                Console.WriteLine($"Game info updated: {gameInfo}");
                
                // Reset the hash to force a retranslation when game info changes
                Logic.Instance.ResetHash();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating game info: {ex.Message}");
            }
        }
        
        private void MinTextFragmentSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(minTextFragmentSizeTextBox.Text, out int minSize) && minSize >= 0)
                {
                    ConfigManager.Instance.SetMinTextFragmentSize(minSize);
                    Console.WriteLine($"Minimum text fragment size set to: {minSize}");
                    
                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minTextFragmentSizeTextBox.Text = ConfigManager.Instance.GetMinTextFragmentSize().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum text fragment size: {ex.Message}");
            }
        }
        
        private void MinLetterConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (double.TryParse(minLetterConfidenceTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    string currentOcr = ConfigManager.Instance.GetOcrMethod();
                    ConfigManager.Instance.SetMinLetterConfidence(currentOcr, confidence);
                    
                    Console.WriteLine($"Minimum letter confidence for {currentOcr} set to: {confidence}");
                    
                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    string currentOcr = ConfigManager.Instance.GetOcrMethod();
                    minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence(currentOcr).ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum letter confidence: {ex.Message}");
            }
        }
        
        private void MinLineConfidenceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (double.TryParse(minLineConfidenceTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    string currentOcr = ConfigManager.Instance.GetOcrMethod();
                    ConfigManager.Instance.SetMinLineConfidence(currentOcr, confidence);
                    
                    Console.WriteLine($"Minimum line confidence for {currentOcr} set to: {confidence}");
                    
                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    string currentOcr = ConfigManager.Instance.GetOcrMethod();
                    minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence(currentOcr).ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum line confidence: {ex.Message}");
            }
        }
        
        private void TextAreaExpansionWidthTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(textAreaExpansionWidthTextBox.Text, out int width) && width >= 0)
                {
                    ConfigManager.Instance.SetMonitorTextAreaExpansionWidth(width);
                    Console.WriteLine($"Text area expansion width set to: {width}");
                    
                    // Trigger OCR refresh
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    textAreaExpansionWidthTextBox.Text = ConfigManager.Instance.GetMonitorTextAreaExpansionWidth().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text area expansion width: {ex.Message}");
            }
        }
        
        private void TextAreaExpansionHeightTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(textAreaExpansionHeightTextBox.Text, out int height) && height >= 0)
                {
                    ConfigManager.Instance.SetMonitorTextAreaExpansionHeight(height);
                    Console.WriteLine($"Text area expansion height set to: {height}");
                    
                    // Trigger OCR refresh
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    textAreaExpansionHeightTextBox.Text = ConfigManager.Instance.GetMonitorTextAreaExpansionHeight().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text area expansion height: {ex.Message}");
            }
        }
        
        private void TextOverlayBorderRadiusTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (int.TryParse(textOverlayBorderRadiusTextBox.Text, out int radius) && radius >= 0)
                {
                    ConfigManager.Instance.SetMonitorTextOverlayBorderRadius(radius);
                    Console.WriteLine($"Text overlay border radius set to: {radius}");
                    
                    // Refresh overlays to apply the new border radius
                    MonitorWindow.Instance.RefreshOverlays();
                    MainWindow.Instance.RefreshMainWindowOverlays();
                }
                else
                {
                    // Reset to current value from config if invalid
                    textOverlayBorderRadiusTextBox.Text = ConfigManager.Instance.GetMonitorTextOverlayBorderRadius().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text overlay border radius: {ex.Message}");
            }
        }
        
        private void TextOverlayHAlignComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (textOverlayHAlignComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ConfigManager.Instance.SetTextOverlayHorizontalAlignment(tag);
                MonitorWindow.Instance?.RefreshOverlays();
                MainWindow.Instance?.RefreshMainWindowOverlays();
            }
        }
        
        private void TextOverlayVAlignComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (textOverlayVAlignComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ConfigManager.Instance.SetTextOverlayVerticalAlignment(tag);
                MonitorWindow.Instance?.RefreshOverlays();
                MainWindow.Instance?.RefreshMainWindowOverlays();
            }
        }
        
        // Ignore Phrases methods
        
        // Load ignore phrases from ConfigManager
        private void LoadIgnorePhrases()
        {
            try
            {
                _ignorePhrases.Clear();
                
                // Get phrases from ConfigManager
                var phrases = ConfigManager.Instance.GetIgnorePhrases();
                
                // Add each phrase to the collection
                foreach (var (phrase, exactMatch) in phrases)
                {
                    if (!string.IsNullOrEmpty(phrase))
                    {
                        _ignorePhrases.Add(new IgnorePhrase(phrase, exactMatch));
                    }
                }
                
                // Set the ListView's ItemsSource
                ignorePhraseListView.ItemsSource = _ignorePhrases;
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Loaded {_ignorePhrases.Count} ignore phrases");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ignore phrases: {ex.Message}");
            }
        }
        
        // Save all ignore phrases to ConfigManager
        private void SaveIgnorePhrases()
        {
            try
            {
                if (_isInitializing)
                    return;
                    
                // Convert collection to list of tuples
                var phrases = _ignorePhrases.Select(p => (p.Phrase, p.ExactMatch)).ToList();
                
                // Save to ConfigManager
                ConfigManager.Instance.SaveIgnorePhrases(phrases);
                
                // Force the Logic to refresh
                Logic.Instance.ResetHash();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ignore phrases: {ex.Message}");
            }
        }
        
        // Add a new ignore phrase
        private void AddIgnorePhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string phrase = newIgnorePhraseTextBox.Text.Trim();
                
                if (string.IsNullOrEmpty(phrase))
                {
                    MessageBox.Show("Please enter a phrase to ignore.", 
                        "Missing Phrase", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Check if the phrase already exists
                if (_ignorePhrases.Any(p => p.Phrase == phrase))
                {
                    MessageBox.Show($"The phrase '{phrase}' is already in the list.", 
                        "Duplicate Phrase", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                bool exactMatch = newExactMatchCheckBox.IsChecked ?? true;
                
                // Add to the collection
                _ignorePhrases.Add(new IgnorePhrase(phrase, exactMatch));
                
                // Save to ConfigManager
                SaveIgnorePhrases();
                
                // Clear the input
                newIgnorePhraseTextBox.Text = "";
                
                Console.WriteLine($"Added ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding ignore phrase: {ex.Message}");
                MessageBox.Show($"Error adding phrase: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Remove a selected ignore phrase
        private void RemoveIgnorePhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ignorePhraseListView.SelectedItem is IgnorePhrase selectedPhrase)
                {
                    string phrase = selectedPhrase.Phrase;
                    
                    // Ask for confirmation
                    MessageBoxResult result = MessageBox.Show($"Are you sure you want to remove the phrase '{phrase}'?", 
                        "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        
                    if (result == MessageBoxResult.Yes)
                    {
                        // Remove from the collection
                        _ignorePhrases.Remove(selectedPhrase);
                        
                        // Save to ConfigManager
                        SaveIgnorePhrases();
                        
                        Console.WriteLine($"Removed ignore phrase: '{phrase}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing ignore phrase: {ex.Message}");
                MessageBox.Show($"Error removing phrase: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Handle selection changed event
        private void IgnorePhraseListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Enable or disable the Remove button based on selection
            removeIgnorePhraseButton.IsEnabled = ignorePhraseListView.SelectedItem != null;
        }
        
        // Handle checkbox changed event
        private void IgnorePhrase_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitializing)
                    return;
                    
                if (sender is System.Windows.Controls.CheckBox checkbox && checkbox.Tag is string phrase)
                {
                    bool exactMatch = checkbox.IsChecked ?? false;
                    
                    // Find and update the phrase in the collection
                    foreach (var ignorePhrase in _ignorePhrases)
                    {
                        if (ignorePhrase.Phrase == phrase)
                        {
                            ignorePhrase.ExactMatch = exactMatch;
                            
                            // Save to ConfigManager
                            SaveIgnorePhrases();
                            
                            Console.WriteLine($"Updated ignore phrase: '{phrase}' (Exact Match: {exactMatch})");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ignore phrase: {ex.Message}");
            }
        }

        private void PopulateFontFamilyComboBoxes()
        {
            try
            {
                // Get all font families
                var fontFamilies = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
                
                // Populate source language font combo box
                if (sourceLanguageFontFamilyComboBox != null)
                {
                    sourceLanguageFontFamilyComboBox.ItemsSource = fontFamilies;
                    sourceLanguageFontFamilyComboBox.DisplayMemberPath = "Source";
                }
                
                // Populate target language font combo box
                if (targetLanguageFontFamilyComboBox != null)
                {
                    targetLanguageFontFamilyComboBox.ItemsSource = fontFamilies;
                    targetLanguageFontFamilyComboBox.DisplayMemberPath = "Source";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error populating font family combo boxes: {ex.Message}");
            }
        }

        private void LoadFontSettings()
        {
            try
            {
                // Temporarily remove event handlers to prevent triggering changes during initialization
                sourceLanguageFontFamilyComboBox.SelectionChanged -= SourceLanguageFontFamilyComboBox_SelectionChanged;
                targetLanguageFontFamilyComboBox.SelectionChanged -= TargetLanguageFontFamilyComboBox_SelectionChanged;
                sourceLanguageFontBoldCheckBox.Checked -= SourceLanguageFontBoldCheckBox_CheckedChanged;
                sourceLanguageFontBoldCheckBox.Unchecked -= SourceLanguageFontBoldCheckBox_CheckedChanged;
                targetLanguageFontBoldCheckBox.Checked -= TargetLanguageFontBoldCheckBox_CheckedChanged;
                targetLanguageFontBoldCheckBox.Unchecked -= TargetLanguageFontBoldCheckBox_CheckedChanged;
                
                // Load source language font family
                string sourceFontFamily = ConfigManager.Instance.GetSourceLanguageFontFamily();
                if (sourceLanguageFontFamilyComboBox != null)
                {
                    // Try to find matching font family
                    var matchingFont = sourceLanguageFontFamilyComboBox.Items.Cast<FontFamily>()
                        .FirstOrDefault(f => f.Source == sourceFontFamily || f.Source.Contains(sourceFontFamily.Split(',')[0].Trim()));
                    if (matchingFont != null)
                    {
                        sourceLanguageFontFamilyComboBox.SelectedItem = matchingFont;
                    }
                    else
                    {
                        // If not found, add as a text item (for custom font strings)
                        sourceLanguageFontFamilyComboBox.Text = sourceFontFamily;
                    }
                }
                
                // Load source language font bold
                sourceLanguageFontBoldCheckBox.IsChecked = ConfigManager.Instance.GetSourceLanguageFontBold();
                
                // Load target language font family
                string targetFontFamily = ConfigManager.Instance.GetTargetLanguageFontFamily();
                if (targetLanguageFontFamilyComboBox != null)
                {
                    // Try to find matching font family
                    var matchingFont = targetLanguageFontFamilyComboBox.Items.Cast<FontFamily>()
                        .FirstOrDefault(f => f.Source == targetFontFamily || f.Source.Contains(targetFontFamily.Split(',')[0].Trim()));
                    if (matchingFont != null)
                    {
                        targetLanguageFontFamilyComboBox.SelectedItem = matchingFont;
                    }
                    else
                    {
                        // If not found, add as a text item (for custom font strings)
                        targetLanguageFontFamilyComboBox.Text = targetFontFamily;
                    }
                }
                
                // Load target language font bold
                targetLanguageFontBoldCheckBox.IsChecked = ConfigManager.Instance.GetTargetLanguageFontBold();
                
                // Reattach event handlers
                if (sourceLanguageFontFamilyComboBox != null)
                    sourceLanguageFontFamilyComboBox.SelectionChanged += SourceLanguageFontFamilyComboBox_SelectionChanged;
                if (targetLanguageFontFamilyComboBox != null)
                    targetLanguageFontFamilyComboBox.SelectionChanged += TargetLanguageFontFamilyComboBox_SelectionChanged;
                sourceLanguageFontBoldCheckBox.Checked += SourceLanguageFontBoldCheckBox_CheckedChanged;
                sourceLanguageFontBoldCheckBox.Unchecked += SourceLanguageFontBoldCheckBox_CheckedChanged;
                targetLanguageFontBoldCheckBox.Checked += TargetLanguageFontBoldCheckBox_CheckedChanged;
                targetLanguageFontBoldCheckBox.Unchecked += TargetLanguageFontBoldCheckBox_CheckedChanged;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading font settings: {ex.Message}");
            }
        }

        // Source Language Font Family changed
        private void SourceLanguageFontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            try
            {
                if (sourceLanguageFontFamilyComboBox.SelectedItem is FontFamily selectedFont)
                {
                    ConfigManager.Instance.SetSourceLanguageFontFamily(selectedFont.Source);
                    Console.WriteLine($"Source language font family set to: {selectedFont.Source}");
                    
                    // Refresh text objects to apply new font
                    RefreshTextObjectsWithNewFont();
                    
                    // Trigger OCR refresh
                    Logic.Instance.ResetHash();
                }
                else if (!string.IsNullOrWhiteSpace(sourceLanguageFontFamilyComboBox.Text))
                {
                    // Handle custom font string (comma-separated list)
                    ConfigManager.Instance.SetSourceLanguageFontFamily(sourceLanguageFontFamilyComboBox.Text);
                    Console.WriteLine($"Source language font family set to custom: {sourceLanguageFontFamilyComboBox.Text}");
                    
                    // Refresh text objects to apply new font
                    RefreshTextObjectsWithNewFont();
                    
                    // Trigger OCR refresh
                    Logic.Instance.ResetHash();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating source language font family: {ex.Message}");
            }
        }

        // Source Language Font Bold changed
        private void SourceLanguageFontBoldCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            try
            {
                bool isBold = sourceLanguageFontBoldCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetSourceLanguageFontBold(isBold);
                Console.WriteLine($"Source language font bold set to: {isBold}");
                
                // Refresh text objects to apply new font
                RefreshTextObjectsWithNewFont();
                
                // Trigger OCR refresh
                Logic.Instance.ResetHash();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating source language font bold: {ex.Message}");
            }
        }

        // Target Language Font Family changed
        private void TargetLanguageFontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            try
            {
                if (targetLanguageFontFamilyComboBox.SelectedItem is FontFamily selectedFont)
                {
                    ConfigManager.Instance.SetTargetLanguageFontFamily(selectedFont.Source);
                    Console.WriteLine($"Target language font family set to: {selectedFont.Source}");
                    
                    // Refresh text objects and chat box to apply new font
                    RefreshTextObjectsWithNewFont();
                    if (ChatBoxWindow.Instance != null)
                    {
                        ChatBoxWindow.Instance.UpdateChatHistory();
                    }
                    
                    // Trigger OCR refresh
                    Logic.Instance.ResetHash();
                }
                else if (!string.IsNullOrWhiteSpace(targetLanguageFontFamilyComboBox.Text))
                {
                    // Handle custom font string (comma-separated list)
                    ConfigManager.Instance.SetTargetLanguageFontFamily(targetLanguageFontFamilyComboBox.Text);
                    Console.WriteLine($"Target language font family set to custom: {targetLanguageFontFamilyComboBox.Text}");
                    
                    // Refresh text objects and chat box to apply new font
                    RefreshTextObjectsWithNewFont();
                    if (ChatBoxWindow.Instance != null)
                    {
                        ChatBoxWindow.Instance.UpdateChatHistory();
                    }
                    
                    // Trigger OCR refresh
                    Logic.Instance.ResetHash();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating target language font family: {ex.Message}");
            }
        }

        // Target Language Font Bold changed
        private void TargetLanguageFontBoldCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            try
            {
                bool isBold = targetLanguageFontBoldCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetTargetLanguageFontBold(isBold);
                Console.WriteLine($"Target language font bold set to: {isBold}");
                
                // Refresh text objects and chat box to apply new font
                RefreshTextObjectsWithNewFont();
                if (ChatBoxWindow.Instance != null)
                {
                    ChatBoxWindow.Instance.UpdateChatHistory();
                }
                
                // Trigger OCR refresh
                Logic.Instance.ResetHash();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating target language font bold: {ex.Message}");
            }
        }

        // Helper method to refresh all text objects with new font settings
        private void RefreshTextObjectsWithNewFont()
        {
            try
            {
                var textObjects = Logic.Instance.GetTextObjects();
                foreach (var textObj in textObjects)
                {
                    if (textObj != null)
                    {
                        textObj.UpdateUIElement();
                    }
                }
                
                // Refresh monitor window overlays
                if (MonitorWindow.Instance != null)
                {
                    MonitorWindow.Instance.RefreshOverlays();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing text objects: {ex.Message}");
            }
        }
    }
}
