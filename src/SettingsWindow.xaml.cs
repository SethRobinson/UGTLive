using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
            Console.WriteLine("SettingsWindow constructor: Setting _isInitializing to true");
            
            InitializeComponent();
            _instance = this;
            
            // Add Loaded event handler to ensure controls are initialized
            this.Loaded += SettingsWindow_Loaded;
            
            // Disable shortcuts while this window has focus so we can type freely
            this.Activated += (s, e) => KeyboardShortcuts.SetShortcutsEnabled(false);
            // Re-enable when focus leaves (but not yet hidden)
            this.Deactivated += (s, e) => KeyboardShortcuts.SetShortcutsEnabled(true);
            
            // Set up closing behavior (hide instead of close)
            this.Closing += (s, e) => 
            {
                e.Cancel = true;  // Cancel the close
                this.Hide();      // Just hide the window
                // Re-enable shortcuts when settings window is hidden
                KeyboardShortcuts.SetShortcutsEnabled(true);
            };
        }
        
        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        // Collection to hold the ignore phrases
        private ObservableCollection<IgnorePhrase> _ignorePhrases = new ObservableCollection<IgnorePhrase>();
        
        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("SettingsWindow_Loaded: Starting initialization");
                
                // Set initialization flag to prevent saving during setup
                _isInitializing = true;
                
                // Populate Whisper Language ComboBox
                PopulateWhisperLanguageComboBox();
                
                // Make sure keyboard shortcuts work from this window too
                PreviewKeyDown -= Application_KeyDown;
                PreviewKeyDown += Application_KeyDown;
                
                // Set initial values only after the window is fully loaded
                LoadSettingsFromMainWindow();
                
                // Make sure service-specific settings are properly initialized
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                UpdateServiceSpecificSettings(currentService);
                
                // Now that initialization is complete, allow saving changes
                _isInitializing = false;
                
                // Force the OCR method and translation service to match the config again
                // This ensures the config values are preserved and not overwritten
                string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
                string configTransService = ConfigManager.Instance.GetCurrentTranslationService();
                Console.WriteLine($"Ensuring config values are preserved: OCR={configOcrMethod}, Translation={configTransService}");
                
                ConfigManager.Instance.SetOcrMethod(configOcrMethod);
                ConfigManager.Instance.SetTranslationService(configTransService);
                
                Console.WriteLine("Settings window fully loaded and initialized. Changes will now be saved.");
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
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
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
                        
                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();
                        
                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
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
                        Console.WriteLine($"Settings window: Set source language from config to {configSourceLanguage}");
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
                        Console.WriteLine($"Settings window: Set target language from config to {configTargetLanguage}");
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
            Console.WriteLine($"SettingsWindow: Loading OCR method '{savedOcrMethod}'");
            
            // Temporarily remove event handler to prevent triggering during initialization
            ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;
            
            // Find matching ComboBoxItem
            foreach (ComboBoxItem item in ocrMethodComboBox.Items)
            {
                string itemText = item.Content.ToString() ?? "";
                if (string.Equals(itemText, savedOcrMethod, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found matching OCR method: '{itemText}'");
                    ocrMethodComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Re-attach event handler
            ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
            
            // Get auto-translate setting from config instead of MainWindow
            // This ensures the setting persists across application restarts
            autoTranslateCheckBox.IsChecked = ConfigManager.Instance.IsAutoTranslateEnabled();
            Console.WriteLine($"Settings window: Loading auto-translate from config: {ConfigManager.Instance.IsAutoTranslateEnabled()}");
            
            // Set leave translation onscreen setting
            leaveTranslationOnscreenCheckBox.IsChecked = ConfigManager.Instance.IsLeaveTranslationOnscreenEnabled();
            
            // Set block detection settings directly from BlockDetectionManager
            // Temporarily remove event handlers to prevent triggering changes
            blockDetectionPowerTextBox.LostFocus -= BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus -= SettleTimeTextBox_LostFocus;
            maxSettleTimeTextBox.LostFocus -= MaxSettleTimeTextBox_LostFocus;
            
           
            blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2");
            settleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionSettleTime().ToString("F2");
            maxSettleTimeTextBox.Text = ConfigManager.Instance.GetBlockDetectionMaxSettleTime().ToString("F2");
            
            Console.WriteLine($"SettingsWindow: Loaded block detection power: {blockDetectionPowerTextBox.Text}");
            Console.WriteLine($"SettingsWindow: Loaded settle time: {settleTimeTextBox.Text}");
            
            // Reattach event handlers
            blockDetectionPowerTextBox.LostFocus += BlockDetectionPowerTextBox_LostFocus;
            settleTimeTextBox.LostFocus += SettleTimeTextBox_LostFocus;
            maxSettleTimeTextBox.LostFocus += MaxSettleTimeTextBox_LostFocus;
            
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
                    Console.WriteLine($"OpenAI voice set from config to {currentVoice}");
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
                Logic.Instance.ClearAllTextObjects();
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
                Logic.Instance.ClearAllTextObjects();
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
                string? ocrMethod = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                
                if (!string.IsNullOrEmpty(ocrMethod))
                {
                    Console.WriteLine($"SettingsWindow OCR method changed to: '{ocrMethod}'");
                    
                    // Update MonitorWindow OCR method
                    if (MonitorWindow.Instance.ocrMethodComboBox != null)
                    {
                        // Find and select the matching item by content, not index
                        foreach (ComboBoxItem item in MonitorWindow.Instance.ocrMethodComboBox.Items)
                        {
                            if (string.Equals(item.Content.ToString(), ocrMethod, StringComparison.OrdinalIgnoreCase))
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
                    
                    // Reset the OCR hash to force a fresh comparison after changing OCR method
                    Logic.Instance.ResetHash();
                    
                    // Clear any existing text objects
                    Logic.Instance.ClearAllTextObjects();
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
        
        private void LeaveTranslationOnscreenCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Skip if initializing
            if (_isInitializing)
                return;
                
            bool isEnabled = leaveTranslationOnscreenCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetLeaveTranslationOnscreenEnabled(isEnabled);
            Console.WriteLine($"Leave translation onscreen enabled: {isEnabled}");
        }
        
        // Language swap button handler
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
                MonitorWindow.Instance.blockDetectionPowerTextBox.Text = blockDetectionPowerTextBox.Text;
            }
            
            // Update BlockDetectionManager if applicable
            if (float.TryParse(blockDetectionPowerTextBox.Text, out float power))
            {
                // Note: SetBlockDetectionScale will save to config
                BlockDetectionManager.Instance.SetBlockDetectionScale(power);
                // Reset hash to force recalculation of text blocks
                Logic.Instance.ResetHash();
            }
            else
            {
                // If text is invalid, reset to the current value from BlockDetectionManager
                blockDetectionPowerTextBox.Text = BlockDetectionManager.Instance.GetBlockDetectionScale().ToString("F2");
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
            Console.WriteLine($"SettingsWindow.TranslationServiceComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
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
                        
                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();
                        
                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
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
            SaveCurrentPrompt();
        }
        
        // Text box lost focus - save prompt
        private void PromptTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveCurrentPrompt();
        }
        
        // Save the current prompt to the selected service
        private void SaveCurrentPrompt()
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
                    }
                }
            }
        }
        
        // Update service-specific settings visibility
        private void UpdateServiceSpecificSettings(string selectedService)
        {
            try
            {
                bool isOllamaSelected = selectedService == "Ollama";
                bool isGeminiSelected = selectedService == "Gemini";
                bool isChatGptSelected = selectedService == "ChatGPT";
                bool isGoogleTranslateSelected = selectedService == "Google Translate";
                
                // Make sure the window is fully loaded and controls are initialized
                if (ollamaUrlLabel == null || ollamaUrlTextBox == null || 
                    ollamaPortLabel == null || ollamaPortTextBox == null ||
                    ollamaModelLabel == null || ollamaModelGrid == null ||
                    geminiApiKeyLabel == null || geminiApiKeyPasswordBox == null ||
                    geminiModelLabel == null || geminiModelGrid == null ||
                    chatGptApiKeyLabel == null || chatGptApiKeyGrid == null ||
                    chatGptModelLabel == null || chatGptModelGrid == null ||
                    googleTranslateApiKeyLabel == null || googleTranslateApiKeyGrid == null ||
                    googleTranslateServiceTypeLabel == null || googleTranslateServiceTypeComboBox == null ||
                    googleTranslateMappingLabel == null || googleTranslateMappingCheckBox == null)
                {
                    Console.WriteLine("UI elements not initialized yet. Skipping visibility update.");
                    return;
                }
                
                // Show/hide Gemini-specific settings
                geminiApiKeyLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiApiKeyPasswordBox.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiApiKeyHelpText.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiModelLabel.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                geminiModelGrid.Visibility = isGeminiSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Ollama-specific settings
                ollamaUrlLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaUrlGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaPortTextBox.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelLabel.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                ollamaModelGrid.Visibility = isOllamaSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide ChatGPT-specific settings
                chatGptApiKeyLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptApiKeyGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelLabel.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                chatGptModelGrid.Visibility = isChatGptSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Google Translate-specific settings
                googleTranslateServiceTypeLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateServiceTypeComboBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateMappingCheckBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Hide prompt template for Google Translate
                bool showPromptTemplate = !isGoogleTranslateSelected;
                
                // API key is only visible for Google Translate if Cloud API is selected
                bool showGoogleTranslateApiKey = isGoogleTranslateSelected && 
                    (googleTranslateServiceTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() == "Cloud API (paid)";
                    
                googleTranslateApiKeyLabel.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                googleTranslateApiKeyGrid.Visibility = showGoogleTranslateApiKey ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide prompt template and related controls for Google Translate
                promptLabel.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                promptTemplateTextBox.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                savePromptButton.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                
                // Load service-specific settings if they're being shown
                if (isGeminiSelected)
                {
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
                    ollamaUrlTextBox.Text = ConfigManager.Instance.GetOllamaUrl();
                    ollamaPortTextBox.Text = ConfigManager.Instance.GetOllamaPort();
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
                }
                else if (isGoogleTranslateSelected)
                {
                    // Set Google Translate service type
                    bool useCloudApi = ConfigManager.Instance.GetGoogleTranslateUseCloudApi();
                    
                    // Temporarily remove event handler
                    googleTranslateServiceTypeComboBox.SelectionChanged -= GoogleTranslateServiceTypeComboBox_SelectionChanged;
                    
                    googleTranslateServiceTypeComboBox.SelectedIndex = useCloudApi ? 1 : 0; // 0 = Free, 1 = Cloud API
                    
                    // Reattach event handler
                    googleTranslateServiceTypeComboBox.SelectionChanged += GoogleTranslateServiceTypeComboBox_SelectionChanged;
                    
                    // Set API key if using Cloud API
                   // if (useCloudApi)
                    {
                        googleTranslateApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTranslateApiKey();
                    }
                    
                    // Set language mapping checkbox
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
                    googleTtsVoiceComboBox == null)
                {
                    Console.WriteLine("TTS UI elements not initialized yet. Skipping visibility update.");
                    return;
                }
                
                // Show/hide ElevenLabs-specific settings
                elevenLabsApiKeyLabel.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyGrid.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
                elevenLabsApiKeyHelpText.Visibility = isElevenLabsSelected ? Visibility.Visible : Visibility.Collapsed;
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
                Console.WriteLine("Gemini API key updated");
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
                    model = selectedItem.Content?.ToString() ?? "gemini-2.0-flash";
                }
                else
                {
                    // For manually entered text
                    model = geminiModelComboBox.Text?.Trim() ?? "gemini-2.0-flash";
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
                        
                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();
                        
                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
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
                        
                        // Reset the hash to force a retranslation
                        Logic.Instance.ResetHash();
                        
                        // Clear any existing text objects to refresh the display
                        Logic.Instance.ClearAllTextObjects();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating ChatGPT model: {ex.Message}");
            }
        }
        
        private void ElevenLabsApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://elevenlabs.io/app/api-key");
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
                    ConfigManager.Instance.SetMinLetterConfidence(confidence);
                    Console.WriteLine($"Minimum letter confidence set to: {confidence}");
                    
                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minLetterConfidenceTextBox.Text = ConfigManager.Instance.GetMinLetterConfidence().ToString();
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
                    ConfigManager.Instance.SetMinLineConfidence(confidence);
                    Console.WriteLine($"Minimum line confidence set to: {confidence}");
                    
                    // Reset the hash to force new OCR processing
                    Logic.Instance.ResetHash();
                }
                else
                {
                    // Reset to current value from config if invalid
                    minLineConfidenceTextBox.Text = ConfigManager.Instance.GetMinLineConfidence().ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating minimum line confidence: {ex.Message}");
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
                
                Console.WriteLine($"Loaded {_ignorePhrases.Count} ignore phrases");
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
    }
}