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
    public partial class MainWindow : Window
    {
        // For screen capture
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
        
        
        // DWM API for getting actual window bounds without shadows
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
        
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        
        // Get actual current DPI for window (may still be virtualized)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOPMOST = 0x00000008;

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
        private const double DEFAULT_WINDOW_WIDTH = 800;
        private const double DEFAULT_WINDOW_HEIGHT = 600;

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
        private bool _wasStartedBeforeMinimize = false;
        private Rect _restoreBoundsBeforeMaximize;
        private bool _isSnapshotOverlayDisplayed = false;
        private bool _snapshotInProgress = false;
        private bool _logCaptureRectOnce = false; // Debug flag for capture rect logging
        private DispatcherTimer _captureTimer;
        private string outputPath = DEFAULT_OUTPUT_PATH;
        private WindowInteropHelper helper;
        private System.Drawing.Rectangle captureRect;
        
        // Translation status timer and tracking
        private DispatcherTimer? _translationStatusTimer;
        private DateTime _translationStartTime;
        private bool _isShowingSettling = false;
        
        // Periodic timer to re-assert HWND_TOPMOST when the window silently loses it
        private DispatcherTimer? _topmostGuardTimer;

        // Debounce timer for window position/size persistence
        private DispatcherTimer? _windowPersistenceTimer;
        private bool _pendingPositionSave = false;
        private bool _pendingSizeSave = false;
        
        // OCR status display (no tracking - handled by Logic.cs)
        
        // Store previous capture position to calculate offset
        private int previousCaptureX;
        private int previousCaptureY;
        
        // Auto translation
        private bool isAutoTranslateEnabled = false;

        // Capture area selector
        private bool _isSelectingCaptureArea = false;

        // Floating toolbar window
        private ToolbarWindow? _toolbarWindow;
        private bool _isToolbarDragging = false;

        // Accessor properties that delegate to ToolbarWindow's named controls.
        // These replace the old XAML-generated fields that were removed when the
        // buttons moved from MainWindow's header into the floating toolbar.
        private System.Windows.Controls.Button? hideButton => _toolbarWindow?.hideButton;
        private System.Windows.Controls.Button? drawBorderButton => _toolbarWindow?.drawBorderButton;
        private System.Windows.Controls.Button? toggleButton => _toolbarWindow?.toggleButton;
        private System.Windows.Controls.Button? snapshotButton => _toolbarWindow?.snapshotButton;
        private System.Windows.Controls.Button? monitorButton => _toolbarWindow?.monitorButton;
        private System.Windows.Controls.Button? chatBoxButton => _toolbarWindow?.chatBoxButton;
        private System.Windows.Controls.Button? listenButton => _toolbarWindow?.listenButton;
        private System.Windows.Controls.Button? logButton => _toolbarWindow?.logButton;
        private System.Windows.Controls.Button? settingsButton => _toolbarWindow?.settingsButton;
        private System.Windows.Controls.Button? exportButton => _toolbarWindow?.exportButton;
        private System.Windows.Controls.Button? playAllAudioButton => _toolbarWindow?.playAllAudioButton;
        private System.Windows.Controls.RadioButton? overlayHideRadio => _toolbarWindow?.overlayHideRadio;
        private System.Windows.Controls.RadioButton? overlaySourceRadio => _toolbarWindow?.overlaySourceRadio;
        private System.Windows.Controls.RadioButton? overlayTranslatedRadio => _toolbarWindow?.overlayTranslatedRadio;
        private System.Windows.Controls.CheckBox? mousePassthroughCheckBox => _toolbarWindow?.mousePassthroughCheckBox;

        //allow this to be accesible through an "Instance" variable
        public static MainWindow Instance { get { return _this!; } }

        // Properties for initial window size (bound in XAML)
        public double InitialWidth
        {
            get
            {
                if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
                {
                    double width = ConfigManager.Instance.GetOcrWindowWidth();
                    if (!double.IsNaN(width) && width > 0)
                    {
                        return width;
                    }
                }
                return DEFAULT_WINDOW_WIDTH;
            }
        }

        public double InitialHeight
        {
            get
            {
                if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
                {
                    double height = ConfigManager.Instance.GetOcrWindowHeight();
                    if (!double.IsNaN(height) && height > 0)
                    {
                        return height;
                    }
                }
                return DEFAULT_WINDOW_HEIGHT;
            }
        }
        // Socket connection status
        private TextBlock? socketStatusText;
        
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
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"MainWindow.SetOcrMethod called with method: {method} (isInitializing: {_isInitializing})");
            }
            
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
                else if (method == "MangaOCR")
                {
                    SetStatus("Using MangaOCR");
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
                else if (method == "MangaOCR")
                {
                    SetStatus("Using MangaOCR");
                }
                else if (method == "docTR")
                {
                    SetStatus("Using docTR");
                }
                else
                {
                    SetStatus("Using EasyOCR");
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
            
            _this = this;

            // Set DataContext to self for property bindings
            this.DataContext = this;

            InitializeComponent();

            this.StateChanged += MainWindow_StateChanged;

            // Set high-res icon
            IconHelper.SetWindowIcon(this);

            // Initialize console but keep it hidden initially
            InitializeConsole();
            
            // Initialize LogWindow after console is set up
            // This ensures LogWindow wraps the properly configured console output
            _ = LogWindow.Instance;
            
            // Hide the console window initially
            consoleWindow = NativeMethods.GetConsoleWindow();
            KeyboardShortcuts.SetConsoleWindowHandle(consoleWindow);
            NativeMethods.ShowWindow(consoleWindow, SW_HIDE);

            // Initialize helper
            helper = new WindowInteropHelper(this);

            // Setup timer for continuous capture
            _captureTimer = new DispatcherTimer();
            _captureTimer.Interval = TimeSpan.FromSeconds(1 / 60.0f);
            _captureTimer.Tick += OnUpdateTick;
            _captureTimer.Start();

            // Guard timer: re-assert HWND_TOPMOST if the window silently loses it
            _topmostGuardTimer = new DispatcherTimer();
            _topmostGuardTimer.Interval = TimeSpan.FromSeconds(2);
            _topmostGuardTimer.Tick += TopmostGuard_Tick;
            _topmostGuardTimer.Start();

            // Initial update of capture rectangle and setup after window is loaded
            this.Loaded += MainWindow_Loaded;

            // Subscribe to window size and location changes for persistence
            this.SizeChanged += MainWindow_SizeChanged;
            this.LocationChanged += MainWindow_LocationChanged;
            
            // Subscribe to DPI changes (when window moves to different monitor or user changes display scale)
            this.DpiChanged += MainWindow_DpiChanged;
            
            // Subscribe to system settings changes (detects Windows Text Size changes)
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            
            // Subscribe to display settings changes (detects display scale changes more reliably)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            
            // Create socket status text block
            CreateSocketStatusIndicator();
            
            // Get reference to the already initialized ChatBoxWindow
            chatBoxWindow = ChatBoxWindow.Instance;

            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;
            this.PreviewKeyUp += Application_KeyUp;
            
            // Register hotkey events with the new HotkeyManager
            HotkeyManager.Instance.StartStopRequested += (s, e) => HandleToggleButton();
            HotkeyManager.Instance.MonitorToggleRequested += (s, e) => HandleMonitorButton();
            HotkeyManager.Instance.ChatBoxToggleRequested += (s, e) => HandleChatBoxButton();
            HotkeyManager.Instance.SettingsToggleRequested += (s, e) => HandleSettingsButton();
            HotkeyManager.Instance.LogToggleRequested += (s, e) => HandleLogButton();
            HotkeyManager.Instance.ListenToggleRequested += (s, e) => HandleListenButton();
            HotkeyManager.Instance.ViewInBrowserRequested += (s, e) => HandleExportButton();
            HotkeyManager.Instance.MainWindowVisibilityToggleRequested += (s, e) => ToggleMainWindowVisibility();
            HotkeyManager.Instance.PlayAllAudioRequested += (s, e) => HandlePlayAllAudioButton();
            HotkeyManager.Instance.ClearOverlaysRequested += (s, e) => {
                // Cancel any in-progress translation
                Logic.Instance.CancelTranslation();
                
                // Clear text objects instantly
                Logic.Instance.ClearAllTextObjects();
                
                // Clear hash so OCR will recreate text if active
                Logic.Instance.ResetHash();
                
                // Clear snapshot overlay state
                _isSnapshotOverlayDisplayed = false;
                UpdateSnapshotButtonState();
                
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
                            // Reset the re-enable time so the check in SetOCRCheckIsWanted passes
                            _ocrReenableTime = DateTime.MinValue;
                            SetOCRCheckIsWanted(true);
                            Console.WriteLine("OCR re-enabled after delay");
                        }), DispatcherPriority.Normal);
                    });
                }
            };
            HotkeyManager.Instance.PassthroughToggleRequested += (s, e) => TogglePassthrough();
            HotkeyManager.Instance.OverlayModeToggleRequested += (s, e) => ToggleOverlayMode();
            HotkeyManager.Instance.OverlayModePreviousRequested += (s, e) => PreviousOverlayMode();
            HotkeyManager.Instance.SnapshotRequested += (s, e) => PerformSnapshot();
            HotkeyManager.Instance.EditModeToggleRequested += (s, e) =>
            {
                bool newState = !ConfigManager.Instance.GetEditModeEnabled();
                HandleEditModeChanged(newState);
                ToolbarWindow.Instance?.SyncEditMode(newState);
            };
            HotkeyManager.Instance.FontSizeIncreaseRequested += (s, e) =>
            {
                AdjustFocusedFontSize(1.10);
            };
            HotkeyManager.Instance.FontSizeDecreaseRequested += (s, e) =>
            {
                AdjustFocusedFontSize(0.90);
            };
            HotkeyManager.Instance.SaveScreenshotRequested += (s, e) => HandleSaveScreenshot();
            
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WindowCaptureHelper.ExcludeTooltipFromCapture(fullEnumeration: true);
            }), DispatcherPriority.Background);
        }
        
        // Update tooltips with current hotkey bindings
        public void UpdateTooltips()
        {
            UpdateHotkeyTooltips();
        }
        
        private void UpdateHotkeyTooltips()
        {
            // Force close any currently open tooltips so they refresh with new content
            ToolTipService.SetIsEnabled(this, false);
            ToolTipService.SetIsEnabled(this, true);
            
            // Update button tooltips
            if (toggleButton != null)
                toggleButton.ToolTip = $"Auto Mode: Continuous OCR + Translation{HotkeyManager.Instance.GetHotkeyDisplayString("start_stop")}";
                
            if (monitorButton != null)
                monitorButton.ToolTip = $"Toggle Monitor Window{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_monitor")}";
                
            if (chatBoxButton != null)
                chatBoxButton.ToolTip = $"Toggle Transcript{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_chatbox")}";
                
            if (settingsButton != null)
                settingsButton.ToolTip = $"Toggle Settings{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_settings")}";
                
            if (logButton != null)
                logButton.ToolTip = $"Toggle Log Console{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_log")}";
                
            if (listenButton != null)
                listenButton.ToolTip = $"Toggle voice listening{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_listen")}";
                
            if (exportButton != null)
                exportButton.ToolTip = $"View current capture in browser{HotkeyManager.Instance.GetHotkeyDisplayString("view_in_browser")}";
                
            if (hideButton != null)
                hideButton.ToolTip = $"Toggle red border visibility{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_main_window")}";
                
            if (mousePassthroughCheckBox != null)
                mousePassthroughCheckBox.ToolTip = $"Toggle mouse passthrough mode{HotkeyManager.Instance.GetHotkeyDisplayString("toggle_passthrough")}";
            
            if (snapshotButton != null)
                snapshotButton.ToolTip = $"Snap: Single OCR capture{HotkeyManager.Instance.GetHotkeyDisplayString("snapshot")}";
            
            // Update overlay radio buttons
            string overlayHotkey = HotkeyManager.Instance.GetHotkeyDisplayString("toggle_overlay_mode");
            if (overlayHideRadio != null)
                overlayHideRadio.ToolTip = $"Hide overlay{overlayHotkey}";
            if (overlaySourceRadio != null)
                overlaySourceRadio.ToolTip = $"Show source text{overlayHotkey}";
            if (overlayTranslatedRadio != null)
                overlayTranslatedRadio.ToolTip = $"Show translated text{overlayHotkey}";
            
            // Update Utilities menu hotkey labels
            _toolbarWindow?.UpdateUtilitiesMenuHotkeys();
            
            // Setup individual tooltip opened handlers for each control
            SetupIndividualTooltipHandlers();
        }
        
        private void SetupIndividualTooltipHandlers()
        {
            var controls = new FrameworkElement?[] 
            { 
                toggleButton, snapshotButton, monitorButton, chatBoxButton, settingsButton, logButton, 
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
                            WindowCaptureHelper.ExcludeTooltipFromCapture(fullEnumeration: true);
                        }), DispatcherPriority.Background);
                    };
                }
            }
        }

        public void SetStatus(string text)
        {
            if (socketStatusText != null)
            {
                // Never allow empty text - use "Ready" as default to maintain title bar height
                socketStatusText!.Text = string.IsNullOrWhiteSpace(text) ? "Ready" : text;
            }
        }

        private void CreateSocketStatusIndicator()
        {
            // Create socket status text
            socketStatusText = new TextBlock
            {
                Text = "Ready",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
                MinHeight = 16 // Ensure minimum height to prevent title bar collapse
            };
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore window position if persistence is enabled
            // Width and Height are now bound in XAML via InitialWidth/InitialHeight properties
            if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
            {
                double left = ConfigManager.Instance.GetOcrWindowLeft();
                double top = ConfigManager.Instance.GetOcrWindowTop();
                double width = ConfigManager.Instance.GetOcrWindowWidth();
                double height = ConfigManager.Instance.GetOcrWindowHeight();

				System.Diagnostics.Debug.WriteLine($"MainWindow_Loaded: Restoring window position: Left={left}, Top={top}, Width={width}, Height={height}");

                double actualWidth = double.IsNaN(this.Width) || this.Width <= 0 ? DEFAULT_WINDOW_WIDTH : this.Width;
                double actualHeight = double.IsNaN(this.Height) || this.Height <= 0 ? DEFAULT_WINDOW_HEIGHT : this.Height;

                if (ConfigManager.IsWindowBoundsValid(left, top, actualWidth, actualHeight))
                {
                    this.Left = left;
                    this.Top = top;
                    Console.WriteLine($"Restored window position: Left={left}, Top={top}, Width={actualWidth}, Height={actualHeight}");
                }
                else
                {
                    // Saved position is off-screen (e.g. monitor was removed) — reset to center of primary screen
                    var workArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
                    if (workArea.HasValue)
                    {
                        this.Left = workArea.Value.Left + (workArea.Value.Width - actualWidth) / 2;
                        this.Top = workArea.Value.Top + (workArea.Value.Height - actualHeight) / 2;
                    }
                    else
                    {
                        this.Left = 100;
                        this.Top = 100;
                    }
                    Console.WriteLine($"Window position reset (was off-screen): Left={this.Left}, Top={this.Top} (saved was Left={left}, Top={top})");
                }
            }

            // Safety net: ensure the main window is on a visible screen regardless of persistence setting
            ensureWindowOnScreen();

            // Create and show the floating toolbar
            CreateAndShowToolbar();

            // Update tooltips with hotkeys
            UpdateHotkeyTooltips();
            
            // Update capture rectangle
            UpdateCaptureRect();
            
            // Subscribe to Play All state changes
            AudioPlaybackManager.Instance.PlayAllStateChanged += AudioPlaybackManager_PlayAllStateChanged;
            // Subscribe to current playing text object changes
            AudioPlaybackManager.Instance.CurrentPlayingTextObjectChanged += AudioPlaybackManager_CurrentPlayingTextObjectChanged;
           
            // Socket status text is no longer shown in the simplified header bar.
            // Status is displayed through translationStatusLabel instead.
            
           
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
                
                monitorButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
            }
            
            // Restore ChatBox and Monitor windows if they were active on last close
            if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
            {
                RestoreSecondaryWindowStates();
            }
            
            // Test configuration loading
            TestConfigLoading();
            
            // Initialization is complete, now we can save settings changes
            _isInitializing = false;
            Console.WriteLine("MainWindow initialization complete. Settings changes will now be saved.");
            
            // Force the OCR method to match the config again
            // This ensures the config value is preserved and not overwritten
            string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");
            }
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
            
            // Set initial mouse passthrough state (always unchecked at startup to avoid confusion)
            bool mousePassthrough = false;
            mousePassthroughCheckBox.IsChecked = mousePassthrough;
            // CRITICAL: Save to config BEFORE async WebView2 initialization reads it
            ConfigManager.Instance.SetMainWindowMousePassthrough(mousePassthrough);
            updateMousePassthrough(mousePassthrough);
            Console.WriteLine($"MainWindow mouse passthrough initialized: {(mousePassthrough ? "enabled" : "disabled")}");
            
            // Set initial edit mode state (always off at startup)
            ConfigManager.Instance.SetEditModeEnabled(false);
            _toolbarWindow?.SyncEditMode(false);
        }
        
        public void SendCtrlKeyToWebView(bool isDown)
        {
            if (_overlayWebViewInitialized && textOverlayWebView?.CoreWebView2 != null)
            {
                string eventType = isDown ? "keydown" : "keyup";
                _ = textOverlayWebView.CoreWebView2.ExecuteScriptAsync(
                    $"(function() {{ var e = new KeyboardEvent('{eventType}', {{ key: 'Control', bubbles: true }}); e._synthetic = true; document.dispatchEvent(e); }})();");
            }
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            
            if (e.Key == System.Windows.Input.Key.LeftCtrl || e.Key == System.Windows.Input.Key.RightCtrl)
            {
                SendCtrlKeyToWebView(true);
                MonitorWindow.Instance?.SendCtrlKeyToWebView(true);
            }
            
            if (HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                // Global hook handles global bindings; we handle local-only bindings here
                bool handled = HotkeyManager.Instance.HandleKeyDownLocal(e.Key, modifiers);
                if (handled || e.Key == System.Windows.Input.Key.Tab)
                {
                    e.Handled = true;
                }
            }
            else
            {
                // Master switch off: all bindings fire from window handler
                bool handled = HotkeyManager.Instance.HandleKeyDownAll(e.Key, modifiers);
                if (handled || e.Key == System.Windows.Input.Key.Tab)
                {
                    e.Handled = true;
                }
            }
        }
       
        private void Application_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.LeftCtrl || e.Key == System.Windows.Input.Key.RightCtrl)
            {
                SendCtrlKeyToWebView(false);
                MonitorWindow.Instance?.SendCtrlKeyToWebView(false);
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

            // Store previous position for calculating offset
            previousCaptureX = captureRect.Left;
            previousCaptureY = captureRect.Top;

            // Use DwmGetWindowAttribute to get actual visible window bounds (excludes shadows)
            // WPF's layout already accounts for text scaling, so we only need DPI conversion
            if (textOverlayWebView != null && textOverlayWebView.IsLoaded && textOverlayWebView.ActualWidth > 0)
            {
                try
                {
                    // Get actual visible window bounds using DWM API (excludes extended frame/shadows)
                    // NOTE: DWM returns bounds in ACTUAL physical screen pixels, even when DPI is virtualized
                    RECT windowRect;
                    int result = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out windowRect, 
                        System.Runtime.InteropServices.Marshal.SizeOf(typeof(RECT)));
                    
                    if (result != 0)
                    {
                        // Fallback to GetWindowRect if DWM fails
                        GetWindowRect(hwnd, out windowRect);
                    }
                    
                    // Get actual monitor DPI (what the screen is really at)
                    double actualDpiScale = GetActualDpiScale();
                    // Get virtualized DPI (what WPF thinks we're at)
                    double virtualizedDpiScale = GetVirtualizedDpiScale();
                    
                    // WPF dimensions are in DIPs. When multiplied by virtualizedDpiScale,
                    // we get "virtualized physical pixels". But the window rect from DWM is
                    // in actual physical pixels. We need to correct for this.
                    double dpiCorrectionFactor = actualDpiScale / virtualizedDpiScale;
                    
                    // Get WebView's position within the window (in WPF DIPs)
                    // WPF layout already accounts for text scaling in the DIP values
                    var transform = textOverlayWebView.TransformToAncestor(this);
                    System.Windows.Point webViewInWindow = transform.Transform(new System.Windows.Point(0, 0));
                    
                    // Convert WPF offset to actual physical pixels
                    // DIPs * virtualizedDpiScale = virtualized physical pixels
                    // virtualized physical pixels * correctionFactor = actual physical pixels
                    int offsetX = (int)(webViewInWindow.X * virtualizedDpiScale * dpiCorrectionFactor);
                    int offsetY = (int)(webViewInWindow.Y * virtualizedDpiScale * dpiCorrectionFactor);
                    
                    // Calculate capture size in actual physical pixels
                    int captureWidth = (int)(textOverlayWebView.ActualWidth * virtualizedDpiScale * dpiCorrectionFactor);
                    int captureHeight = (int)(textOverlayWebView.ActualHeight * virtualizedDpiScale * dpiCorrectionFactor);
                    
                    // Calculate capture position: window physical position + WebView offset
                    int captureLeft = windowRect.Left + offsetX;
                    int captureTop = windowRect.Top + offsetY;
                    
                    // Debug: log once when snapshot is taken
                    if (_logCaptureRectOnce && ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        _logCaptureRectOnce = false;
                        double textScale = DisplayHelper.GetWindowsTextScaleFactor();
                        Console.WriteLine($"[DEBUG] Window rect: L={windowRect.Left}, T={windowRect.Top}, W={windowRect.Width}, H={windowRect.Height}");
                        Console.WriteLine($"[DEBUG] WebView in window (DIPs): X={webViewInWindow.X:F1}, Y={webViewInWindow.Y:F1}");
                        Console.WriteLine($"[DEBUG] WebView actual size (DIPs): {textOverlayWebView.ActualWidth:F0}x{textOverlayWebView.ActualHeight:F0}");
                        Console.WriteLine($"[DEBUG] Actual DPI: {actualDpiScale}, Virtualized DPI: {virtualizedDpiScale}, Correction: {dpiCorrectionFactor:F3}");
                        Console.WriteLine($"[DEBUG] Text scale: {textScale}");
                        Console.WriteLine($"[DEBUG] Calculated offset: X={offsetX}, Y={offsetY}");
                        Console.WriteLine($"[DEBUG] Capture rect: L={captureLeft}, T={captureTop}, {captureWidth}x{captureHeight}");
                    }
                    
                    captureRect = new System.Drawing.Rectangle(captureLeft, captureTop, captureWidth, captureHeight);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UpdateCaptureRect] Coordinate calculation failed: {ex.Message}");
                    UpdateCaptureRectFallback(hwnd);
                }
            }
            else
            {
                // OverlayContent not ready yet, use fallback
                UpdateCaptureRectFallback(hwnd);
            }
                
            // If position changed and we have text objects, update their positions
            if ((previousCaptureX != captureRect.Left || previousCaptureY != captureRect.Top) && 
                Logic.Instance.TextObjects.Count > 0)
            {
                // Calculate the offset
                int offsetX = captureRect.Left - previousCaptureX;
                int offsetY = captureRect.Top - previousCaptureY;
                
                // Apply offset to text objects
                Logic.Instance.UpdateTextObjectPositions(offsetX, offsetY);
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Capture position changed by ({offsetX}, {offsetY}). Text overlays updated.");
                }
            }
        }

        // Fallback method for when WebView isn't ready - uses GetWindowRect with estimated offsets
        private void UpdateCaptureRectFallback(IntPtr hwnd)
        {
            RECT windowRect;
            GetWindowRect(hwnd, out windowRect);

            // Get DPI scale factor
            double dpiScale = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }

            // Get the actual header height
            int customTitleBarHeight = (int)Math.Ceiling(TITLE_BAR_HEIGHT * dpiScale);
            if (TopControlGrid != null && TopControlGrid.ActualHeight > 0)
            {
                customTitleBarHeight = (int)Math.Ceiling(TopControlGrid.ActualHeight * dpiScale);
            }
            
            // Border thickness settings matching OverlayContent margin (15,header,15,15) for resize borders
            int leftBorderThickness = (int)Math.Ceiling(15 * dpiScale);
            int rightBorderThickness = (int)Math.Ceiling(15 * dpiScale);
            int bottomBorderThickness = (int)Math.Ceiling(15 * dpiScale);

            captureRect = new System.Drawing.Rectangle(
                windowRect.Left + leftBorderThickness,
                windowRect.Top + customTitleBarHeight,
                (windowRect.Right - windowRect.Left) - leftBorderThickness - rightBorderThickness,
                (windowRect.Bottom - windowRect.Top) - customTitleBarHeight - bottomBorderThickness);
        }

        // Get actual DPI scale factor using Win32 API (bypasses Windows DPI virtualization)
        private double GetActualDpiScale()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    // Get the actual monitor DPI (not virtualized)
                    IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                    if (monitor != IntPtr.Zero)
                    {
                        int hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
                        if (hr == 0 && dpiX > 0)
                        {
                            return dpiX / 96.0; // 96 DPI = 100% = scale factor 1.0
                        }
                    }
                    
                    // Fallback to GetDpiForWindow (may be virtualized)
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi > 0)
                    {
                        return dpi / 96.0;
                    }
                }
            }
            catch
            {
                // Fall back to WPF method if Win32 fails
            }
            
            // Fallback to WPF's TransformToDevice (may be virtualized)
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
            return 1.0;
        }
        
        // Get the virtualized DPI that the window thinks it's running at
        private double GetVirtualizedDpiScale()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi > 0)
                    {
                        return dpi / 96.0;
                    }
                }
            }
            catch { }
            
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
            return 1.0;
        }


        //!Main loop

        private void OnUpdateTick(object? sender, EventArgs e)
        {
          
            PerformCapture();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _restoreBoundsBeforeMaximize = new Rect(this.Left, this.Top, this.Width, this.Height);
                this.DragMove();
                e.Handled = true;
                UpdateCaptureRect();
            }
        }
       
        public void HandleToggleButton()
        {
            var btn = toggleButton;

            if (isStarted)
            {
                Logic.Instance.ResetHash();
                isStarted = false;
                if (btn != null)
                {
                    btn.Content = "Auto";
                    btn.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95));
                }
                Logic.Instance.ClearAllTextObjects();
                ChatBoxWindow.Instance?.HideTranslationStatus();
                Logic.Instance.HideOCRStatus();
                HideTranslationStatus();
            }
            else
            {
                Logic.Instance.ResetHash();
                Logic.Instance.ClearAllTextObjects();
                MonitorWindow.Instance.RefreshOverlays();
                
                _snapshotInProgress = false;
                _isSnapshotOverlayDisplayed = false;
                UpdateSnapshotButtonState();
                
                isStarted = true;
                if (btn != null)
                {
                    btn.Content = "Stop";
                    btn.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
                }
                UpdateCaptureRect();
                
                _ocrReenableTime = DateTime.MinValue;
                SetOCRCheckIsWanted(true);
            }
        }

        public void HandleSnapshotButton()
        {
            PerformSnapshot();
        }

        private void PerformSnapshot()
        {
            if (isStarted)
            {
                HandleToggleButton();
            }
            
            bool toggleMode = ConfigManager.Instance.GetSnapshotToggleMode();
            
            // If a snapshot is in progress, always cancel it (regardless of toggle mode)
            if (_snapshotInProgress)
            {
                Logic.Instance.CancelTranslation();
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();
                _isSnapshotOverlayDisplayed = false;
                _snapshotInProgress = false;
                _lastOverlayHtml = string.Empty;
                MonitorWindow.Instance.RefreshOverlays();
                RefreshMainWindowOverlays();
                
                // Stop the translation status timer and hide ChatBox status
                HideTranslationStatus();
                ChatBoxWindow.Instance?.HideTranslationStatus();
                
                // Update status to show snapshot canceled
                if (translationStatusLabel != null)
                {
                    translationStatusLabel.Text = "Snapshot canceled";
                }
                UpdateSnapshotButtonState();
                return;
            }
            
            // If overlay is displayed and toggle mode is enabled, clear and return (toggle off)
            if (toggleMode && _isSnapshotOverlayDisplayed)
            {
                Logic.Instance.CancelTranslation();
                Logic.Instance.ClearAllTextObjects();
                Logic.Instance.ResetHash();
                _isSnapshotOverlayDisplayed = false;
                _lastOverlayHtml = string.Empty;
                MonitorWindow.Instance.RefreshOverlays();
                RefreshMainWindowOverlays();
                
                // Stop the translation status timer and hide ChatBox status
                HideTranslationStatus();
                ChatBoxWindow.Instance?.HideTranslationStatus();
                
                // Update status to show snapshot cleared
                if (translationStatusLabel != null)
                {
                    translationStatusLabel.Text = "Snapshot cleared";
                }
                UpdateSnapshotButtonState();
                return;
            }
            
            // If overlay is displayed and toggle mode is off, clear it and continue to start new snapshot
            if (_isSnapshotOverlayDisplayed)
            {
                _isSnapshotOverlayDisplayed = false;
            }
            
            // Mark snapshot as in progress to prevent double-triggering
            _snapshotInProgress = true;
            _isSnapshotOverlayDisplayed = false; // Will be set true when results arrive
            UpdateSnapshotButtonState();
            
            // Show snapshot status
            string ocrMethod = GetSelectedOcrMethod();
            if (translationStatusLabel != null)
            {
                translationStatusLabel.Text = $"Snapshotting ({ocrMethod})...";
            }
            if (translationStatusBorder != null)
            {
                translationStatusBorder.Visibility = Visibility.Visible;
            }
            
            // Clear any existing overlays
            Logic.Instance.CancelTranslation();
            Logic.Instance.ClearAllTextObjects();
            _lastOverlayHtml = string.Empty;
            MonitorWindow.Instance.RefreshOverlays();
            RefreshMainWindowOverlays();
            
            // Prepare Logic for snapshot mode (bypasses settling)
            Logic.Instance.PrepareSnapshotOCR();
            
            // Enable debug logging for this snapshot
            _logCaptureRectOnce = true;
            
            // Clear any delay restriction
            _ocrReenableTime = DateTime.MinValue;
            
            // Force OCR to run
            SetOCRCheckIsWanted(true);
            
            // Trigger capture directly
            PerformSnapshotCapture();
        }
        
        // Called by Logic when snapshot processing is complete (results displayed or failed)
        public void OnSnapshotComplete(bool success)
        {
            // Only process if snapshot is still in progress (not already canceled)
            if (!_snapshotInProgress)
            {
                Console.WriteLine("Snapshot complete callback ignored - snapshot was already canceled");
                return;
            }
            
            _snapshotInProgress = false;
            if (success)
            {
                _isSnapshotOverlayDisplayed = true;
            }
            else
            {
                _isSnapshotOverlayDisplayed = false;
            }
            UpdateSnapshotButtonState();
        }
        
        private void UpdateSnapshotButtonState()
        {
            if (snapshotButton == null) return;

            bool isActive = _snapshotInProgress || _isSnapshotOverlayDisplayed;
            snapshotButton.Background = isActive
                ? new SolidColorBrush(Color.FromRgb(46, 160, 67))
                : new SolidColorBrush(Color.FromRgb(95, 95, 95));
        }

        // Perform capture for snapshot mode (bypasses normal checks)
        private void PerformSnapshotCapture()
        {
            if (helper.Handle == IntPtr.Zero)
            {
                OnSnapshotComplete(false);
                return;
            }

            // Update the capture rectangle to ensure correct dimensions
            UpdateCaptureRect();

            // If capture rect is less than 1 pixel, don't capture
            if (captureRect.Width < 1 || captureRect.Height < 1)
            {
                OnSnapshotComplete(false);
                return;
            }

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
                        Console.WriteLine($"Error during snapshot capture: {ex.Message}");
                        OnSnapshotComplete(false);
                        return;
                    }
                }
                
                // Store the current capture coordinates for use with OCR results
                Logic.Instance.SetCurrentCapturePosition(captureRect.Left, captureRect.Top);

                // Save image for debugging if enabled
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    try
                    {
                        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        string imagePath = Path.Combine(appDirectory, "image_sent_to_ocr.png");
                        bitmap.Save(imagePath, ImageFormat.Png);
                        Console.WriteLine($"[DEBUG] Snapshot image saved: {bitmap.Width}x{bitmap.Height}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error saving debug image: {ex.Message}");
                    }
                }

                try
                {
                    // Update Monitor window with the copy
                    MonitorWindow.Instance.UpdateScreenshotFromBitmap(bitmap, showWindow: false);

                    // Send to OCR (snapshot mode bypasses settling in Logic)
                    // Note: OnSnapshotComplete will be called by Logic when processing finishes
                    string ocrMethod = GetSelectedOcrMethod();
                    if (ocrMethod == "Windows OCR")
                    {
                        string sourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                        Logic.Instance.ProcessWithWindowsOCR(bitmap, sourceLanguage);
                    }
                    else if (ocrMethod == "Google Vision")
                    {
                        string sourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                        Logic.Instance.ProcessWithGoogleVision(bitmap, sourceLanguage);
                    }
                    else
                    {
                        Logic.Instance.SendImageToHttpOCR(bitmap);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing snapshot: {ex.Message}");
                    OnSnapshotComplete(false);
                }
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        
        private bool _passthroughStateBeforeHide = false;

        public void HandleHideButton()
        {
            if (MainBorder.Visibility == Visibility.Visible)
            {
                MainBorder.Visibility = Visibility.Collapsed;

                // Save passthrough state and force it on so the invisible overlay doesn't block clicks
                _passthroughStateBeforeHide = mousePassthroughCheckBox?.IsChecked ?? false;
                if (!_passthroughStateBeforeHide && mousePassthroughCheckBox != null)
                {
                    mousePassthroughCheckBox.IsChecked = true;
                }

                // Update the toolbar button to show it's in "hidden" state
                if (hideButton != null)
                {
                    hideButton.Content = "Show red border";
                    hideButton.Background = new SolidColorBrush(Color.FromRgb(20, 180, 20));
                }
            }
            else
            {
                MainBorder.Visibility = Visibility.Visible;

                // Restore the passthrough state that was active before hiding
                if (!_passthroughStateBeforeHide && mousePassthroughCheckBox != null)
                {
                    mousePassthroughCheckBox.IsChecked = false;
                }

                // Update the toolbar button back to normal state
                if (hideButton != null)
                {
                    hideButton.Content = "Hide red border";
                    hideButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95));
                }
            }

            BringToFront();
        }

        public void HandleDrawBorderButton()
        {
            if (_isSelectingCaptureArea)
            {
                _isSelectingCaptureArea = false;
                if (drawBorderButton != null)
                {
                    drawBorderButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95));
                }

                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is CaptureSelectorWindow selectorWindow)
                    {
                        selectorWindow.Close();
                        return;
                    }
                }
                return;
            }

            CaptureSelectorWindow selectorWindow2 = CaptureSelectorWindow.GetInstance();
            selectorWindow2.SelectionComplete += CaptureSelector_SelectionComplete;
            selectorWindow2.Closed += (s, e) =>
            {
                _isSelectingCaptureArea = false;
                if (drawBorderButton != null)
                {
                    drawBorderButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95));
                }
            };
            selectorWindow2.Owner = this;
            selectorWindow2.Show();
            _toolbarWindow?.BringToFront();

            _isSelectingCaptureArea = true;
            if (drawBorderButton != null)
            {
                drawBorderButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67));
            }
        }

        private void CaptureSelector_SelectionComplete(object? sender, Rect selectionRect)
        {
            // The selectionRect is in screen coordinates (physical pixels).
            // We need to convert to WPF DIPs and account for the window chrome
            // (title bar + borders) so the capture area matches the drawn rect.
            double dpiScale = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }

            // The border/chrome offsets in DIPs that surround the capture area
            double borderLeft = 15;
            double borderRight = 15;
            double borderBottom = 15;
            double titleBarHeight = TITLE_BAR_HEIGHT;

            // Convert screen-pixel selection to DIPs
            double selLeftDip = selectionRect.X / dpiScale;
            double selTopDip = selectionRect.Y / dpiScale;
            double selWidthDip = selectionRect.Width / dpiScale;
            double selHeightDip = selectionRect.Height / dpiScale;

            // Position the MainWindow so that its capture area aligns with the selection
            this.Left = selLeftDip - borderLeft;
            this.Top = selTopDip - titleBarHeight;
            this.Width = selWidthDip + borderLeft + borderRight;
            this.Height = selHeightDip + titleBarHeight + borderBottom;

            // Make sure the border is visible
            if (MainBorder.Visibility != Visibility.Visible)
            {
                HandleHideButton();
            }

            UpdateCaptureRect();

            Console.WriteLine($"Capture area drawn: screen({selectionRect.X:F0},{selectionRect.Y:F0} {selectionRect.Width:F0}x{selectionRect.Height:F0}) -> window({this.Left:F0},{this.Top:F0} {this.Width:F0}x{this.Height:F0})");
        }

        public void HandleMinimizeButton()
        {
            this.WindowState = WindowState.Minimized;
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

            // Skip confirm dialog in batch mode (keep prior behavior: stop only owned services)
            if (ServerSetupDialog.BatchMode)
            {
                PythonServicesManager.Instance.ExitAction = GpuServiceExitAction.CloseOwned;
            }
            else
            {
                var exitDialog = new ExitConfirmDialog { Owner = this };
                exitDialog.ShowDialog();

                if (!exitDialog.Confirmed)
                    return; // user cancelled the exit; e.Cancel is already true

                PythonServicesManager.Instance.ExitAction = exitDialog.SelectedAction;
            }

            // Save ChatBox and Monitor window state before closing (if persistence is enabled)
            if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
            {
                SaveSecondaryWindowStates();
            }
            
            // Close Monitor window immediately before showing shutdown dialog
            if (MonitorWindow.Instance.IsVisible)
            {
                MonitorWindow.Instance.ForceClose();
            }

            // Close the floating toolbar
            if (_toolbarWindow != null)
            {
                _toolbarWindow.Close();
                _toolbarWindow = null;
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
                // Stop OCR if it's currently running to prevent conflicts during shutdown
                if (isStarted)
                {
                    shutdownDialog.UpdateStatus("Stopping OCR...");
                    await Task.Delay(50);
                    
                    Logic.Instance.ResetHash();
                    isStarted = false;
                    ChatBoxWindow.Instance?.HideTranslationStatus();
                    Logic.Instance.HideOCRStatus();
                    HideTranslationStatus();
                }
                
                // Unsubscribe from system events to prevent memory leaks
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                
                // Remove global keyboard hook
                shutdownDialog.UpdateStatus("Closing connections...");
                await Task.Delay(50); // Small delay to allow UI update
                KeyboardShortcuts.CleanupGlobalHook();
                
                shutdownDialog.UpdateStatus("Cleaning up resources...");
                await Task.Delay(50);
                MouseManager.Instance.Cleanup();
                
                shutdownDialog.UpdateStatus("Stopping services...");
                await Task.Delay(50);
                await Logic.Instance.Finish();
                
                // Note: Logic.Finish() already stops Python services via PythonServicesManager
                // No need for additional server cleanup
                
                // Make sure the console is closed
                if (consoleWindow != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(consoleWindow, SW_HIDE);
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

        public void HandleSettingsButton()
        {
            ToggleSettingsWindow();
        }
        
        // Remember the settings window position
        private double settingsWindowLeft = -1;
        private double settingsWindowTop = -1;
        
        // Show/hide the settings window
        private void ToggleSettingsWindow()
        {
            var settingsWindow = SettingsWindow.Instance;
            
            // Check if settings window is visible and not minimized
            if (settingsWindow.IsVisible && settingsWindow.WindowState != WindowState.Minimized)
            {
                // Store current position before hiding
                settingsWindowLeft = settingsWindow.Left;
                settingsWindowTop = settingsWindow.Top;
                
                Console.WriteLine($"Saving settings position: {settingsWindowLeft}, {settingsWindowTop}");
                
                settingsWindow.Topmost = false;
                settingsWindow.Hide();
                // Re-enable hotkeys now that the Settings window is hidden
                HotkeyManager.Instance.SetEnabled(true);
                Console.WriteLine("Settings window hidden");
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                _toolbarWindow?.BringToFront();
            }
            else
            {
                // Always use the remembered position if it has been set
                if (settingsWindowLeft != -1 || settingsWindowTop != -1)
                {
                    // Restore previous position
                    settingsWindow.Left = settingsWindowLeft;
                    settingsWindow.Top = settingsWindowTop;
                    Console.WriteLine($"Restoring settings position to: {settingsWindowLeft}, {settingsWindowTop}");
                }
                else
                {
                    // Position to the right of the main window for first run
                    double mainRight = this.Left + this.ActualWidth;
                    double mainTop = this.Top;
                    
                    settingsWindow.Left = mainRight + 10; // 10px gap
                    settingsWindow.Top = mainTop;
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine("No saved position, positioning settings window to the right");
                    }
                }
                
                // Ensure window is not minimized
                if (settingsWindow.WindowState == WindowState.Minimized)
                {
                    settingsWindow.WindowState = WindowState.Normal;
                }
                
                // Set MainWindow as owner to ensure Settings window appears above it
                settingsWindow.Owner = this;
                // Topmost so the toolbar (also topmost) does not paint over Settings; restored to false on hide.
                settingsWindow.Topmost = true;
                settingsWindow.Show();
                
                // Ensure window is visible, on top, and activated
                settingsWindow.Visibility = Visibility.Visible;
                settingsWindow.Activate();
                settingsWindow.Focus();
                settingsWindow.BringIntoView();
                
                // Disable hotkeys while the Settings window is active so we can type normally
                HotkeyManager.Instance.SetEnabled(false);
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Settings window shown at position {settingsWindow.Left}, {settingsWindow.Top}");
                }
                settingsButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Active indicator
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

                    // Save image for debugging if enabled
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        try
                        {
                            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                            string imagePath = Path.Combine(appDirectory, "image_sent_to_ocr.png");
                            bitmap.Save(imagePath, ImageFormat.Png);
                            Console.WriteLine($"[DEBUG] Live capture image saved: {bitmap.Width}x{bitmap.Height}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error saving debug image: {ex.Message}");
                        }
                    }

                    // Check if we're using Windows OCR or Google Vision - if so, process in memory without saving
                    string ocrMethod = GetSelectedOcrMethod();
                    if (ocrMethod == "Windows OCR")
                    {
                        string sourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                        Logic.Instance.ProcessWithWindowsOCR(bitmap, sourceLanguage);
                    }
                    else if (ocrMethod == "Google Vision")
                    {
                        string sourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                        Logic.Instance.ProcessWithGoogleVision(bitmap, sourceLanguage);
                    }
                    else
                    {
                        // Send directly to HTTP service logic
                        // The logic will handle cloning the bitmap and converting to bytes
                        Logic.Instance.SendImageToHttpOCR(bitmap);
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

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                // Windows snap/Aero Snap can maximize the window when dragged to screen edge.
                // A maximized capture frame is useless, so restore to previous size immediately.
                this.WindowState = WindowState.Normal;

                if (_restoreBoundsBeforeMaximize.Width > 0 && _restoreBoundsBeforeMaximize.Height > 0)
                {
                    this.Left = _restoreBoundsBeforeMaximize.Left;
                    this.Top = _restoreBoundsBeforeMaximize.Top;
                    this.Width = _restoreBoundsBeforeMaximize.Width;
                    this.Height = _restoreBoundsBeforeMaximize.Height;
                }

                Console.WriteLine("Blocked window maximize (snap) - restored to previous size");
                return;
            }

            if (this.WindowState == WindowState.Minimized)
            {
                _wasStartedBeforeMinimize = isStarted;
                if (isStarted)
                {
                    isStarted = false;
                    SetOCRCheckIsWanted(false);
                    Console.WriteLine("Auto mode paused (minimized)");
                }

                HotkeyManager.Instance.SetEnabled(false);
                KeyboardShortcuts.SetShortcutsEnabled(false);
                Console.WriteLine("Window minimized - global hotkeys disabled");

                if (_toolbarWindow != null)
                    _toolbarWindow.Hide();
            }
            else
            {
                HotkeyManager.Instance.SetEnabled(true);
                KeyboardShortcuts.SetShortcutsEnabled(true);
                Console.WriteLine("Window restored - global hotkeys enabled");

                if (_wasStartedBeforeMinimize)
                {
                    isStarted = true;
                    SetOCRCheckIsWanted(true);
                    Console.WriteLine("Auto mode resumed (restored)");
                }

                if (_toolbarWindow != null)
                    _toolbarWindow.Show();
            }
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
                    
                    // Update the UI based on the selected OCR method
                    if (ocrMethod == "Windows OCR")
                    {
                        SetStatus("Using Windows OCR (built-in)");
                    }
                    else if (ocrMethod == "MangaOCR")
                    {
                        SetStatus("Using MangaOCR");
                    }
                    else if (ocrMethod == "docTR")
                    {
                        SetStatus("Using docTR");
                    }
                    else if (ocrMethod == "EasyOCR")
                    {
                        SetStatus("Using EasyOCR");
                    }
                    // HTTP services are used - connection status is checked per-request
                }
            }
        }
        
        // Keep track of selected OCR method
        private string selectedOcrMethod = "Windows OCR";
        
        public string GetSelectedOcrMethod()
        {
            return selectedOcrMethod;
        }

        // Header is now fixed-height (50px total: 10px resize strip + 40px bar).
        // OverlayContent margin is set statically in XAML to (15,50,15,15) and no longer
        // needs dynamic adjustment, so this handler is kept as a simple capture rect update.
        private void HeaderBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCaptureRect();
        }

        // Window size changed - save to config if persistence is enabled
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
            {
                _pendingSizeSave = true;
                RestartWindowPersistenceTimer();
            }

            UpdateToolbarPosition();
        }

        // DPI changed - update capture rect and refresh overlays
        private void MainWindow_DpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
        {
            Console.WriteLine($"DPI changed: {e.OldDpi.DpiScaleX:F2} -> {e.NewDpi.DpiScaleX:F2}");
            UpdateCaptureRect();
            _lastOverlayHtml = string.Empty; // Clear cache to force regeneration
            RefreshMainWindowOverlays();
        }

        // System settings changed - detects Windows Text Size changes
        private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            // Only respond to General category which includes accessibility settings
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General ||
                e.Category == Microsoft.Win32.UserPreferenceCategory.Accessibility)
            {
                // Dispatch to UI thread since this event can fire on a different thread
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Console.WriteLine($"System settings changed (category: {e.Category}), refreshing overlays");
                    UpdateCaptureRect();
                    _lastOverlayHtml = string.Empty; // Clear cache to force regeneration
                    RefreshMainWindowOverlays();
                }));
            }
        }

        // Display settings changed - detects display scale changes
        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            // Dispatch to UI thread since this event can fire on a different thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Console.WriteLine("Display settings changed, refreshing overlays");
                UpdateCaptureRect();
                _lastOverlayHtml = string.Empty; // Clear cache to force regeneration
                RefreshMainWindowOverlays();
            }));
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (ConfigManager.Instance.IsPersistWindowSizeEnabled())
            {
                _pendingPositionSave = true;
                RestartWindowPersistenceTimer();
            }

            UpdateToolbarPosition();
        }
        
        // Restart the debounce timer for window persistence
        private void RestartWindowPersistenceTimer()
        {
            if (_windowPersistenceTimer == null)
            {
                _windowPersistenceTimer = new DispatcherTimer();
                _windowPersistenceTimer.Interval = TimeSpan.FromMilliseconds(500);
                _windowPersistenceTimer.Tick += WindowPersistenceTimer_Tick;
            }
            
            // Reset the timer
            _windowPersistenceTimer.Stop();
            _windowPersistenceTimer.Start();
        }
        
        // Timer tick - save pending changes
        private void WindowPersistenceTimer_Tick(object? sender, EventArgs e)
        {
            _windowPersistenceTimer?.Stop();
            
            // Save any pending changes in a single save operation
            if (_pendingPositionSave && _pendingSizeSave)
            {
                // Both changed - save all at once
                ConfigManager.Instance.SetOcrWindowBounds(this.Left, this.Top, this.Width, this.Height);
            }
            else if (_pendingPositionSave)
            {
                ConfigManager.Instance.SetOcrWindowPosition(this.Left, this.Top);
            }
            else if (_pendingSizeSave)
            {
                ConfigManager.Instance.SetOcrWindowSize(this.Width, this.Height);
            }
            
            _pendingPositionSave = false;
            _pendingSizeSave = false;
        }
        
        // Save ChatBox and Monitor window states for restoration on next startup
        private void SaveSecondaryWindowStates()
        {
            // Save ChatBox window state
            var chatBox = ChatBoxWindow.Instance;
            if (chatBox != null)
            {
                bool chatBoxWasActive = chatBox.IsVisible;
                ConfigManager.Instance.SetChatBoxWindowState(
                    chatBox.Left,
                    chatBox.Top,
                    chatBox.Width,
                    chatBox.Height,
                    chatBoxWasActive
                );
                Console.WriteLine($"Saved ChatBox state: Left={chatBox.Left}, Top={chatBox.Top}, Width={chatBox.Width}, Height={chatBox.Height}, WasActive={chatBoxWasActive}");
            }
            
            // Save Monitor window state
            var monitor = MonitorWindow.Instance;
            if (monitor != null)
            {
                bool monitorWasActive = monitor.IsVisible;
                ConfigManager.Instance.SetMonitorWindowState(
                    monitor.Left,
                    monitor.Top,
                    monitor.Width,
                    monitor.Height,
                    monitorWasActive
                );
                Console.WriteLine($"Saved Monitor state: Left={monitor.Left}, Top={monitor.Top}, Width={monitor.Width}, Height={monitor.Height}, WasActive={monitorWasActive}");
            }
        }
        
        // Restore ChatBox and Monitor windows if they were active on last close
        private void RestoreSecondaryWindowStates()
        {
            // Restore ChatBox window if it was active
            if (ConfigManager.Instance.GetChatBoxWindowWasActive())
            {
                double left = ConfigManager.Instance.GetChatBoxWindowLeft();
                double top = ConfigManager.Instance.GetChatBoxWindowTop();
                double width = ConfigManager.Instance.GetChatBoxWindowWidth();
                double height = ConfigManager.Instance.GetChatBoxWindowHeight();
                
                // Validate the bounds are in a legal position on available screens
                if (ConfigManager.IsWindowBoundsValid(left, top, width, height))
                {
                    var chatBox = ChatBoxWindow.Instance;
                    if (chatBox != null)
                    {
                        chatBox.Left = left;
                        chatBox.Top = top;
                        chatBox.Width = width;
                        chatBox.Height = height;
                        chatBox.Owner = this;
                        chatBox.Show();
                        _toolbarWindow?.BringToFront();
                        
                        isChatBoxVisible = true;
                        chatBoxWindow = chatBox;
                        chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Red when active
                        
                        // Attach event handlers if not already attached
                        if (!_chatBoxEventsAttached)
                        {
                            chatBox.Closed += (s, e) =>
                            {
                                isChatBoxVisible = false;
                                chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                            };
                            
                            chatBox.IsVisibleChanged += (s, e) =>
                            {
                                if (!(bool)e.NewValue) // Window is now hidden
                                {
                                    isChatBoxVisible = false;
                                    chatBoxButton.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95)); // Neutral
                                }
                            };
                            
                            _chatBoxEventsAttached = true;
                        }
                        
                        Console.WriteLine($"Restored ChatBox: Left={left}, Top={top}, Width={width}, Height={height}");
                    }
                }
                else
                {
                    Console.WriteLine($"ChatBox bounds invalid or off-screen, not restoring: Left={left}, Top={top}, Width={width}, Height={height}");
                }
            }
            
            // Restore Monitor window if it was active
            if (ConfigManager.Instance.GetMonitorWindowWasActive())
            {
                double left = ConfigManager.Instance.GetMonitorWindowLeft();
                double top = ConfigManager.Instance.GetMonitorWindowTop();
                double width = ConfigManager.Instance.GetMonitorWindowWidth();
                double height = ConfigManager.Instance.GetMonitorWindowHeight();
                
                // Validate the bounds are in a legal position on available screens
                if (ConfigManager.IsWindowBoundsValid(left, top, width, height))
                {
                    var monitor = MonitorWindow.Instance;
                    if (monitor != null)
                    {
                        monitor.Left = left;
                        monitor.Top = top;
                        monitor.Width = width;
                        monitor.Height = height;
                        
                        // Update the tracked position
                        monitorWindowLeft = left;
                        monitorWindowTop = top;
                        
                        monitor.Owner = this;
                        monitor.Show();
                        _toolbarWindow?.BringToFront();
                        
                        monitorButton.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Active indicator
                        
                        // If we have a recent screenshot, load it
                        if (File.Exists(outputPath))
                        {
                            monitor.UpdateScreenshot(outputPath);
                            monitor.RefreshOverlays();
                        }
                        
                        Console.WriteLine($"Restored Monitor: Left={left}, Top={top}, Width={width}, Height={height}");
                    }
                }
                else
                {
                    Console.WriteLine($"Monitor bounds invalid or off-screen, not restoring: Left={left}, Top={top}, Width={width}, Height={height}");
                }
            }
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
            string elapsedStr = $"{elapsed.Minutes:D1}:{elapsed.Seconds:D2}";
            
            // Build status text with optional token count (llama.cpp streaming updates tokens; other services are non-streaming)
            string statusText;
            
            if (TranslationStatus.IsStreaming)
            {
                int tokenCount = TranslationStatus.TokenCount;
                
                if (TranslationStatus.IsThinking)
                {
                    statusText = tokenCount > 0
                        ? $"LLM thinking... {elapsedStr} (tokens: {tokenCount})"
                        : $"LLM thinking... {elapsedStr}";
                }
                else if (tokenCount > 0)
                {
                    statusText = $"Waiting for {service}... {elapsedStr} (tokens: {tokenCount})";
                }
                else
                {
                    statusText = $"Waiting for {service}... {elapsedStr}";
                }
            }
            else
            {
                statusText = $"Waiting for {service}... {elapsedStr}";
            }
            
            // Broadcast to all windows
            TranslationStatus.SetStatus(statusText);
            
            Dispatcher.Invoke(() =>
            {
                if (translationStatusLabel != null)
                {
                    translationStatusLabel.Text = statusText;
                }
            });
        }
        
        // Show the translation status
        public void ShowTranslationStatus(bool bSettling, double elapsedSettleTime = 0, double maxSettleTime = 0)
        {
            if (bSettling)
            {
                _isShowingSettling = true;
                string statusText = maxSettleTime > 0 
                    ? $"Settling... {elapsedSettleTime:F1}s / {maxSettleTime:F1}s"
                    : "Settling...";
                
                // Broadcast to all windows
                TranslationStatus.SetStatus(statusText);
                
                Dispatcher.Invoke(() =>
                {
                    if (translationStatusLabel != null)
                    {
                        translationStatusLabel.Text = statusText;
                    }
                    
                    if (translationStatusBorder != null)
                        translationStatusBorder.Visibility = Visibility.Visible;

                    if (translationProgressBar != null)
                    {
                        translationProgressBar.IsIndeterminate = true;
                        translationProgressBar.Visibility = Visibility.Visible;
                    }
                });
                return;
            }
            
            _isShowingSettling = false;
            _translationStartTime = DateTime.Now;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            string initialStatusText = $"Waiting for {service}... 0:00";
            
            // Broadcast to all windows
            TranslationStatus.SetStatus(initialStatusText);
            
            Dispatcher.Invoke(() =>
            {
                if (translationStatusLabel != null)
                    translationStatusLabel.Text = initialStatusText;
                
                if (translationStatusBorder != null)
                    translationStatusBorder.Visibility = Visibility.Visible;

                if (translationProgressBar != null)
                {
                    translationProgressBar.IsIndeterminate = true;
                    translationProgressBar.Visibility = Visibility.Visible;
                }

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
            
            // Broadcast to all windows
            TranslationStatus.SetStatus("Stopped");
            
            Dispatcher.Invoke(() =>
            {
                if (_translationStatusTimer != null && _translationStatusTimer.IsEnabled)
                {
                    _translationStatusTimer.Stop();
                }
                
                // Update status to "Stopped" instead of hiding the border
                // This maintains title bar height
                if (translationStatusLabel != null)
                {
                    translationStatusLabel.Text = "Stopped";
                }
                
                // Keep the border visible to maintain title bar height
                if (translationStatusBorder != null)
                    translationStatusBorder.Visibility = Visibility.Visible;

                if (translationProgressBar != null)
                {
                    translationProgressBar.IsIndeterminate = false;
                    translationProgressBar.Visibility = Visibility.Collapsed;
                }
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
            
            string newText = $"{ocrMethod} (fps: {fps:F1})";
            
            // Broadcast to all windows
            TranslationStatus.SetStatus(newText);
            
            if (translationStatusLabel != null)
            {
                // Only update text if it has changed to avoid flickering
                if (translationStatusLabel.Text != newText)
                {
                    translationStatusLabel.Text = newText;
                }
            }
            
            if (translationStatusBorder != null && translationStatusBorder.Visibility != Visibility.Visible)
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
            
            // Broadcast to all windows
            TranslationStatus.SetStatus("Stopped");
            
            // Update status to "Stopped" instead of hiding
            // This maintains title bar height
            if (translationStatusLabel != null)
            {
                translationStatusLabel.Text = "Stopped";
            }
            
            // Keep border visible to maintain title bar height
            if (translationStatusBorder != null)
            {
                translationStatusBorder.Visibility = Visibility.Visible;
            }
        }
        
        public void HandleExportButton()
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

        public void HandleSaveScreenshot()
        {
            ScreenshotManager.Instance.SaveScreenshot();
        }

        public void HandleBatchConvert()
        {
            var dialog = new BatchConverterDialog();
            dialog.Owner = this;
            dialog.Show();
        }

        public void BringToFront()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            _toolbarWindow?.BringToFront();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    BringToFront();
                }
            }), DispatcherPriority.Input);
        }

        private void TopmostGuard_Tick(object? sender, EventArgs e)
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
                return;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOPMOST) == 0)
            {
                Console.WriteLine("Topmost guard: window lost HWND_TOPMOST, re-asserting");
                BringToFront();
            }
        }

        private void ensureWindowOnScreen()
        {
            double w = double.IsNaN(this.Width) || this.Width <= 0 ? DEFAULT_WINDOW_WIDTH : this.Width;
            double h = double.IsNaN(this.Height) || this.Height <= 0 ? DEFAULT_WINDOW_HEIGHT : this.Height;
            double l = this.Left;
            double t = this.Top;

            if (double.IsNaN(l) || double.IsNaN(t) || !ConfigManager.IsWindowBoundsValid(l, t, w, h))
            {
                var workArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
                if (workArea.HasValue)
                {
                    this.Left = workArea.Value.Left + (workArea.Value.Width - w) / 2;
                    this.Top = workArea.Value.Top + (workArea.Value.Height - h) / 2;
                }
                else
                {
                    this.Left = 100;
                    this.Top = 100;
                }
                Console.WriteLine($"ensureWindowOnScreen: Moved main window to Left={this.Left}, Top={this.Top} (was Left={l}, Top={t})");
            }
        }
    }
}
