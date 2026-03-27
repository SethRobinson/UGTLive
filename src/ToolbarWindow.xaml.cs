using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace UGTLive
{
    public partial class ToolbarWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static ToolbarWindow? Instance { get; private set; }

        private bool _isInitialized = false;

        public ToolbarWindow()
        {
            Instance = this;
            InitializeComponent();
            IconHelper.SetWindowIcon(this);

            this.SourceInitialized += ToolbarWindow_SourceInitialized;
            this.Loaded += ToolbarWindow_Loaded;
        }

        private void ToolbarWindow_SourceInitialized(object? sender, EventArgs e)
        {
            SetExcludeFromCapture();
        }

        private void ToolbarWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;
        }

        // --- Capture exclusion ---

        private void SetExcludeFromCapture()
        {
            try
            {
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;

                if (hwnd != IntPtr.Zero)
                {
                    uint affinity = visibleInScreenshots ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE;
                    bool success = SetWindowDisplayAffinity(hwnd, affinity);
                    Console.WriteLine($"Toolbar window {(visibleInScreenshots ? "included in" : "excluded from")} screen capture (HWND: {hwnd}, success: {success})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting toolbar capture exclusion: {ex.Message}");
            }
        }

        public void UpdateCaptureExclusion()
        {
            SetExcludeFromCapture();
        }

        public void BringToFront()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);

            if (SettingsWindow.IsOpenAndVisible())
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && !SettingsWindow.IsOpenAndVisible())
                {
                    BringToFront();
                }
            }), DispatcherPriority.Input);
        }

        // --- Window-level drag (anywhere that isn't an interactive control) ---

        private bool isInteractiveElement(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is System.Windows.Controls.Button ||
                    element is System.Windows.Controls.CheckBox ||
                    element is System.Windows.Controls.RadioButton)
                    return true;
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isInteractiveElement(e.OriginalSource as DependencyObject))
                return;

            if (e.ClickCount == 2)
            {
                MainWindow.Instance?.ResetToolbarPosition();
                e.Handled = true;
                return;
            }

            this.DragMove();
            e.Handled = true;
            MainWindow.Instance?.SaveToolbarOffset();
        }

        // --- Button click delegates (forward to MainWindow) ---

        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleHideButton();
        }

        private void DrawBorderButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleDrawBorderButton();
        }

        private void MinimizeAppButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleMinimizeButton();
        }

        private void CloseAppButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Close();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleToggleButton();
        }

        private void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleSnapshotButton();
        }

        private void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleMonitorButton();
        }

        private void ChatBoxButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleChatBoxButton();
        }

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleListenButton();
        }

        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleLogButton();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleSettingsButton();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleExportButton();
        }

        private void PlayAllAudioButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandlePlayAllAudioButton();
        }

        private void OverlayRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            MainWindow.Instance?.HandleOverlayRadioChanged(sender);
        }

        private void MousePassthroughCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            MainWindow.Instance?.HandlePassthroughChanged(mousePassthroughCheckBox.IsChecked ?? false);
        }

        // --- Sync state from MainWindow ---

        public void SyncOverlayMode(string mode)
        {
            _isInitialized = false;
            switch (mode)
            {
                case "Hide":
                    overlayHideRadio.IsChecked = true;
                    break;
                case "Source":
                    overlaySourceRadio.IsChecked = true;
                    break;
                case "Translated":
                    overlayTranslatedRadio.IsChecked = true;
                    break;
            }
            _isInitialized = true;
        }

        public void SyncPassthrough(bool enabled)
        {
            _isInitialized = false;
            mousePassthroughCheckBox.IsChecked = enabled;
            _isInitialized = true;
        }
    }
}
