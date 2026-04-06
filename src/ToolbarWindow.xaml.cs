using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace UGTLive
{
    public partial class ToolbarWindow : Window
    {
        public static ToolbarWindow? Instance { get; private set; }

        private bool _isInitialized = false;

        public ToolbarWindow()
        {
            Instance = this;
            InitializeComponent();
            IconHelper.SetWindowIcon(this);

            this.SourceInitialized += ToolbarWindow_SourceInitialized;
            this.Loaded += ToolbarWindow_Loaded;
            this.PreviewKeyDown += Application_KeyDown;
        }

        private void ToolbarWindow_SourceInitialized(object? sender, EventArgs e)
        {
            WindowCaptureHelper.SetExcludeFromCapture(this, "Toolbar");
        }

        private void ToolbarWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;
        }

        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            
            if (HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                bool handled = HotkeyManager.Instance.HandleKeyDownLocal(e.Key, modifiers);
                if (handled || e.Key == System.Windows.Input.Key.Tab)
                {
                    e.Handled = true;
                }
            }
            else
            {
                bool handled = HotkeyManager.Instance.HandleKeyDownAll(e.Key, modifiers);
                if (handled || e.Key == System.Windows.Input.Key.Tab)
                {
                    e.Handled = true;
                }
            }
        }

        public void UpdateCaptureExclusion()
        {
            WindowCaptureHelper.SetExcludeFromCapture(this, "Toolbar");
        }

        public void BringToFront()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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

        private void UtilitiesButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void SaveScreenshotMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.HandleSaveScreenshot();
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
            bool isOn = mousePassthroughCheckBox.IsChecked ?? false;
            mousePassthroughCheckBox.Foreground = isOn ? System.Windows.Media.Brushes.Yellow : System.Windows.Media.Brushes.White;
            MainWindow.Instance?.HandlePassthroughChanged(isOn);
        }

        private void EditModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isOn = editModeCheckBox.IsChecked ?? false;
            editModeCheckBox.Foreground = isOn ? System.Windows.Media.Brushes.Yellow : System.Windows.Media.Brushes.White;
            MainWindow.Instance?.HandleEditModeChanged(isOn);
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
            mousePassthroughCheckBox.Foreground = enabled ? System.Windows.Media.Brushes.Yellow : System.Windows.Media.Brushes.White;
            _isInitialized = true;
        }

        public void UpdateUtilitiesMenuHotkeys()
        {
            string hotkeyStr = HotkeyManager.Instance.GetHotkeyDisplayString("save_screenshot");
            saveScreenshotMenuItem.Header = "Save Screenshot" + (string.IsNullOrEmpty(hotkeyStr) ? "" : hotkeyStr);
        }

        public void SyncEditMode(bool enabled)
        {
            _isInitialized = false;
            editModeCheckBox.IsChecked = enabled;
            editModeCheckBox.Foreground = enabled ? System.Windows.Media.Brushes.Yellow : System.Windows.Media.Brushes.White;
            _isInitialized = true;
        }
    }
}
