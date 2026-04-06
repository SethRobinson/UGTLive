using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Drawing.Imaging;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Text;
using System.Windows.Shell;
using System.Threading.Tasks;
using MessageBox = System.Windows.MessageBox;


namespace UGTLive
{
    public partial class MainWindow
    {
        // Remember the monitor window position
        private double monitorWindowLeft = -1;
        private double monitorWindowTop = -1;

        // ChatBox management
        private ChatBoxWindow? chatBoxWindow;
        private bool isChatBoxVisible = false;
        private bool isSelectingChatBoxArea = false;
        private bool _chatBoxEventsAttached = false;

        // Track overlay mode for MainWindow
        private OverlayMode _currentOverlayMode = OverlayMode.Translated; // Default to Translated
        private bool _updatingOverlayMode = false; // Flag to prevent event recursion

        // Public getter for overlay mode
        public OverlayMode GetOverlayMode()
        {
            return _currentOverlayMode;
        }

        // Toggle the monitor window
        public void HandleMonitorButton()
        {
            ToggleMonitorWindow();
        }

        public void UpdateMonitorButtonState(bool isVisible)
        {
            if (monitorButton == null) return;
            monitorButton.Background = isVisible
                ? new SolidColorBrush(Color.FromRgb(46, 160, 67))
                : new SolidColorBrush(Color.FromRgb(95, 95, 95));
        }

        // Show/hide the monitor window
        private void ToggleMonitorWindow()
        {
            if (MonitorWindow.Instance.IsVisible)
            {
                // Store current position before hiding
                monitorWindowLeft = MonitorWindow.Instance.Left;
                monitorWindowTop = MonitorWindow.Instance.Top;
                
                Console.WriteLine($"Saving monitor position: {monitorWindowLeft}, {monitorWindowTop}");
                
                MonitorWindow.Instance.Hide();
                Console.WriteLine("Monitor window hidden from MainWindow toggle");
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
            }
            else
            {
                // Always use the remembered position if it has been set
                // We use Double.MinValue as our uninitialized flag
                if (monitorWindowLeft != -1 || monitorWindowTop != -1)
                {
                    // Restore previous position
                    MonitorWindow.Instance.Left = monitorWindowLeft;
                    MonitorWindow.Instance.Top = monitorWindowTop;
                    Console.WriteLine($"Restoring monitor position to: {monitorWindowLeft}, {monitorWindowTop}");
                }
                else
                {
                    // Only position to the right if we don't have a saved position yet
                    // This should only happen on first run
                    PositionMonitorWindowToTheRight();
                    Console.WriteLine("No saved position, positioning monitor window to the right");
                }
                
                // Set MainWindow as owner to ensure Monitor window appears above it
                MonitorWindow.Instance.Owner = this;
                MonitorWindow.Instance.Show();
                _toolbarWindow?.BringToFront();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Monitor window shown at position {MonitorWindow.Instance.Left}, {MonitorWindow.Instance.Top}");
                }
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Active indicator

                // If we have a recent screenshot, load it
                if (File.Exists(outputPath))
                {
                    MonitorWindow.Instance.UpdateScreenshot(outputPath);
                    MonitorWindow.Instance.RefreshOverlays();
                }
            }
        }

        // Position the monitor window to the right of the main window
        private void PositionMonitorWindowToTheRight()
        {
            // Get the position of the main window
            double mainRight = this.Left + this.ActualWidth;
            double mainTop = this.Top;
            
            // Set the position of the monitor window
            MonitorWindow.Instance.Left = mainRight + 10; // 10px gap between windows
            MonitorWindow.Instance.Top = mainTop;
        }

        public void HandleLogButton()
        {
            toggleLogWindow();
        }
        
        // Toggle log window visibility
        private void toggleLogWindow()
        {
            if (LogWindow.Instance.IsVisible)
            {
                // Hide log window
                LogWindow.Instance.Hide();
                logButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
            }
            else
            {
                // Show log window
                // Set MainWindow as owner to ensure Log window appears above it
                LogWindow.Instance.Owner = this;
                LogWindow.Instance.Show();
                _toolbarWindow?.BringToFront();
                logButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Active indicator
            }
        }
        
        public void updateLogButtonState(bool isVisible)
        {
            if (logButton == null) return;
            logButton.Background = isVisible
                ? new SolidColorBrush(Color.FromRgb(46, 160, 67))
                : new SolidColorBrush(Color.FromRgb(95, 95, 95));
        }

        // ChatBox Button click handler
        public void HandleChatBoxButton()
        {
            ToggleChatBox();
        }
        
        // Toggle ChatBox visibility and position
        private void ToggleChatBox()
        {
            if (isSelectingChatBoxArea)
            {
                // Cancel the selection mode if already selecting
                isSelectingChatBoxArea = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral

                // Find and close any existing selector window
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is ChatBoxSelectorWindow selectorWindow)
                    {
                        selectorWindow.Close();
                        return;
                    }
                }
                return;
            }
            
            // ChatBoxWindow.Instance is always available, but may not be visible
            // Make sure our chatBoxWindow reference is up to date
            chatBoxWindow = ChatBoxWindow.Instance;
            
            if (isChatBoxVisible && chatBoxWindow != null)
            {
                // Hide ChatBox
                chatBoxWindow.Hide();
                isChatBoxVisible = false;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                
                // Don't set chatBoxWindow to null here - we're just hiding it, not closing it
            }
            else
            {
                // Show selector to allow user to position ChatBox
                ChatBoxSelectorWindow selectorWindow = ChatBoxSelectorWindow.GetInstance();
                selectorWindow.SelectionComplete += ChatBoxSelector_SelectionComplete;
                selectorWindow.Closed += (s, e) => 
                {
                    isSelectingChatBoxArea = false;
                    // Only set button to blue if the ChatBox isn't visible (was cancelled)
                    if (!isChatBoxVisible || chatBoxWindow == null || !chatBoxWindow.IsVisible)
                    {
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                    }
                };
                // Set MainWindow as owner to ensure selector window appears above it
                selectorWindow.Owner = this;
                selectorWindow.Show();
                _toolbarWindow?.BringToFront();
                
                // Set button to red while selector is active
                isSelectingChatBoxArea = true;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Red
            }
        }
        
        // Handle selection completion
        private void ChatBoxSelector_SelectionComplete(object? sender, Rect selectionRect)
        {
            // Use the existing ChatBoxWindow.Instance
            chatBoxWindow = ChatBoxWindow.Instance;
            
            // Check if event handlers are already attached
            if (!_chatBoxEventsAttached && chatBoxWindow != null)
            {
                // Subscribe to both Closed and IsVisibleChanged events
                chatBoxWindow.Closed += (s, e) =>
                {
                    isChatBoxVisible = false;
                    chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                };
                
                // Also handle visibility changes for when the X button is clicked (which now hides instead of closes)
                chatBoxWindow.IsVisibleChanged += (s, e) =>
                {
                    if (!(bool)e.NewValue) // Window is now hidden
                    {
                        isChatBoxVisible = false;
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                    }
                };
                
                _chatBoxEventsAttached = true;
            }
            
            // Position and size the ChatBox
            chatBoxWindow!.Left = selectionRect.Left;
            chatBoxWindow.Top = selectionRect.Top;
            chatBoxWindow.Width = selectionRect.Width;
            chatBoxWindow.Height = selectionRect.Height;
            
            // Show the ChatBox
            // Set MainWindow as owner to ensure ChatBox window appears above it
            chatBoxWindow.Owner = this;
            chatBoxWindow.Show();
            _toolbarWindow?.BringToFront();
            isChatBoxVisible = true;
            chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Red when active
            
            // The ChatBox will get its data from MainWindow.GetTranslationHistory()
            // No need to manually load entries, just trigger an update
            if (chatBoxWindow != null)
            {
                Console.WriteLine($"Updating ChatBox with {_translationHistory.Count} translation entries");
                chatBoxWindow.UpdateChatHistory();
            }
        }

        // Initialize console window with proper encoding and font
        private void InitializeConsole()
        {
            AllocConsole();
            
            // Disable console input to prevent the app from freezing
            DisableConsoleInput();
            
            // Set Windows console code page to UTF-8 (65001)
            SetConsoleCP(65001);
            SetConsoleOutputCP(65001);
            
            // Set up a proper font for Japanese characters
            IntPtr hConsoleOutput = GetStdHandle(STD_OUTPUT_HANDLE);
            CONSOLE_FONT_INFOEX fontInfo = new CONSOLE_FONT_INFOEX();
            fontInfo.cbSize = (uint)Marshal.SizeOf(fontInfo);
            fontInfo.FaceName = "MS Gothic"; // Font with good Japanese support
            fontInfo.FontFamily = 54; // FF_MODERN and TMPF_TRUETYPE
            fontInfo.FontWeight = 400; // Normal weight
            fontInfo.dwFontSize = new COORD { X = 0, Y = 16 }; // Font size
            SetCurrentConsoleFontEx(hConsoleOutput, false, ref fontInfo);
            
            // Set .NET console encoding to UTF-8
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            
            // Redirect standard output to the console with UTF-8 encoding
            // Only set if LogWindow hasn't already set up console redirection
            if (!(Console.Out is MultiTextWriter))
            {
                StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8)
                {
                    AutoFlush = true
                };
                Console.SetOut(standardOutput);
            }
        }
        
        // Disable console input to prevent app freezing when focus is in the console
        private void DisableConsoleInput()
        {
            try
            {
                // Get the console input handle
                IntPtr hStdIn = GetStdHandle(STD_INPUT_HANDLE);
                if (hStdIn == IntPtr.Zero || hStdIn == new IntPtr(-1))
                {
                    Console.WriteLine("Error getting console input handle");
                    return;
                }
                
                // Get current console mode
                uint mode;
                if (!GetConsoleMode(hStdIn, out mode))
                {
                    Console.WriteLine($"Error getting console mode: {Marshal.GetLastWin32Error()}");
                    return;
                }
                
                // CRITICAL: Disable QuickEdit mode to prevent console from blocking when user selects text
                // QuickEdit mode causes the entire app to freeze when text is selected in the console
                // We must use ENABLE_EXTENDED_FLAGS and explicitly turn off ENABLE_QUICK_EDIT_MODE
                uint newMode = ENABLE_EXTENDED_FLAGS;
                
                // Remove QuickEdit and mouse input from the mode
                newMode &= ~ENABLE_QUICK_EDIT_MODE;
                newMode &= ~ENABLE_MOUSE_INPUT;
                newMode &= ~ENABLE_INSERT_MODE;
                
                if (!SetConsoleMode(hStdIn, newMode))
                {
                    Console.WriteLine($"Error setting console mode: {Marshal.GetLastWin32Error()}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disabling console input: {ex.Message}");
            }
        }

        private void ToggleMainWindowVisibility()
        {
            HandleHideButton();
        }
        
        private void TogglePassthrough()
        {
            bool currentState = mousePassthroughCheckBox?.IsChecked ?? false;
            bool newState = !currentState;
            // Setting IsChecked triggers the checkbox's Checked/Unchecked event in the toolbar,
            // which in turn calls HandlePassthroughChanged
            if (mousePassthroughCheckBox != null)
            {
                mousePassthroughCheckBox.IsChecked = newState;
            }
            Console.WriteLine($"Passthrough toggled: {(newState ? "enabled" : "disabled")}");
        }
        
        // Toggle overlay mode between Hide, Source, and Translated
        private void ToggleOverlayMode()
        {
            _updatingOverlayMode = true;
            
            try
            {
                if (_currentOverlayMode == OverlayMode.Hide)
                {
                    _currentOverlayMode = OverlayMode.Source;
                    overlaySourceRadio.IsChecked = true;
                }
                else if (_currentOverlayMode == OverlayMode.Source)
                {
                    _currentOverlayMode = OverlayMode.Translated;
                    overlayTranslatedRadio.IsChecked = true;
                }
                else
                {
                    _currentOverlayMode = OverlayMode.Hide;
                    overlayHideRadio.IsChecked = true;
                }
                
                // Save to config and update display
                string mode = _currentOverlayMode switch
                {
                    OverlayMode.Hide => "Hide",
                    OverlayMode.Source => "Source",
                    OverlayMode.Translated => "Translated",
                    _ => "Translated"
                };
                ConfigManager.Instance.SetMainWindowOverlayMode(mode);
                RefreshMainWindowOverlays();
                
                Console.WriteLine($"Overlay mode toggled to: {_currentOverlayMode}");
            }
            finally
            {
                _updatingOverlayMode = false;
            }
        }
        
        // Cycle overlay mode in reverse order: Translated -> Source -> Hide -> Translated
        private void PreviousOverlayMode()
        {
            _updatingOverlayMode = true;
            
            try
            {
                if (_currentOverlayMode == OverlayMode.Hide)
                {
                    _currentOverlayMode = OverlayMode.Translated;
                    overlayTranslatedRadio.IsChecked = true;
                }
                else if (_currentOverlayMode == OverlayMode.Source)
                {
                    _currentOverlayMode = OverlayMode.Hide;
                    overlayHideRadio.IsChecked = true;
                }
                else
                {
                    _currentOverlayMode = OverlayMode.Source;
                    overlaySourceRadio.IsChecked = true;
                }
                
                // Save to config and update display
                string mode = _currentOverlayMode switch
                {
                    OverlayMode.Hide => "Hide",
                    OverlayMode.Source => "Source",
                    OverlayMode.Translated => "Translated",
                    _ => "Translated"
                };
                ConfigManager.Instance.SetMainWindowOverlayMode(mode);
                RefreshMainWindowOverlays();
                
                Console.WriteLine($"Overlay mode previous to: {_currentOverlayMode}");
            }
            finally
            {
                _updatingOverlayMode = false;
            }
        }

        public void HandleOverlayRadioChanged(object sender)
        {
            if (_updatingOverlayMode)
                return;
                
            if (sender is System.Windows.Controls.RadioButton radioButton && radioButton.Tag != null)
            {
                string mode = radioButton.Tag.ToString() ?? "Translated";
                
                switch (mode)
                {
                    case "Hide":
                        _currentOverlayMode = OverlayMode.Hide;
                        break;
                    case "Source":
                        _currentOverlayMode = OverlayMode.Source;
                        break;
                    case "Translated":
                        _currentOverlayMode = OverlayMode.Translated;
                        break;
                }
                
                Console.WriteLine($"MainWindow overlay mode changed to: {_currentOverlayMode}");
                
                // Save to config
                ConfigManager.Instance.SetMainWindowOverlayMode(mode);
                
                // Update overlay display
                RefreshMainWindowOverlays();
            }
        }

        public void HandlePassthroughChanged(bool isEnabled)
        {
            ConfigManager.Instance.SetMainWindowMousePassthrough(isEnabled);
            updateMousePassthrough(isEnabled);
            UpdateMainWindowTextInteraction();
            BringToFront();
            Console.WriteLine($"Mouse passthrough {(isEnabled ? "enabled" : "disabled")}");
        }
        
        public void HandleEditModeChanged(bool isEnabled)
        {
            ConfigManager.Instance.SetEditModeEnabled(isEnabled);
            
            if (isEnabled)
            {
                // Force passthrough off when entering edit mode
                ConfigManager.Instance.SetMainWindowMousePassthrough(false);
                updateMousePassthrough(false);
                ToolbarWindow.Instance?.SyncPassthrough(false);
            }
            
            UpdateMainWindowTextInteraction();
            RefreshMainWindowOverlays();
            MonitorWindow.Instance?.RefreshOverlays();
            BringToFront();
            Console.WriteLine($"Edit mode {(isEnabled ? "enabled" : "disabled")}");
        }
        
        // Helper method to update mouse passthrough state
        private void updateMousePassthrough(bool enabled)
        {
            if (OverlayContent == null)
                return;
                
            if (enabled)
            {
                // Enable mouse passthrough - clicks go through to apps behind
                OverlayContent.IsHitTestVisible = false;
                OverlayContent.Background = System.Windows.Media.Brushes.Transparent;
                Console.WriteLine("Mouse passthrough: overlay now transparent and non-interactive");
            }
            else
            {
                // Disable mouse passthrough - allow interaction with text overlays
                OverlayContent.IsHitTestVisible = true;
                OverlayContent.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(1, 0, 0, 0)); // #01000000
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("Mouse passthrough: overlay now interactive with minimal background");
                }
            }
        }
    }
}
