using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class TtsVoiceSelectorDialog : Window
    {
        public string SelectedService { get; private set; } = "ElevenLabs";
        public string SelectedVoice { get; private set; } = "21m00Tcm4TlvDq8ikWAM";
        public bool UseCustomVoiceId { get; private set; } = false;
        public string? CustomVoiceId { get; private set; } = null;
        
        public TtsVoiceSelectorDialog()
        {
            InitializeComponent();
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
            
            // Set custom voice ID settings
            if (useCustomVoiceCheckBox != null)
            {
                useCustomVoiceCheckBox.IsChecked = UseCustomVoiceId;
            }
            if (customVoiceIdTextBox != null)
            {
                customVoiceIdTextBox.Text = CustomVoiceId ?? "";
                customVoiceIdTextBox.IsEnabled = UseCustomVoiceId;
            }
            if (voiceComboBox != null)
            {
                voiceComboBox.IsEnabled = !UseCustomVoiceId;
            }
            
            // Update voice list based on service
            UpdateVoiceList();
            
            // Set voice (only if not using custom voice ID)
            if (!UseCustomVoiceId)
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
        }
        
        private void TtsServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ttsServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedService = selectedItem.Content.ToString() ?? "ElevenLabs";
                UpdateVoiceList();
                
                // Show/hide custom voice ID panel for ElevenLabs
                if (customVoicePanel != null)
                {
                    customVoicePanel.Visibility = SelectedService == "ElevenLabs" ? Visibility.Visible : Visibility.Collapsed;
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
            if (voiceComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                SelectedVoice = selectedItem.Tag.ToString() ?? "";
            }
        }
        
        private void UseCustomVoiceCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (useCustomVoiceCheckBox != null)
            {
                UseCustomVoiceId = useCustomVoiceCheckBox.IsChecked ?? false;
                if (customVoiceIdTextBox != null)
                {
                    customVoiceIdTextBox.IsEnabled = UseCustomVoiceId;
                }
                if (voiceComboBox != null)
                {
                    voiceComboBox.IsEnabled = !UseCustomVoiceId;
                }
            }
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate selection
            if (SelectedService == "ElevenLabs" && UseCustomVoiceId)
            {
                if (customVoiceIdTextBox != null && !string.IsNullOrWhiteSpace(customVoiceIdTextBox.Text))
                {
                    CustomVoiceId = customVoiceIdTextBox.Text.Trim();
                    SelectedVoice = CustomVoiceId; // Use custom voice ID as the selected voice
                }
                else
                {
                    MessageBox.Show("Please enter a custom voice ID.", "Selection Required", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
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
                CustomVoiceId = null;
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

