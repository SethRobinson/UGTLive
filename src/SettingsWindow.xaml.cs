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
    // Class to represent an ignore phrase
    public class IgnorePhrase
    {
        public string Phrase { get; set; } = string.Empty;
        public bool ExactMatch { get; set; } = true;
        
        public IgnorePhrase(string phrase, bool exactMatch)
        {
            Phrase = phrase;
            ExactMatch = exactMatch;
        }
    }

    public partial class SettingsWindow : Window
    {
        private static SettingsWindow? _instance;
        
        // Shared language codes for source/target language combo boxes
        // Display names are derived from Logic.GetLanguageName() to avoid duplication
        private static readonly List<string> _languageCodes = new List<string>
        {
            "ja",      // Japanese
            "en",      // English
            "ch_sim",  // Chinese (Simplified)
            "ch_tra",  // Chinese (Traditional)
            "ko",      // Korean
            "es",      // Spanish
            "fr",      // French
            "it",      // Italian
            "de",      // German
            "pt",      // Portuguese
            "ru",      // Russian
            "pl",      // Polish
            "nl",      // Dutch
            "sv",      // Swedish
            "cs",      // Czech
            "hu",      // Hungarian
            "ro",      // Romanian
            "el",      // Greek
            "uk",      // Ukrainian
            "tr",      // Turkish
            "ar",      // Arabic
            "hi",      // Hindi
            "th",      // Thai
            "vi",      // Vietnamese
            "id",      // Indonesian
            "fa"       // Persian (Farsi)
        };
        
        public static SettingsWindow Instance
        {
            get
            {
                if (_instance == null || !IsWindowValid(_instance))
                {
                    _instance = new SettingsWindow();
                }
                return _instance;
            }
        }

        /// <summary>
        /// True when settings is shown and not minimized. Does not instantiate the window.
        /// </summary>
        public static bool IsOpenAndVisible()
        {
            if (_instance == null || !IsWindowValid(_instance))
            {
                return false;
            }

            return _instance.IsVisible && _instance.WindowState != WindowState.Minimized;
        }
        
        public SettingsWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine("SettingsWindow constructor: Setting _isInitializing to true");
            }
            
            InitializeComponent();
            _instance = this;
            
            // Set high-res icon
            IconHelper.SetWindowIcon(this);
            
            // Setup tooltip exclusion from screenshots
            SetupTooltipExclusion();
            
            // Add SourceInitialized event handler for screenshot exclusion
            this.SourceInitialized += SettingsWindow_SourceInitialized;
            
            // Add Loaded event handler to ensure controls are initialized
            this.Loaded += SettingsWindow_Loaded;
            
            // Disable hotkeys while this window has focus so we can type freely
            this.Activated += (s, e) => 
            {
                HotkeyManager.Instance.SetEnabled(false);
                KeyboardShortcuts.SetShortcutsEnabled(false);
            };
            // Re-enable when focus leaves (but not yet hidden)
            this.Deactivated += (s, e) => 
            {
                HotkeyManager.Instance.SetEnabled(true);
                KeyboardShortcuts.SetShortcutsEnabled(true);
            };
            
            // Set up closing behavior (hide instead of close)
            this.Closing += (s, e) => 
            {
                if (System.Windows.Application.Current?.MainWindow == null)
                {
                    return;
                }
                e.Cancel = true;  // Cancel the close
                MainWindow.Instance?.HandleSettingsButton();
            };
        }

        private void PopulateOcrMethodOptions()
        {
            ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;
            ocrMethodComboBox.Items.Clear();

            foreach (string method in ConfigManager.SupportedOcrMethods)
            {
                string displayName = ConfigManager.GetOcrMethodDisplayName(method);
                ocrMethodComboBox.Items.Add(new ComboBoxItem 
                { 
                    Content = displayName,
                    Tag = method  // Store internal ID in Tag
                });
            }

            ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
        }
        
        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        // Collection to hold the ignore phrases
        private ObservableCollection<IgnorePhrase> _ignorePhrases = new ObservableCollection<IgnorePhrase>();
        
        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("SettingsWindow_Loaded: Starting initialization");
                }
                
                // Set initialization flag to prevent saving during setup
                _isInitializing = true;
                
                // Populate language combo boxes
                PopulateLanguageComboBoxes();
                
                // Populate Whisper Language ComboBox
                PopulateWhisperLanguageComboBox();

                // Populate OCR method options from shared configuration
                PopulateOcrMethodOptions();
                
                // Populate font family combo boxes
                PopulateFontFamilyComboBoxes();
                
                // Make sure keyboard shortcuts work from this window too
                PreviewKeyDown -= Application_KeyDown;
                PreviewKeyDown += Application_KeyDown;
                
                // Set initial values only after the window is fully loaded
                LoadSettingsFromMainWindow();
                
                // Make sure service-specific settings are properly initialized
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                UpdateServiceSpecificSettings(currentService);
                
                // Load hotkeys
                loadActions();
                
                // Now that initialization is complete, allow saving changes
                _isInitializing = false;
                
                // Force the OCR method and translation service to match the config again
                // This ensures the config values are preserved and not overwritten
                string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
                string configTransService = ConfigManager.Instance.GetCurrentTranslationService();
                Console.WriteLine($"Ensuring config values are preserved: OCR={configOcrMethod}, Translation={configTransService}");
                
                ConfigManager.Instance.SetOcrMethod(configOcrMethod);
                ConfigManager.Instance.SetTranslationService(configTransService);
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Settings window fully loaded and initialized. Changes will now be saved.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Settings window: {ex.Message}");
                _isInitializing = false; // Ensure we don't get stuck in initialization mode
            }
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            
            if (HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                bool handled = HotkeyManager.Instance.HandleKeyDownLocal(e.Key, modifiers);
                if (handled)
                {
                    e.Handled = true;
                }
            }
            else
            {
                bool handled = HotkeyManager.Instance.HandleKeyDownAll(e.Key, modifiers);
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        // Google Translate API Key changed
        private void GoogleTranslateApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                string apiKey = googleTranslateApiKeyPasswordBox.Password.Trim();
                
                // Update the config
                ConfigManager.Instance.SetGoogleTranslateApiKey(apiKey);
                Console.WriteLine("Google Translate API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google Translate API key: {ex.Message}");
            }
        }

        // Google Translate Service Type changed
        private void GoogleTranslateServiceTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (googleTranslateServiceTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    bool isCloudApi = selectedItem.Content.ToString() == "Cloud API (paid)";
                    
                    // Show/hide API key field based on selection
                    googleTranslateApiKeyLabel.Visibility = isCloudApi ? Visibility.Visible : Visibility.Collapsed;
                    googleTranslateApiKeyGrid.Visibility = isCloudApi ? Visibility.Visible : Visibility.Collapsed;
                    
                    // Save to config
                    ConfigManager.Instance.SetGoogleTranslateUseCloudApi(isCloudApi);
                    Console.WriteLine($"Google Translate service type set to: {(isCloudApi ? "Cloud API" : "Free Web Service")}");
                    
                    // Trigger retranslation if the current service is Google Translate
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Google Translate")
                    {
                        Console.WriteLine("Google Translate service type changed. Triggering retranslation...");
                        
                        // Clear translation history/context buffer to avoid influencing new translations
                        MainWindow.Instance.ClearTranslationHistory();
                        
                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();
                        
                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                        
                        // Force OCR/translation to run again if active
                        if (MainWindow.Instance.GetIsStarted())
                        {
                            MainWindow.Instance.SetOCRCheckIsWanted(true);
                            Console.WriteLine("Triggered OCR/translation refresh after Google Translate service type change");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google Translate service type: {ex.Message}");
            }
        }
        
// Google Translate language mapping checkbox changed
        private void GoogleTranslateMappingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                bool isEnabled = googleTranslateMappingCheckBox.IsChecked ?? true;
                
                // Save to config
                ConfigManager.Instance.SetGoogleTranslateAutoMapLanguages(isEnabled);
                Console.WriteLine($"Google Translate auto language mapping set to: {isEnabled}");
                
                // Trigger retranslation if the current service is Google Translate
                if (ConfigManager.Instance.GetCurrentTranslationService() == "Google Translate")
                {
                    Console.WriteLine("Google Translate language mapping changed. Triggering retranslation...");
                    
                    // Reset the hash to force a retranslation
                    Logic.Instance.ResetHash();
                    
                    // Clear any existing text objects to refresh the display
                    Logic.Instance.ClearAllTextObjects();
                    
                    // Force OCR/translation to run again if active
                    if (MainWindow.Instance.GetIsStarted())
                    {
                        MainWindow.Instance.SetOCRCheckIsWanted(true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Google Translate language mapping: {ex.Message}");
            }
        }

        // Google Translate API link click
        private void GoogleTranslateApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://cloud.google.com/translate/docs/setup");
        }

        // Helper method to check if a window instance is still valid
        private static bool IsWindowValid(Window window)
        {
            // Check if the window still exists in the application's window collection
            var windowCollection = System.Windows.Application.Current.Windows;
            for (int i = 0; i < windowCollection.Count; i++)
            {
                if (windowCollection[i] == window)
                {
                    return true;
                }
            }
            return false;
        }
        
        private void restartListenIfActive()
        {
            MainWindow.Instance?.RestartListenIfActive();
        }

        private void LoadSettingsFromMainWindow()
        {
            // Temporarily remove event handlers to prevent triggering changes during initialization
            sourceLanguageComboBox.SelectionChanged -= SourceLanguageComboBox_SelectionChanged;
            targetLanguageComboBox.SelectionChanged -= TargetLanguageComboBox_SelectionChanged;
            
            // Remove focus event handlers
            maxContextPiecesTextBox.LostFocus -= MaxContextPiecesTextBox_LostFocus;
            minContextSizeTextBox.LostFocus -= MinContextSizeTextBox_LostFocus;
            minChatBoxTextSizeTextBox.LostFocus -= MinChatBoxTextSizeTextBox_LostFocus;
            gameInfoTextBox.TextChanged -= GameInfoTextBox_TextChanged;
            minTextFragmentSizeTextBox.LostFocus -= MinTextFragmentSizeTextBox_LostFocus;
            minLetterConfidenceTextBox.LostFocus -= MinLetterConfidenceTextBox_LostFocus;
            minLineConfidenceTextBox.LostFocus -= MinLineConfidenceTextBox_LostFocus;
            blockDetectionPowerTextBox.LostFocus -= BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus -= SettleTimeTextBox_LostFocus;
            maxSettleTimeTextBox.LostFocus -= MaxSettleTimeTextBox_LostFocus;
            overlayClearDelayTextBox.LostFocus -= OverlayClearDelayTextBox_LostFocus;
            
            // Set context settings
            maxContextPiecesTextBox.Text = ConfigManager.Instance.GetMaxContextPieces().ToString();
            minContextSizeTextBox.Text = ConfigManager.Instance.GetMinContextSize().ToString();
            minChatBoxTextSizeTextBox.Text = ConfigManager.Instance.GetChatBoxMinTextSize().ToString();
            gameInfoTextBox.Text = ConfigManager.Instance.GetGameInfo();
            minTextFragmentSizeTextBox.Text = ConfigManager.Instance.GetMinTextFragmentSize().ToString();
            minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString();
            minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString();
            
            // Reattach focus event handlers
            maxContextPiecesTextBox.LostFocus += MaxContextPiecesTextBox_LostFocus;
            minContextSizeTextBox.LostFocus += MinContextSizeTextBox_LostFocus;
            minChatBoxTextSizeTextBox.LostFocus += MinChatBoxTextSizeTextBox_LostFocus;
            gameInfoTextBox.TextChanged += GameInfoTextBox_TextChanged;
            minTextFragmentSizeTextBox.LostFocus += MinTextFragmentSizeTextBox_LostFocus;
            minLetterConfidenceTextBox.LostFocus += MinLetterConfidenceTextBox_LostFocus;
            minLineConfidenceTextBox.LostFocus += MinLineConfidenceTextBox_LostFocus;
            
            // Load source language from config
            string configSourceLanguage = ConfigManager.Instance.GetSourceLanguage();
            if (!string.IsNullOrEmpty(configSourceLanguage))
            {
                foreach (ComboBoxItem item in sourceLanguageComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), configSourceLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceLanguageComboBox.SelectedItem = item;
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"Settings window: Set source language from config to {configSourceLanguage}");
                        }
                        break;
                    }
                }
            }
            
            // Load target language from config
            string configTargetLanguage = ConfigManager.Instance.GetTargetLanguage();
            if (!string.IsNullOrEmpty(configTargetLanguage))
            {
                foreach (ComboBoxItem item in targetLanguageComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), configTargetLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLanguageComboBox.SelectedItem = item;
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"Settings window: Set target language from config to {configTargetLanguage}");
                        }
                        break;
                    }
                }
            }
            
            // Reattach event handlers
            sourceLanguageComboBox.SelectionChanged += SourceLanguageComboBox_SelectionChanged;
            targetLanguageComboBox.SelectionChanged += TargetLanguageComboBox_SelectionChanged;
            
            // Set OCR settings from config
            string savedOcrMethod = ConfigManager.Instance.GetOcrMethod();
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"SettingsWindow: Loading OCR method '{savedOcrMethod}'");
            }
            
            // Temporarily remove event handler to prevent triggering during initialization
            ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;
            
            // Find matching ComboBoxItem by Tag (internal ID)
            foreach (ComboBoxItem item in ocrMethodComboBox.Items)
            {
                string itemId = item.Tag?.ToString() ?? "";
                if (string.Equals(itemId, savedOcrMethod, StringComparison.OrdinalIgnoreCase))
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Found matching OCR method: '{itemId}'");
                    }
                    ocrMethodComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Re-attach event handler
            ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
            
            // Update OCR-specific settings visibility based on saved method
            UpdateOcrSpecificSettings(savedOcrMethod);
            
            // Get auto-translate setting from config instead of MainWindow
            // This ensures the setting persists across application restarts
            autoTranslateCheckBox.IsChecked = ConfigManager.Instance.IsAutoTranslateEnabled();
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"Settings window: Loading auto-translate from config: {ConfigManager.Instance.IsAutoTranslateEnabled()}");
            }
            
            // Get pause OCR while translating setting from config
            pauseOcrWhileTranslatingCheckBox.IsChecked = ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled();
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"Settings window: Loading pause OCR while translating from config: {ConfigManager.Instance.IsPauseOcrWhileTranslatingEnabled()}");
            }
            
            // Load Cloud OCR Color Correction
            if (cloudOcrColorCorrectionCheckBox != null)
            {
                cloudOcrColorCorrectionCheckBox.IsChecked = ConfigManager.Instance.IsCloudOcrColorCorrectionEnabled();
            }

            // Note: Leave translation onscreen setting is loaded per-OCR in UpdateOcrSpecificSettings()
            
            // Load Monitor Window Override Color settings
            overrideBgColorCheckBox.IsChecked = ConfigManager.Instance.IsMonitorOverrideBgColorEnabled();
            overrideFontColorCheckBox.IsChecked = ConfigManager.Instance.IsMonitorOverrideFontColorEnabled();
            windowsVisibleInScreenshotsCheckBox.IsChecked = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
            
            // Load debug logging settings
            logExtraDebugStuffCheckBox.IsChecked = ConfigManager.Instance.GetLogExtraDebugStuff();
            
            // Load persist window size setting
            persistWindowSizeCheckBox.IsChecked = ConfigManager.Instance.IsPersistWindowSizeEnabled();
            
            // Load completion sound setting
            playCompletionSoundCheckBox.IsChecked = ConfigManager.Instance.IsCompletionSoundEnabled();

            // Load snapshot toggle mode setting
            snapshotToggleModeCheckBox.IsChecked = ConfigManager.Instance.GetSnapshotToggleMode();
            
            // Load colors and update UI
            Color bgColor = ConfigManager.Instance.GetMonitorOverrideBgColor();
            Color fontColor = ConfigManager.Instance.GetMonitorOverrideFontColor();
            
            overrideBgColorButton.Background = new SolidColorBrush(bgColor);
            overrideBgColorText.Text = ColorToHexString(bgColor);
            
            overrideFontColorButton.Background = new SolidColorBrush(fontColor);
            overrideFontColorText.Text = ColorToHexString(fontColor);
            
            // Load background opacity and update UI
            double opacity = ConfigManager.Instance.GetMonitorBgOpacity();
            bgOpacitySlider.ValueChanged -= BgOpacitySlider_ValueChanged;
            bgOpacitySlider.Value = opacity;
            bgOpacitySlider.ValueChanged += BgOpacitySlider_ValueChanged;
            bgOpacityText.Text = $"{(int)(opacity * 100)}%";
            
            // Load Text Area Size Expansion settings
            textAreaExpansionWidthTextBox.LostFocus -= TextAreaExpansionWidthTextBox_LostFocus;
            textAreaExpansionHeightTextBox.LostFocus -= TextAreaExpansionHeightTextBox_LostFocus;
            textOverlayBorderRadiusTextBox.LostFocus -= TextOverlayBorderRadiusTextBox_LostFocus;
            
            textAreaExpansionWidthTextBox.Text = ConfigManager.Instance.GetMonitorTextAreaExpansionWidth().ToString();
            textAreaExpansionHeightTextBox.Text = ConfigManager.Instance.GetMonitorTextAreaExpansionHeight().ToString();
            textOverlayBorderRadiusTextBox.Text = ConfigManager.Instance.GetMonitorTextOverlayBorderRadius().ToString();
            
            textAreaExpansionWidthTextBox.LostFocus += TextAreaExpansionWidthTextBox_LostFocus;
            textAreaExpansionHeightTextBox.LostFocus += TextAreaExpansionHeightTextBox_LostFocus;
            textOverlayBorderRadiusTextBox.LostFocus += TextOverlayBorderRadiusTextBox_LostFocus;
            
            // Load Text Alignment settings
            string hAlign = ConfigManager.Instance.GetTextOverlayHorizontalAlignment();
            string vAlign = ConfigManager.Instance.GetTextOverlayVerticalAlignment();
            
            textOverlayHAlignComboBox.SelectionChanged -= TextOverlayHAlignComboBox_SelectionChanged;
            textOverlayVAlignComboBox.SelectionChanged -= TextOverlayVAlignComboBox_SelectionChanged;
            
            foreach (ComboBoxItem item in textOverlayHAlignComboBox.Items)
            {
                if (item.Tag is string tag && tag == hAlign)
                {
                    textOverlayHAlignComboBox.SelectedItem = item;
                    break;
                }
            }
            
            foreach (ComboBoxItem item in textOverlayVAlignComboBox.Items)
            {
                if (item.Tag is string tag && tag == vAlign)
                {
                    textOverlayVAlignComboBox.SelectedItem = item;
                    break;
                }
            }
            
            textOverlayHAlignComboBox.SelectionChanged += TextOverlayHAlignComboBox_SelectionChanged;
            textOverlayVAlignComboBox.SelectionChanged += TextOverlayVAlignComboBox_SelectionChanged;
            
            // Load Font Settings
            LoadFontSettings();
            
            // Load Lesson Settings
            lessonPromptTemplateTextBox.LostFocus -= LessonPromptTemplateTextBox_LostFocus;
            lessonUrlTemplateTextBox.LostFocus -= LessonUrlTemplateTextBox_LostFocus;
            
            lessonPromptTemplateTextBox.Text = ConfigManager.Instance.GetLessonPromptTemplate();
            lessonUrlTemplateTextBox.Text = ConfigManager.Instance.GetLessonUrlTemplate();
            
            lessonPromptTemplateTextBox.LostFocus += LessonPromptTemplateTextBox_LostFocus;
            lessonUrlTemplateTextBox.LostFocus += LessonUrlTemplateTextBox_LostFocus;
            
            // Set block detection settings directly from BlockDetectionManager
            // Temporarily remove event handlers to prevent triggering changes
            blockDetectionPowerTextBox.LostFocus -= BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus -= SettleTimeTextBox_LostFocus;
            maxSettleTimeTextBox.LostFocus -= MaxSettleTimeTextBox_LostFocus;
            overlayClearDelayTextBox.LostFocus -= OverlayClearDelayTextBox_LostFocus;
            cooldownHashCompareLengthTextBox.LostFocus -= CooldownHashCompareLengthTextBox_LostFocus;
            
            
            // Block detection power is deprecated/removed, hiding or setting to default
            blockDetectionPowerTextBox.Visibility = Visibility.Collapsed; 
            if (blockDetectionPowerLabel != null) blockDetectionPowerLabel.Visibility = Visibility.Collapsed;
            
            settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2", CultureInfo.InvariantCulture);
            maxSettleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionMaxSettleTime().ToString("F2", CultureInfo.InvariantCulture);
            overlayClearDelayTextBox.Text = ConfigManager.Instance.GetOverlayClearDelaySeconds().ToString("F2", CultureInfo.InvariantCulture);
            cooldownHashCompareLengthTextBox.Text = ConfigManager.Instance.GetCooldownHashCompareLength().ToString();
            
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"SettingsWindow: Loaded settle time: {settleTimeTextBox.Text}");
                Console.WriteLine($"SettingsWindow: Loaded overlay clear delay: {overlayClearDelayTextBox.Text}");
            }
            
            // Reattach event handlers
            blockDetectionPowerTextBox.LostFocus += BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus += SettleTimeTextBox_LostFocus;
            maxSettleTimeTextBox.LostFocus += MaxSettleTimeTextBox_LostFocus;
            overlayClearDelayTextBox.LostFocus += OverlayClearDelayTextBox_LostFocus;
            cooldownHashCompareLengthTextBox.LostFocus += CooldownHashCompareLengthTextBox_LostFocus;
            
            // Set translation service from config
            string currentService = ConfigManager.Instance.GetCurrentTranslationService();
            
            // Temporarily remove event handler
            translationServiceComboBox.SelectionChanged -= TranslationServiceComboBox_SelectionChanged;
            
            foreach (ComboBoxItem item in translationServiceComboBox.Items)
            {
                if (string.Equals(item.Content.ToString(), currentService, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found matching translation service: '{item.Content}'");
                    translationServiceComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Re-attach event handler
            translationServiceComboBox.SelectionChanged += TranslationServiceComboBox_SelectionChanged;
            
            // Initialize API key for Gemini
            geminiApiKeyPasswordBox.Password = ConfigManager.Instance.GetGeminiApiKey();
            
            // Initialize unified thinking checkbox
            thinkingEnabledCheckBox.IsChecked = ConfigManager.Instance.GetThinkingEnabled();
            
            // Initialize Ollama settings
            ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
            ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
            ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();
            
            // Initialize llama.cpp settings
            // Temporarily remove event handlers to prevent triggering changes during initialization
            llamacppUrlTextBox.TextChanged -= LlamacppUrlTextBox_TextChanged;
            llamacppPortTextBox.TextChanged -= LlamacppPortTextBox_TextChanged;
            llamacppModelTextBox.TextChanged -= LlamacppModelTextBox_TextChanged;
            
            llamacppUrlTextBox.Text = ConfigManager.Instance.GetLlamaCppUrl();
            llamacppPortTextBox.Text = ConfigManager.Instance.GetLlamaCppPort();
            llamacppModelTextBox.Text = ConfigManager.Instance.GetLlamaCppModel();
            // Reattach event handlers
            llamacppUrlTextBox.TextChanged += LlamacppUrlTextBox_TextChanged;
            llamacppPortTextBox.TextChanged += LlamacppPortTextBox_TextChanged;
            llamacppModelTextBox.TextChanged += LlamacppModelTextBox_TextChanged;
            
            // Update service-specific settings visibility based on selected service
            UpdateServiceSpecificSettings(currentService);
            
            // Load the current service's prompt
            LoadCurrentServicePrompt();
            
            // Load TTS settings
            
            // Temporarily remove TTS event handlers
            ttsServiceComboBox.SelectionChanged -= TtsServiceComboBox_SelectionChanged;
            elevenLabsVoiceComboBox.SelectionChanged -= ElevenLabsVoiceComboBox_SelectionChanged;
            googleTtsVoiceComboBox.SelectionChanged -= GoogleTtsVoiceComboBox_SelectionChanged;
            if (elevenLabsCustomVoiceCheckBox != null)
            {
                elevenLabsCustomVoiceCheckBox.Checked -= ElevenLabsCustomVoiceCheckBox_CheckedChanged;
                elevenLabsCustomVoiceCheckBox.Unchecked -= ElevenLabsCustomVoiceCheckBox_CheckedChanged;
            }
            if (elevenLabsCustomVoiceIdTextBox != null)
            {
                elevenLabsCustomVoiceIdTextBox.LostFocus -= ElevenLabsCustomVoiceIdTextBox_LostFocus;
            }
            
            // Set TTS service
            string ttsService = ConfigManager.Instance.GetTtsService();
            foreach (ComboBoxItem item in ttsServiceComboBox.Items)
            {
                if (string.Equals(item.Content.ToString(), ttsService, StringComparison.OrdinalIgnoreCase))
                {
                    ttsServiceComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Update service-specific settings visibility
            UpdateTtsServiceSpecificSettings(ttsService);
            
            // Set ElevenLabs API key
            elevenLabsApiKeyPasswordBox.Password = ConfigManager.Instance.GetElevenLabsApiKey();
            
            // Set ElevenLabs voice
            string elevenLabsVoiceId = ConfigManager.Instance.GetElevenLabsVoice();
            foreach (ComboBoxItem item in elevenLabsVoiceComboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), elevenLabsVoiceId, StringComparison.OrdinalIgnoreCase))
                {
                    elevenLabsVoiceComboBox.SelectedItem = item;
                    break;
                }
            }

            // Set custom ElevenLabs voice settings
            bool useCustomElevenLabsVoice = ConfigManager.Instance.GetElevenLabsUseCustomVoiceId();
            if (elevenLabsCustomVoiceCheckBox != null)
            {
                elevenLabsCustomVoiceCheckBox.IsChecked = useCustomElevenLabsVoice;
            }
            if (elevenLabsCustomVoiceIdTextBox != null)
            {
                elevenLabsCustomVoiceIdTextBox.Text = ConfigManager.Instance.GetElevenLabsCustomVoiceId();
                elevenLabsCustomVoiceIdTextBox.IsEnabled = useCustomElevenLabsVoice;
            }
            elevenLabsVoiceComboBox.IsEnabled = !useCustomElevenLabsVoice;
            elevenLabsVoiceLabel.IsEnabled = !useCustomElevenLabsVoice;
            
            // Set Google TTS API key
            googleTtsApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTtsApiKey();
            
            // Set Google TTS voice
            string googleVoiceId = ConfigManager.Instance.GetGoogleTtsVoice();
            foreach (ComboBoxItem item in googleTtsVoiceComboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), googleVoiceId, StringComparison.OrdinalIgnoreCase))
                {
                    googleTtsVoiceComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Set Qwen3-TTS settings
            if (qwen3TtsVoiceComboBox != null)
            {
                string qwen3VoiceId = ConfigManager.Instance.GetQwen3TtsVoice();
                foreach (ComboBoxItem item in qwen3TtsVoiceComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), qwen3VoiceId, StringComparison.OrdinalIgnoreCase))
                    {
                        qwen3TtsVoiceComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            
            // Re-attach TTS event handlers
            ttsServiceComboBox.SelectionChanged += TtsServiceComboBox_SelectionChanged;
            elevenLabsVoiceComboBox.SelectionChanged += ElevenLabsVoiceComboBox_SelectionChanged;
            googleTtsVoiceComboBox.SelectionChanged += GoogleTtsVoiceComboBox_SelectionChanged;
            if (elevenLabsCustomVoiceCheckBox != null)
            {
                elevenLabsCustomVoiceCheckBox.Checked += ElevenLabsCustomVoiceCheckBox_CheckedChanged;
                elevenLabsCustomVoiceCheckBox.Unchecked += ElevenLabsCustomVoiceCheckBox_CheckedChanged;
            }
            if (elevenLabsCustomVoiceIdTextBox != null)
            {
                elevenLabsCustomVoiceIdTextBox.LostFocus += ElevenLabsCustomVoiceIdTextBox_LostFocus;
            }
            
            // Load audio preload settings
            LoadAudioPreloadSettings();
            
            // Load ignore phrases
            LoadIgnorePhrases();

            // Audio Processing settings
            LoadAudioInputDevices(); // Load and set audio input devices
            openAiRealtimeApiKeyPasswordBox.Password = ConfigManager.Instance.GetOpenAiRealtimeApiKey();

            // Set VAD eagerness dropdown
            string currentEagerness = ConfigManager.Instance.GetSemanticVadEagerness();
            foreach (ComboBoxItem item in vadEagernessComboBox.Items)
            {
                if (item.Tag is string tag && tag == currentEagerness)
                {
                    vadEagernessComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Load listen mode prompts
            listenTextPromptTextBox.Text = ConfigManager.Instance.GetListenTextPrompt();
            listenSpokenPromptTextBox.Text = ConfigManager.Instance.GetListenSpokenPrompt();
            updateAutoGeneratedPromptPreview();
            
            // Initialize OpenAI voice selection
            openAiVoiceComboBox.SelectionChanged -= OpenAiVoiceComboBox_SelectionChanged;
            string currentVoice = ConfigManager.Instance.GetOpenAIVoice();
            foreach (ComboBoxItem item in openAiVoiceComboBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), currentVoice, StringComparison.OrdinalIgnoreCase))
                {
                    openAiVoiceComboBox.SelectedItem = item;
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"OpenAI voice set from config to {currentVoice}");
                    }
                    break;
                }
            }
            openAiVoiceComboBox.SelectionChanged += OpenAiVoiceComboBox_SelectionChanged;
            
            // Set up audio translation type dropdown
            audioTranslationTypeComboBox.SelectionChanged -= AudioTranslationTypeComboBox_SelectionChanged;
            
            // Determine which option to select based on current settings
            bool useGoogleTranslate = ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled();
            bool useOpenAITranslation = ConfigManager.Instance.IsOpenAITranslationEnabled();
            
            if (useOpenAITranslation)
            {
                // Select OpenAI option
                foreach (ComboBoxItem item in audioTranslationTypeComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), "openai", StringComparison.OrdinalIgnoreCase))
                    {
                        audioTranslationTypeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            else if (useGoogleTranslate)
            {
                // Select Google Translate option
                foreach (ComboBoxItem item in audioTranslationTypeComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), "google", StringComparison.OrdinalIgnoreCase))
                    {
                        audioTranslationTypeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                // Select No translation option
                foreach (ComboBoxItem item in audioTranslationTypeComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), "none", StringComparison.OrdinalIgnoreCase))
                    {
                        audioTranslationTypeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            
            // Reattach the event handler
            audioTranslationTypeComboBox.SelectionChanged += AudioTranslationTypeComboBox_SelectionChanged;

            // Load Whisper source language
            // Temporarily remove event handler
            if (whisperSourceLanguageComboBox != null)
            {
                whisperSourceLanguageComboBox.SelectionChanged -= WhisperSourceLanguageComboBox_SelectionChanged;
                string currentWhisperLanguage = ConfigManager.Instance.GetWhisperSourceLanguage();
                foreach (ComboBoxItem item in whisperSourceLanguageComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), currentWhisperLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        whisperSourceLanguageComboBox.SelectedItem = item;
                        Console.WriteLine($"SettingsWindow: Set Whisper source language from config to {currentWhisperLanguage}");
                        break;
                    }
                }
                // Re-attach event handler
                whisperSourceLanguageComboBox.SelectionChanged += WhisperSourceLanguageComboBox_SelectionChanged;
            }

            // Load Transcription Model
            if (transcriptionModelComboBox != null)
            {
                transcriptionModelComboBox.SelectionChanged -= TranscriptionModelComboBox_SelectionChanged;
                string currentModel = ConfigManager.Instance.GetOpenAITranscriptionModel();
                foreach (ComboBoxItem item in transcriptionModelComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), currentModel, StringComparison.OrdinalIgnoreCase))
                    {
                        transcriptionModelComboBox.SelectedItem = item;
                        break;
                    }
                }
                transcriptionModelComboBox.SelectionChanged += TranscriptionModelComboBox_SelectionChanged;
            }

            // Load Noise Reduction
            if (noiseReductionComboBox != null)
            {
                noiseReductionComboBox.SelectionChanged -= NoiseReductionComboBox_SelectionChanged;
                string currentNR = ConfigManager.Instance.GetOpenAINoiseReduction();
                foreach (ComboBoxItem item in noiseReductionComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), currentNR, StringComparison.OrdinalIgnoreCase))
                    {
                        noiseReductionComboBox.SelectedItem = item;
                        break;
                    }
                }
                noiseReductionComboBox.SelectionChanged += NoiseReductionComboBox_SelectionChanged;
            }

            // Load Screenshot settings
            screenshotFilenameTextBox.TextChanged -= ScreenshotFilenameTextBox_TextChanged;
            screenshotFolderTextBox.TextChanged -= ScreenshotFolderTextBox_TextChanged;
            screenshotTypeComboBox.SelectionChanged -= ScreenshotTypeComboBox_SelectionChanged;

            screenshotFilenameTextBox.Text = ConfigManager.Instance.GetScreenshotFilename();
            screenshotFolderTextBox.Text = ConfigManager.Instance.GetScreenshotFolder();

            string savedScreenshotType = ConfigManager.Instance.GetScreenshotType();
            foreach (ComboBoxItem item in screenshotTypeComboBox.Items)
            {
                if (string.Equals(item.Content?.ToString(), savedScreenshotType, StringComparison.OrdinalIgnoreCase))
                {
                    screenshotTypeComboBox.SelectedItem = item;
                    break;
                }
            }

            screenshotFilenameTextBox.TextChanged += ScreenshotFilenameTextBox_TextChanged;
            screenshotFolderTextBox.TextChanged += ScreenshotFolderTextBox_TextChanged;
            screenshotTypeComboBox.SelectionChanged += ScreenshotTypeComboBox_SelectionChanged;
        }
        
        // Language settings
        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }
            
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Tag?.ToString() ?? "ja";
                Console.WriteLine($"Settings: Source language changed to: {language}");
                
                // Cleanup audio preloading when language changes
                AudioPreloadService.Instance.CancelAllPreloads();
                AudioPlaybackManager.Instance.StopCurrentPlayback();
                AudioPreloadService.Instance.ClearAudioCache();
                
                // Save to config
                ConfigManager.Instance.SetSourceLanguage(language);
                
                // Reset the OCR hash to force a fresh comparison after changing source language
                Logic.Instance.ResetHash();
                
                // Clear translation history/context buffer to avoid influencing new translations
                MainWindow.Instance.ClearTranslationHistory();
                
                // Clear any existing text objects to refresh the display
                Logic.Instance.ClearAllTextObjects();
                
                // Force OCR/translation to run again if active
                if (MainWindow.Instance.GetIsStarted())
                {
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    Console.WriteLine("Triggered OCR/translation refresh after source language change");
                }
            }
        }
        
        private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (_isInitializing)
            {
                return;
            }
            
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Tag?.ToString() ?? "en";
                Console.WriteLine($"Settings: Target language changed to: {language}");
                
                // Cleanup audio preloading when language changes
                AudioPreloadService.Instance.CancelAllPreloads();
                AudioPlaybackManager.Instance.StopCurrentPlayback();
                AudioPreloadService.Instance.ClearAudioCache();
                
                // Save to config
                ConfigManager.Instance.SetTargetLanguage(language);
                
                // Reset the OCR hash to force a fresh comparison after changing target language
                Logic.Instance.ResetHash();

                // Clear translation history/context buffer to avoid influencing new translations
                MainWindow.Instance.ClearTranslationHistory();
                
                // Clear any existing text objects to refresh the display
                Logic.Instance.ClearAllTextObjects();
                
                // Force OCR/translation to run again if active
                if (MainWindow.Instance.GetIsStarted())
                {
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                    Console.WriteLine("Triggered OCR/translation refresh after target language change");
                }

                updateAutoGeneratedPromptPreview();
            }
        }
        
        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }
            
            bool isEnabled = autoTranslateCheckBox.IsChecked ?? false;
            Console.WriteLine($"Settings window: Auto-translate changed to {isEnabled}");
            
            // Update auto translate setting in MainWindow
            // This will also save to config and update the UI
            MainWindow.Instance.SetAutoTranslateEnabled(isEnabled);
            
            // Update MonitorWindow CheckBox if needed
            if (MonitorWindow.Instance.autoTranslateCheckBox != null)
            {
                MonitorWindow.Instance.autoTranslateCheckBox.IsChecked = autoTranslateCheckBox.IsChecked;
            }
        }
        
        private void PauseOcrWhileTranslatingCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }
            
            bool isEnabled = pauseOcrWhileTranslatingCheckBox.IsChecked ?? false;
            Console.WriteLine($"Settings window: Pause OCR while translating changed to {isEnabled}");
            
            // Save to config
            ConfigManager.Instance.SetPauseOcrWhileTranslatingEnabled(isEnabled);
        }
        
        private void CloudOcrColorCorrectionCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
             // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }
            
            bool isEnabled = cloudOcrColorCorrectionCheckBox.IsChecked ?? false;
            // Save to config
            ConfigManager.Instance.SetCloudOcrColorCorrectionEnabled(isEnabled);
            
            // Reset OCR hash to force refresh with new color detection setting
            Logic.Instance.ResetHash();
            
            // Clear existing text objects to trigger fresh OCR with colors
            Logic.Instance.ClearAllTextObjects();
            
            // Trigger OCR if currently active
            if (MainWindow.Instance.GetIsStarted())
            {
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
        }
        
        private void LeaveTranslationOnscreenCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
            
            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            bool isEnabled = leaveTranslationOnscreenCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetLeaveTranslationOnscreen(currentOcr, isEnabled);
            Console.WriteLine($"{currentOcr} leave translation onscreen enabled: {isEnabled}");
        }

        // Monitor Window Override Color handlers
        
        private void OverrideBgColorCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool isEnabled = overrideBgColorCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetMonitorOverrideBgColorEnabled(isEnabled);
            Console.WriteLine($"Monitor override BG color enabled: {isEnabled}");
            
            // Refresh overlays to apply changes immediately
            MonitorWindow.Instance.RefreshOverlays();
            
            // Trigger OCR refresh
            Logic.Instance.ResetHash();
        }

        private void OverrideFontColorCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool isEnabled = overrideFontColorCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetMonitorOverrideFontColorEnabled(isEnabled);
            Console.WriteLine($"Monitor override font color enabled: {isEnabled}");
            
            // Refresh overlays to apply changes immediately
            MonitorWindow.Instance.RefreshOverlays();
            
            // Trigger OCR refresh
            Logic.Instance.ResetHash();
        }
        
        private void WindowsVisibleInScreenshotsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool visible = windowsVisibleInScreenshotsCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetWindowsVisibleInScreenshots(visible);
            Console.WriteLine($"Windows visible in screenshots: {visible}");
            
            // Update all windows to apply the new capture exclusion setting
            ChatBoxWindow.Instance?.UpdateCaptureExclusion();
            MonitorWindow.Instance?.UpdateCaptureExclusion();
            MainWindow.Instance?.UpdateCaptureExclusion();
            ToolbarWindow.Instance?.UpdateCaptureExclusion();
            
            // Update this window as well
            UpdateCaptureExclusion();
        }
        
        private void LogExtraDebugStuffCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool enabled = logExtraDebugStuffCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetLogExtraDebugStuff(enabled);
            Console.WriteLine($"Log extra debug stuff: {enabled}");
        }

        private void PersistWindowSizeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool enabled = persistWindowSizeCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetPersistWindowSizeEnabled(enabled);
            Console.WriteLine($"Persist window size: {enabled}");
        }

        private void PlayCompletionSoundCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            bool enabled = playCompletionSoundCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetCompletionSoundEnabled(enabled);
        }

        private void ResetPromptsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will reset the LLM translation prompts for all services " +
                "(Gemini, Ollama, ChatGPT, llama.cpp) to the latest defaults.\n\n" +
                "Any custom prompt modifications will be lost.\n\nContinue?",
                "Reset Prompts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                ConfigManager.Instance.ResetAllPromptsToDefault();
                MessageBox.Show("All prompts have been reset to defaults.", "Reset Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SnapshotToggleModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool enabled = snapshotToggleModeCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetSnapshotToggleMode(enabled);
            Console.WriteLine($"Snapshot toggle mode: {enabled}");
        }
        
        // Language swap button handler
        private void ServerSetupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ServerSetupDialog.ShowDialogSafe(fromSettings: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening server setup dialog: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"Error opening server setup dialog: {ex.Message}");
            }
        }
        
        private void SwapLanguagesButton_Click(object sender, RoutedEventArgs e)
        {
            // Store the current language codes
            string sourceCode = GetLanguageCode(sourceLanguageComboBox);
            string targetCode = GetLanguageCode(targetLanguageComboBox);
            
            // Find and select matching items by language code (Tag)
            foreach (ComboBoxItem item in sourceLanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == targetCode)
                {
                    sourceLanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            
            foreach (ComboBoxItem item in targetLanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == sourceCode)
                {
                    targetLanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // The SelectionChanged events will handle updating the MainWindow
            Console.WriteLine($"Languages swapped: {GetLanguageCode(sourceLanguageComboBox)} ⇄ {GetLanguageCode(targetLanguageComboBox)}");
            
            // Trigger fresh OCR/translation after swapping languages
            Logic.Instance.ResetHash();
            Logic.Instance.ClearAllTextObjects();
            MainWindow.Instance.SetOCRCheckIsWanted(true);
            MonitorWindow.Instance.RefreshOverlays();
        }
        
        // Helper method to get language code from ComboBox
        private string GetLanguageCode(ComboBox comboBox)
        {
            return ((ComboBoxItem)comboBox.SelectedItem).Tag?.ToString() ?? "";
        }
        
        // Block detection settings
        private void BlockDetectionPowerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }
            
            // Update block detection power in MonitorWindow
            if (MonitorWindow.Instance.blockDetectionPowerTextBox != null)
            {
                // MonitorWindow.Instance.blockDetectionPowerTextBox.Text = blockDetectionPowerTextBox.Text;
                MonitorWindow.Instance.blockDetectionPowerTextBox.Visibility = Visibility.Collapsed;
            }
            
            // BlockDetectionManager has been removed. 
            // This setting is now obsolete as we use Horizontal/Vertical glue.
            // We'll just keep the UI field for now but it does nothing.
            // Or better, we should probably hide it or repurpose it, but user asked to remove the functionality.
            
            if (double.TryParse(blockDetectionPowerTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double power))
            {
                // Just update the config if it still exists there, but logic ignores it.
                 ConfigManager.Instance.SetBlockDetectionScale(power);
            }
        }
        
        private void SettleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent overriding values from config
            if (_isInitializing)
            {
                return;
            }
            
            // Update settle time in ConfigManager
            if (float.TryParse(settleTimeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float settleTime) && settleTime >= 0)
            {
                ConfigManager.Instance.SetBlockDetectionSettleTime(settleTime);
                Console.WriteLine($"Block detection settle time set to: {settleTime:F2} seconds");
                
                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from ConfigManager
                settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2", CultureInfo.InvariantCulture);
            }
        }
        
        // Handle Clear Context button click
        private void ClearContextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Clearing translation context and history");
                
                // Clear translation history in MainWindow
                MainWindow.Instance.ClearTranslationHistory();
                
                // Reset hash to force new translation on next capture
                Logic.Instance.ResetHash();
                
                // Clear any existing text objects
                Logic.Instance.ClearAllTextObjects();
                
                // Force OCR/translation to run again if active
                if (MainWindow.Instance.GetIsStarted())
                {
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                }
                
                // Show success message
                MessageBox.Show("Translation context and history have been cleared.", 
                    "Context Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
                
                Console.WriteLine("Translation context cleared successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing translation context: {ex.Message}");
                MessageBox.Show($"Error clearing context: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateLanguageComboBoxes()
        {
            // Populate both combo boxes from the language codes list
            // Display names are derived from Logic.GetLanguageName() to avoid duplication
            // Sort alphabetically by display name for easier lookup
            var sortedLanguages = _languageCodes
                .Select(code => new { Code = code, Name = Logic.GetLanguageName(code) })
                .OrderBy(lang => lang.Name)
                .ToList();
            
            foreach (var comboBox in new[] { sourceLanguageComboBox, targetLanguageComboBox })
            {
                if (comboBox == null) continue;
                
                var handler = comboBox == sourceLanguageComboBox 
                    ? (SelectionChangedEventHandler)SourceLanguageComboBox_SelectionChanged 
                    : TargetLanguageComboBox_SelectionChanged;
                
                comboBox.SelectionChanged -= handler;
                comboBox.Items.Clear();
                foreach (var lang in sortedLanguages)
                {
                    comboBox.Items.Add(new ComboBoxItem { Content = lang.Name, Tag = lang.Code });
                }
                comboBox.SelectionChanged += handler;
            }
        }
        
        private void PopulateWhisperLanguageComboBox()
        {
            var languages = new List<(string Name, string Code)>
            {
                ("Auto", "Auto"), ("English", "en"), ("Japanese", "ja"), ("Chinese", "zh"),
                ("Spanish", "es"), ("French", "fr"), ("German", "de"), ("Italian", "it"),
                ("Korean", "ko"), ("Portuguese", "pt"), ("Russian", "ru"), ("Arabic", "ar"),
                ("Hindi", "hi"), ("Turkish", "tr"), ("Dutch", "nl"), ("Polish", "pl"),
                ("Swedish", "sv"), ("Norwegian", "no"), ("Danish", "da"), ("Finnish", "fi"),
                ("Czech", "cs"), ("Hungarian", "hu"), ("Romanian", "ro"), ("Greek", "el"),
                ("Thai", "th"), ("Vietnamese", "vi"), ("Indonesian", "id"), ("Malay", "ms"),
                ("Hebrew", "he"), ("Ukrainian", "uk")
                // Add more languages as needed
            };

            if (whisperSourceLanguageComboBox != null)
            {
                whisperSourceLanguageComboBox.Items.Clear();
                foreach (var lang in languages)
                {
                    whisperSourceLanguageComboBox.Items.Add(new ComboBoxItem { Content = lang.Name, Tag = lang.Code });
                }
            }
        }

        private void MaxSettleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(maxSettleTimeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double maxSettleTime) && maxSettleTime >= 0)
            {
                ConfigManager.Instance.SetBlockDetectionMaxSettleTime(maxSettleTime);
                Console.WriteLine($"Max settle time set to: {maxSettleTime}");
            }
            else
            {
                // If invalid, reset to current value from config
                maxSettleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionMaxSettleTime().ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        private void OverlayClearDelayTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(overlayClearDelayTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double delay) && delay >= 0)
            {
                ConfigManager.Instance.SetOverlayClearDelaySeconds(delay);
                Console.WriteLine($"Overlay clear delay set to: {delay}");
            }
            else
            {
                // If invalid, reset to current value from config
                overlayClearDelayTextBox.Text = ConfigManager.Instance.GetOverlayClearDelaySeconds().ToString("F2", CultureInfo.InvariantCulture);
            }
        }
        
        private void CooldownHashCompareLengthTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(cooldownHashCompareLengthTextBox.Text, out int length) && length >= 0)
            {
                ConfigManager.Instance.SetCooldownHashCompareLength(length);
                Console.WriteLine($"Cooldown hash compare length set to: {length}");
            }
            else
            {
                // If invalid, reset to current value from config
                cooldownHashCompareLengthTextBox.Text = ConfigManager.Instance.GetCooldownHashCompareLength().ToString();
            }
        }
        
        #region Screenshot Exclusion
        
        private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
        {
            WindowCaptureHelper.SetExcludeFromCapture(this, "Settings");
        }
        
        public void UpdateCaptureExclusion()
        {
            WindowCaptureHelper.SetExcludeFromCapture(this, "Settings");
        }
        
        #endregion
        
        #region Tooltip Exclusion from Screenshots
        
        // Setup tooltip exclusion from screenshots
        private void SetupTooltipExclusion()
        {
            // Use ToolTipService to add an event handler for when any tooltip opens
            this.AddHandler(ToolTipService.ToolTipOpeningEvent, new RoutedEventHandler(OnToolTipOpening));
        }
        
        private void OnToolTipOpening(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WindowCaptureHelper.ExcludeTooltipFromCapture(fullEnumeration: true);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        
        // Lesson Settings event handlers
        private void LessonPromptTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
            
            ConfigManager.Instance.SetLessonPromptTemplate(lessonPromptTemplateTextBox.Text);
            Console.WriteLine("Lesson prompt template updated from settings");
        }
        
        private void LessonUrlTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
            
            ConfigManager.Instance.SetLessonUrlTemplate(lessonUrlTemplateTextBox.Text);
            Console.WriteLine("Lesson URL template updated from settings");
        }
        
        private void LessonSetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            // Default prompt template
            string defaultPrompt = "Create a comprehensive lesson to help me learn about this Japanese text and its translation: \"{0}\"\n\nPlease include:\n1. A detailed breakdown table with columns for: Japanese text, Reading (furigana), Literal meaning, and Grammar notes\n2. Key vocabulary with example sentences\n3. Cultural or contextual notes if relevant\n4. At the end, provide 5 helpful flashcards in a clear format for memorization";
            
            // Default URL template
            string defaultUrl = "https://chat.openai.com/?q={0}";
            
            // Temporarily remove event handlers to prevent triggering changes
            lessonPromptTemplateTextBox.LostFocus -= LessonPromptTemplateTextBox_LostFocus;
            lessonUrlTemplateTextBox.LostFocus -= LessonUrlTemplateTextBox_LostFocus;
            
            // Set defaults in config
            ConfigManager.Instance.SetLessonPromptTemplate(defaultPrompt);
            ConfigManager.Instance.SetLessonUrlTemplate(defaultUrl);
            
            // Update UI
            lessonPromptTemplateTextBox.Text = defaultPrompt;
            lessonUrlTemplateTextBox.Text = defaultUrl;
            
            // Re-attach event handlers
            lessonPromptTemplateTextBox.LostFocus += LessonPromptTemplateTextBox_LostFocus;
            lessonUrlTemplateTextBox.LostFocus += LessonUrlTemplateTextBox_LostFocus;
            
            Console.WriteLine("Lesson settings reset to defaults");
        }
        
        
        #endregion

        #region Screenshot Settings

        private void ScreenshotFilenameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ConfigManager.Instance.SetScreenshotFilename(screenshotFilenameTextBox.Text);
        }

        private void ScreenshotFolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ConfigManager.Instance.SetScreenshotFolder(screenshotFolderTextBox.Text);
        }

        private void ScreenshotTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (screenshotTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string type = selectedItem.Content?.ToString() ?? "Both";
                ConfigManager.Instance.SetScreenshotType(type);
            }
        }

        private void ScreenshotFolderBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select screenshot save folder";
                string currentFolder = screenshotFolderTextBox.Text;
                if (!string.IsNullOrWhiteSpace(currentFolder))
                {
                    string fullPath = System.IO.Path.IsPathRooted(currentFolder)
                        ? currentFolder
                        : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, currentFolder);
                    if (System.IO.Directory.Exists(fullPath))
                    {
                        dialog.SelectedPath = fullPath;
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    screenshotFolderTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void ScreenshotFolderOpen_Click(object sender, RoutedEventArgs e)
        {
            string folder = screenshotFolderTextBox.Text;
            if (!System.IO.Path.IsPathRooted(folder))
            {
                folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder);
            }

            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        #endregion
    }
}
