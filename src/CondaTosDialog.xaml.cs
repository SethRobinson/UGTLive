using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class CondaTosDialog : Window
    {
        public bool TosAccepted { get; private set; } = false;
        
        public CondaTosDialog()
        {
            InitializeComponent();
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (acceptTosCheckBox.IsChecked == true)
            {
                TosAccepted = true;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(
                    "You must accept the Conda Terms of Service to proceed with installation.",
                    "Acceptance Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
        
        private void TosLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}

