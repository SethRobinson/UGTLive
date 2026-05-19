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
        
        // View last LLM request sent button clicked
        private void ViewLastLlmRequestButton_Click(object sender, RoutedEventArgs e)
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = System.IO.Path.Combine(appDirectory, "last_llm_request_sent.txt");
            OpenTroubleshootingFile(filePath, "last LLM request");
        }
        
        // View last LLM reply received button clicked
        private void ViewLastLlmReplyButton_Click(object sender, RoutedEventArgs e)
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = System.IO.Path.Combine(appDirectory, "last_llm_reply_received.txt");
            OpenTroubleshootingFile(filePath, "last LLM reply");
        }
        
        // Helper to open troubleshooting files
        private void OpenTroubleshootingFile(string filePath, string description, string creationHint = "translation request")
        {
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening {description} file: {ex.Message}");
                    MessageBox.Show($"Unable to open {description} file.\n\nError: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"No {description} file found yet.\n\nThis file is created after your first {creationHint}.",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        // View last OCR reply received button clicked
        private void ViewLastOcrReplyButton_Click(object sender, RoutedEventArgs e)
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = System.IO.Path.Combine(appDirectory, "last_ocr_reply_received.txt");
            OpenTroubleshootingFile(filePath, "last OCR reply", "OCR request");
        }
        
        // Delete the last OCR reply file when switching OCR methods
        private void DeleteLastOcrReplyFile()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = System.IO.Path.Combine(appDirectory, "last_ocr_reply_received.txt");
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    Console.WriteLine("Deleted last_ocr_reply_received.txt due to OCR method change");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting last OCR reply file: {ex.Message}");
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
                bool isAnthropicSelected = string.Equals(selectedService, "Anthropic", StringComparison.OrdinalIgnoreCase);
                bool isOpenRouterSelected = string.Equals(selectedService, "OpenRouter", StringComparison.OrdinalIgnoreCase);
                bool isClaudeCliSelected = string.Equals(selectedService, "ClaudeCli", StringComparison.OrdinalIgnoreCase);
                bool isCodexCliSelected = string.Equals(selectedService, "CodexCli", StringComparison.OrdinalIgnoreCase);
                bool isGeminiCliSelected = string.Equals(selectedService, "GeminiCli", StringComparison.OrdinalIgnoreCase);
                
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
                if (thinkingEnabledCheckBox != null)
                {
                    bool isGoogleTranslate = string.Equals(selectedService, "Google Translate", StringComparison.OrdinalIgnoreCase);
                    thinkingEnabledCheckBox.Visibility = isGoogleTranslate ? Visibility.Collapsed : Visibility.Visible;
                }
                
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
                if (llamacppModelLabel != null)
                    llamacppModelLabel.Visibility = isLlamacppSelected ? Visibility.Visible : Visibility.Collapsed;
                if (llamacppModelGrid != null)
                    llamacppModelGrid.Visibility = isLlamacppSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Google Translate-specific settings
                if (googleTranslateServiceTypeLabel != null)
                    googleTranslateServiceTypeLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateServiceTypeComboBox != null)
                    googleTranslateServiceTypeComboBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateMappingLabel != null)
                    googleTranslateMappingLabel.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                if (googleTranslateMappingCheckBox != null)
                    googleTranslateMappingCheckBox.Visibility = isGoogleTranslateSelected ? Visibility.Visible : Visibility.Collapsed;
                
                // Show/hide Anthropic-specific settings
                Visibility anthropicVis = isAnthropicSelected ? Visibility.Visible : Visibility.Collapsed;
                if (anthropicApiKeyLabel != null) anthropicApiKeyLabel.Visibility = anthropicVis;
                if (anthropicApiKeyGrid != null) anthropicApiKeyGrid.Visibility = anthropicVis;
                if (anthropicModelLabel != null) anthropicModelLabel.Visibility = anthropicVis;
                if (anthropicModelGrid != null) anthropicModelGrid.Visibility = anthropicVis;
                if (anthropicMaxTokensLabel != null) anthropicMaxTokensLabel.Visibility = anthropicVis;
                if (anthropicMaxTokensTextBox != null) anthropicMaxTokensTextBox.Visibility = anthropicVis;

                // Show/hide OpenRouter-specific settings
                Visibility openRouterVis = isOpenRouterSelected ? Visibility.Visible : Visibility.Collapsed;
                if (openRouterApiKeyLabel != null) openRouterApiKeyLabel.Visibility = openRouterVis;
                if (openRouterApiKeyGrid != null) openRouterApiKeyGrid.Visibility = openRouterVis;
                if (openRouterModelLabel != null) openRouterModelLabel.Visibility = openRouterVis;
                if (openRouterModelGrid != null) openRouterModelGrid.Visibility = openRouterVis;

                // Show/hide Claude CLI-specific settings
                Visibility claudeCliVis = isClaudeCliSelected ? Visibility.Visible : Visibility.Collapsed;
                if (claudeCliCommandLabel != null) claudeCliCommandLabel.Visibility = claudeCliVis;
                if (claudeCliCommandGrid != null) claudeCliCommandGrid.Visibility = claudeCliVis;
                if (claudeCliModelLabel != null) claudeCliModelLabel.Visibility = claudeCliVis;
                if (claudeCliModelGrid != null) claudeCliModelGrid.Visibility = claudeCliVis;

                // Show/hide Codex CLI-specific settings
                Visibility codexCliVis = isCodexCliSelected ? Visibility.Visible : Visibility.Collapsed;
                if (codexCliCommandLabel != null) codexCliCommandLabel.Visibility = codexCliVis;
                if (codexCliCommandGrid != null) codexCliCommandGrid.Visibility = codexCliVis;
                if (codexCliModelLabel != null) codexCliModelLabel.Visibility = codexCliVis;
                if (codexCliModelGrid != null) codexCliModelGrid.Visibility = codexCliVis;

                // Show/hide Gemini CLI-specific settings
                Visibility geminiCliVis = isGeminiCliSelected ? Visibility.Visible : Visibility.Collapsed;
                if (geminiCliCommandLabel != null) geminiCliCommandLabel.Visibility = geminiCliVis;
                if (geminiCliCommandGrid != null) geminiCliCommandGrid.Visibility = geminiCliVis;
                if (geminiCliModelLabel != null) geminiCliModelLabel.Visibility = geminiCliVis;
                if (geminiCliModelGrid != null) geminiCliModelGrid.Visibility = geminiCliVis;

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
                
                // Hide the whole prompt GroupBox for Google Translate (it uses no prompt)
                if (promptTemplateGroupBox != null)
                    promptTemplateGroupBox.Visibility = showPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
                
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
                        llamacppModelTextBox.TextChanged -= LlamacppModelTextBox_TextChanged;
                        
                        llamacppUrlTextBox.Text = ConfigManager.Instance.GetLlamaCppUrl();
                        llamacppPortTextBox.Text = ConfigManager.Instance.GetLlamaCppPort();
                        llamacppModelTextBox.Text = ConfigManager.Instance.GetLlamaCppModel();
                        
                        // Reattach event handlers
                        llamacppUrlTextBox.TextChanged += LlamacppUrlTextBox_TextChanged;
                        llamacppPortTextBox.TextChanged += LlamacppPortTextBox_TextChanged;
                        llamacppModelTextBox.TextChanged += LlamacppModelTextBox_TextChanged;
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
                    
                    if (googleTranslateApiKeyPasswordBox != null)
                    {
                        googleTranslateApiKeyPasswordBox.Password = ConfigManager.Instance.GetGoogleTranslateApiKey();
                    }
                    
                    // Set language mapping checkbox
                    if (googleTranslateMappingCheckBox != null)
                        googleTranslateMappingCheckBox.IsChecked = ConfigManager.Instance.GetGoogleTranslateAutoMapLanguages();
                }
                else if (isAnthropicSelected)
                {
                    if (anthropicApiKeyPasswordBox != null)
                        anthropicApiKeyPasswordBox.Password = ConfigManager.Instance.GetAnthropicApiKey();

                    string model = ConfigManager.Instance.GetAnthropicModel();
                    foreach (ComboBoxItem item in anthropicModelComboBox.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), model, StringComparison.OrdinalIgnoreCase))
                        {
                            anthropicModelComboBox.SelectedItem = item;
                            break;
                        }
                    }

                    if (anthropicMaxTokensTextBox != null)
                        anthropicMaxTokensTextBox.Text = ConfigManager.Instance.GetAnthropicMaxTokens().ToString();
                }
                else if (isOpenRouterSelected)
                {
                    if (openRouterApiKeyPasswordBox != null)
                        openRouterApiKeyPasswordBox.Password = ConfigManager.Instance.GetOpenRouterApiKey();

                    SelectEditableComboValue(openRouterModelComboBox, ConfigManager.Instance.GetOpenRouterModel(),
                        OpenRouterModelComboBox_SelectionChanged);
                }
                else if (isClaudeCliSelected)
                {
                    if (claudeCliCommandTextBox != null)
                    {
                        claudeCliCommandTextBox.TextChanged -= ClaudeCliCommandTextBox_TextChanged;
                        claudeCliCommandTextBox.Text = ConfigManager.Instance.GetClaudeCliCommand();
                        claudeCliCommandTextBox.TextChanged += ClaudeCliCommandTextBox_TextChanged;
                    }
                    SelectEditableComboValue(claudeCliModelComboBox, ConfigManager.Instance.GetClaudeCliModel(),
                        ClaudeCliModelComboBox_SelectionChanged);
                }
                else if (isCodexCliSelected)
                {
                    if (codexCliCommandTextBox != null)
                    {
                        codexCliCommandTextBox.TextChanged -= CodexCliCommandTextBox_TextChanged;
                        codexCliCommandTextBox.Text = ConfigManager.Instance.GetCodexCliCommand();
                        codexCliCommandTextBox.TextChanged += CodexCliCommandTextBox_TextChanged;
                    }
                    SelectEditableComboValue(codexCliModelComboBox, ConfigManager.Instance.GetCodexCliModel(),
                        CodexCliModelComboBox_SelectionChanged);
                }
                else if (isGeminiCliSelected)
                {
                    if (geminiCliCommandTextBox != null)
                    {
                        geminiCliCommandTextBox.TextChanged -= GeminiCliCommandTextBox_TextChanged;
                        geminiCliCommandTextBox.Text = ConfigManager.Instance.GetGeminiCliCommand();
                        geminiCliCommandTextBox.TextChanged += GeminiCliCommandTextBox_TextChanged;
                    }
                    SelectEditableComboValue(geminiCliModelComboBox, ConfigManager.Instance.GetGeminiCliModel(),
                        GeminiCliModelComboBox_SelectionChanged);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service-specific settings: {ex.Message}");
            }
        }

        // Select a value in an editable ComboBox without triggering its SelectionChanged save
        private void SelectEditableComboValue(ComboBox combo, string value, SelectionChangedEventHandler handler)
        {
            if (combo == null)
                return;

            combo.SelectionChanged -= handler;
            bool found = false;
            foreach (ComboBoxItem item in combo.Items)
            {
                if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                combo.Text = value;
            }
            combo.SelectionChanged += handler;
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
        
        // llama.cpp Model changed
        private void LlamacppModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip if initializing or if the sender isn't the expected TextBox
            if (_isInitializing || sender != llamacppModelTextBox)
                return;
                
            string model = llamacppModelTextBox.Text.Trim();
            ConfigManager.Instance.SetLlamaCppModel(model);
            Console.WriteLine($"llama.cpp model set to: {model}");
            
            // Trigger retranslation if the current service is llama.cpp
            if (ConfigManager.Instance.GetCurrentTranslationService() == "llama.cpp")
            {
                Console.WriteLine("llama.cpp model changed. Triggering retranslation...");
                Logic.Instance.ResetHash();
            }
        }
        
        // llama.cpp List Models button clicked
        private async void LlamacppListModelsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button while fetching
                llamacppListModelsButton.IsEnabled = false;
                llamacppListModelsButton.Content = "Loading...";
                
                // Fetch models from llama.cpp server
                List<string> models = await FetchLlamaCppModelsAsync();
                
                // Re-enable button
                llamacppListModelsButton.IsEnabled = true;
                llamacppListModelsButton.Content = "List Models";
                
                if (models == null || models.Count == 0)
                {
                    MessageBox.Show(
                        "No models found or failed to connect to llama.cpp server.\n\n" +
                        "Please check:\n" +
                        "1. The llama.cpp server is running\n" +
                        "2. The server URL and port in settings are correct\n" +
                        "3. The server may be running in single-model mode (not router mode)",
                        "No Models Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                
                // Show model selector dialog
                var dialog = new LlamaCppModelSelectorWindow
                {
                    Owner = this
                };
                dialog.SetModels(models);
                
                if (dialog.ShowDialog() == true && dialog.SelectedModel != null)
                {
                    // Update the model text box
                    llamacppModelTextBox.Text = dialog.SelectedModel;
                    // The TextChanged event handler will save it to config
                }
            }
            catch (Exception ex)
            {
                llamacppListModelsButton.IsEnabled = true;
                llamacppListModelsButton.Content = "List Models";
                
                MessageBox.Show(
                    $"Error fetching models: {ex.Message}\n\n" +
                    "Please check your llama.cpp server settings.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        // Fetch available models from llama.cpp server
        private async Task<List<string>> FetchLlamaCppModelsAsync()
        {
            try
            {
                string llamacppUrl = ConfigManager.Instance.GetLlamaCppUrl();
                string llamacppPort = ConfigManager.Instance.GetLlamaCppPort();
                
                // Correctly format the URL
                if (!llamacppUrl.StartsWith("http://") && !llamacppUrl.StartsWith("https://"))
                {
                    llamacppUrl = "http://" + llamacppUrl;
                }
                
                // OpenAI-compatible /v1/models endpoint
                string apiUrl = $"{llamacppUrl}:{llamacppPort}/v1/models";
                Console.WriteLine($"Fetching models from URL: {apiUrl}");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    HttpResponseMessage response = await client.GetAsync(apiUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Response from llama.cpp models API: {jsonResponse}");
                        
                        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                        List<string> models = new List<string>();
                        
                        // OpenAI format: { "object": "list", "data": [{ "id": "model-name", ... }] }
                        if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement))
                        {
                            foreach (JsonElement modelElement in dataElement.EnumerateArray())
                            {
                                if (modelElement.TryGetProperty("id", out JsonElement idElement))
                                {
                                    string modelId = idElement.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(modelId))
                                    {
                                        models.Add(modelId);
                                        Console.WriteLine($"Found available model: {modelId}");
                                    }
                                }
                            }
                        }
                        
                        return models.OrderBy(m => m).ToList();
                    }
                    else
                    {
                        string errorMessage = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"llama.cpp API error: {response.StatusCode}, {errorMessage}");
                        return new List<string>();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching llama.cpp models: {ex.Message}");
                return new List<string>();
            }
        }
        
        // Unified Thinking Mode checkbox changed
        private void ThinkingEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            bool isChecked = thinkingEnabledCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetThinkingEnabled(isChecked);
            Console.WriteLine($"Thinking mode set to: {isChecked}");
            
            string currentService = ConfigManager.Instance.GetCurrentTranslationService();
            if (currentService != "Google Translate")
            {
                Console.WriteLine("Thinking mode changed. Triggering retranslation...");
                Logic.Instance.ResetHash();
            }
        }
        
        // Model downloader instance
        private readonly OllamaModelDownloader _modelDownloader = new OllamaModelDownloader();
        
        private async void TestModelButton_Click(object sender, RoutedEventArgs e)
        {
            string model = ollamaModelTextBox.Text.Trim();
            await _modelDownloader.TestAndDownloadModel(model, this);
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
        
        // Legacy handlers removed - now using unified ThinkingEnabledCheckBox_Changed
        
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
                    string model = selectedItem.Tag?.ToString() ?? "gpt-5.4-nano";
                    
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
        
        // Retrigger a retranslation if the changed provider is the active one
        private void TriggerRetranslationIfCurrent(string service)
        {
            if (ConfigManager.Instance.GetCurrentTranslationService() != service)
                return;

            Console.WriteLine($"{service} setting changed. Triggering retranslation...");
            MainWindow.Instance.ClearTranslationHistory();
            Logic.Instance.ResetHash();
            Logic.Instance.ClearAllTextObjects();
            if (MainWindow.Instance.GetIsStarted())
            {
                MainWindow.Instance.SetOCRCheckIsWanted(true);
            }
        }

        // ===== Anthropic (direct API) =====

        private void AnthropicApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ConfigManager.Instance.SetAnthropicApiKey(anthropicApiKeyPasswordBox.Password.Trim());
        }

        private void AnthropicModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (anthropicModelComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string model = selectedItem.Tag?.ToString() ?? "claude-sonnet-4-6";
                ConfigManager.Instance.SetAnthropicModel(model);
                TriggerRetranslationIfCurrent("Anthropic");
            }
        }

        private void AnthropicMaxTokensTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (anthropicMaxTokensTextBox != null && int.TryParse(anthropicMaxTokensTextBox.Text, out int max) && max > 0)
            {
                ConfigManager.Instance.SetAnthropicMaxTokens(max);
            }
        }

        private void AnthropicApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://console.anthropic.com/settings/keys");
        }

        private void ViewAnthropicModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://docs.claude.com/en/docs/about-claude/models/overview");
        }

        // ===== OpenRouter =====

        private void OpenRouterApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ConfigManager.Instance.SetOpenRouterApiKey(openRouterApiKeyPasswordBox.Password.Trim());
        }

        private void OpenRouterModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            string model = (openRouterModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? openRouterModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
            {
                ConfigManager.Instance.SetOpenRouterModel(model);
                TriggerRetranslationIfCurrent("OpenRouter");
            }
        }

        private void OpenRouterModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string model = openRouterModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
            {
                ConfigManager.Instance.SetOpenRouterModel(model);
            }
        }

        private void OpenRouterApiLink_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://openrouter.ai/keys");
        }

        private void ViewOpenRouterModelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://openrouter.ai/models");
        }

        // ===== CLI / subscription providers =====

        private void ClaudeCliCommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || sender != claudeCliCommandTextBox) return;
            ConfigManager.Instance.SetClaudeCliCommand(claudeCliCommandTextBox.Text.Trim());
        }

        private void ClaudeCliModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            string model = (claudeCliModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? claudeCliModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
            {
                ConfigManager.Instance.SetClaudeCliModel(model);
                TriggerRetranslationIfCurrent("ClaudeCli");
            }
        }

        private void ClaudeCliModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string model = claudeCliModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
                ConfigManager.Instance.SetClaudeCliModel(model);
        }

        private void CodexCliCommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || sender != codexCliCommandTextBox) return;
            ConfigManager.Instance.SetCodexCliCommand(codexCliCommandTextBox.Text.Trim());
        }

        private void CodexCliModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            string model = (codexCliModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? codexCliModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
            {
                ConfigManager.Instance.SetCodexCliModel(model);
                TriggerRetranslationIfCurrent("CodexCli");
            }
        }

        private void CodexCliModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string model = codexCliModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
                ConfigManager.Instance.SetCodexCliModel(model);
        }

        private void GeminiCliCommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || sender != geminiCliCommandTextBox) return;
            ConfigManager.Instance.SetGeminiCliCommand(geminiCliCommandTextBox.Text.Trim());
        }

        private void GeminiCliModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            string model = (geminiCliModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? geminiCliModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
            {
                ConfigManager.Instance.SetGeminiCliModel(model);
                TriggerRetranslationIfCurrent("GeminiCli");
            }
        }

        private void GeminiCliModelComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string model = geminiCliModelComboBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(model))
                ConfigManager.Instance.SetGeminiCliModel(model);
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
    }
}
