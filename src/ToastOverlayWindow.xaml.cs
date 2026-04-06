using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UGTLive
{
    public partial class ToastOverlayWindow : Window
    {
        private DispatcherTimer? _dismissTimer;

        public ToastOverlayWindow()
        {
            InitializeComponent();
            SourceInitialized += ToastOverlayWindow_SourceInitialized;
        }

        private void ToastOverlayWindow_SourceInitialized(object? sender, EventArgs e)
        {
            WindowCaptureHelper.ExcludeFromCaptureByHandle(new WindowInteropHelper(this).Handle);
        }

        private void startDismissTimer(double seconds)
        {
            _dismissTimer = new DispatcherTimer();
            _dismissTimer.Interval = TimeSpan.FromSeconds(seconds);
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer.Stop();
                fadeOutAndClose();
            };
            _dismissTimer.Start();
        }

        private void fadeOutAndClose()
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private void positionNearMainWindow()
        {
            if (MainWindow.Instance == null)
                return;

            var mainWindow = MainWindow.Instance;
            double mainLeft = mainWindow.Left;
            double mainTop = mainWindow.Top;
            double mainWidth = mainWindow.Width;
            double mainHeight = mainWindow.Height;

            UpdateLayout();

            Left = mainLeft + (mainWidth - ActualWidth) / 2;
            Top = mainTop + mainHeight - ActualHeight - 40;
        }

        public static void ShowToast(string message, double durationSeconds = 3.0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new ToastOverlayWindow();
                toast.ToastText.Text = message;
                toast.Show();
                toast.positionNearMainWindow();
                toast.startDismissTimer(durationSeconds);
            });
        }
    }
}
