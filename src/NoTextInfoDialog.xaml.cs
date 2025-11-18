using System.Windows;

namespace UGTLive
{
    public partial class NoTextInfoDialog : Window
    {
        public NoTextInfoDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

