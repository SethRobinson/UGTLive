using System;
using System.Diagnostics;
using System.Windows;

namespace UGTLive
{
    public partial class UpdateAvailableDialog : Window
    {
        private string _downloadUrl;
        private string _updateMessage;
        private double _newVersion;
        
        public UpdateAvailableDialog(double newVersion, string updateMessage, string downloadUrl)
        {
            InitializeComponent();
            
            IconHelper.SetWindowIcon(this);
            
            _newVersion = newVersion;
            _updateMessage = updateMessage;
            _downloadUrl = downloadUrl;
            
            versionTextBlock.Text = $"Version {newVersion.ToString("0.00")} is now available (you have {SplashManager.CurrentVersion.ToString("0.00")})";
            updateMessageTextBlock.Text = updateMessage;
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open the download URL in the default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = _downloadUrl,
                    UseShellExecute = true
                });
                
                Console.WriteLine($"Opening download URL: {_downloadUrl}");
                
                // Set dialog result to true
                DialogResult = true;
                
                // Close this dialog first
                Close();
                
                // Shutdown the application on the next dispatcher cycle to avoid InvalidOperationException
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening download page: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Failed to open download page: {ex.Message}\n\nPlease visit rtsoft.com to download manually.", 
                    "Error", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}

