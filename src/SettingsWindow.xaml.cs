using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
                e.Cancel = true;  // Cancel the close
                this.Hide();      // Just hide the window
                // Re-enable hotkeys when settings window is hidden
                HotkeyManager.Instance.SetEnabled(true);
                KeyboardShortcuts.SetShortcutsEnabled(true);
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
            // Only process hotkeys at window level if global hotkeys are disabled
            // (When global hotkeys are enabled, the global hook handles them)
            if (!HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                var modifiers = System.Windows.Input.Keyboard.Modifiers;
                bool handled = HotkeyManager.Instance.HandleKeyDown(e.Key, modifiers);
                
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
            
            // Load source language either from config or MainWindow as fallback
            string configSourceLanguage = ConfigManager.Instance.GetSourceLanguage();
            if (!string.IsNullOrEmpty(configSourceLanguage))
            {
                // First try to load from config
                foreach (ComboBoxItem item in sourceLanguageComboBox.Items)
                {
                    if (string.Equals(item.Content.ToString(), configSourceLanguage, StringComparison.OrdinalIgnoreCase))
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
            else if (MainWindow.Instance.sourceLanguageComboBox != null && 
                     MainWindow.Instance.sourceLanguageComboBox.SelectedIndex >= 0)
            {
                // Fallback to MainWindow if config doesn't have a value
                sourceLanguageComboBox.SelectedIndex = MainWindow.Instance.sourceLanguageComboBox.SelectedIndex;
            }
            
            // Load target language either from config or MainWindow as fallback
            string configTargetLanguage = ConfigManager.Instance.GetTargetLanguage();
            if (!string.IsNullOrEmpty(configTargetLanguage))
            {
                // First try to load from config
                foreach (ComboBoxItem item in targetLanguageComboBox.Items)
                {
                    if (string.Equals(item.Content.ToString(), configTargetLanguage, StringComparison.OrdinalIgnoreCase))
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
            else if (MainWindow.Instance.targetLanguageComboBox != null && 
                     MainWindow.Instance.targetLanguageComboBox.SelectedIndex >= 0)
            {
                // Fallback to MainWindow if config doesn't have a value
                targetLanguageComboBox.SelectedIndex = MainWindow.Instance.targetLanguageComboBox.SelectedIndex;
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
            
            
            // Block detection power is deprecated/removed, hiding or setting to default
            blockDetectionPowerTextBox.Visibility = Visibility.Collapsed; 
            if (blockDetectionPowerLabel != null) blockDetectionPowerLabel.Visibility = Visibility.Collapsed;
            
            settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2");
            maxSettleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionMaxSettleTime().ToString("F2");
            overlayClearDelayTextBox.Text = ConfigManager.Instance.GetOverlayClearDelaySeconds().ToString("F2");
            
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
            
            // Initialize Ollama settings
            ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
            ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
            ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();
            
            // Initialize llama.cpp settings
            // Temporarily remove event handlers to prevent triggering changes during initialization
            llamacppUrlTextBox.TextChanged -= LlamacppUrlTextBox_TextChanged;
            llamacppPortTextBox.TextChanged -= LlamacppPortTextBox_TextChanged;
            
            llamacppUrlTextBox.Text = ConfigManager.Instance.GetLlamaCppUrl();
            llamacppPortTextBox.Text = ConfigManager.Instance.GetLlamaCppPort();
            
            // Reattach event handlers
            llamacppUrlTextBox.TextChanged += LlamacppUrlTextBox_TextChanged;
            llamacppPortTextBox.TextChanged += LlamacppPortTextBox_TextChanged;
            
            // Update service-specific settings visibility based on selected service
            UpdateServiceSpecificSettings(currentService);
            
            // Load the current service's prompt
            LoadCurrentServicePrompt();
            
            // Load TTS settings
            
            // Temporarily remove TTS event handlers
            ttsEnabledCheckBox.Checked -= TtsEnabledCheckBox_CheckedChanged;
            ttsEnabledCheckBox.Unchecked -= TtsEnabledCheckBox_CheckedChanged;
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
            
            // Set TTS enabled state
            ttsEnabledCheckBox.IsChecked = ConfigManager.Instance.IsTtsEnabled();
            
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
            
            // Re-attach TTS event handlers
            ttsEnabledCheckBox.Checked += TtsEnabledCheckBox_CheckedChanged;
            ttsEnabledCheckBox.Unchecked += TtsEnabledCheckBox_CheckedChanged;
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
            audioProcessingProviderComboBox.SelectedIndex = 0; // Only one for now
            openAiRealtimeApiKeyPasswordBox.Password = ConfigManager.Instance.GetOpenAiRealtimeApiKey();
            openAiSilenceDurationMsTextBox.Text = ConfigManager.Instance.GetOpenAiSilenceDurationMs().ToString();
            
            // Load speech prompt
            openAiSpeechPromptTextBox.Text = ConfigManager.Instance.GetOpenAISpeechPrompt();
            
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
                string language = selectedItem.Content.ToString() ?? "ja";
                Console.WriteLine($"Settings: Source language changed to: {language}");
                
                // Cleanup audio preloading when language changes
                AudioPreloadService.Instance.CancelAllPreloads();
                AudioPlaybackManager.Instance.StopCurrentPlayback();
                AudioPreloadService.Instance.ClearAudioCache();
                
                // Save to config
                ConfigManager.Instance.SetSourceLanguage(language);
                
                // Update MainWindow source language
                if (MainWindow.Instance.sourceLanguageComboBox != null)
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in MainWindow.Instance.sourceLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), language, StringComparison.OrdinalIgnoreCase))
                        {
                            MainWindow.Instance.sourceLanguageComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
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
                string language = selectedItem.Content.ToString() ?? "en";
                Console.WriteLine($"Settings: Target language changed to: {language}");
                
                // Cleanup audio preloading when language changes
                AudioPreloadService.Instance.CancelAllPreloads();
                AudioPlaybackManager.Instance.StopCurrentPlayback();
                AudioPreloadService.Instance.ClearAudioCache();
                
                // Save to config
                ConfigManager.Instance.SetTargetLanguage(language);
                
                // Update MainWindow target language
                if (MainWindow.Instance.targetLanguageComboBox != null)
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in MainWindow.Instance.targetLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), language, StringComparison.OrdinalIgnoreCase))
                        {
                            MainWindow.Instance.targetLanguageComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
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
            }
        }
        
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
                    
                if (double.TryParse(mangaOcrOverlapTextBox.Text, out double percent) && percent >= 0 && percent <= 100)
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
                    mangaOcrOverlapTextBox.Text = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent().ToString("F1");
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
                    
                if (double.TryParse(mangaOcrYoloConfidenceTextBox.Text, out double confidence) && confidence >= 0.0 && confidence <= 1.0)
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
                    mangaOcrYoloConfidenceTextBox.Text = ConfigManager.Instance.GetMangaOcrYoloConfidence().ToString("F2");
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
                bool isEasyOcrSelected = string.Equals(selectedOcr, "EasyOCR", StringComparison.OrdinalIgnoreCase);
                bool isDocTrSelected = string.Equals(selectedOcr, "docTR", StringComparison.OrdinalIgnoreCase);

                // Confidence settings are only useful for EasyOCR, docTR, and Google Vision
                bool showConfidenceSettings = isEasyOcrSelected || isDocTrSelected || isGoogleVisionSelected || isPaddleOcrSelected;
                
                // EasyOCR, PaddleOCR and docTR don't use character-level confidence (or at least we don't use it), so hide that specific setting
                // Google Vision DOES support character-level confidence
                bool showLetterConfidence = showConfidenceSettings && !isEasyOcrSelected && !isDocTrSelected && !isPaddleOcrSelected;

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
                    minLineConfidenceLabel.Visibility = showConfidenceSettings ? Visibility.Visible : Visibility.Collapsed;
                if (minLineConfidenceTextBox != null)
                {
                    minLineConfidenceTextBox.Visibility = showConfidenceSettings ? Visibility.Visible : Visibility.Collapsed;
                    if (showConfidenceSettings)
                    {
                        minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence(selectedOcr).ToString();
                    }
                }

                // Glue settings are available for all OCRs EXCEPT MangaOCR (which has its own logic/model)
                bool shouldShowGlueSettings = !isMangaOcrSelected;
                
                // Color Correction is only for Windows OCR and Google Cloud Vision
                bool isWindowsOcrSelected = string.Equals(selectedOcr, "Windows OCR", StringComparison.OrdinalIgnoreCase);
                bool showColorCorrection = isGoogleVisionSelected || isWindowsOcrSelected;
                
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

                // Show/hide PaddleOCR settings
                if (paddleOcrAngleClsLabel != null)
                    paddleOcrAngleClsLabel.Visibility = isPaddleOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                if (paddleOcrAngleClsCheckBox != null)
                    paddleOcrAngleClsCheckBox.Visibility = isPaddleOcrSelected ? Visibility.Visible : Visibility.Collapsed;
                
                if (isPaddleOcrSelected && paddleOcrAngleClsCheckBox != null)
                {
                    paddleOcrAngleClsCheckBox.IsChecked = ConfigManager.Instance.GetPaddleOcrUseAngleCls();
                }

                // Show/hide Google Vision-specific settings
                if (googleVisionApiKeyLabel != null)
                    googleVisionApiKeyLabel.Visibility = isGoogleVisionSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleVisionApiKeyGrid != null)
                    googleVisionApiKeyGrid.Visibility = isGoogleVisionSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Text Grouping settings (Universal)
                Visibility glueVisibility = shouldShowGlueSettings ? Visibility.Visible : Visibility.Collapsed;

                if (googleVisionGroupingLabel != null)
                    googleVisionGroupingLabel.Visibility = glueVisibility;
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
                    if (googleVisionHorizontalGlueTextBox != null)
                    {
                        googleVisionHorizontalGlueTextBox.Text = ConfigManager.Instance.GetHorizontalGlue(selectedOcr).ToString("F2");
                    }
                    
                    if (googleVisionVerticalGlueTextBox != null)
                    {
                        googleVisionVerticalGlueTextBox.Text = ConfigManager.Instance.GetVerticalGlue(selectedOcr).ToString("F2");
                    }
                    
                    if (googleVisionVerticalGlueOverlapTextBox != null)
                    {
                        googleVisionVerticalGlueOverlapTextBox.Text = ConfigManager.Instance.GetVerticalGlueOverlap(selectedOcr).ToString("F1");
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
                        mangaOcrOverlapTextBox.Text = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent().ToString("F1");
                    }
                    
                    if (mangaOcrYoloConfidenceTextBox != null)
                    {
                        mangaOcrYoloConfidenceTextBox.Text = ConfigManager.Instance.GetMangaOcrYoloConfidence().ToString("F2");
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
            
            if (double.TryParse(googleVisionHorizontalGlueTextBox.Text, out double value))
            {
                // Clamp to range (-2000 to 2000)
                value = Math.Max(-2000.0, Math.Min(2000.0, value));
                googleVisionHorizontalGlueTextBox.Text = value.ToString("F2");
                
                ConfigManager.Instance.SetHorizontalGlue(currentOcr, value);
                Console.WriteLine($"{currentOcr} horizontal glue set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionHorizontalGlueTextBox.Text = ConfigManager.Instance.GetHorizontalGlue(currentOcr).ToString("F2");
            }
        }

        // Google Vision Vertical Glue text changed
        private void GoogleVisionVerticalGlueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            if (double.TryParse(googleVisionVerticalGlueTextBox.Text, out double value))
            {
                // Clamp to range (-2000 to 2000)
                value = Math.Max(-2000.0, Math.Min(2000.0, value));
                googleVisionVerticalGlueTextBox.Text = value.ToString("F2");
                
                ConfigManager.Instance.SetVerticalGlue(currentOcr, value);
                Console.WriteLine($"{currentOcr} vertical glue set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionVerticalGlueTextBox.Text = ConfigManager.Instance.GetVerticalGlue(currentOcr).ToString("F2");
            }
        }

        // Google Vision Vertical Glue Overlap text changed
        private void GoogleVisionVerticalGlueOverlapTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;

            // Get current OCR method to save settings per-OCR
            string currentOcr = MainWindow.Instance.GetSelectedOcrMethod();
            
            if (double.TryParse(googleVisionVerticalGlueOverlapTextBox.Text, out double value))
            {
                // Clamp to range (0 to 100)
                value = Math.Max(0, Math.Min(100.0, value));
                googleVisionVerticalGlueOverlapTextBox.Text = value.ToString("F1");
                
                ConfigManager.Instance.SetVerticalGlueOverlap(currentOcr, value);
                Console.WriteLine($"{currentOcr} vertical glue overlap set to {value}");
                
                // Force refresh
                Logic.Instance.ResetHash();
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
            else
            {
                // Reset to current value if invalid
                googleVisionVerticalGlueOverlapTextBox.Text = ConfigManager.Instance.GetVerticalGlueOverlap(currentOcr).ToString("F1");
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
            // Store the current selections
            int sourceIndex = sourceLanguageComboBox.SelectedIndex;
            int targetIndex = targetLanguageComboBox.SelectedIndex;
            
            // Swap the selections
            sourceLanguageComboBox.SelectedIndex = targetIndex;
            targetLanguageComboBox.SelectedIndex = sourceIndex;
            
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
            return ((ComboBoxItem)comboBox.SelectedItem).Content.ToString() ?? "";
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
            
            if (double.TryParse(blockDetectionPowerTextBox.Text, out double power))
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
            if (float.TryParse(settleTimeTextBox.Text, out float settleTime) && settleTime >= 0)
            {
                ConfigManager.Instance.SetBlockDetectionSettleTime(settleTime);
                Console.WriteLine($"Block detection settle time set to: {settleTime:F2} seconds");
                
                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from ConfigManager
                settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2");
            }
        }
        
        // Translation service changed
        private void TranslationServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"SettingsWindow.TranslationServiceComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
            }
            if (_isInitializing)
            {
                Console.WriteLine("Skipping translation service change during initialization");
                return;
            }
            
            try
            {
                if (translationServiceComboBox == null)
                {
                    Console.WriteLine("Translation service combo box not initialized yet");
                    return;
                }
                
                if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                    
                    Console.WriteLine($"SettingsWindow translation service changed to: '{selectedService}'");
                    
                    // Save the selected service to config
                    ConfigManager.Instance.SetTranslationService(selectedService);
                    
                    // Update service-specific settings visibility
                    UpdateServiceSpecificSettings(selectedService);
                    
                    // Load the prompt for the selected service
                    LoadCurrentServicePrompt();
                    
                    // Only trigger retranslation if not initializing (i.e., user changed it manually)
                    if (!_isInitializing)
                    {
                        Console.WriteLine("Translation service changed. Triggering retranslation...");
                        
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
                            Console.WriteLine("Triggered OCR/translation refresh after translation service change");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling translation service change: {ex.Message}");
            }
        }
        
        // Load prompt for the currently selected translation service
        private void LoadCurrentServicePrompt()
        {
            try
            {
                if (translationServiceComboBox == null || promptTemplateTextBox == null)
                {
                    Console.WriteLine("Translation service controls not initialized yet. Skipping prompt loading.");
                    return;
                }
                
                if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                    string prompt = ConfigManager.Instance.GetServicePrompt(selectedService);
                    
                    // Update the text box
                    promptTemplateTextBox.Text = prompt;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading prompt template: {ex.Message}");
            }
        }
        
        // Save prompt button clicked
        private void SavePromptButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt(clearContextAndRefresh: true);
        }
        
        // Restore default prompt button clicked
        private void RestoreDefaultPromptButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                    string defaultPrompt = ConfigManager.Instance.GetDefaultPrompt(selectedService);
                    
                    // Set the default prompt in the text box (user can then save it if they want)
                    promptTemplateTextBox.Text = defaultPrompt;
                    Console.WriteLine($"Default prompt restored for {selectedService}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error restoring default prompt: {ex.Message}");
            }
        }
        
        // Text box lost focus - save prompt
        private void PromptTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt(clearContextAndRefresh: false);
        }
        
        // Save the current prompt to the selected service
        private void SaveCurrentPrompt(bool clearContextAndRefresh = false)
        {
            if (translationServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedService = selectedItem.Content.ToString() ?? "Gemini";
                string prompt = promptTemplateTextBox.Text;
                
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    // Save to config
                    bool success = ConfigManager.Instance.SaveServicePrompt(selectedService, prompt);
                    
                    if (success)
                    {
                        Console.WriteLine($"Prompt saved for {selectedService}");
                        
                        // Clear context and refresh if requested (button click)
                        if (clearContextAndRefresh)
                        {
                            // Clear context (same as "Clear Context" button)
                            Console.WriteLine("Clearing translation context and history after prompt save");
                            
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
                                Console.WriteLine("Triggered OCR/translation refresh after prompt save");
                            }
                        }
                    }
                }
            }
        }
        
        // Update service-specific settings visibility
        private void UpdateServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isOllamaSelected = string.Equals(selectedService, "Ollama", StringComparison.OrdinalIgnoreCase);
                bool isGeminiSelected = string.Equals(selectedService, "Gemini", StringComparison.OrdinalIgnoreCase);
                bool isChatGptSelected = string.Equals(selectedService, "ChatGPT", StringComparison.OrdinalIgnoreCase);
                bool isLlamacppSelected = string.Equals(selectedService, "llama.cpp", StringComparison.OrdinalIgnoreCase);
                bool isGoogleTranslateSelected = string.Equals(selectedService, "Google Translate", StringComparison.OrdinalIgnoreCase);
                
                // Don't return early - set visibility for whatever elements are available
                // This ensures partial initialization doesn't prevent any visibility updates
                
                // Show/hide Gemini-specific settings
                if (geminiApiKeyLabel != null)
                    geminiApiKeyLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                if (geminiApiKeyPasswordBox != null)
                    geminiApiKeyPasswordBox.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                if (geminiApiKeyHelpText != null)
                    geminiApiKeyHelpText.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                if (geminiModelLabel != null)
                    geminiModelLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                if (geminiModelGrid != null)
                    geminiModelGrid.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Ollama-specific settings
                if (ollamaUrlLabel != null)
                    ollamaUrlLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                if (ollamaUrlGrid != null)
                    ollamaUrlGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                if (ollamaPortLabel != null)
                    ollamaPortLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                if (ollamaPortTextBox != null)
                    ollamaPortTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                if (ollamaModelLabel != null)
                    ollamaModelLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                if (ollamaModelGrid != null)
                    ollamaModelGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide ChatGPT-specific settings
                if (chatGptApiKeyLabel != null)
                    chatGptApiKeyLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                if (chatGptApiKeyGrid != null)
                    chatGptApiKeyGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                if (chatGptModelLabel != null)
                    chatGptModelLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                if (chatGptModelGrid != null)
                    chatGptModelGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                if (chatGptMaxTokensLabel != null)
                    chatGptMaxTokensLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                if (chatGptMaxTokensTextBox != null)
                    chatGptMaxTokensTextBox.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide llama.cpp-specific settings
                if (llamacppUrlLabel != null)
                    llamacppUrlLabel.Visibility = isLlamacppSelected ? Visibility.Visible : Visibility.Collapsed;
                if (llamacppUrlGrid != null)
                    llamacppUrlGrid.Visibility = isLlamacppSelected ? Visibility.Visible : Visibility.Collapsed;
                if (llamacppPortLabel != null)
                    llamacppPortLabel.Visibility = isLlamacppSelected ? Visibility.Visible : Visibility.Collapsed;
                if (llamacppPortTextBox != null)
                    llamacppPortTextBox.Visibility = isLlamacppSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Google Translate-specific settings
                if (googleTranslateServiceTypeLabel != null)
                    googleTranslateServiceTypeLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateServiceTypeComboBox != null)
                    googleTranslateServiceTypeComboBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateMappingLabel != null)
                    googleTranslateMappingLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateMappingCheckBox != null)
                    googleTranslateMappingCheckBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Hide prompt template for Google Translate
                bool showPromptTemplate = !isGoogleTranslateSelected;
                
                // API key is only visible for Google Translate if Cloud API is selected
                bool showGoogleTranslateApiKey = isGoogleTranslateSelected && 
                    googleTranslateServiceTypeComboBox != null &&
                    (googleTranslateServiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() == "Cloud API (paid)";
                    
                if (googleTranslateApiKeyLabel != null)
                    googleTranslateApiKeyLabel.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateApiKeyGrid != null)
                    googleTranslateApiKeyGrid.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide prompt template and related controls for Google Translate
                if (promptLabel != null)
                    promptLabel.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                if (promptTemplateTextBox != null)
                    promptTemplateTextBox.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                if (savePromptButton != null)
                    savePromptButton.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                if (restoreDefaultPromptButton != null)
                    restoreDefaultPromptButton.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                
                // Load service-specific settings if they're being shown
                if (isGeminiSelected)
                {
                    if (geminiApiKeyPasswordBox != null)
                        geminiApiKeyPasswordBox.Password = ConfigManager.Instance.GetGeminiApiKey();
                    
                    // Set selected Gemini model
                    string geminiModel = ConfigManager.Instance.GetGeminiModel();
                    
                        // Temporarily remove event handlers to avoid triggering changes
                    geminiModelComboBox.SelectionChanged -= GeminiModelComboBox_SelectionChanged;
                    
                    // First try to find exact match in dropdown items
                    bool found = false;
                    foreach (ComboBoxItem item in geminiModelComboBox.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), geminiModel, StringComparison.OrdinalIgnoreCase))
                        {
                            geminiModelComboBox.SelectedItem = item;
                            found = true;
                            break;
                        }
                    }
                    
                    // If not found in dropdown, set as custom text
                    if (!found)
                    {
                        geminiModelComboBox.Text = geminiModel;
                    }
                    
                    // Reattach event handler
                    geminiModelComboBox.SelectionChanged += GeminiModelComboBox_SelectionChanged;
                }
                else if (isOllamaSelected)
                {
                    if (ollamaUrlTextBox != null)
                        ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
                    if (ollamaPortTextBox != null)
                        ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
                    if (ollamaModelTextBox != null)
                        ollamaModelTextBox.Text = ConfigManager.Instance.GetOllamaModel();
                }
                else if (isChatGptSelected)
                {
                    chatGptApiKeyPasswordBox.Password = ConfigManager.Instance.GetChatGptApiKey();
                    
                    // Set selected model
                    string model = ConfigManager.Instance.GetChatGptModel();
                    foreach (ComboBoxItem item in chatGptModelComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), model, StringComparison.OrdinalIgnoreCase))
                        {
                            chatGptModelComboBox.SelectedItem = item;
                            break;
                        }
                    }
                    
                    // Set max completion tokens
                    int maxTokens = ConfigManager.Instance.GetChatGptMaxCompletionTokens();
                    if (chatGptMaxTokensTextBox != null)
                        chatGptMaxTokensTextBox.Text = maxTokens.ToString();
                }
                else if (isLlamacppSelected)
                {
                    if (llamacppUrlTextBox != null && llamacppPortTextBox != null)
                    {
                        // Temporarily remove event handlers to prevent triggering changes when switching services
                        llamacppUrlTextBox.TextChanged -= LlamacppUrlTextBox_TextChanged;
                        llamacppPortTextBox.TextChanged -= LlamacppPortTextBox_TextChanged;
                        
                        llamacppUrlTextBox.Text = ConfigManager.Instance.GetLlamaCppUrl();
                        llamacppPortTextBox.Text = ConfigManager.Instance.GetLlamaCppPort();
                        
                        // Reattach event handlers
                        llamacppUrlTextBox.TextChanged += LlamacppUrlTextBox_TextChanged;
                        llamacppPortTextBox.TextChanged += LlamacppPortTextBox_TextChanged;
                    }
                }
                else if (isGoogleTranslateSelected)
                {
                    // Set Google Translate service type
                    bool useCloudApi = ConfigManager.Instance.GetGoogleTranslateUseCloudApi();
                    
                    if (googleTranslateServiceTypeComboBox != null)
                    {
                        // Temporarily remove event handler
                        googleTranslateServiceTypeComboBox.SelectionChanged -= GoogleTranslateServiceTypeComboBox_SelectionChanged;
                        
                        googleTranslateServiceTypeComboBox.SelectedIndex = useCloudApi ? 1 : 0; // 0 = Free, 1 = Cloud API
                        
                        // Reattach event handler
                        googleTranslateServiceTypeComboBox.SelectionChanged += GoogleTranslateServiceTypeComboBox_SelectionChanged;
                    }
                    
                    // Set API key if using Cloud API
                   // if (useCloudApi)
                    if (googleTranslateApiKeyPasswordBox != null)
                    {
                        googleTranslateApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTranslateApiKey();
                    }
                    
                    // Set language mapping checkbox
                    if (googleTranslateMappingCheckBox != null)
                        googleTranslateMappingCheckBox.IsChecked = ConfigManager.Instance.GetGoogleTranslateAutoMapLanguages();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service-specific settings: {ex.Message}");
            }
        }
        
        private void UpdateTtsServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isElevenLabsSelected = selectedService == "ElevenLabs";
                bool isGoogleTtsSelected = selectedService == "Google Cloud TTS";
                
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS service-specific settings: {ex.Message}");
            }
        }
        
        // Gemini API Key changed
        private void GeminiApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                string apiKey = geminiApiKeyPasswordBox.Password.Trim();
                
                // Update the config
                ConfigManager.Instance.SetGeminiApiKey(apiKey);
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Gemini API key updated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Gemini API key: {ex.Message}");
            }
        }
        
        // Ollama URL changed
        private void OllamaUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = ollamaUrlTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                ConfigManager.Instance.SetOllamaUrl(url);
            }
        }
        
        // Ollama Port changed
        private void OllamaPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string port = ollamaPortTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(port))
            {
                // Validate that the port is a number
                if (int.TryParse(port, out _))
                {
                    ConfigManager.Instance.SetOllamaPort(port);
                }
                else
                {
                    // Reset to default if invalid
                    ollamaPortTextBox.Text = "11434";
                }
            }
        }
        
        // Ollama Model changed
        private void OllamaModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip if initializing or if the sender isn't the expected TextBox
            if (_isInitializing || sender != ollamaModelTextBox)
                return;
                
            string sanitizedModel = ollamaModelTextBox.Text.Trim();
          
            
            // Save valid model to config
            ConfigManager.Instance.SetOllamaModel(sanitizedModel);
            Console.WriteLine($"Ollama model set to: {sanitizedModel}");
            
            // Trigger retranslation if the current service is Ollama
            if (ConfigManager.Instance.GetCurrentTranslationService() == "Ollama")
            {
                Console.WriteLine("Ollama model changed. Triggering retranslation...");
                
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
        
        // llama.cpp URL changed
        private void LlamacppUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip if initializing or if the sender isn't the expected TextBox
            if (_isInitializing || sender != llamacppUrlTextBox)
                return;
                
            string url = llamacppUrlTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                ConfigManager.Instance.SetLlamaCppUrl(url);
            }
        }
        
        // llama.cpp Port changed
        private void LlamacppPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip if initializing or if the sender isn't the expected TextBox
            if (_isInitializing || sender != llamacppPortTextBox)
                return;
                
            string port = llamacppPortTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(port))
            {
                // Validate that the port is a number
                if (int.TryParse(port, out _))
                {
                    ConfigManager.Instance.SetLlamaCppPort(port);
                }
                else
                {
                    // Reset to default if invalid
                    llamacppPortTextBox.Text = "8080";
                }
            }
        }
        
        // Model downloader instance
        private readonly OllamaModelDownloader _modelDownloader = new OllamaModelDownloader();
        
        private async void TestModelButton_Click(object sender, RoutedEventArgs e)
        {
            string model = ollamaModelTextBox.Text.Trim();
            await _modelDownloader.TestAndDownloadModel(model);
        }
        
        private void ViewModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com/search");
        }
        
        private async void ListInstalledModelsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button while fetching
                listInstalledModelsButton.IsEnabled = false;
                listInstalledModelsButton.Content = "Loading...";
                
                // Fetch models from Ollama
                List<string> models = await FetchInstalledModelsAsync();
                
                // Re-enable button
                listInstalledModelsButton.IsEnabled = true;
                listInstalledModelsButton.Content = "List 'em";
                
                if (models == null || models.Count == 0)
                {
                    MessageBox.Show(
                        "No models found or failed to connect to Ollama server.\n\n" +
                        "Please check:\n" +
                        "1. Ollama is running\n" +
                        "2. The server URL and port in settings are correct\n" +
                        "3. Your firewall/antivirus isn't blocking the connection",
                        "No Models Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                
                // Show model selector dialog
                var dialog = new OllamaModelSelectorWindow
                {
                    Owner = this
                };
                dialog.SetModels(models);
                
                if (dialog.ShowDialog() == true && dialog.SelectedModel != null)
                {
                    // Update the model text box
                    ollamaModelTextBox.Text = dialog.SelectedModel;
                    // The TextChanged event handler will save it to config
                }
            }
            catch (Exception ex)
            {
                listInstalledModelsButton.IsEnabled = true;
                listInstalledModelsButton.Content = "List 'em";
                
                MessageBox.Show(
                    $"Error fetching models: {ex.Message}\n\n" +
                    "Please check your Ollama server settings.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private async Task<List<string>> FetchInstalledModelsAsync()
        {
            try
            {
                string ollamaUrl = ConfigManager.Instance.GetOllamaUrl();
                string ollamaPort = ConfigManager.Instance.GetOllamaPort();
                
                // Correctly format the URL
                if (!ollamaUrl.StartsWith("http://") && !ollamaUrl.StartsWith("https://"))
                {
                    ollamaUrl = "http://" + ollamaUrl;
                }
                
                string apiUrl = $"{ollamaUrl}:{ollamaPort}/api/tags";
                Console.WriteLine($"Fetching models from URL: {apiUrl}");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    HttpResponseMessage response = await client.GetAsync(apiUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Response from Ollama tags API: {jsonResponse}");
                        
                        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                        List<string> models = new List<string>();
                        
                        // Check if the models array exists
                        if (doc.RootElement.TryGetProperty("models", out JsonElement modelsElement))
                        {
                            foreach (JsonElement modelElement in modelsElement.EnumerateArray())
                            {
                                if (modelElement.TryGetProperty("name", out JsonElement nameElement))
                                {
                                    string modelName = nameElement.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(modelName))
                                    {
                                        models.Add(modelName);
                                        Console.WriteLine($"Found installed model: {modelName}");
                                    }
                                }
                            }
                        }
                        
                        return models.OrderBy(m => m).ToList();
                    }
                    else
                    {
                        string errorMessage = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Ollama API error: {response.StatusCode}, {errorMessage}");
                        return new List<string>();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching models: {ex.Message}");
                return new List<string>();
            }
        }
        
        private void GeminiApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/tutorials/setup");
        }
        
        private void GeminiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                
                string model;
                
                // Handle both dropdown selection and manually typed values
                if (geminiModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    model = selectedItem.Content?.ToString() ?? "gemini-2.5-flash";
                }
                else
                {
                    // For manually entered text
                    model = geminiModelComboBox.Text?.Trim() ?? "gemini-2.5-flash";
                }
                
                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetGeminiModel(model);
                    Console.WriteLine($"Gemini model set to: {model}");
                    
                    // Trigger retranslation if the current service is Gemini
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Gemini")
                    {
                        Console.WriteLine("Gemini model changed. Triggering retranslation...");
                        
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
                            Console.WriteLine("Triggered OCR/translation refresh after Gemini model change");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Gemini model: {ex.Message}");
            }
        }
        
        private void ViewGeminiModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ai.google.dev/gemini-api/docs/models");
        }
        
        private void GeminiModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                
                string model = geminiModelComboBox.Text?.Trim() ?? "";
                
                if (!string.IsNullOrWhiteSpace(model))
                {
                    // Save to config
                    ConfigManager.Instance.SetGeminiModel(model);
                    Console.WriteLine($"Gemini model set from text input to: {model}");
                    
                    // Trigger retranslation if the current service is Gemini
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "Gemini")
                    {
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Gemini model from text input: {ex.Message}");
            }
        }
        
        private void OllamaDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://ollama.com");
        }
        
        private void LlamacppDocsLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/ggerganov/llama.cpp");
        }
        
        private void ChatGptApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/api-keys");
        }
        
        private void ViewChatGptModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://platform.openai.com/docs/models");
        }
        
        // ChatGPT API Key changed
        private void ChatGptApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                string apiKey = chatGptApiKeyPasswordBox.Password.Trim();
                
                // Update the config
                ConfigManager.Instance.SetChatGptApiKey(apiKey);
                Console.WriteLine("ChatGPT API key updated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ChatGPT API key: {ex.Message}");
            }
        }
        
        // ChatGPT Model changed
        private void ChatGptModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (chatGptModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string model = selectedItem.Tag?.ToString() ?? "gpt-3.5-turbo";
                    
                    // Save to config
                    ConfigManager.Instance.SetChatGptModel(model);
                    Console.WriteLine($"ChatGPT model set to: {model}");
                    
                    // Trigger retranslation if the current service is ChatGPT
                    if (ConfigManager.Instance.GetCurrentTranslationService() == "ChatGPT")
                    {
                        Console.WriteLine("ChatGPT model changed. Triggering retranslation...");
                        
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
                            Console.WriteLine("Triggered OCR/translation refresh after ChatGPT model change");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ChatGPT model: {ex.Message}");
            }
        }
        
        // ChatGPT Max Completion Tokens changed
        private void ChatGptMaxTokensTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                if (chatGptMaxTokensTextBox != null && !string.IsNullOrWhiteSpace(chatGptMaxTokensTextBox.Text))
                {
                    if (int.TryParse(chatGptMaxTokensTextBox.Text, out int maxTokens) && maxTokens > 0)
                    {
                        ConfigManager.Instance.SetChatGptMaxCompletionTokens(maxTokens);
                        Console.WriteLine($"ChatGPT max completion tokens set to: {maxTokens}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ChatGPT max completion tokens: {ex.Message}");
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
        
        private void OpenUrl(string url)
        {
            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening URL: {ex.Message}");
                MessageBox.Show($"Unable to open URL: {url}\n\nError: {ex.Message}", 
                    "Error Opening URL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Text-to-Speech settings handlers
        
        private void TtsEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Skip if initializing
                if (_isInitializing)
                    return;
                    
                bool isEnabled = ttsEnabledCheckBox.IsChecked ?? false;
                ConfigManager.Instance.SetTtsEnabled(isEnabled);
                Console.WriteLine($"TTS enabled: {isEnabled}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating TTS enabled state: {ex.Message}");
            }
        }
        
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
                if (ttsMaxConcurrentDownloadsTextBox != null)
                {
                    ttsMaxConcurrentDownloadsTextBox.LostFocus -= TtsMaxConcurrentDownloadsTextBox_LostFocus;
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
                
                // Load max concurrent downloads
                if (ttsMaxConcurrentDownloadsTextBox != null)
                {
                    int maxConcurrent = ConfigManager.Instance.GetTtsMaxConcurrentDownloads();
                    _lastMaxConcurrentDownloadsValue = maxConcurrent;
                    ttsMaxConcurrentDownloadsTextBox.Text = maxConcurrent.ToString();
                }
                
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
                if (ttsMaxConcurrentDownloadsTextBox != null)
                {
                    ttsMaxConcurrentDownloadsTextBox.LostFocus += TtsMaxConcurrentDownloadsTextBox_LostFocus;
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
                
                if (double.TryParse(text, out double threshold))
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
                    
                if (double.TryParse(minLetterConfidenceTextBox.Text, out double confidence) && confidence >= 0 && confidence <= 1)
                {
                    string currentOcr = ConfigManager.Instance.GetOcrMethod();
                    ConfigManager.Instance.SetMinLetterConfidence(currentOcr, confidence);
                    // Also update legacy/global for backward compatibility if needed, but we are moving away from it
                    // ConfigManager.Instance.SetMinLetterConfidence(confidence); 
                    
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
                    
                if (double.TryParse(minLineConfidenceTextBox.Text, out double confidence) && confidence >= 0 && confidence <= 1)
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

        private void AudioProcessingProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (audioProcessingProviderComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                ConfigManager.Instance.SetAudioProcessingProvider(selectedItem.Content.ToString() ?? "OpenAI Realtime API");
            }
        }

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating OpenAI audio playback setting: {ex.Message}");
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
                }
            }
        }

        private void OpenAiSilenceDurationMsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (int.TryParse(openAiSilenceDurationMsTextBox.Text, out int duration) && duration >= 0)
            {
                ConfigManager.Instance.SetOpenAiSilenceDurationMs(duration);
                Console.WriteLine($"OpenAI Silence Duration set to: {duration}ms");
            }
            else
            {
                // Reset to current config value if input is invalid
                openAiSilenceDurationMsTextBox.Text = ConfigManager.Instance.GetOpenAiSilenceDurationMs().ToString();
                MessageBox.Show("Invalid silence duration. Please enter a non-negative number.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenAiSpeechPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            
            string prompt = openAiSpeechPromptTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(prompt))
            {
                ConfigManager.Instance.SetOpenAISpeechPrompt(prompt);
                Console.WriteLine("OpenAI speech prompt updated");
            }
        }

        // Handle Set Default Speech Prompt button click
        private void SetDefaultSpeechPromptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            
            // Reset to default prompt by calling the config method with a null or empty string
            ConfigManager.Instance.ResetOpenAISpeechPromptToDefault();
            
            // Update the text box with the new default value
            openAiSpeechPromptTextBox.Text = ConfigManager.Instance.GetOpenAISpeechPrompt();
            
            Console.WriteLine("OpenAI speech prompt reset to default");
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

        private void MaxSettleTimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(maxSettleTimeTextBox.Text, out double maxSettleTime) && maxSettleTime >= 0)
            {
                ConfigManager.Instance.SetBlockDetectionMaxSettleTime(maxSettleTime);
                Console.WriteLine($"Max settle time set to: {maxSettleTime}");
            }
            else
            {
                // If invalid, reset to current value from config
                maxSettleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionMaxSettleTime().ToString("F2");
            }
        }

        private void OverlayClearDelayTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(overlayClearDelayTextBox.Text, out double delay) && delay >= 0)
            {
                ConfigManager.Instance.SetOverlayClearDelaySeconds(delay);
                Console.WriteLine($"Overlay clear delay set to: {delay}");
            }
            else
            {
                // If invalid, reset to current value from config
                overlayClearDelayTextBox.Text = ConfigManager.Instance.GetOverlayClearDelaySeconds().ToString("F2");
            }
        }
        
        #region Hotkey Editor
        
        // Class to represent hotkey display items
        // Display item for action list
        public class ActionDisplayItem
        {
            public string ActionId { get; set; } = "";
            public string ActionName { get; set; } = "";
            
            public override string ToString()
            {
                return ActionName;
            }
        }
        
        // Display item for bindings list
        public class BindingDisplayItem
        {
            public HotkeyEntry Binding { get; set; }
            public string BindingType { get; set; } = "";
            public string BindingString { get; set; } = "";
            
            public BindingDisplayItem(HotkeyEntry entry)
            {
                Binding = entry;
                
                if (entry.HasKeyboardHotkey())
                {
                    BindingType = "Keyboard";
                    BindingString = entry.GetKeyboardHotkeyString();
                }
                else if (entry.HasGamepadHotkey())
                {
                    BindingType = "Gamepad";
                    BindingString = entry.GetGamepadHotkeyString();
                }
            }
        }
        
        private string? _selectedActionId;
        
        // Load actions into the list
        private void loadActions()
        {
            // Define all available actions
            var actions = new List<ActionDisplayItem>
            {
                new ActionDisplayItem { ActionId = "start_stop", ActionName = "Start/Stop OCR" },
                new ActionDisplayItem { ActionId = "toggle_monitor", ActionName = "Toggle Monitor Window" },
                new ActionDisplayItem { ActionId = "toggle_chatbox", ActionName = "Toggle ChatBox" },
                new ActionDisplayItem { ActionId = "toggle_settings", ActionName = "Toggle Settings" },
                new ActionDisplayItem { ActionId = "toggle_log", ActionName = "Toggle Log" },
                new ActionDisplayItem { ActionId = "toggle_listen", ActionName = "Toggle Listen" },
                new ActionDisplayItem { ActionId = "view_in_browser", ActionName = "View in Browser" },
                new ActionDisplayItem { ActionId = "toggle_main_window", ActionName = "Toggle Main Window" },
                new ActionDisplayItem { ActionId = "clear_overlays", ActionName = "Clear Overlays" },
                new ActionDisplayItem { ActionId = "toggle_passthrough", ActionName = "Toggle Passthrough" },
                new ActionDisplayItem { ActionId = "toggle_overlay_mode", ActionName = "Toggle Overlay Mode" },
                new ActionDisplayItem { ActionId = "play_all_audio", ActionName = "Play All Audio Toggle" }
            };
            
            actionsListBox.ItemsSource = actions;
            
            // Load global hotkeys enabled state
            globalHotkeysEnabledCheckBox.IsChecked = HotkeyManager.Instance.GetGlobalHotkeysEnabled();
            
            Console.WriteLine($"Loaded {actions.Count} actions into settings");
        }
        
        // Load bindings for the selected action
        private void loadBindingsForSelectedAction()
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                bindingsListView.ItemsSource = null;
                return;
            }
            
            var bindings = HotkeyManager.Instance.GetBindings(_selectedActionId);
            var displayItems = bindings.Select(b => new BindingDisplayItem(b)).ToList();
            
            bindingsListView.ItemsSource = displayItems;
            
            Console.WriteLine($"Loaded {displayItems.Count} bindings for action {_selectedActionId}");
        }
        
        // Actions list selection changed
        private void ActionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (actionsListBox.SelectedItem is ActionDisplayItem actionItem)
            {
                _selectedActionId = actionItem.ActionId;
                loadBindingsForSelectedAction();
            }
            else
            {
                _selectedActionId = null;
                bindingsListView.ItemsSource = null;
            }
        }
        
        // Bindings list selection changed
        private void BindingsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Just track selection, no action needed
        }
        
        // Add keyboard binding
        private void AddKeyboardBindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                MessageBox.Show("Please select an action first.", "No Action Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Show dialog to capture keyboard input
            var dialog = new Window
            {
                Title = "Press Key Combination",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = "Press a key combination...",
                IsReadOnly = true,
                Margin = new Thickness(20),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                FontSize = 14
            };
            
            HotkeyEntry? capturedBinding = null;
            
            textBox.PreviewKeyDown += (s, args) =>
            {
                args.Handled = true;
                
                var key = args.Key;
                if (key == System.Windows.Input.Key.System)
                {
                    key = args.SystemKey;
                }
                
                // Skip modifier keys themselves
                if (key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                    key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                    key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                    key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
                {
                    return;
                }
                
                // Escape cancels
                if (key == System.Windows.Input.Key.Escape)
                {
                    dialog.Close();
                    return;
                }
                
                // Get modifiers
                bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
                bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
                bool alt = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt;
                
                // Get action name
                var actionItem = actionsListBox.SelectedItem as ActionDisplayItem;
                string actionName = actionItem?.ActionName ?? "";
                
                // Create binding
                capturedBinding = new HotkeyEntry(_selectedActionId, actionName);
                capturedBinding.KeyboardKey = key;
                capturedBinding.UseShift = shift;
                capturedBinding.UseCtrl = ctrl;
                capturedBinding.UseAlt = alt;
                
                textBox.Text = capturedBinding.GetKeyboardHotkeyString();
                
                // Auto-close after brief delay
                var closeTimer = new System.Windows.Threading.DispatcherTimer();
                closeTimer.Interval = TimeSpan.FromMilliseconds(500);
                closeTimer.Tick += (ts, te) =>
                {
                    closeTimer.Stop();
                    dialog.Close();
                };
                closeTimer.Start();
            };
            
            dialog.Content = textBox;
            textBox.Focus();
            
            dialog.ShowDialog();
            
            if (capturedBinding != null)
            {
                // Add binding and auto-save
                HotkeyManager.Instance.AddBinding(capturedBinding);
                loadBindingsForSelectedAction();
                
                // Update tooltips in MainWindow
                MainWindow.Instance.UpdateTooltips();
            }
        }
        
        // Add gamepad binding
        private void AddGamepadBindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                MessageBox.Show("Please select an action first.", "No Action Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Show dialog to capture gamepad input
            var dialog = new Window
            {
                Title = "Press Gamepad Button(s)",
                Width = 500,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "Press and hold gamepad button(s), then release...",
                Margin = new Thickness(20),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 14,
                TextWrapping = System.Windows.TextWrapping.Wrap
            };
            
            dialog.Content = textBlock;
            
            HotkeyEntry? capturedBinding = null;
            List<string> maxButtons = new List<string>();
            int stableCount = 0;
            const int STABLE_FRAMES = 5;
            
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (s, args) =>
            {
                var pressedButtons = GamepadManager.Instance.GetCurrentlyPressedButtons();
                
                if (pressedButtons.Count > maxButtons.Count)
                {
                    maxButtons = new List<string>(pressedButtons);
                    stableCount = 0;
                    textBlock.Text = $"Holding: {string.Join("+", maxButtons)}... (release when ready)";
                }
                else if (pressedButtons.Count == maxButtons.Count && pressedButtons.Count > 0)
                {
                    bool same = pressedButtons.All(b => maxButtons.Contains(b));
                    if (same)
                    {
                        stableCount++;
                    }
                    else
                    {
                        maxButtons = new List<string>(pressedButtons);
                        stableCount = 0;
                    }
                }
                else if (pressedButtons.Count == 0 && maxButtons.Count > 0 && stableCount >= STABLE_FRAMES)
                {
                    // Get action name
                    var actionItem = actionsListBox.SelectedItem as ActionDisplayItem;
                    string actionName = actionItem?.ActionName ?? "";
                    
                    // Create binding
                    capturedBinding = new HotkeyEntry(_selectedActionId, actionName);
                    capturedBinding.GamepadButtons = maxButtons;
                    
                    timer.Stop();
                    dialog.Close();
                }
            };
            
            dialog.Closed += (s, args) => timer.Stop();
            timer.Start();
            dialog.ShowDialog();
            
            if (capturedBinding != null)
            {
                // Add binding and auto-save
                HotkeyManager.Instance.AddBinding(capturedBinding);
                loadBindingsForSelectedAction();
                
                // Update tooltips in MainWindow
                MainWindow.Instance.UpdateTooltips();
            }
        }
        
        // Remove selected binding
        private void RemoveBindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                MessageBox.Show("Please select an action first.", "No Action Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (bindingsListView.SelectedItem is BindingDisplayItem bindingItem)
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Remove binding \"{bindingItem.BindingString}\" from this action?",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Remove binding and auto-save
                    HotkeyManager.Instance.RemoveBinding(_selectedActionId, bindingItem.Binding);
                    loadBindingsForSelectedAction();
                    
                    // Update tooltips in MainWindow
                    MainWindow.Instance.UpdateTooltips();
                }
            }
            else
            {
                MessageBox.Show("Please select a binding to remove.", "No Binding Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        // Reset all hotkeys to defaults
        private void ResetHotkeysButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all hotkeys to defaults?", 
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Reset to defaults
                HotkeyManager.Instance.ResetToDefaults();
                
                // Refresh the UI
                loadActions();
                loadBindingsForSelectedAction();
                
                // Update tooltips in MainWindow
                MainWindow.Instance.UpdateTooltips();
                
                MessageBox.Show("Hotkeys reset to defaults!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        // Global hotkeys enabled checkbox changed
        private void GlobalHotkeysEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            bool enabled = globalHotkeysEnabledCheckBox.IsChecked ?? true;
            HotkeyManager.Instance.SetGlobalHotkeysEnabled(enabled);
            HotkeyManager.Instance.SaveHotkeys();
            
            Console.WriteLine($"Global hotkeys {(enabled ? "enabled" : "disabled")}");
        }
        
        #endregion
        
        #region Screenshot Exclusion
        
        private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Apply WDA_EXCLUDEFROMCAPTURE as early as possible (right after HWND creation)
            SetExcludeFromCapture();
        }
        
        private void SetExcludeFromCapture()
        {
            try
            {
                // Check if user wants windows visible in screenshots
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                
                if (hwnd != IntPtr.Zero)
                {
                    // If visibleInScreenshots is true, set to WDA_NONE (include in capture)
                    // If visibleInScreenshots is false, set to WDA_EXCLUDEFROMCAPTURE (exclude from capture)
                    uint affinity = visibleInScreenshots ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE;
                    bool success = SetWindowDisplayAffinity(hwnd, affinity);
                    
                    if (success)
                    {
                        Console.WriteLine($"Settings window {(visibleInScreenshots ? "included in" : "excluded from")} screen capture successfully (HWND: {hwnd})");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to set Settings window capture mode. Last error: {Marshal.GetLastWin32Error()}");
                    }
                }
                else
                {
                    Console.WriteLine("Settings window HWND is null, cannot set capture mode");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Settings window capture mode: {ex.Message}");
            }
        }
        
        public void UpdateCaptureExclusion()
        {
            SetExcludeFromCapture();
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
            // Schedule exclusion check on next UI thread cycle (tooltip window needs to be created first)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExcludeTooltipFromCapture();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        
        private void ExcludeTooltipFromCapture()
        {
            try
            {
                // Check if user wants windows visible in screenshots
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                
                // If visible in screenshots, don't exclude
                if (visibleInScreenshots)
                {
                    return;
                }
                
                // Find all tooltip windows and exclude them
                var tooltipWindows = System.Windows.Application.Current.Windows.OfType<Window>()
                    .Where(w => w.GetType().Name.Contains("ToolTip") || w.GetType().Name.Contains("Popup"));
                
                foreach (var window in tooltipWindows)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(window);
                    IntPtr hwnd = helper.Handle;
                    
                    if (hwnd != IntPtr.Zero)
                    {
                        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                    }
                }
                
                // Also try to find popup windows via interop
                // WPF tooltips are displayed in Popup windows which are top-level HWND windows
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                foreach (System.Diagnostics.ProcessThread thread in currentProcess.Threads)
                {
                    try
                    {
                        EnumThreadWindows((uint)thread.Id, (hWnd, lParam) =>
                        {
                            var className = new System.Text.StringBuilder(256);
                            GetClassName(hWnd, className, className.Capacity);
                            string cls = className.ToString();
                            
                            // WPF tooltip windows typically have these class names
                            if (cls.Contains("Popup") || cls.Contains("ToolTip") || cls.Contains("HwndWrapper"))
                            {
                                SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE);
                            }
                            
                            return true; // Continue enumeration
                        }, IntPtr.Zero);
                    }
                    catch
                    {
                        // Thread may have terminated, ignore
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error excluding tooltip from capture: {ex.Message}");
            }
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
        
        // Windows API for excluding windows from screen capture
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        
        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        
        private delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);
        
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        private const uint WDA_NONE = 0x00000000;
        
        #endregion
    }
}
