using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class TtsVoiceSelectorDialog : Window
    {
        private bool _isInitializing = false;
        
        public string SelectedService { get; private set; } = "ElevenLabs";
        public string SelectedVoice { get; private set; } = "21m00Tcm4TlvDq8ikWAM";
        public bool UseCustomVoiceId { get; private set; } = false;
        public string? CustomVoiceId { get; private set; } = null;
        
        public TtsVoiceSelectorDialog()
        {
            InitializeComponent();
            IconHelper.SetWindowIcon(this);
            LoadCurrentSettings();
        }
        
        public TtsVoiceSelectorDialog(string currentService, string currentVoice, bool useCustom, string customVoiceId)
        {
            InitializeComponent();
            // Set values before loading settings
            SelectedService = currentService;
            SelectedVoice = currentVoice;
            UseCustomVoiceId = useCustom;
            CustomVoiceId = customVoiceId;
            LoadCurrentSettings();
        }
        
        private void LoadCurrentSettings()
        {
            _isInitializing = true;
            
            // Set service
            foreach (ComboBoxItem item in ttsServiceComboBox.Items)
            {
                if (string.Equals(item.Content.ToString(), SelectedService, StringComparison.OrdinalIgnoreCase))
                {
                    ttsServiceComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // Show/hide custom voice ID panel for ElevenLabs
            if (customVoicePanel != null)
            {
                customVoicePanel.Visibility = SelectedService == "ElevenLabs" ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Set custom voice ID settings (only for ElevenLabs)
            if (useCustomVoiceCheckBox != null)
            {
                useCustomVoiceCheckBox.IsChecked = UseCustomVoiceId;
            }
            if (customVoiceIdTextBox != null)
            {
                customVoiceIdTextBox.Text = CustomVoiceId ?? "";
                customVoiceIdTextBox.IsEnabled = UseCustomVoiceId;
            }
            
            // Voice combo box should only be disabled for ElevenLabs with custom voice ID
            if (voiceComboBox != null)
            {
                voiceComboBox.IsEnabled = SelectedService != "ElevenLabs" || !UseCustomVoiceId;
            }
            
            // Update voice list based on service
            UpdateVoiceList();
            
            // Set voice (only if not using custom voice ID)
            if (!UseCustomVoiceId && voiceComboBox != null && voiceComboBox.Items != null)
            {
                foreach (ComboBoxItem item in voiceComboBox.Items)
                {
                    if (item.Tag != null && string.Equals(item.Tag.ToString(), SelectedVoice, StringComparison.OrdinalIgnoreCase))
                    {
                        voiceComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            
            _isInitializing = false;
        }
        
        private void TtsServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            if (ttsServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedService = selectedItem.Content.ToString() ?? "ElevenLabs";
                UpdateVoiceList();
                
                // Show/hide custom voice ID panel for ElevenLabs
                if (customVoicePanel != null)
                {
                    customVoicePanel.Visibility = SelectedService == "ElevenLabs" ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Update voice combo box enabled state
                if (voiceComboBox != null)
                {
                    voiceComboBox.IsEnabled = SelectedService != "ElevenLabs" || !UseCustomVoiceId;
                }
            }
        }
        
        private void UpdateVoiceList()
        {
            voiceComboBox.Items.Clear();
            
            if (SelectedService == "ElevenLabs")
            {
                // ElevenLabs voices
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Rachel", Tag = "21m00Tcm4TlvDq8ikWAM" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Domi", Tag = "AZnzlk1XvdvUeBnXmlld" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Bella", Tag = "EXAVITQu4vr4xnSDxMaL" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Antoni", Tag = "ErXwobaYiN019PkySvjV" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Elli", Tag = "MF3mGyEYCl7XYWbV9V6O" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Josh", Tag = "TxGEqnHWrfWFTfGW9XjX" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Arnold", Tag = "VR6AewLTigWG4xSOukaG" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Adam", Tag = "pNInz6obpgDQGcFmaJgB" });
                voiceComboBox.Items.Add(new ComboBoxItem { Content = "Sam", Tag = "yoZ06aMxZJJ28mfd3POQ" });
            }
            else if (SelectedService == "Google Cloud TTS")
            {
                // Google TTS voices - use the same dictionary as the main settings
                foreach (var voice in GoogleTTSService.AvailableVoices)
                {
                    voiceComboBox.Items.Add(new ComboBoxItem { Content = voice.Key, Tag = voice.Value });
                }
            }
            
            // Select first item if available
            if (voiceComboBox.Items.Count > 0)
            {
                voiceComboBox.SelectedIndex = 0;
            }
        }
        
        private void VoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            if (voiceComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                SelectedVoice = selectedItem.Tag.ToString() ?? "";
            }
        }
        
        private void UseCustomVoiceCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            if (useCustomVoiceCheckBox != null)
            {
                UseCustomVoiceId = useCustomVoiceCheckBox.IsChecked ?? false;
                if (customVoiceIdTextBox != null)
                {
                    customVoiceIdTextBox.IsEnabled = UseCustomVoiceId;
                }
                if (voiceComboBox != null)
                {
                    voiceComboBox.IsEnabled = SelectedService != "ElevenLabs" || !UseCustomVoiceId;
                }
            }
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Always save custom voice ID if entered, so it persists when switching services
            if (customVoiceIdTextBox != null && !string.IsNullOrWhiteSpace(customVoiceIdTextBox.Text))
            {
                CustomVoiceId = customVoiceIdTextBox.Text.Trim();
            }

            // Validate selection
            if (SelectedService == "ElevenLabs" && UseCustomVoiceId)
            {
                if (string.IsNullOrEmpty(CustomVoiceId))
                {
                    MessageBox.Show("Please enter a custom voice ID.", "Selection Required", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedVoice = CustomVoiceId; // Use custom voice ID as the selected voice
            }
            else
            {
                if (voiceComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a voice.", "Selection Required", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (voiceComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
                {
                    SelectedVoice = selectedItem.Tag.ToString() ?? "";
                }
                // Don't clear CustomVoiceId so it's remembered if we switch back
            }
            
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

