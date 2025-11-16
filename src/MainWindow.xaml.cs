using System;
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
    public partial class MainWindow : Window
    {
        // For screen capture
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
        
        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        // ShowWindow commands
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        // Constants
        private const string DEFAULT_OUTPUT_PATH = @"webserver\image_to_process.png";
        private const double CAPTURE_INTERVAL_SECONDS = 1;
        private const int TITLE_BAR_HEIGHT = 50; // Height of our custom title bar (includes 10px for resize)

        bool _bOCRCheckIsWanted = false;
        private DateTime _ocrReenableTime = DateTime.MinValue;
        
        public void SetOCRCheckIsWanted(bool bCaptureIsWanted) 
        { 
            // Don't allow re-enabling OCR if we're still in the delay period after clearing overlays
            if (bCaptureIsWanted && DateTime.Now < _ocrReenableTime)
            {
                Console.WriteLine($"OCR re-enable blocked - still in delay period for {(_ocrReenableTime - DateTime.Now).TotalSeconds:F1}s");
                return;
            }
            _bOCRCheckIsWanted = bCaptureIsWanted; 
        }
        
        public bool GetOCRCheckIsWanted() { return _bOCRCheckIsWanted; }
        private bool isStarted = false;
        private DispatcherTimer _captureTimer;
        private string outputPath = DEFAULT_OUTPUT_PATH;
        private WindowInteropHelper helper;
        private System.Drawing.Rectangle captureRect;
        
        // Translation status timer and tracking
        private DispatcherTimer? _translationStatusTimer;
        private DateTime _translationStartTime;
        private bool _isShowingSettling = false;
        
        // OCR status display (no tracking - handled by Logic.cs)
        
        // Store previous capture position to calculate offset
        private int previousCaptureX;
        private int previousCaptureY;
        
        // Auto translation
        private bool isAutoTranslateEnabled = false;
        
        // ChatBox management
        private ChatBoxWindow? chatBoxWindow;
        private bool isChatBoxVisible = false;
        private bool isSelectingChatBoxArea = false;
        private bool _chatBoxEventsAttached = false;
        
        // Keep translation history even when ChatBox is closed
        private List<TranslationEntry> _translationHistory = new List<TranslationEntry>();
        
        // Accessor for ChatBoxWindow to get the translation history
        public List<TranslationEntry> GetTranslationHistory()
        {
            return _translationHistory;
        }
        
        // Method to clear translation history
        public void ClearTranslationHistory()
        {
            _translationHistory.Clear();
            Console.WriteLine("Translation history cleared from MainWindow");
        }
       
  
        //allow this to be accesible through an "Instance" variable
        public static MainWindow Instance { get { return _this!; } }
        // Socket connection status
        private TextBlock? socketStatusText;
        
        // Console visibility management
        private bool isConsoleVisible = false;
        private IntPtr consoleWindow;

        static MainWindow? _this = null;
     
        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleOutputCP(uint wCodePageID);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCP(uint wCodePageID);
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFOEX lpConsoleCurrentFontEx);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);
        
        // Console mode control
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        
        // Standard input handle constant
        public const int STD_INPUT_HANDLE = -10;
        
        // Console input mode flags
        public const uint ENABLE_ECHO_INPUT = 0x0004;
        public const uint ENABLE_LINE_INPUT = 0x0002;
        public const uint ENABLE_PROCESSED_INPUT = 0x0001;
        public const uint ENABLE_WINDOW_INPUT = 0x0008;
        public const uint ENABLE_MOUSE_INPUT = 0x0010;
        public const uint ENABLE_INSERT_MODE = 0x0020;
        public const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        public const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        public const uint ENABLE_AUTO_POSITION = 0x0100;
        
        // Keyboard hooks are now managed in KeyboardShortcuts.cs
        
        // We'll use a different approach that doesn't rely on SetConsoleCtrlHandler
        
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CONSOLE_FONT_INFOEX
        {
            public uint cbSize;
            public uint nFont;
            public COORD dwFontSize;
            public int FontFamily;
            public int FontWeight;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FaceName;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;
        }
        
        public const int STD_OUTPUT_HANDLE = -11;
        public bool GetIsStarted() { return isStarted; }    
        public bool GetTranslateEnabled() { return isAutoTranslateEnabled; }
        
        // Methods for syncing UI controls with MonitorWindow
        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        
        public void SetOcrMethod(string method)
        {
            Console.WriteLine($"MainWindow.SetOcrMethod called with method: {method} (isInitializing: {_isInitializing})");
            
            // Only update the MainWindow's internal state during initialization
            // Don't update other windows or save to config
            if (_isInitializing)
            {
                Console.WriteLine($"Setting OCR method during initialization: {method}");
                selectedOcrMethod = method;
                // Important: Update status text even during initialization
                if (method == "Windows OCR")
                {
                    SetStatus("Using Windows OCR (built-in)");
                }
                else if (method == "Google Vision")
                {
                    SetStatus("Using Google Cloud Vision (non-local, costs $)");
                }
                else if (method == "Manga OCR")
                {
                    SetStatus("Using Manga OCR");
                }
                else if (method == "docTR")
                {
                    SetStatus("Using docTR");
                }
                else
                {
                    SetStatus("Using EasyOCR");
                }
                return;
            }
            
            // Only process if actually changing the method
            if (selectedOcrMethod != method)
            {
                Console.WriteLine($"MainWindow changing OCR method from {selectedOcrMethod} to {method}");
                selectedOcrMethod = method;
                // No need to handle socket connection here, the MonitorWindow handles that
                if (method == "Windows OCR")
                {
                    SetStatus("Using Windows OCR (built-in)");
                }
                else if (method == "Google Vision")
                {
                    SetStatus("Using Google Cloud Vision (non-local, costs $)");
                }
                else if (method == "Manga OCR")
                {
                    SetStatus("Using Manga OCR");
                    
                    // Ensure we're connected when switching to Manga OCR
                    if (!SocketManager.Instance.IsConnected)
                    {
                        Console.WriteLine("Socket not connected when switching to Manga OCR");
                        _ = Task.Run(async () => {
                            try {
                                bool reconnected = await SocketManager.Instance.TryReconnectAsync();
                                
                                if (!reconnected || !SocketManager.Instance.IsConnected)
                                {
                                    // Only show an error message if explicitly requested by user action
                                    Console.WriteLine("Failed to connect to socket server - Manga OCR will not be available");
                                }

                                
                            }
                            catch (Exception ex) {
                                Console.WriteLine($"Error reconnecting: {ex.Message}");
                                
                                // Show an error message
                                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show($"Socket connection error: {ex.Message}",
                                        "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                });
                            }
                        });
                    }
                }
                else if (method == "docTR")
                {
                    SetStatus("Using docTR");
                    
                    // Ensure we're connected when switching to docTR
                    if (!SocketManager.Instance.IsConnected)
                    {
                        Console.WriteLine("Socket not connected when switching to docTR");
                        _ = Task.Run(async () => {
                            try {
                                bool reconnected = await SocketManager.Instance.TryReconnectAsync();
                                
                                if (!reconnected || !SocketManager.Instance.IsConnected)
                                {
                                    // Only show an error message if explicitly requested by user action
                                    Console.WriteLine("Failed to connect to socket server - docTR will not be available");
                                }

                                
                            }
                            catch (Exception ex) {
                                Console.WriteLine($"Error reconnecting: {ex.Message}");
                                
                                // Show an error message
                                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show($"Socket connection error: {ex.Message}",
                                        "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                });
                            }
                        });
                    }
                }
                else
                {
                    SetStatus("Using EasyOCR");
                    
                    // Ensure we're connected when switching to EasyOCR
                    if (!SocketManager.Instance.IsConnected)
                    {
                        Console.WriteLine("Socket not connected when switching to EasyOCR");
                        _ = Task.Run(async () => {
                            try {
                                bool reconnected = await SocketManager.Instance.TryReconnectAsync();
                                
                                if (!reconnected || !SocketManager.Instance.IsConnected)
                                {
                                    // Only show an error message if explicitly requested by user action
                                    Console.WriteLine("Failed to connect to socket server - EasyOCR will not be available");
                                }

                                
                            }
                            catch (Exception ex) {
                                Console.WriteLine($"Error reconnecting: {ex.Message}");
                                
                                // Show an error message
                                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show($"Socket connection error: {ex.Message}",
                                        "Connection Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                });
                            }
                        });
                    }
                }
            }
        }
        
        public void SetAutoTranslateEnabled(bool enabled)
        {
            if (isAutoTranslateEnabled != enabled)
            {
                isAutoTranslateEnabled = enabled;
                
                // Save to config
                ConfigManager.Instance.SetAutoTranslateEnabled(enabled);
                
                // Clear text objects
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();
                
                // Force OCR to run again
                SetOCRCheckIsWanted(true);
                
                MonitorWindow.Instance.RefreshOverlays();
            }
        }
        
        // The global keyboard hooks are now managed by KeyboardShortcuts class
        
        public MainWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MainWindow constructor: Setting _isInitializing to true");
            
            _this = this;
            InitializeComponent();

            // Initialize console but keep it hidden initially
            InitializeConsole();
            
            // Hide the console window initially
            consoleWindow = GetConsoleWindow();
            KeyboardShortcuts.SetConsoleWindowHandle(consoleWindow);
            ShowWindow(consoleWindow, SW_HIDE);

            // Initialize helper
            helper = new WindowInteropHelper(this);

            // Setup timer for continuous capture
            _captureTimer = new DispatcherTimer();
            _captureTimer.Interval = TimeSpan.FromSeconds(1 / 60.0f);
            _captureTimer.Tick += OnUpdateTick;
            _captureTimer.Start();
            
            // Initial update of capture rectangle and setup after window is loaded
            this.Loaded += MainWindow_Loaded;
            
            // Create socket status text block
            CreateSocketStatusIndicator();
            
            // Get reference to the already initialized ChatBoxWindow
            chatBoxWindow = ChatBoxWindow.Instance;

            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;
            
            // Register hotkey events with the new HotkeyManager
            HotkeyManager.Instance.StartStopRequested += (s, e) => OnStartButtonToggleClicked(toggleButton, new RoutedEventArgs());
            HotkeyManager.Instance.MonitorToggleRequested += (s, e) => MonitorButton_Click(monitorButton, new RoutedEventArgs());
            HotkeyManager.Instance.ChatBoxToggleRequested += (s, e) => ChatBoxButton_Click(chatBoxButton, new RoutedEventArgs());
            HotkeyManager.Instance.SettingsToggleRequested += (s, e) => SettingsButton_Click(settingsButton, new RoutedEventArgs());
            HotkeyManager.Instance.LogToggleRequested += (s, e) => LogButton_Click(logButton, new RoutedEventArgs());
            HotkeyManager.Instance.ListenToggleRequested += (s, e) => ListenButton_Click(listenButton, new RoutedEventArgs());
            HotkeyManager.Instance.ViewInBrowserRequested += (s, e) => ExportButton_Click(exportButton, new RoutedEventArgs());
            HotkeyManager.Instance.MainWindowVisibilityToggleRequested += (s, e) => ToggleMainWindowVisibility();
            HotkeyManager.Instance.ClearOverlaysRequested += (s, e) => {
                // Cancel any in-progress translation
                Logic.Instance.CancelTranslation();
                
                // Clear text objects instantly
                Logic.Instance.ClearAllTextObjects();
                
                // Clear hash so OCR will recreate text if active
                Logic.Instance.ResetHash();
                
                // Immediately disable OCR capture to prevent it from triggering during the delay
                SetOCRCheckIsWanted(false);
                
                // Set the re-enable time based on configured delay
                double delaySeconds = ConfigManager.Instance.GetOverlayClearDelaySeconds();
                _ocrReenableTime = DateTime.Now.AddSeconds(delaySeconds);
                Console.WriteLine($"OCR disabled until {_ocrReenableTime:HH:mm:ss.fff} ({delaySeconds}s delay)");
                
                // Refresh overlays immediately and synchronously for instant visual update
                if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    // Clear HTML cache to force WebView update
                    _lastOverlayHtml = string.Empty;
                    MonitorWindow.Instance.RefreshOverlays();
                    RefreshMainWindowOverlays();
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Clear HTML cache to force WebView update
                        _lastOverlayHtml = string.Empty;
                        MonitorWindow.Instance.RefreshOverlays();
                        RefreshMainWindowOverlays();
                    }, DispatcherPriority.Send);
                }
                
                Console.WriteLine("Overlays cleared");
                
                // If OCR is active, trigger it again after configured delay
                if (GetIsStarted())
                {
                    _ = Task.Delay((int)(delaySeconds * 1000)).ContinueWith(_ =>
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            SetOCRCheckIsWanted(true);
                            Console.WriteLine("OCR re-enabled after delay");
                        }), DispatcherPriority.Normal);
                    });
                }
            };
            HotkeyManager.Instance.PassthroughToggleRequested += (s, e) => TogglePassthrough();
            HotkeyManager.Instance.OverlayModeToggleRequested += (s, e) => ToggleOverlayMode();
            
            // Start gamepad manager
            GamepadManager.Instance.Start();
            
            // Set up global keyboard hook to handle shortcuts even when console has focus
            KeyboardShortcuts.InitializeGlobalHook();
            
            // Set up tooltip exclusion from screenshots
            SetupTooltipExclusion();
        }
        
        // Setup tooltip exclusion from screenshots
        private void SetupTooltipExclusion()
        {
            // Use ToolTipService to add an event handler for when any tooltip opens
            this.AddHandler(ToolTipService.ToolTipOpeningEvent, new RoutedEventHandler(OnToolTipOpening));
        }
        
        private void OnToolTipOpening(object sender, RoutedEventArgs e)
        {
            // Schedule exclusion check on next UI thread cycle (tooltip window needs to be created first)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExcludeTooltipFromCapture();
            }), DispatcherPriority.Background);
        }
        
        private void ExcludeTooltipFromCapture()
        {
            try
            {
                // Check if user wants windows visible in screenshots
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                
                // If visible in screenshots, don't exclude
                if (visibleInScreenshots)
                {
                    return;
                }
                
                // Find all tooltip windows and exclude them
                var tooltipWindows = System.Windows.Application.Current.Windows.OfType<Window>()
                    .Where(w => w.GetType().Name.Contains("ToolTip") || w.GetType().Name.Contains("Popup"));
                
                foreach (var window in tooltipWindows)
                {
                    var helper = new WindowInteropHelper(window);
                    IntPtr hwnd = helper.Handle;
                    
                    if (hwnd != IntPtr.Zero)
                    {
                        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                    }
                }
                
                // Also try to find popup windows via interop
                // WPF tooltips are displayed in Popup windows which are top-level HWND windows
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                foreach (System.Diagnostics.ProcessThread thread in currentProcess.Threads)
                {
                    try
                    {
                        EnumThreadWindows((uint)thread.Id, (hWnd, lParam) =>
                        {
                            var className = new StringBuilder(256);
                            GetClassName(hWnd, className, className.Capacity);
                            string cls = className.ToString();
                            
                            // WPF tooltip windows typically have these class names
                            if (cls.Contains("Popup") || cls.Contains("ToolTip") || cls.Contains("HwndWrapper"))
                            {
                                SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE);
                            }
                            
                            return true; // Continue enumeration
                        }, IntPtr.Zero);
                    }
                    catch
                    {
                        // Thread may have terminated, ignore
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error excluding tooltip from capture: {ex.Message}");
            }
        }
        
        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
        
        private delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);
        
       
        // add method for show/hide the main window
        private void ToggleMainWindowVisibility()
        {
            if (MainBorder.Visibility == Visibility.Visible)
            {
                HideButton_Click(hideButton, new RoutedEventArgs());
            }
            else
            {
                if (showButton != null)
                {
                    ShowButton_Click(showButton, new RoutedEventArgs());
                }
            }

        }
        
        // Toggle passthrough mode
        private void TogglePassthrough()
        {
            if (mousePassthroughCheckBox != null)
            {
                bool newState = !(mousePassthroughCheckBox.IsChecked ?? false);
                mousePassthroughCheckBox.IsChecked = newState;
                Console.WriteLine($"Passthrough toggled: {(newState ? "enabled" : "disabled")}");
            }
        }
        
        // Toggle overlay mode between Hide, Source, and Translated
        private void ToggleOverlayMode()
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
            
            Console.WriteLine($"Overlay mode toggled to: {_currentOverlayMode}");
        }
        
        // Update tooltips with current hotkey bindings
        public void UpdateTooltips()
        {
            UpdateHotkeyTooltips();
        }
        
        private void UpdateHotkeyTooltips()
        {
            // Helper function to get hotkey string for an action
            string GetHotkeyString(string actionId)
            {
                var bindings = HotkeyManager.Instance.GetBindings(actionId);
                if (bindings.Count == 0)
                    return "";
                    
                List<string> parts = new List<string>();
                foreach (var binding in bindings)
                {
                    if (binding.HasKeyboardHotkey())
                        parts.Add(binding.GetKeyboardHotkeyString());
                    if (binding.HasGamepadHotkey())
                        parts.Add($"Gamepad: {binding.GetGamepadHotkeyString()}");
                }
                    
                return parts.Count > 0 ? $" ({string.Join(" or ", parts)})" : "";
            }
            
            // Force close any currently open tooltips so they refresh with new content
            ToolTipService.SetIsEnabled(this, false);
            ToolTipService.SetIsEnabled(this, true);
            
            // Update button tooltips
            if (toggleButton != null)
                toggleButton.ToolTip = $"Start/Stop OCR{GetHotkeyString("start_stop")}";
                
            if (monitorButton != null)
                monitorButton.ToolTip = $"Toggle Monitor Window{GetHotkeyString("toggle_monitor")}";
                
            if (chatBoxButton != null)
                chatBoxButton.ToolTip = $"Toggle ChatBox{GetHotkeyString("toggle_chatbox")}";
                
            if (settingsButton != null)
                settingsButton.ToolTip = $"Toggle Settings{GetHotkeyString("toggle_settings")}";
                
            if (logButton != null)
                logButton.ToolTip = $"Toggle Log Console{GetHotkeyString("toggle_log")}";
                
            if (listenButton != null)
                listenButton.ToolTip = $"Toggle voice listening{GetHotkeyString("toggle_listen")}";
                
            if (exportButton != null)
                exportButton.ToolTip = $"View current capture in browser{GetHotkeyString("view_in_browser")}";
                
            if (hideButton != null)
                hideButton.ToolTip = $"Toggle Main Window{GetHotkeyString("toggle_main_window")}";
                
            if (mousePassthroughCheckBox != null)
                mousePassthroughCheckBox.ToolTip = $"Toggle mouse passthrough mode{GetHotkeyString("toggle_passthrough")}";
            
            // Update overlay radio buttons
            string overlayHotkey = GetHotkeyString("toggle_overlay_mode");
            if (overlayHideRadio != null)
                overlayHideRadio.ToolTip = $"Hide overlay{overlayHotkey}";
            if (overlaySourceRadio != null)
                overlaySourceRadio.ToolTip = $"Show source text{overlayHotkey}";
            if (overlayTranslatedRadio != null)
                overlayTranslatedRadio.ToolTip = $"Show translated text{overlayHotkey}";
            
            // Setup individual tooltip opened handlers for each control
            SetupIndividualTooltipHandlers();
        }
        
        private void SetupIndividualTooltipHandlers()
        {
            var controls = new FrameworkElement[] 
            { 
                toggleButton, monitorButton, chatBoxButton, settingsButton, logButton, 
                listenButton, exportButton, hideButton, mousePassthroughCheckBox,
                overlayHideRadio, overlaySourceRadio, overlayTranslatedRadio
            };
            
            foreach (var control in controls)
            {
                if (control != null)
                {
                    ToolTipService.SetToolTip(control, control.ToolTip);
                    control.ToolTipOpening += (s, e) =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ExcludeTooltipFromCapture();
                        }), DispatcherPriority.Background);
                    };
                }
            }
        }

        public void SetStatus(string text)
        {
            if (socketStatusText != null)
            {
                socketStatusText!.Text = text;
            }
        }

        private void CreateSocketStatusIndicator()
        {
            // Create socket status text
            socketStatusText = new TextBlock
            {
                Text = "Connecting to Python backend...",
                Foreground = new SolidColorBrush(Colors.Red),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Update tooltips with hotkeys
            UpdateHotkeyTooltips();
            
            // Update capture rectangle
            UpdateCaptureRect();
           
            // Add socket status to the header
            if (HeaderBorder != null && HeaderBorder.Child is Grid headerGrid)
            {
                // Find the StackPanel in the header
                var elements = headerGrid.Children;
                foreach (var element in elements)
                {
                    if (element is StackPanel stackPanel && 
                        stackPanel.HorizontalAlignment == System.Windows.HorizontalAlignment.Left)
                    {
                        // Add socket status text to the stack panel
                        if (socketStatusText != null)
                        {
                            stackPanel.Children.Add(socketStatusText);
                        }
                        break;
                    }
                }
            }
            
           
            // Load OCR method from config
            string savedOcrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"MainWindow_Loaded: Loading OCR method from config: '{savedOcrMethod}'");
            
            // Set OCR method in this window (MainWindow)
            SetOcrMethod(savedOcrMethod);
            
            // Subscribe to translation events
            Logic.Instance.TranslationCompleted += Logic_TranslationCompleted;
            
            // Initialize monitor window position (but don't show it - defaults to off)
            if (!MonitorWindow.Instance.IsVisible)
            {
                // Position to the right of the main window for initial positioning
                PositionMonitorWindowToTheRight();
                
                // Consider this the initial position for the monitor window toggle
                monitorWindowLeft = MonitorWindow.Instance.Left;
                monitorWindowTop = MonitorWindow.Instance.Top;
                
                // Monitor window defaults to off (blue button)
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
            }
            
            // Test configuration loading
            TestConfigLoading();
            
            // Initialization is complete, now we can save settings changes
            _isInitializing = false;
            Console.WriteLine("MainWindow initialization complete. Settings changes will now be saved.");
            
            // Force the OCR method to match the config again
            // This ensures the config value is preserved and not overwritten
            string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");
            ConfigManager.Instance.SetOcrMethod(configOcrMethod);

            // Initialize the Logic
            Logic.Instance.Init();

            // Load language settings from config
            LoadLanguageSettingsFromConfig();
            
            // Load auto-translate setting from config
            isAutoTranslateEnabled = ConfigManager.Instance.IsAutoTranslateEnabled();
            
            // Initialize the overlay WebView2
            InitializeMainWindowOverlayWebView();
            
            // Load overlay mode from config
            string overlayMode = ConfigManager.Instance.GetMainWindowOverlayMode();
            switch (overlayMode)
            {
                case "Hide":
                    _currentOverlayMode = OverlayMode.Hide;
                    overlayHideRadio.IsChecked = true;
                    break;
                case "Source":
                    _currentOverlayMode = OverlayMode.Source;
                    overlaySourceRadio.IsChecked = true;
                    break;
                case "Translated":
                default:
                    _currentOverlayMode = OverlayMode.Translated;
                    overlayTranslatedRadio.IsChecked = true;
                    break;
            }
            
            // Set initial mouse passthrough state
            bool mousePassthrough = ConfigManager.Instance.GetMainWindowMousePassthrough();
            mousePassthroughCheckBox.IsChecked = mousePassthrough;
            updateMousePassthrough(mousePassthrough);
            Console.WriteLine($"MainWindow mouse passthrough initialized: {(mousePassthrough ? "enabled" : "disabled")}");
            
            // Set initial text interaction state based on passthrough (inverse relationship)
            bool canInteract = !mousePassthrough;
            if (textOverlayWebView != null)
            {
                textOverlayWebView.IsHitTestVisible = canInteract;
                Console.WriteLine($"MainWindow text interaction initialized: {(canInteract ? "enabled" : "disabled")}");
            }
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Only process hotkeys at window level if global hotkeys are disabled
            // (When global hotkeys are enabled, the global hook handles them)
            if (!HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                // Forward to the HotkeyManager
                var modifiers = System.Windows.Input.Keyboard.Modifiers;
                bool handled = HotkeyManager.Instance.HandleKeyDown(e.Key, modifiers);
                
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }
       
        private void TestConfigLoading()
        {
            try
            {
                // Get and log configuration values
                string apiKey = Logic.Instance.GetGeminiApiKey();
                string llmPrompt = Logic.Instance.GetLlmPrompt();
                string ocrMethod = ConfigManager.Instance.GetOcrMethod();
                string translationService = ConfigManager.Instance.GetCurrentTranslationService();
                
                Console.WriteLine("=== Configuration Test ===");
                Console.WriteLine($"API Key: {(string.IsNullOrEmpty(apiKey) ? "Not set" : "Set - " + apiKey.Substring(0, 4) + "...")}");
                Console.WriteLine($"LLM Prompt: {(string.IsNullOrEmpty(llmPrompt) ? "Not set" : "Set - " + llmPrompt.Length + " chars")}");
                Console.WriteLine($"OCR Method: {ocrMethod}");
                Console.WriteLine($"Translation Service: {translationService}");
                
                if (!string.IsNullOrEmpty(llmPrompt))
                {
                    Console.WriteLine("First 100 characters of LLM Prompt:");
                    Console.WriteLine(llmPrompt.Length > 100 ? llmPrompt.Substring(0, 100) + "..." : llmPrompt);
                }
                
                Console.WriteLine("=========================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing config: {ex.Message}");
            }
        }
          
        private void UpdateCaptureRect()
        {
            // Retrieve the handle using WindowInteropHelper
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            // Get our window position including the entire window (client + custom chrome)
            RECT windowRect;
            GetWindowRect(hwnd, out windowRect);

            // Get the actual header height (handles wrapping buttons dynamically)
            int customTitleBarHeight = TITLE_BAR_HEIGHT; // Default fallback
            if (TopControlGrid != null && TopControlGrid.ActualHeight > 0)
            {
                customTitleBarHeight = (int)Math.Ceiling(TopControlGrid.ActualHeight);
            }
            
            // Border thickness settings
            int leftBorderThickness = 9;  // Increased from 7 to 9
            int rightBorderThickness = 8; // Adjusted to 8
            int bottomBorderThickness = 9; // Increased from 7 to 9

            // Store previous position for calculating offset
            previousCaptureX = captureRect.Left;
            previousCaptureY = captureRect.Top;

            // Adjust the capture rectangle to exclude the custom title bar and border areas
            captureRect = new System.Drawing.Rectangle(
                windowRect.Left + leftBorderThickness,
                windowRect.Top + customTitleBarHeight,
                (windowRect.Right - windowRect.Left) - leftBorderThickness - rightBorderThickness,
                (windowRect.Bottom - windowRect.Top) - customTitleBarHeight - bottomBorderThickness);
                
            // If position changed and we have text objects, update their positions
            if ((previousCaptureX != captureRect.Left || previousCaptureY != captureRect.Top) && 
                Logic.Instance.TextObjects.Count > 0)
            {
                // Calculate the offset
                int offsetX = captureRect.Left - previousCaptureX;
                int offsetY = captureRect.Top - previousCaptureY;
                
                // Apply offset to text objects
                Logic.Instance.UpdateTextObjectPositions(offsetX, offsetY);
                
                Console.WriteLine($"Capture position changed by ({offsetX}, {offsetY}). Text overlays updated.");
            }
        }

        //!Main loop

        private void OnUpdateTick(object? sender, EventArgs e)
        {
          
            PerformCapture();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Standard header drag functionality
            if (e.ClickCount == 1)
            {
                this.DragMove();
                e.Handled = true;
                UpdateCaptureRect();
            }
        }
       
        private void OnStartButtonToggleClicked(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button btn = (System.Windows.Controls.Button)sender;

            if (isStarted)
            {
                Logic.Instance.ResetHash();
                isStarted = false;
                btn.Content = "Start";
                btn.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)); // Green
                // Don't clear text objects when stopping - keep them for overlay switching
                // Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.HideTranslationStatus();
                // Also hide the ChatBox "Waiting for translation" indicator (if visible)
                ChatBoxWindow.Instance?.HideTranslationStatus();
                
                // Hide OCR status when stopping
                Logic.Instance.HideOCRStatus();
                HideTranslationStatus();
                
                // Optional: Add a way to clear overlays manually if needed
                // You could add a separate "Clear" button or keyboard shortcut
            }
            else
            {
                // Clear old text objects when starting again
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
                
                isStarted = true;
                btn.Content = "Stop";
                UpdateCaptureRect();
                
                // Clear any delay restriction when manually starting OCR
                _ocrReenableTime = DateTime.MinValue;
                SetOCRCheckIsWanted(true);
                btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red
                
                // Show OCR status when starting (handled by Logic.cs)
            }
        }

        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        
        // Button to show the window when it's hidden
        private System.Windows.Controls.Button? showButton;
        
        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide the main window elements
            MainBorder.Visibility = Visibility.Collapsed;
            
            // Create a small "Show" button that remains visible
            if (showButton == null)
            {
                showButton = new System.Windows.Controls.Button
                {
                    Content = "Show",
                    Width = 30,
                    Height = 20,
                    Padding = new Thickness(2, 0, 2, 0),
                    FontSize = 10,
                    Background = new SolidColorBrush(Color.FromRgb(20, 180, 20)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(10, 10, 0, 0),
                    // Make sure it receives all input events
                    IsHitTestVisible = true
                };
                
                // Make button visible to WindowChrome
                WindowChrome.SetIsHitTestVisibleInChrome(showButton, true);
                
                showButton.Click += ShowButton_Click;
                
                // Get the main grid
                var mainGrid = this.Content as Grid;
                if (mainGrid != null)
                {
                    // Add the button as the last child (top-most)
                    mainGrid.Children.Add(showButton);
                    
                    // Ensure it's on top by setting a high ZIndex
                    System.Windows.Controls.Panel.SetZIndex(showButton, 1000);
                    
                    Console.WriteLine("Show button added to main grid");
                }
                else
                {
                    Console.WriteLine("ERROR: Couldn't find main grid");
                }
            }
            else
            {
                showButton.Visibility = Visibility.Visible;
            }
        }
        
        private void ShowButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the main window elements
            MainBorder.Visibility = Visibility.Visible;
            
            // Hide the show button
            if (showButton != null)
            {
                showButton.Visibility = Visibility.Collapsed;
            }
        }

        private bool _isShuttingDown = false;
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // If we're already shutting down, allow the close
            if (_isShuttingDown)
            {
                base.OnClosing(e);
                return;
            }
            
            // Cancel the close for now
            e.Cancel = true;
            
            // Close Monitor window immediately before showing shutdown dialog
            if (MonitorWindow.Instance.IsVisible)
            {
                MonitorWindow.Instance.ForceClose();
            }
            
            // Show shutdown dialog
            ShutdownDialog shutdownDialog = new ShutdownDialog();
            shutdownDialog.Show();
            shutdownDialog.UpdateStatus("Closing connections...");
            
            // Process UI messages to ensure dialog is visible
            System.Windows.Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            
            // Perform shutdown operations
            PerformShutdown(shutdownDialog);
        }
        
        private async void PerformShutdown(ShutdownDialog shutdownDialog)
        {
            try
            {
                // Remove global keyboard hook
                shutdownDialog.UpdateStatus("Closing connections...");
                await Task.Delay(50); // Small delay to allow UI update
                KeyboardShortcuts.CleanupGlobalHook();
                
                shutdownDialog.UpdateStatus("Cleaning up resources...");
                await Task.Delay(50);
                MouseManager.Instance.Cleanup();
                
                shutdownDialog.UpdateStatus("Finalizing...");
                await Task.Delay(50);
                Logic.Instance.Finish();
                
                shutdownDialog.UpdateStatus("Stopping server...");
                await Task.Delay(50);
                ServerProcessManager.Instance.StopServer();
                
                // Make sure the console is closed
                if (consoleWindow != IntPtr.Zero)
                {
                    ShowWindow(consoleWindow, SW_HIDE);
                }
                
                // Close shutdown dialog
                shutdownDialog.Close();
                
                // Mark as shutting down and close the window
                _isShuttingDown = true;
                this.Close();
                
                // Make sure the application exits
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during shutdown: {ex.Message}");
                shutdownDialog.Close();
                _isShuttingDown = true;
                this.Close();
                System.Windows.Application.Current.Shutdown();
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
        
        // Settings button toggle handler
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSettingsWindow();
        }
        
        // Remember the settings window position
        private double settingsWindowLeft = -1;
        private double settingsWindowTop = -1;
        
        // Show/hide the settings window
        private void ToggleSettingsWindow()
        {
            // Check if settings window is visible
            if (SettingsWindow.Instance.IsVisible)
            {
                // Store current position before hiding
                settingsWindowLeft = SettingsWindow.Instance.Left;
                settingsWindowTop = SettingsWindow.Instance.Top;
                
                Console.WriteLine($"Saving settings position: {settingsWindowLeft}, {settingsWindowTop}");
                
                SettingsWindow.Instance.Hide();
                // Re-enable hotkeys now that the Settings window is hidden
                HotkeyManager.Instance.SetEnabled(true);
                Console.WriteLine("Settings window hidden");
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(176, 125, 69)); // Orange
            }
            else
            {
                // Always use the remembered position if it has been set
                if (settingsWindowLeft != -1 || settingsWindowTop != -1)
                {
                    // Restore previous position
                    SettingsWindow.Instance.Left = settingsWindowLeft;
                    SettingsWindow.Instance.Top = settingsWindowTop;
                    Console.WriteLine($"Restoring settings position to: {settingsWindowLeft}, {settingsWindowTop}");
                }
                else
                {
                    // Position to the right of the main window for first run
                    double mainRight = this.Left + this.ActualWidth;
                    double mainTop = this.Top;
                    
                    SettingsWindow.Instance.Left = mainRight + 10; // 10px gap
                    SettingsWindow.Instance.Top = mainTop;
                    Console.WriteLine("No saved position, positioning settings window to the right");
                }
                
                // Set MainWindow as owner to ensure Settings window appears above it
                SettingsWindow.Instance.Owner = this;
                SettingsWindow.Instance.Show();
                // Disable hotkeys while the Settings window is active so we can type normally
                HotkeyManager.Instance.SetEnabled(false);
                Console.WriteLine($"Settings window shown at position {SettingsWindow.Instance.Left}, {SettingsWindow.Instance.Top}");
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(69, 125, 176)); // Blue-ish
            }
        }

        //!This is where we decide to process the bitmap we just grabbed or not
        private void PerformCapture()
        {

            if (helper.Handle == IntPtr.Zero) return;

            // Update the capture rectangle to ensure correct dimensions
            UpdateCaptureRect();

            //if capture rect is less than 1 pixel, don't capture
            if (captureRect.Width < 1 || captureRect.Height < 1) return;

            // Create bitmap with window dimensions
            using (Bitmap bitmap = new Bitmap(captureRect.Width, captureRect.Height))
            {
                // Use direct GDI capture with the overlay hidden
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    // Configure for speed and quality
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    g.InterpolationMode = InterpolationMode.Low;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                   
                    try
                    {
                        g.CopyFromScreen(
                            captureRect.Left,
                            captureRect.Top,
                            0, 0,
                            bitmap.Size,
                            CopyPixelOperation.SourceCopy);
                      
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during screen capture: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                      
                }
                
                // Store the current capture coordinates for use with OCR results
                Logic.Instance.SetCurrentCapturePosition(captureRect.Left, captureRect.Top);

                try
                {

                    // Update Monitor window with the copy (without saving to file)
                    // Always update, even when not visible, so "View in browser" has the latest image
                    MonitorWindow.Instance.UpdateScreenshotFromBitmap(bitmap, showWindow: false);

                    //do we actually want to do OCR right now?  
                    if (!GetIsStarted()) return;

                    if (!GetOCRCheckIsWanted())
                    {
                        return;
                    }

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    SetOCRCheckIsWanted(false);
                    
                    // OCR timing is now tracked in Logic.cs via NotifyOCRCompleted()

                    // Check if we're using Windows OCR or Google Vision - if so, process in memory without saving
                    string ocrMethod = GetSelectedOcrMethod();
                    if (ocrMethod == "Windows OCR")
                    {
                        string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                        Logic.Instance.ProcessWithWindowsOCR(bitmap, sourceLanguage);
                    }
                    else if (ocrMethod == "Google Vision")
                    {
                        string sourceLanguage = (sourceLanguageComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()!;
                        Logic.Instance.ProcessWithGoogleVision(bitmap, sourceLanguage);
                    }
                    else
                    {
                        //write saving bitmap to log
                        Console.WriteLine($"Saving bitmap to {outputPath}");
                        bitmap.Save(outputPath, ImageFormat.Png);
                        Logic.Instance.SendImageToEasyOCR(outputPath);
                    }

                    stopwatch.Stop();
                }
                catch (Exception ex)
                {
                    // Handle potential file lock or other errors
                    Console.WriteLine($"Error processing screenshot: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    Thread.Sleep(100);
                }
            }

        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        
        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Convert sender to CheckBox
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                isAutoTranslateEnabled = checkBox.IsChecked ?? false;
                Console.WriteLine($"Auto-translate {(isAutoTranslateEnabled ? "enabled" : "disabled")}");
                //Clear textobjects
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();
                //force OCR to run again
                SetOCRCheckIsWanted(true);

                MonitorWindow.Instance.RefreshOverlays();
            }
        }
        
   
        // Reset OCR hash when language selection changes
        private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip saving during initialization
            if (_isInitializing)
            {
                return;
            }
            
            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "ja";
                Console.WriteLine($"Source language changed to: {language}");
                
                // Save to config
                ConfigManager.Instance.SetSourceLanguage(language);
            }
            
            // Reset the OCR hash to force a fresh comparison after changing source language
            Logic.Instance.ClearAllTextObjects();
        }

        private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip saving during initialization
            if (_isInitializing)
            {
                return;
            }
            
            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString() ?? "en";
                Console.WriteLine($"Target language changed to: {language}");
                
                // Save to config
                ConfigManager.Instance.SetTargetLanguage(language);
            }
            
            // Reset the OCR hash to force a fresh comparison after changing target language
            Logic.Instance.ClearAllTextObjects();
        }

        private void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox)
            {
                // Get internal ID from Tag property
                string? ocrMethod = (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
                
                if (!string.IsNullOrEmpty(ocrMethod))
                {
                    // Reset the OCR hash to force a fresh comparison after changing OCR method
                    Logic.Instance.ResetHash();
                    
                    Console.WriteLine($"OCR method changed to: {ocrMethod}");
                    
                    // Clear any existing text objects
                    Logic.Instance.ClearAllTextObjects();
                    
                    // Update the UI and connection state based on the selected OCR method
                    if (ocrMethod == "Windows OCR")
                    {
                        // Using Windows OCR, no need for socket connection
                        SocketManager.Instance.Disconnect();
                        SetStatus("Using Windows OCR (built-in)");
                    }
                    else if (ocrMethod == "Manga OCR")
                    {
                        // Using Manga OCR, try to connect to the socket server
                        if (!SocketManager.Instance.IsConnected)
                        {
                            _ = SocketManager.Instance.TryReconnectAsync();
                            SetStatus("Connecting to Python backend for Manga OCR...");
                        }
                        else
                        {
                            SetStatus("Using Manga OCR");
                        }
                    }
                    else if (ocrMethod == "docTR")
                    {
                        // Using docTR, try to connect to the socket server
                        if (!SocketManager.Instance.IsConnected)
                        {
                            _ = SocketManager.Instance.TryReconnectAsync();
                            SetStatus("Connecting to Python backend for docTR...");
                        }
                        else
                        {
                            SetStatus("Using docTR");
                        }
                    }
                    else
                    {
                        // Using EasyOCR, try to connect to the socket server
                        if (!SocketManager.Instance.IsConnected)
                        {
                            _ = SocketManager.Instance.TryReconnectAsync();
                            SetStatus("Connecting to Python backend...");
                        }
                    }
                }
            }
        }
        
        // Keep track of selected OCR method
        private string selectedOcrMethod = "Windows OCR";
        
        public string GetSelectedOcrMethod()
        {
            return selectedOcrMethod;
        }
        
        // Track overlay mode for MainWindow
        private OverlayMode _currentOverlayMode = OverlayMode.Translated; // Default to Translated
        private bool _overlayWebViewInitialized = false;
        private string _lastOverlayHtml = string.Empty;
        private string? _currentMainWindowContextMenuTextObjectId;
        private string? _currentMainWindowContextMenuSelection;
        
        // Win32 API for WDA_EXCLUDEFROMCAPTURE
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
       
        // Toggle the monitor window
        private void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMonitorWindow();
        }
        
        // Handler for the Log button click
        private void LogButton_Click(object sender, RoutedEventArgs e)
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
                logButton.Background = new SolidColorBrush(Color.FromRgb(153, 69, 176)); // Purple
            }
            else
            {
                // Show log window
                // Set MainWindow as owner to ensure Log window appears above it
                LogWindow.Instance.Owner = this;
                LogWindow.Instance.Show();
                logButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 153)); // Pink/Red
            }
        }
        
        // Update log button state (called from LogWindow when it's closed)
        public void updateLogButtonState(bool isVisible)
        {
            logButton.Background = isVisible 
                ? new SolidColorBrush(Color.FromRgb(176, 69, 153)) // Pink/Red - visible
                : new SolidColorBrush(Color.FromRgb(153, 69, 176)); // Purple - hidden
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
            StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8)
            {
                AutoFlush = true
            };
            Console.SetOut(standardOutput);
            
            // Write initial message
            Console.WriteLine("Console output initialized. Toggle visibility with the Log button.");
            Console.WriteLine("Note: Text selection is disabled to prevent application freeze.");
            Console.WriteLine("You can scroll freely, but cannot select/copy text from this console.");
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
                
                Console.WriteLine("Console input and QuickEdit mode disabled successfully");
                Console.WriteLine("NOTE: You cannot select text in this console to prevent app freezing");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disabling console input: {ex.Message}");
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
        
        // Remember the monitor window position
        private double monitorWindowLeft = -1;
        private double monitorWindowTop = -1;
        
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
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
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
                Console.WriteLine($"Monitor window shown at position {MonitorWindow.Instance.Left}, {MonitorWindow.Instance.Top}");
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
                
                // If we have a recent screenshot, load it
                if (File.Exists(outputPath))
                {
                    MonitorWindow.Instance.UpdateScreenshot(outputPath);
                    MonitorWindow.Instance.RefreshOverlays();
                }
            }
        }
        
        // ChatBox Button click handler
        private void ChatBoxButton_Click(object sender, RoutedEventArgs e)
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
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                
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
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                
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
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                    }
                };
                // Set MainWindow as owner to ensure selector window appears above it
                selectorWindow.Owner = this;
                selectorWindow.Show();
                
                // Set button to red while selector is active
                isSelectingChatBoxArea = true;
                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red
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
                    chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
                };
                
                // Also handle visibility changes for when the X button is clicked (which now hides instead of closes)
                chatBoxWindow.IsVisibleChanged += (s, e) =>
                {
                    if (!(bool)e.NewValue) // Window is now hidden
                    {
                        isChatBoxVisible = false;
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(69, 105, 176)); // Blue
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
            isChatBoxVisible = true;
            chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(176, 69, 69)); // Red when active
            
            // The ChatBox will get its data from MainWindow.GetTranslationHistory()
            // No need to manually load entries, just trigger an update
            if (chatBoxWindow != null)
            {
                Console.WriteLine($"Updating ChatBox with {_translationHistory.Count} translation entries");
                chatBoxWindow.UpdateChatHistory();
            }
        }
        
        // **** MODIFIED: Returns the ID of the added/updated entry ****
        public string AddTranslationToHistory(string originalText, string translatedText)
        {
            string entryId = string.Empty;
            bool entryUpdated = false;

            try
            {
                // Check if the new text is essentially the same as the last entry's original text
                // and if the new translated text is non-empty (avoid overwriting translation with empty)
                if (_translationHistory.Count > 0 && !string.IsNullOrEmpty(translatedText))
                {
                    var lastEntry = _translationHistory[_translationHistory.Count - 1]; // More direct access with List
                    
                    // Check if original texts match (case-insensitive, trimmed)
                    if (string.Equals(lastEntry.OriginalText?.Trim(), originalText?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        // Update existing entry ONLY if the new translation is different
                        // This prevents unnecessary updates if only the transcript arrived
                        if (!string.Equals(lastEntry.TranslatedText?.Trim(), translatedText?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            lastEntry.TranslatedText = translatedText;
                            lastEntry.Timestamp = DateTime.Now;
                            Console.WriteLine($"Updated last translation entry ID: {lastEntry.Id}");
                            entryId = lastEntry.Id;
                            entryUpdated = true;
                        }
                        else
                        {
                             // Texts match, but translation is the same. Return existing ID but mark as not needing UI refresh yet.
                             entryId = lastEntry.Id;
                             // entryUpdated remains false - UI doesn't need immediate full refresh
                             Console.WriteLine($"Skipping update, translation same for ID: {lastEntry.Id}");
                        }
                    }
                }

                if (!entryUpdated && !string.IsNullOrEmpty(originalText)) // If not updated, it's a new entry (and original text isn't empty)
                {
                    var entry = new TranslationEntry
                    {
                        Id = Guid.NewGuid().ToString(), // Assign new ID
                        OriginalText = originalText,
                        TranslatedText = translatedText,
                        Timestamp = DateTime.Now
                    };
                    _translationHistory.Add(entry); // Use Add for List
                    entryId = entry.Id; // Store the new ID
                    entryUpdated = true; // Mark that we've handled this (new entry requires UI refresh)
                    Console.WriteLine($"Added new translation entry ID: {entryId}");
                }

                // Keep history size limited
                int maxHistorySize = ConfigManager.Instance.GetChatBoxHistorySize();
                while (_translationHistory.Count > maxHistorySize)
                {
                    _translationHistory.RemoveAt(0); // Remove oldest entry
                }

                // Update ChatBoxWindow if an entry was actually added or updated
                if (entryUpdated)
                {
                    ChatBoxWindow.Instance?.UpdateChatHistory();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding/updating translation history: {ex.Message}");
            }
            
            return entryId; // Return the ID of the added or updated entry
        }
        
        // **** NEW: Method to update a specific entry by ID ****
        public void UpdateTranslationInHistory(string id, string newTranslatedText)
        {
            if (string.IsNullOrEmpty(id))
            {
                Console.WriteLine("UpdateTranslationInHistory called with empty ID.");
                return;
            }

            try
            {
                TranslationEntry? entryToUpdate = null;
                // Use LINQ to find the entry efficiently
                entryToUpdate = _translationHistory.FirstOrDefault(entry => entry.Id == id);

                if (entryToUpdate != null)
                {
                    // Update the translation and timestamp
                    entryToUpdate.TranslatedText = newTranslatedText;
                    entryToUpdate.Timestamp = DateTime.Now; // Update timestamp on modification
                    Console.WriteLine($"Updated translation for entry ID: {id}");

                    // Refresh the ChatBox UI
                    ChatBoxWindow.Instance?.UpdateChatHistory();
                }
                else
                {
                    Console.WriteLine($"Could not find translation entry with ID: {id} to update.");
                }
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Error updating translation history by ID: {ex.Message}");
            }
        }

        // Handle translation events from Logic
        private void Logic_TranslationCompleted(object? sender, TranslationEventArgs e)
        {
            AddTranslationToHistory(e.OriginalText, e.TranslatedText);
        }
        
        // Load language settings from config
        private void LoadLanguageSettingsFromConfig()
        {
            try
            {
                string savedSourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                string savedTargetLanguage = ConfigManager.Instance.GetTargetLanguage();
                
                Console.WriteLine($"Loading language settings from config: Source={savedSourceLanguage}, Target={savedTargetLanguage}");
                
                // Set source language if found in config
                if (!string.IsNullOrEmpty(savedSourceLanguage))
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in sourceLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), savedSourceLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            // Temporarily remove event handler to prevent triggering changes
                            sourceLanguageComboBox.SelectionChanged -= SourceLanguageComboBox_SelectionChanged;
                            
                            sourceLanguageComboBox.SelectedItem = item;
                            Console.WriteLine($"Set source language to {savedSourceLanguage}");
                            
                            // Reattach event handler
                            sourceLanguageComboBox.SelectionChanged += SourceLanguageComboBox_SelectionChanged;
                            break;
                        }
                    }
                }
                
                // Set target language if found in config
                if (!string.IsNullOrEmpty(savedTargetLanguage))
                {
                    // Find and select matching ComboBoxItem by content
                    foreach (ComboBoxItem item in targetLanguageComboBox.Items)
                    {
                        if (string.Equals(item.Content.ToString(), savedTargetLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            // Temporarily remove event handler to prevent triggering changes
                            targetLanguageComboBox.SelectionChanged -= TargetLanguageComboBox_SelectionChanged;
                            
                            targetLanguageComboBox.SelectedItem = item;
                            Console.WriteLine($"Set target language to {savedTargetLanguage}");
                            
                            // Reattach event handler
                            targetLanguageComboBox.SelectionChanged += TargetLanguageComboBox_SelectionChanged;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading language settings from config: {ex.Message}");
            }
        }

        private bool isListening = false;
        private OpenAIRealtimeAudioServiceWhisper? openAIRealtimeAudioService = null;

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            if (isListening)
            {
                isListening = false;
                btn.Content = "Listen";
                btn.Background = new SolidColorBrush(Color.FromRgb(69, 119, 176)); // Blue
                openAIRealtimeAudioService?.Stop();
            }
            else
            {
                isListening = true;
                btn.Content = "Stop Listening";
                btn.Background = new SolidColorBrush(Color.FromRgb(220, 0, 0)); // Red

                // Show a message if ChatBox isn't visible
                var chatBoxWin = ChatBoxWindow.Instance;
                if (chatBoxWin == null || !chatBoxWin.IsVisible)
                {
                    MessageBox.Show(this, "The Listen button listens for audio and shows the detected dialog in the chatbox. Please open the chatbox window to see the detected dialog.", "ChatBox Not Visible", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (openAIRealtimeAudioService == null)
                    openAIRealtimeAudioService = new OpenAIRealtimeAudioServiceWhisper();
                
                // Start the realtime audio service using loopback capture to record system audio
                // **** MODIFIED: Pass new callbacks ****
                openAIRealtimeAudioService.StartRealtimeAudioService(
                    OnOpenAITranscriptReceived_Initial, 
                    OnOpenAITranslationUpdate_WithId, 
                    false); 
            }
        }

        // **** MODIFIED: Renamed, now returns ID ****
        private string OnOpenAITranscriptReceived_Initial(string text, string initialTranslation)
        {
            const string audioPrefix = "🎤 ";
            string idToReturn = string.Empty;
            Dispatcher.Invoke(() =>
            {
                // Add translation/history with audio icon prefix for easy identification in ChatBox
                string originalWithIcon = string.IsNullOrWhiteSpace(text) ? string.Empty : audioPrefix + text;
                string translatedWithIcon = string.IsNullOrWhiteSpace(initialTranslation) ? string.Empty : audioPrefix + initialTranslation;
                // **** Call modified method that returns ID ****
                idToReturn = AddTranslationToHistory(originalWithIcon, translatedWithIcon);
            });
            return idToReturn; // Return the ID
        }
        
        // **** NEW: Callback to handle translation updates via ID ****
        private void OnOpenAITranslationUpdate_WithId(string lineId, string originalText, string translatedText)
        {
            if (string.IsNullOrEmpty(lineId))
            {
                Console.WriteLine("OnOpenAITranslationUpdate_WithId called with empty lineId.");
                return; // Can't update if no ID
            }

            const string audioPrefix = "🎤 ";
            Dispatcher.Invoke(() =>
            {
                string translatedWithIcon = string.IsNullOrWhiteSpace(translatedText) ? string.Empty : audioPrefix + translatedText;
                // Call new method to update the specific entry
                UpdateTranslationInHistory(lineId, translatedWithIcon);
            });
        }
        
        // Initialize the overlay WebView2 for MainWindow
        private async void InitializeMainWindowOverlayWebView()
        {
            try
            {
                var environment = await WebViewEnvironmentManager.GetEnvironmentAsync();
                await textOverlayWebView.EnsureCoreWebView2Async(environment);
                
                if (textOverlayWebView.CoreWebView2 != null)
                {
                    textOverlayWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    textOverlayWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    textOverlayWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    textOverlayWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    
                    // Enable interaction with the WebView2 control
                    textOverlayWebView.IsHitTestVisible = true;
                    
                    // Add event handlers for context menu
                    textOverlayWebView.CoreWebView2.WebMessageReceived += MainWindowOverlayWebView_WebMessageReceived;
                    textOverlayWebView.CoreWebView2.ContextMenuRequested += MainWindowOverlayWebView_ContextMenuRequested;
                    
                    _overlayWebViewInitialized = true;
                    
                    // Initial empty render
                    UpdateMainWindowOverlayWebView();
                    
                    // Exclude WebView2 from capture - use a longer delay to ensure child windows are fully created
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            SetWebViewExcludeFromCapture();
                        });
                    });
                    
                    Console.WriteLine("MainWindow overlay WebView2 initialized successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing MainWindow overlay WebView2: {ex.Message}");
            }
        }
        
        private void SetWebViewExcludeFromCapture()
        {
            try
            {
                // Check if user wants windows visible in screenshots
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                
                if (textOverlayWebView?.CoreWebView2 != null)
                {
                    // WebView2 is based on Chromium and doesn't create traditional Win32 child windows
                    // Instead, we need to get the WebView2 control's HWND using HwndSource
                    var presentationSource = PresentationSource.FromVisual(textOverlayWebView);
                    if (presentationSource is HwndSource hwndSource)
                    {
                        IntPtr webViewHwnd = hwndSource.Handle;
                        
                        if (webViewHwnd != IntPtr.Zero)
                        {
                            uint affinity = visibleInScreenshots ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE;
                            bool success = SetWindowDisplayAffinity(webViewHwnd, affinity);
                            
                            if (success)
                            {
                                Console.WriteLine($"MainWindow WebView2 excluded from screen capture successfully (HWND: {webViewHwnd})");
                            }
                            else
                            {
                                Console.WriteLine($"Failed to set MainWindow WebView2 capture mode. Last error: {Marshal.GetLastWin32Error()}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("MainWindow WebView2 HWND is null");
                        }
                    }
                    else
                    {
                        Console.WriteLine("MainWindow WebView2: Could not get HwndSource, WebView2 may share parent window HWND");
                        // WebView2 shares the parent window's HWND, so we don't need to do anything special
                        // The translucent/transparent parts won't be captured anyway
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting MainWindow WebView2 capture mode: {ex.Message}");
            }
        }
        
        private void ExcludeContextMenuFromCapture(System.Windows.Controls.ContextMenu contextMenu)
        {
            try
            {
                // Check if user wants windows visible in screenshots
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                
                // If visible in screenshots, don't exclude
                if (visibleInScreenshots)
                {
                    return;
                }
                
                // Use Dispatcher.BeginInvoke with a small delay to allow the popup window to be created
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Try to get the PresentationSource from the ContextMenu
                        var presentationSource = PresentationSource.FromVisual(contextMenu);
                        if (presentationSource is HwndSource hwndSource)
                        {
                            IntPtr popupHwnd = hwndSource.Handle;
                            
                            if (popupHwnd != IntPtr.Zero)
                            {
                                bool success = SetWindowDisplayAffinity(popupHwnd, WDA_EXCLUDEFROMCAPTURE);
                                
                                if (success)
                                {
                                    Console.WriteLine($"Context menu popup excluded from screen capture successfully (HWND: {popupHwnd})");
                                }
                                else
                                {
                                    Console.WriteLine($"Failed to set context menu popup capture mode. Last error: {Marshal.GetLastWin32Error()}");
                                }
                            }
                        }
                        else
                        {
                            // If we can't get HwndSource directly, try finding the popup window using Win32 APIs
                            // WPF ContextMenu creates a popup that might not be directly accessible via PresentationSource
                            // Try to find it by looking for child windows or popup windows
                            TryFindAndExcludePopupWindow();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error excluding context menu from capture: {ex.Message}");
                        // Fallback: try to find popup window using Win32 APIs
                        TryFindAndExcludePopupWindow();
                    }
                }), DispatcherPriority.Background, new object[] { });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up context menu exclusion: {ex.Message}");
            }
        }
        
        private void TryFindAndExcludePopupWindow()
        {
            try
            {
                // Get the main window's HWND
                var helper = new WindowInteropHelper(this);
                IntPtr mainHwnd = helper.Handle;
                
                if (mainHwnd == IntPtr.Zero)
                {
                    return;
                }
                
                // Try to find popup windows by enumerating child windows
                // WPF ContextMenu popups are typically top-level windows, not children
                // But we can try to find them by looking for windows with menu class names
                IntPtr foundPopup = IntPtr.Zero;
                
                // Look for popup windows by checking child windows
                EnumChildWindows(mainHwnd, (hWnd, lParam) =>
                {
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    
                    // WPF popup windows might have class names like "#32768" (menu class) or other popup classes
                    string classNameStr = className.ToString();
                    if (classNameStr.Contains("Popup") || classNameStr == "#32768" || classNameStr.Contains("Menu"))
                    {
                        foundPopup = hWnd;
                        return false; // Stop enumeration
                    }
                    
                    return true; // Continue enumeration
                }, IntPtr.Zero);
                
                if (foundPopup != IntPtr.Zero)
                {
                    bool success = SetWindowDisplayAffinity(foundPopup, WDA_EXCLUDEFROMCAPTURE);
                    if (success)
                    {
                        Console.WriteLine($"Context menu popup found and excluded from screen capture (HWND: {foundPopup})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding popup window: {ex.Message}");
            }
        }
        
        // Win32 API for finding popup windows
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        
        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;
        
        // Win32 API for enumerating child windows
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        public void UpdateCaptureExclusion()
        {
            // Update the WebView2 child windows
            SetWebViewExcludeFromCapture();
        }
        
        public void UpdateMainWindowTextInteraction()
        {
            // Update the IsHitTestVisible property based on passthrough state (inverse relationship)
            bool mousePassthrough = ConfigManager.Instance.GetMainWindowMousePassthrough();
            bool canInteract = !mousePassthrough;
            
            if (textOverlayWebView != null)
            {
                textOverlayWebView.IsHitTestVisible = canInteract;
                Console.WriteLine($"MainWindow text interaction: {(canInteract ? "enabled" : "disabled (click-through)")}");
            }
            
            // Regenerate overlay HTML with updated interaction settings
            RefreshMainWindowOverlays();
        }
        
        private void UpdateMainWindowOverlayWebView()
        {
            if (!_overlayWebViewInitialized || textOverlayWebView?.CoreWebView2 == null)
            {
                return;
            }
            
            try
            {
                string html = GenerateMainWindowOverlayHtml();
                
                // Only update if HTML changed
                if (html == _lastOverlayHtml)
                {
                    return;
                }
                
                _lastOverlayHtml = html;
                textOverlayWebView.CoreWebView2.NavigateToString(html);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating MainWindow overlay WebView: {ex.Message}");
            }
        }
        
        private string GenerateMainWindowOverlayHtml()
        {
            // Check if click-through is enabled based on passthrough state (inverse relationship)
            bool mousePassthrough = ConfigManager.Instance.GetMainWindowMousePassthrough();
            bool canInteract = !mousePassthrough;
            
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset='utf-8'/>");
            html.AppendLine("<style>");
            html.AppendLine("html, body {");
            html.AppendLine("  margin: 0;");
            html.AppendLine("  padding: 0;");
            html.AppendLine("  width: 100%;");
            html.AppendLine("  height: 100%;");
            html.AppendLine("  overflow: hidden;");
            html.AppendLine("  background: transparent;");
            html.AppendLine("  pointer-events: none;"); // Body itself is non-interactive
            html.AppendLine("}");
            html.AppendLine(".text-overlay {");
            html.AppendLine("  position: absolute;");
            html.AppendLine("  box-sizing: border-box;");
            html.AppendLine("  overflow: hidden;");
            html.AppendLine("  white-space: normal;");
            html.AppendLine("  word-wrap: break-word;");
            
            if (canInteract)
            {
                html.AppendLine("  pointer-events: auto;");
                html.AppendLine("  user-select: text;");
            }
            else
            {
                html.AppendLine("  pointer-events: none;");
                html.AppendLine("  user-select: none;");
            }
            
            html.AppendLine("  padding: 0;");
            html.AppendLine("  margin: 0;");
            html.AppendLine("  line-height: 1;");
            html.AppendLine("  display: flex;");
            html.AppendLine("  align-items: center;");
            html.AppendLine("  justify-content: flex-start;");
            html.AppendLine("}");
            html.AppendLine(".vertical-text {");
            html.AppendLine("  writing-mode: vertical-rl;");
            html.AppendLine("  text-orientation: upright;");
            html.AppendLine("  align-items: flex-start;");
            html.AppendLine("  justify-content: center;");
            html.AppendLine("}");
            html.AppendLine("</style>");
            html.AppendLine("<script>");
            html.AppendLine("function fitTextToBox(element) {");
            html.AppendLine("  const minSize = 8;");
            html.AppendLine("  const maxSize = 64;");
            html.AppendLine("  let bestSize = minSize;");
            html.AppendLine("  ");
            html.AppendLine("  // Binary search for the best font size");
            html.AppendLine("  let low = minSize;");
            html.AppendLine("  let high = maxSize;");
            html.AppendLine("  ");
            html.AppendLine("  while (high - low > 0.5) {");
            html.AppendLine("    const mid = (low + high) / 2;");
            html.AppendLine("    element.style.fontSize = mid + 'px';");
            html.AppendLine("    ");
            html.AppendLine("    if (element.scrollHeight <= element.clientHeight && element.scrollWidth <= element.clientWidth) {");
            html.AppendLine("      bestSize = mid;");
            html.AppendLine("      low = mid;");
            html.AppendLine("    } else {");
            html.AppendLine("      high = mid;");
            html.AppendLine("    }");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  element.style.fontSize = bestSize + 'px';");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("window.addEventListener('load', function() {");
            html.AppendLine("  const overlays = document.querySelectorAll('.text-overlay');");
            html.AppendLine("  overlays.forEach(overlay => fitTextToBox(overlay));");
            html.AppendLine("});");
            html.AppendLine("");
            
            // Only add context menu handling if interaction is enabled (use variable declared at top)
            if (canInteract)
            {
                html.AppendLine("document.addEventListener('contextmenu', function(event) {");
                html.AppendLine("  try {");
                html.AppendLine("    // Find which text overlay was clicked");
                html.AppendLine("    let target = event.target;");
                html.AppendLine("    while (target && !target.classList.contains('text-overlay')) {");
                html.AppendLine("      target = target.parentElement;");
                html.AppendLine("    }");
                html.AppendLine("    if (target && target.id) {");
                html.AppendLine("      const selection = window.getSelection();");
                html.AppendLine("      const message = {");
                html.AppendLine("        kind: 'contextmenu',");
                html.AppendLine("        textObjectId: target.id.replace('overlay-', ''),");
                html.AppendLine("        x: event.clientX,");
                html.AppendLine("        y: event.clientY,");
                html.AppendLine("        selection: selection ? selection.toString() : ''");
                html.AppendLine("      };");
                html.AppendLine("      if (window.chrome && window.chrome.webview) {");
                html.AppendLine("        window.chrome.webview.postMessage(JSON.stringify(message));");
                html.AppendLine("      }");
                html.AppendLine("    }");
                html.AppendLine("    event.preventDefault();");
                html.AppendLine("  } catch (error) {");
                html.AppendLine("    console.error(error);");
                html.AppendLine("  }");
                html.AppendLine("});");
            }
            
            html.AppendLine("</script>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            // Add all text overlays if mode is not Hide
            if (_currentOverlayMode != OverlayMode.Hide && Logic.Instance != null)
            {
                var textObjects = Logic.Instance.GetTextObjects();
                if (textObjects != null)
                {
                    foreach (var textObj in textObjects)
                    {
                        if (textObj == null) continue;
                        
                        // Determine which text to show
                        string textToShow;
                        bool isTranslated = false;
                        string displayOrientation = textObj.TextOrientation;
                        
                        if (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated))
                        {
                            textToShow = textObj.TextTranslated;
                            isTranslated = true;
                            
                            // Check if target language supports vertical
                            if (textObj.TextOrientation == "vertical")
                            {
                                string targetLang = ConfigManager.Instance.GetTargetLanguage().ToLower();
                                if (!MonitorWindow.IsVerticalSupportedLanguage(targetLang))
                                {
                                    displayOrientation = "horizontal";
                                }
                            }
                        }
                        else
                        {
                            textToShow = textObj.Text;
                        }
                        
                        // Get colors
                        Color bgColor = textObj.BackgroundColor?.Color ?? Colors.Black;
                        Color textColor = textObj.TextColor?.Color ?? Colors.White;
                        
                        // Get font settings
                        string fontFamily = isTranslated
                            ? ConfigManager.Instance.GetTargetLanguageFontFamily()
                            : ConfigManager.Instance.GetSourceLanguageFontFamily();
                        bool isBold = isTranslated
                            ? ConfigManager.Instance.GetTargetLanguageFontBold()
                            : ConfigManager.Instance.GetSourceLanguageFontBold();
                        
                        // Encode text for HTML
                        string encodedText = System.Web.HttpUtility.HtmlEncode(textToShow.Trim())
                            .Replace("\r\n", " ")
                            .Replace("\r", " ")
                            .Replace("\n", " ");
                        
                        // Use text object positions directly (no zoom on main window)
                        double left = textObj.X;
                        double top = textObj.Y;
                        double width = textObj.Width;
                        double height = textObj.Height;
                        
                        // Build the div for this text object
                        string styleAttr = $"left: {left}px; top: {top}px; width: {width}px; height: {height}px; " +
                            $"background-color: rgba({bgColor.R},{bgColor.G},{bgColor.B},{bgColor.A / 255.0:F3}); " +
                            $"color: rgb({textColor.R},{textColor.G},{textColor.B}); " +
                            $"font-family: {string.Join(", ", fontFamily.Split(',').Select(f => $"\"{f.Trim()}\""))}; " +
                            $"font-weight: {(isBold ? "bold" : "normal")}; " +
                            $"font-size: 16px;";
                        
                        string cssClass = displayOrientation == "vertical" ? "text-overlay vertical-text" : "text-overlay";
                        html.Append($"<div id='overlay-{textObj.ID}' class='{cssClass}' style='{styleAttr}'>");
                        html.Append(encodedText);
                        html.AppendLine("</div>");
                    }
                }
            }
            
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
        
        public void RefreshMainWindowOverlays()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshMainWindowOverlays());
                return;
            }
            
            try
            {
                UpdateMainWindowOverlayWebView();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing MainWindow overlays: {ex.Message}");
            }
        }
        
        // Header size changed - adjust overlay margin dynamically and update capture rect
        private void HeaderBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (OverlayContent == null || TopControlGrid == null)
                return;
                
            // Get the actual height of the header (resize strip + header content)
            double headerHeight = TopControlGrid.ActualHeight;
            
            // Update the overlay content margin to match header height
            OverlayContent.Margin = new Thickness(0, headerHeight, 0, 0);
            
            // Update capture rectangle to account for new header height
            UpdateCaptureRect();
        }
        
        // Initialize the translation status timer
        private void InitializeTranslationStatusTimer()
        {
            _translationStatusTimer = new DispatcherTimer();
            _translationStatusTimer.Interval = TimeSpan.FromSeconds(1);
            _translationStatusTimer.Tick += TranslationStatusTimer_Tick;
        }
        
        // Update the translation status timer
        private void TranslationStatusTimer_Tick(object? sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _translationStartTime;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                if (translationStatusLabel != null)
                {
                    translationStatusLabel.Text = $"Waiting for {service}... {elapsed.Minutes:D1}:{elapsed.Seconds:D2}";
                }
            });
        }
        
        // Show the translation status
        public void ShowTranslationStatus(bool bSettling)
        {
            if (bSettling)
            {
                _isShowingSettling = true;
                Dispatcher.Invoke(() =>
                {
                    if (translationStatusLabel != null)
                        translationStatusLabel.Text = "Settling...";
                    
                    if (translationStatusBorder != null)
                        translationStatusBorder.Visibility = Visibility.Visible;
                });
                return;
            }
            
            _isShowingSettling = false;
            _translationStartTime = DateTime.Now;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                if (translationStatusLabel != null)
                    translationStatusLabel.Text = $"Waiting for {service}... 0:00";
                
                if (translationStatusBorder != null)
                    translationStatusBorder.Visibility = Visibility.Visible;
                
                // Start the timer if not already running
                if (_translationStatusTimer == null)
                {
                    InitializeTranslationStatusTimer();
                }
                
                if (_translationStatusTimer != null && !_translationStatusTimer.IsEnabled)
                {
                    _translationStatusTimer.Start();
                }
            });
        }
        
        // Hide the translation status
        public void HideTranslationStatus()
        {
            _isShowingSettling = false;
            
            Dispatcher.Invoke(() =>
            {
                if (translationStatusBorder != null)
                    translationStatusBorder.Visibility = Visibility.Collapsed;
                
                if (_translationStatusTimer != null && _translationStatusTimer.IsEnabled)
                {
                    _translationStatusTimer.Stop();
                }
                
                // Logic.cs will handle showing OCR status if needed
            });
        }
        
        // OCR Status Display Methods (called by Logic.cs)
        
        // Update OCR status display with computed values from Logic
        public void UpdateOCRStatusDisplay(string ocrMethod, double fps)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateOCRStatusDisplay(ocrMethod, fps));
                return;
            }
            
            // Don't show if translation or settling is in progress
            if (_translationStatusTimer?.IsEnabled == true || _isShowingSettling)
            {
                return;
            }
            
            if (translationStatusLabel != null)
            {
                translationStatusLabel.Text = $"{ocrMethod} (fps: {fps:F1})";
            }
            
            if (translationStatusBorder != null)
            {
                translationStatusBorder.Visibility = Visibility.Visible;
            }
        }
        
        // Hide OCR status display
        public void HideOCRStatusDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => HideOCRStatusDisplay());
                return;
            }
            
            // Only hide if no translation status is showing
            if (_translationStatusTimer?.IsEnabled != true && translationStatusBorder != null)
            {
                translationStatusBorder.Visibility = Visibility.Collapsed;
            }
        }
        
        // Export to HTML button handler
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MonitorWindow.Instance.ExportToBrowser();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting to HTML: {ex.Message}");
                MessageBox.Show($"Error exporting to HTML: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Overlay radio button handler
        private void OverlayRadioButton_Checked(object sender, RoutedEventArgs e)
        {
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
        
        // Mouse passthrough checkbox handler
        private void MousePassthroughCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (mousePassthroughCheckBox == null)
                return;
                
            bool isEnabled = mousePassthroughCheckBox.IsChecked ?? false;
            
            // Save to config
            ConfigManager.Instance.SetMainWindowMousePassthrough(isEnabled);
            
            // Update mouse passthrough state
            updateMousePassthrough(isEnabled);
            
            // Update text interaction state (inverse of passthrough)
            UpdateMainWindowTextInteraction();
            
            Console.WriteLine($"Mouse passthrough {(isEnabled ? "enabled" : "disabled")}");
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
                Console.WriteLine("Mouse passthrough: overlay now interactive with minimal background");
            }
        }
        
        // Handle WebView2 web messages for context menu
        private void MainWindowOverlayWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }
                
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(message);
                System.Text.Json.JsonElement root = document.RootElement;
                
                if (root.TryGetProperty("kind", out System.Text.Json.JsonElement kindElement) &&
                    kindElement.GetString() == "contextmenu")
                {
                    string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idElement) 
                        ? idElement.GetString() ?? string.Empty : string.Empty;
                    double x = root.TryGetProperty("x", out System.Text.Json.JsonElement xElement) ? xElement.GetDouble() : 0;
                    double y = root.TryGetProperty("y", out System.Text.Json.JsonElement yElement) ? yElement.GetDouble() : 0;
                    string selection = root.TryGetProperty("selection", out System.Text.Json.JsonElement selectionElement)
                        ? selectionElement.GetString() ?? string.Empty : string.Empty;
                    
                    ShowMainWindowOverlayContextMenu(textObjectId, x, y, selection);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling MainWindow overlay WebView message: {ex.Message}");
            }
        }
        
        private void MainWindowOverlayWebView_ContextMenuRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error suppressing default WebView2 context menu: {ex.Message}");
            }
        }
        
        private void ShowMainWindowOverlayContextMenu(string textObjectId, double clientX, double clientY, string? selection)
        {
            try
            {
                if (string.IsNullOrEmpty(textObjectId))
                {
                    return;
                }
                
                _currentMainWindowContextMenuTextObjectId = textObjectId;
                _currentMainWindowContextMenuSelection = string.IsNullOrWhiteSpace(selection) ? null : selection.Trim();
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Windows.Point contentPoint = new System.Windows.Point(clientX, clientY);
                        System.Windows.Point relativeToWebView = textOverlayWebView.TranslatePoint(contentPoint, this);
                        System.Windows.Point screenPoint = this.PointToScreen(relativeToWebView);
                        
                        System.Windows.Controls.ContextMenu contextMenu = CreateMainWindowOverlayContextMenu();
                        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
                        contextMenu.HorizontalOffset = screenPoint.X;
                        contextMenu.VerticalOffset = screenPoint.Y;
                        contextMenu.IsOpen = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error showing MainWindow context menu: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preparing MainWindow context menu: {ex.Message}");
            }
        }
        
        private System.Windows.Controls.ContextMenu CreateMainWindowOverlayContextMenu()
        {
            System.Windows.Controls.ContextMenu contextMenu = new System.Windows.Controls.ContextMenu();
            
            // Copy menu item
            System.Windows.Controls.MenuItem copyMenuItem = new System.Windows.Controls.MenuItem();
            copyMenuItem.Header = "Copy";
            copyMenuItem.Click += MainWindowOverlayContextMenu_Copy_Click;
            contextMenu.Items.Add(copyMenuItem);
            
            // Copy Translated menu item (only shown when in Source mode)
            System.Windows.Controls.MenuItem copyTranslatedMenuItem = new System.Windows.Controls.MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += MainWindowOverlayContextMenu_CopyTranslated_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Separator
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            
            // Learn menu item
            System.Windows.Controls.MenuItem learnMenuItem = new System.Windows.Controls.MenuItem();
            learnMenuItem.Header = "Learn";
            learnMenuItem.Click += MainWindowOverlayContextMenu_Learn_Click;
            contextMenu.Items.Add(learnMenuItem);
            
            // Speak menu item
            System.Windows.Controls.MenuItem speakMenuItem = new System.Windows.Controls.MenuItem();
            speakMenuItem.Header = "Speak";
            speakMenuItem.Click += MainWindowOverlayContextMenu_Speak_Click;
            contextMenu.Items.Add(speakMenuItem);
            
            // Update menu visibility when opened
            contextMenu.Opened += (s, e) =>
            {
                TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
                if (textObj != null)
                {
                    copyTranslatedMenuItem.Visibility = _currentOverlayMode == OverlayMode.Source ? Visibility.Visible : Visibility.Collapsed;
                    copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(textObj.TextTranslated);
                }
                
                // Exclude context menu popup from screen capture
                ExcludeContextMenuFromCapture(contextMenu);
            };
            
            contextMenu.Closed += (s, e) =>
            {
                _currentMainWindowContextMenuTextObjectId = null;
                _currentMainWindowContextMenuSelection = null;
            };
            
            return contextMenu;
        }
        
        private TextObject? GetMainWindowTextObjectById(string? id)
        {
            if (string.IsNullOrEmpty(id) || Logic.Instance == null)
            {
                return null;
            }
            
            var textObjects = Logic.Instance.GetTextObjects();
            return textObjects?.FirstOrDefault(t => t.ID == id);
        }
        
        private void MainWindowOverlayContextMenu_Copy_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToCopy = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection) 
                    ? _currentMainWindowContextMenuSelection 
                    : (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated) 
                        ? textObj.TextTranslated 
                        : textObj.Text);
                
                System.Windows.Forms.Clipboard.SetText(textToCopy);
                SetStatus("Text copied to clipboard");
            }
        }
        
        private void MainWindowOverlayContextMenu_CopyTranslated_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null && !string.IsNullOrEmpty(textObj.TextTranslated))
            {
                System.Windows.Forms.Clipboard.SetText(textObj.TextTranslated);
                SetStatus("Translated text copied to clipboard");
            }
        }
        
        private void MainWindowOverlayContextMenu_Learn_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToLearn = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection) 
                    ? _currentMainWindowContextMenuSelection 
                    : textObj.Text;
                
                if (!string.IsNullOrWhiteSpace(textToLearn))
                {
                    string url = $"https://jisho.org/search/{Uri.EscapeDataString(textToLearn)}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
        }
        
        private async void MainWindowOverlayContextMenu_Speak_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToSpeak = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection)
                    ? _currentMainWindowContextMenuSelection
                    : (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated)
                        ? textObj.TextTranslated
                        : textObj.Text);
                
                if (!string.IsNullOrWhiteSpace(textToSpeak))
                {
                    // Use the configured TTS service
                    string ttsService = ConfigManager.Instance.GetTtsService();
                    if (ttsService.Equals("Google Cloud TTS", StringComparison.OrdinalIgnoreCase))
                    {
                        await GoogleTTSService.Instance.SpeakText(textToSpeak);
                    }
                    else // Default to ElevenLabs
                    {
                        await ElevenLabsService.Instance.SpeakText(textToSpeak);
                    }
                }
            }
        }
    }
}