using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace UGTLive
{
    public partial class ShutdownDialog : Window
    {
        public ShutdownDialog()
        {
            InitializeComponent();
        }
        
        public void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                statusText.Text = status;
            });
        }
    }
}

