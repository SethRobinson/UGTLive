using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class LlamaCppModelSelectorWindow : Window
    {
        public string? SelectedModel { get; private set; }

        public LlamaCppModelSelectorWindow()
        {
            InitializeComponent();
            
            // Set high-res icon
            IconHelper.SetWindowIcon(this);
        }

        public void SetModels(List<string> models)
        {
            modelsListBox.ItemsSource = models;
            
            if (models.Count > 0)
            {
                statusTextBlock.Text = $"Found {models.Count} available model(s)";
                modelsListBox.SelectedIndex = 0;
            }
            else
            {
                statusTextBlock.Text = "No models found. Server may be running in single-model mode.";
            }
        }

        private void ModelsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (modelsListBox.SelectedItem is string selectedModel)
            {
                SelectedModel = selectedModel;
            }
        }

        private void ModelsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectedModel != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedModel != null)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a model.", "No Model Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

