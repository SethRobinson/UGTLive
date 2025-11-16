using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using Color = System.Windows.Media.Color;
using System.Diagnostics;
using System.Text;


namespace UGTLive
{
    public enum OverlayMode
    {
        Hide,
        Source,
        Translated
    }
    
    public partial class MonitorWindow : Window
    {
        private double currentZoom = 1.0;
        private const double zoomIncrement = 0.1;
        private string lastImagePath = string.Empty;
        private readonly Dictionary<string, (SolidColorBrush bgColor, SolidColorBrush textColor)> _originalColors = new();
        private OverlayMode _currentOverlayMode = OverlayMode.Translated; // Default to Translated
        
        // WebView2 hit testing control for scrollbar and titlebar interaction
        private bool _webViewHitTestingEnabled = true;
        private int _scrollbarWidth = 0;
        private int _scrollbarHeight = 0;
        private int _titleBarHeight = 0;
        private string _lastHitRegion = ""; // Track which region we're over to reduce logging noise
        
        // Singleton pattern to match application style
        private static MonitorWindow? _instance;
        public static MonitorWindow Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MonitorWindow();
                }
                return _instance;
            }
        }
        
        // Flag to allow proper closing during shutdown
        private bool _isShuttingDown = false;
        
        public void ForceClose()
        {
            _isShuttingDown = true;
            Close();
        }
        
        // Getter for current overlay mode
        public OverlayMode CurrentOverlayMode => _currentOverlayMode;
        
        // Settle time is stored in ConfigManager, no need for a local variable
        
        public MonitorWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MonitorWindow constructor: Setting _isInitializing to true");
            
            InitializeComponent();
            
            Console.WriteLine("MonitorWindow constructor started");

            PopulateOcrMethodOptions();
            if (ocrMethodComboBox.Items.Count > 0)
            {
                ocrMethodComboBox.SelectedIndex = 0;
            }
            
            // Subscribe to TextObject events from Logic
            //Logic.Instance.TextObjectAdded += CreateMonitorOverlayFromTextObject;
            
            // Set initial status
            UpdateStatus("Ready");
             
            // Add event handlers
            this.SourceInitialized += MonitorWindow_SourceInitialized;
            this.Loaded += MonitorWindow_Loaded;
            
            // Add size changed handler to update scrollbars
            this.SizeChanged += MonitorWindow_SizeChanged;
            
            // Set up tooltip exclusion from screenshots
            SetupTooltipExclusion();
            
            // Manually connect events (to ensure we have control over when they're attached)
            ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
            autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
            autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;
            
            // Add event handlers for the zoom TextBox
            zoomTextBox.TextChanged += ZoomTextBox_TextChanged;
            zoomTextBox.LostFocus += ZoomTextBox_LostFocus;
            
            // Add KeyDown event handlers for TextBoxes to handle Enter key
            zoomTextBox.KeyDown += TextBox_KeyDown;
            
            // Add MouseWheel event handler for Ctrl+Wheel zoom
            this.MouseWheel += MonitorWindow_MouseWheel;

            SocketManager.Instance.ConnectionChanged += OnSocketConnectionChanged;


            // Set default size if not already set
            if (this.Width == 0)
                this.Width = 600;
            if (this.Height == 0)
                this.Height = 500;
                
            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;

            Console.WriteLine("MonitorWindow constructor completed");
        }

        private void PopulateOcrMethodOptions()
        {
            ocrMethodComboBox.Items.Clear();

            foreach (string method in ConfigManager.SupportedOcrMethods)
            {
                string displayName = ConfigManager.GetOcrMethodDisplayName(method);
                ocrMethodComboBox.Items.Add(new ComboBoxItem 
                { 
                    Content = displayName,
                    Tag = method  // Store internal ID in Tag
                });
            }
        }

        private void OnSocketConnectionChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                //set our status text
                UpdateStatus("Connected to Python backend");
            }
        }


        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        // Timer for updating translation status
        private DispatcherTimer? _translationStatusTimer;
        private DateTime _translationStartTime;
        
        // OCR status display (no tracking - handled by Logic.cs)
        private bool _isShowingSettling = false; // Track if settling message is showing
        
        // OCR Method Selection Changed
        public void OcrMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip event if we're initializing
            Console.WriteLine($"MonitorWindow.OcrMethodComboBox_SelectionChanged called (isInitializing: {_isInitializing})");
            if (_isInitializing)
            {
                Console.WriteLine("Skipping OCR method change during initialization");
                return;
            }
            
            if (ocrMethodComboBox.SelectedItem == null) return;
            
            // Get internal ID from Tag property
            string? ocrMethod = (ocrMethodComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(ocrMethod)) return;
            
            // Reset the OCR hash to force a fresh comparison after changing OCR method
            Logic.Instance.ResetHash();
            
            Console.WriteLine($"OCR method changed to: {ocrMethod}");
            
            // Clear any existing text objects
            Logic.Instance.ClearAllTextObjects();
            
            // Update the UI and connection state based on the selected OCR method
            if (ocrMethod == "Windows OCR")
            {
                // Using Windows OCR, no need for socket connection
                _ = Task.Run(() => 
                {
                    try
                    {
                       SocketManager.Instance.Disconnect();
                        UpdateStatus("Using Windows OCR (built-in)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disconnecting socket: {ex.Message}");
                    }
                });
            }
            else if (ocrMethod == "Google Vision")
            {
                // Using Google Vision API, no need for socket connection
                _ = Task.Run(() => 
                {
                    try
                    {
                       SocketManager.Instance.Disconnect();
                        UpdateStatus("Using Google Cloud Vision (non-local, costs $)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disconnecting socket: {ex.Message}");
                    }
                });
            }
            else
            {
                // Using EasyOCR, check connection status first
                _ = Task.Run(async () => 
                {
                    try
                    {
                        Console.WriteLine("Checking local socket connection...");

                        // If already connected, we're good to go
                        if (SocketManager.Instance.IsConnected)
                        {
                            Console.WriteLine("Already connected to socket server");
                            UpdateStatus("Connected to Python backend");
                            return;
                        }

                        // Not connected yet, attempt to connect silently first
                        UpdateStatus("Connecting to Python backend...");

                        // Connect without disconnecting first (TryReconnectAsync handles cleanup)
                        bool reconnected = await SocketManager.Instance.TryReconnectAsync();
                        
                        // Update status based on reconnection result
                        if (reconnected && SocketManager.Instance.IsConnected)
                        {
                            Console.WriteLine("Successfully connected to socket server");
                            UpdateStatus("Connected to Python backend");
                        }
                        else
                        {
                            Console.WriteLine("Failed to connect to socket server - will retry when needed");
                            UpdateStatus("Not connected to Python backend");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during socket reconnection: {ex.Message}");
                        UpdateStatus("Error connecting to Python backend");
                    }
                });
            }
            
            // Sync the OCR method selection with MainWindow
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetOcrMethod(ocrMethod);
            }
            
            // Only save if not initializing
            if (!_isInitializing)
            {
                Console.WriteLine($"Saving OCR method to config: '{ocrMethod}'");
                ConfigManager.Instance.SetOcrMethod(ocrMethod);
            }
            else
            {
                Console.WriteLine($"Skipping save during initialization for OCR method: '{ocrMethod}'");
            }
        }
        
        // Auto Translate Checkbox Changed
        private void AutoTranslateCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool isAutoTranslateEnabled = autoTranslateCheckBox.IsChecked ?? false;
            
            Console.WriteLine($"Auto-translate {(isAutoTranslateEnabled ? "enabled" : "disabled")}");
            
            // Clear text objects
            Logic.Instance.ClearAllTextObjects();
            Logic.Instance.ResetHash();
            
            // Force OCR to run again
            MainWindow.Instance.SetOCRCheckIsWanted(true);
            
            // Refresh overlays
            RefreshOverlays();
            
            // Sync the checkbox state with MainWindow
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetAutoTranslateEnabled(isAutoTranslateEnabled);
            }
        }
        
        private void MonitorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("MonitorWindow_Loaded: Starting initialization");
            
            // Set initialization flag to true to prevent saving during setup
            _isInitializing = true;
            
            // Make sure keyboard shortcuts work from this window too
            PreviewKeyDown -= Application_KeyDown;
            PreviewKeyDown += Application_KeyDown;
            
            // Hook into Windows messages to intercept WM_MOUSEWHEEL before WebView2 handles it
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
            {
                source.AddHook(WndProc);
            }

            // Store window handle for low-level hook
            _monitorWindowHandle = new WindowInteropHelper(this).Handle;

            // Install low-level mouse hook to catch messages before they reach WebView2
            _mouseHookProc = LowLevelMouseHookProc;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, 
                GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName), 0);
            
            // Initialize scrollbar dimensions from system metrics
            initializeScrollbarDimensions();
            
            // Mouse move tracking is handled by the low-level mouse hook
            // which catches events before WebView2 consumes them
            this.MouseLeave += monitorWindow_MouseLeave;
            
            // Try to load the last screenshot if available
            if (!string.IsNullOrEmpty(lastImagePath) && File.Exists(lastImagePath))
            {
                UpdateScreenshot(lastImagePath);
            }
            
            // Initialize controls from MainWindow
            if (MainWindow.Instance != null)
            {
                // Get OCR method from config
                string ocrMethod = ConfigManager.Instance.GetOcrMethod();
                Console.WriteLine($"MonitorWindow_Loaded: Loading OCR method from config: '{ocrMethod}'");
                
                // Temporarily remove the event handler to prevent triggering
                // a new connection while initializing
                ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;
                
                // Find the matching ComboBoxItem by Tag (internal ID)
                bool foundMatch = false;
                foreach (ComboBoxItem comboItem in ocrMethodComboBox.Items)
                {
                    string itemId = comboItem.Tag?.ToString() ?? "";
                    Console.WriteLine($"Comparing OCR method: '{itemId}' with config value: '{ocrMethod}'");
                    
                    if (string.Equals(itemId, ocrMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Found matching OCR method: '{itemId}'");
                        ocrMethodComboBox.SelectedItem = comboItem;
                        foundMatch = true;
                        break;
                    }
                }
                
                if (!foundMatch)
                {
                    Console.WriteLine($"WARNING: Could not find OCR method '{ocrMethod}' in ComboBox. Available items:");
                    foreach (ComboBoxItem listItem in ocrMethodComboBox.Items)
                    {
                        Console.WriteLine($"  - '{listItem.Tag}' (display: '{listItem.Content}')");
                    }
                }
                
                // Log what we actually set
                if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    Console.WriteLine($"OCR ComboBox is now set to: '{selectedItem.Tag}' (display: '{selectedItem.Content}')");
                }
                
                // Re-attach the event handler
                ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
                
                // Make sure MainWindow has the same OCR method
                if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedComboItem)
                {
                    string selectedOcrMethod = selectedComboItem.Tag?.ToString() ?? "";
                    MainWindow.Instance.SetOcrMethod(selectedOcrMethod);
                }
                
                // Get auto-translate state from MainWindow
                bool isTranslateEnabled = MainWindow.Instance.GetTranslateEnabled();
                
                // Load overlay mode from config
                string overlayMode = ConfigManager.Instance.GetMonitorOverlayMode();
                Console.WriteLine($"MonitorWindow_Loaded: Loading overlay mode from config: '{overlayMode}'");
                
                // Temporarily remove event handlers to prevent triggering saves during initialization
                overlayHideRadio.Checked -= OverlayRadioButton_Checked;
                overlaySourceRadio.Checked -= OverlayRadioButton_Checked;
                overlayTranslatedRadio.Checked -= OverlayRadioButton_Checked;
                
                // Set the appropriate radio button based on config
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
                    default: // Default to Translated if not set or invalid
                        _currentOverlayMode = OverlayMode.Translated;
                        overlayTranslatedRadio.IsChecked = true;
                        break;
                }
                
                // Reattach event handlers
                overlayHideRadio.Checked += OverlayRadioButton_Checked;
                overlaySourceRadio.Checked += OverlayRadioButton_Checked;
                overlayTranslatedRadio.Checked += OverlayRadioButton_Checked;
                
                // Initialization complete, now we can save settings changes
                _isInitializing = false;
                Console.WriteLine("MonitorWindow initialization complete. Settings changes will now be saved.");
                
                // Force the OCR method to match the config again
                // This ensures the config value is preserved and not overwritten
                string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
                Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");
                
                // Now that initialization is complete, save the OCR method from config
                ConfigManager.Instance.SetOcrMethod(configOcrMethod);
                
                // Temporarily remove event handler
                autoTranslateCheckBox.Checked -= AutoTranslateCheckBox_CheckedChanged;
                autoTranslateCheckBox.Unchecked -= AutoTranslateCheckBox_CheckedChanged;
                
                autoTranslateCheckBox.IsChecked = isTranslateEnabled;
                
                // Re-attach event handlers
                autoTranslateCheckBox.Checked += AutoTranslateCheckBox_CheckedChanged;
                autoTranslateCheckBox.Unchecked += AutoTranslateCheckBox_CheckedChanged;
            }
            
            // Initialize the overlay WebView2
            InitializeOverlayWebView();
            
            Console.WriteLine("MonitorWindow initialization complete");
        }
        
        private void MonitorWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Apply WDA_EXCLUDEFROMCAPTURE as early as possible (right after HWND creation)
            SetExcludeFromCapture();
        }
        
        private void SetExcludeFromCapture()
        {
            try
            {
                // Check if user wants windows visible in screenshots
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                
                if (hwnd != IntPtr.Zero)
                {
                    // If visibleInScreenshots is true, set to WDA_NONE (include in capture)
                    // If visibleInScreenshots is false, set to WDA_EXCLUDEFROMCAPTURE (exclude from capture)
                    uint affinity = visibleInScreenshots ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE;
                    bool success = SetWindowDisplayAffinity(hwnd, affinity);
                    
                    if (success)
                    {
                        Console.WriteLine($"Monitor window {(visibleInScreenshots ? "included in" : "excluded from")} screen capture successfully (HWND: {hwnd})");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to set Monitor window capture mode. Last error: {Marshal.GetLastWin32Error()}");
                    }
                }
                else
                {
                    Console.WriteLine("Monitor window HWND is null, cannot set capture mode");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Monitor window capture mode: {ex.Message}");
            }
        }
        
        public void UpdateCaptureExclusion()
        {
            // Update the main window
            SetExcludeFromCapture();
            
            // Update WebView2 child windows
            if (_overlayWebViewInitialized && textOverlayWebView?.CoreWebView2 != null)
            {
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                SetWebView2ExcludeFromCapture(visibleInScreenshots);
            }
        }
        
        private void initializeScrollbarDimensions()
        {
            // Get scrollbar dimensions from Windows system metrics
            _scrollbarWidth = GetSystemMetrics(SM_CXVSCROLL);
            _scrollbarHeight = GetSystemMetrics(SM_CYHSCROLL);
            _titleBarHeight = GetSystemMetrics(SM_CYCAPTION);
        }
        
        private void checkMousePositionAndUpdateHitTesting(System.Windows.Point screenPoint)
        {
            // Check if mouse is over scrollbars or title bar
            // If so, disable WebView2 hit testing to allow interaction with these UI elements
            
            try
            {
                System.Windows.Point mousePosWindow = this.PointFromScreen(screenPoint);
                System.Windows.Point mousePosScrollViewer = imageScrollViewer.PointFromScreen(screenPoint);
                bool shouldDisableHitTesting = false;
                string currentRegion = "content";
                
                // Check if mouse is over title bar first (using window coordinates)
                // Title bar area includes borders, so we check top portion of window
                if (mousePosWindow.Y <= _titleBarHeight && mousePosWindow.Y >= 0)
                {
                    shouldDisableHitTesting = true;
                    currentRegion = "titlebar";
                }
                // Check scrollbars using ScrollViewer coordinates
                else if (mousePosScrollViewer.X >= 0 && mousePosScrollViewer.Y >= 0 &&
                         mousePosScrollViewer.X <= imageScrollViewer.ActualWidth &&
                         mousePosScrollViewer.Y <= imageScrollViewer.ActualHeight)
                {
                    // Mouse is within the ScrollViewer bounds
                    double scrollViewerWidth = imageScrollViewer.ActualWidth;
                    double scrollViewerHeight = imageScrollViewer.ActualHeight;
                    
                    // Check if vertical scrollbar is visible and mouse is over it
                    if (imageScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                    {
                        double vScrollbarLeft = scrollViewerWidth - _scrollbarWidth;
                        if (mousePosScrollViewer.X >= vScrollbarLeft)
                        {
                            shouldDisableHitTesting = true;
                            currentRegion = "vertical_scrollbar";
                        }
                    }
                    
                    // Check if horizontal scrollbar is visible and mouse is over it
                    if (imageScrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
                    {
                        double hScrollbarTop = scrollViewerHeight - _scrollbarHeight;
                        if (mousePosScrollViewer.Y >= hScrollbarTop)
                        {
                            shouldDisableHitTesting = true;
                            currentRegion = "horizontal_scrollbar";
                        }
                    }
                }
                
                // Track region changes
                if (currentRegion != _lastHitRegion)
                {
                    _lastHitRegion = currentRegion;
                }
                
                // Update WebView2 hit testing state if it changed
                if (shouldDisableHitTesting != !_webViewHitTestingEnabled)
                {
                    _webViewHitTestingEnabled = !shouldDisableHitTesting;
                    
                    if (textOverlayWebView != null)
                    {
                        // WebView2 is a native control, so IsHitTestVisible doesn't work
                        // We need to use IsEnabled to prevent it from capturing mouse events
                        textOverlayWebView.IsEnabled = _webViewHitTestingEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in checkMousePositionAndUpdateHitTesting: {ex.Message}");
            }
        }
        
        private void monitorWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // When mouse leaves the window, re-enable WebView2 interaction
            _lastHitRegion = ""; // Reset region tracker
            
            if (!_webViewHitTestingEnabled)
            {
                _webViewHitTestingEnabled = true;
                
                if (textOverlayWebView != null)
                {
                    textOverlayWebView.IsEnabled = true;
                }
            }
        }
        
        private void SetWebView2ExcludeFromCapture(bool visibleInScreenshots)
        {
            try
            {
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
                            }
                            else
                            {
                                Console.WriteLine($"Failed to set Monitor WebView2 capture mode. Last error: {Marshal.GetLastWin32Error()}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Monitor WebView2 HWND is null");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Monitor WebView2: Could not get HwndSource, WebView2 may share parent window HWND");
                        // WebView2 shares the parent window's HWND, so the main window exclusion covers it
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Monitor WebView2 capture mode: {ex.Message}");
            }
        }
        
        // Win32 API for enumerating child windows
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        private bool _overlayWebViewInitialized = false;
        
        private async void InitializeOverlayWebView()
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
                    
                    // Add event handlers for context menu
                    textOverlayWebView.CoreWebView2.WebMessageReceived += OverlayWebView_WebMessageReceived;
                    textOverlayWebView.CoreWebView2.ContextMenuRequested += OverlayWebView_ContextMenuRequested;
                    
                    _overlayWebViewInitialized = true;
                    
                    // Initial empty render
                    UpdateOverlayWebView();
                    
                    // Apply WDA_EXCLUDEFROMCAPTURE to WebView2 child windows
                    // Use a longer delay to ensure child windows are fully created
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                            SetWebView2ExcludeFromCapture(visibleInScreenshots);
                        });
                    });
                    
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing overlay WebView2: {ex.Message}");
            }
        }
        
        private string _lastOverlayHtml = string.Empty;
        
        private void UpdateOverlayWebView()
        {
            if (!_overlayWebViewInitialized || textOverlayWebView?.CoreWebView2 == null)
            {
                return;
            }
            
            try
            {
                string html = GenerateOverlayHtml();
                
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
                Console.WriteLine($"Error updating overlay WebView: {ex.Message}");
            }
        }
        
        private string GenerateOverlayHtml()
        {
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
            html.AppendLine("  pointer-events: none;");
            html.AppendLine("}");
            html.AppendLine(".text-overlay {");
            html.AppendLine("  position: absolute;");
            html.AppendLine("  box-sizing: border-box;");
            html.AppendLine("  overflow: hidden;");
            html.AppendLine("  white-space: normal;");
            html.AppendLine("  word-wrap: break-word;");
            html.AppendLine("  pointer-events: auto;");
            html.AppendLine("  user-select: text;");
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
                                if (!IsVerticalSupportedLanguage(targetLang))
                                {
                                    displayOrientation = "horizontal";
                                }
                            }
                        }
                        else
                        {
                            textToShow = textObj.Text;
                        }
                        
                        // Get colors with override logic
                        Color bgColor;
                        Color textColor;
                        
                        if (ConfigManager.Instance.IsMonitorOverrideBgColorEnabled())
                        {
                            bgColor = ConfigManager.Instance.GetMonitorOverrideBgColor();
                        }
                        else
                        {
                            bgColor = textObj.BackgroundColor?.Color ?? Colors.Black;
                        }
                        
                        if (ConfigManager.Instance.IsMonitorOverrideFontColorEnabled())
                        {
                            textColor = ConfigManager.Instance.GetMonitorOverrideFontColor();
                        }
                        else
                        {
                            textColor = textObj.TextColor?.Color ?? Colors.White;
                        }
                        
                        // Get font settings
                        string fontFamily = isTranslated
                            ? ConfigManager.Instance.GetTargetLanguageFontFamily()
                            : ConfigManager.Instance.GetSourceLanguageFontFamily();
                        bool isBold = isTranslated
                            ? ConfigManager.Instance.GetTargetLanguageFontBold()
                            : ConfigManager.Instance.GetSourceLanguageFontBold();
                        
                        // Encode text for HTML (trim to remove leading/trailing whitespace)
                        // Also normalize internal whitespace
                        string encodedText = System.Web.HttpUtility.HtmlEncode(textToShow.Trim())
                            .Replace("\r\n", " ")
                            .Replace("\r", " ")
                            .Replace("\n", " ");
                        
                        // Apply zoom factor to positions and dimensions
                        double left = textObj.X * currentZoom;
                        double top = textObj.Y * currentZoom;
                        double width = textObj.Width * currentZoom;
                        double height = textObj.Height * currentZoom;
                        
                        // Build the div for this text object (all on one line to avoid newline issues)
                        string styleAttr = $"left: {left}px; top: {top}px; width: {width}px; height: {height}px; " +
                            $"background-color: rgba({bgColor.R},{bgColor.G},{bgColor.B},{bgColor.A / 255.0:F3}); " +
                            $"color: rgb({textColor.R},{textColor.G},{textColor.B}); " +
                            $"font-family: {string.Join(", ", fontFamily.Split(',').Select(f => $"\"{f.Trim()}\""))}; " +
                            $"font-weight: {(isBold ? "bold" : "normal")}; " +
                            $"font-size: {16 * currentZoom}px;";
                        
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
        
        private string? _currentContextMenuTextObjectId;
        private string? _currentContextMenuSelection;
        
        private void OverlayWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
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
                    
                    ShowOverlayContextMenu(textObjectId, x, y, selection);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling overlay WebView message: {ex.Message}");
            }
        }
        
        private void OverlayWebView_ContextMenuRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
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
        
        private void ShowOverlayContextMenu(string textObjectId, double clientX, double clientY, string? selection)
        {
            try
            {
                if (string.IsNullOrEmpty(textObjectId))
                {
                    return;
                }
                
                _currentContextMenuTextObjectId = textObjectId;
                _currentContextMenuSelection = string.IsNullOrWhiteSpace(selection) ? null : selection.Trim();
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // WebView2 coordinates are in content space, need to divide by zoom
                        // because the imageContainer (which contains the WebView) is scaled
                        System.Windows.Point contentPoint = new System.Windows.Point(clientX / currentZoom, clientY / currentZoom);
                        System.Windows.Point relativeToWebView = textOverlayWebView.TranslatePoint(contentPoint, this);
                        System.Windows.Point screenPoint = this.PointToScreen(relativeToWebView);
                        
                        ContextMenu contextMenu = CreateOverlayContextMenu();
                        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
                        contextMenu.HorizontalOffset = screenPoint.X;
                        contextMenu.VerticalOffset = screenPoint.Y;
                        contextMenu.IsOpen = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error showing context menu: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preparing context menu: {ex.Message}");
            }
        }
        
        private ContextMenu CreateOverlayContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();
            
            // Copy menu item
            MenuItem copyMenuItem = new MenuItem();
            copyMenuItem.Header = "Copy";
            copyMenuItem.Click += OverlayContextMenu_Copy_Click;
            contextMenu.Items.Add(copyMenuItem);
            
            // Copy Translated menu item (only shown when in Source mode)
            MenuItem copyTranslatedMenuItem = new MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += OverlayContextMenu_CopyTranslated_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Separator
            contextMenu.Items.Add(new Separator());
            
            // Lesson menu item (ChatGPT)
            MenuItem lessonMenuItem = new MenuItem();
            lessonMenuItem.Header = "Lesson";
            lessonMenuItem.Click += OverlayContextMenu_Lesson_Click;
            contextMenu.Items.Add(lessonMenuItem);
            
            // Jisho lookup menu item (jisho.org)
            MenuItem lookupKanjiMenuItem = new MenuItem();
            lookupKanjiMenuItem.Header = "Jisho lookup";
            lookupKanjiMenuItem.Click += OverlayContextMenu_LookupKanji_Click;
            contextMenu.Items.Add(lookupKanjiMenuItem);
            
            // Speak menu item
            MenuItem speakMenuItem = new MenuItem();
            speakMenuItem.Header = "Speak";
            speakMenuItem.Click += OverlayContextMenu_Speak_Click;
            contextMenu.Items.Add(speakMenuItem);
            
            // Speak (source) menu item (only shown when in Translated mode)
            MenuItem speakSourceMenuItem = new MenuItem();
            speakSourceMenuItem.Header = "Speak (source)";
            speakSourceMenuItem.Click += OverlayContextMenu_SpeakSource_Click;
            contextMenu.Items.Add(speakSourceMenuItem);
            
            // Update menu visibility when opened
            contextMenu.Opened += (s, e) =>
            {
                TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
                if (textObj != null)
                {
                    copyTranslatedMenuItem.Visibility = _currentOverlayMode == OverlayMode.Source ? Visibility.Visible : Visibility.Collapsed;
                    copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(textObj.TextTranslated);
                    speakSourceMenuItem.Visibility = _currentOverlayMode == OverlayMode.Translated ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            
            contextMenu.Closed += (s, e) =>
            {
                _currentContextMenuTextObjectId = null;
                _currentContextMenuSelection = null;
            };
            
            return contextMenu;
        }
        
        private TextObject? GetTextObjectById(string? id)
        {
            if (string.IsNullOrEmpty(id) || Logic.Instance == null)
            {
                return null;
            }
            
            var textObjects = Logic.Instance.GetTextObjects();
            return textObjects?.FirstOrDefault(t => t.ID == id);
        }
        
        private void OverlayContextMenu_Copy_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToCopy = !string.IsNullOrWhiteSpace(_currentContextMenuSelection) 
                    ? _currentContextMenuSelection 
                    : (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated) 
                        ? textObj.TextTranslated 
                        : textObj.Text);
                
                System.Windows.Forms.Clipboard.SetText(textToCopy);
                UpdateStatus("Text copied to clipboard");
            }
        }
        
        private void OverlayContextMenu_CopyTranslated_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null && !string.IsNullOrEmpty(textObj.TextTranslated))
            {
                System.Windows.Forms.Clipboard.SetText(textObj.TextTranslated);
                UpdateStatus("Translated text copied to clipboard");
            }
        }
        
        private void OverlayContextMenu_Lesson_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToLearn = !string.IsNullOrWhiteSpace(_currentContextMenuSelection) 
                    ? _currentContextMenuSelection 
                    : textObj.Text;
                
                if (!string.IsNullOrWhiteSpace(textToLearn))
                {
                    string chatGptPrompt = $"Create a comprehensive lesson to help me learn about this Japanese text and its translation: \"{textToLearn}\"\n\nPlease include:\n1. A detailed breakdown table with columns for: Japanese text, Reading (furigana), Literal meaning, and Grammar notes\n2. Key vocabulary with example sentences\n3. Cultural or contextual notes if relevant\n4. At the end, provide 5 helpful flashcards in a clear format for memorization";
                    string encodedPrompt = Uri.EscapeDataString(chatGptPrompt);
                    string chatGptUrl = $"https://chat.openai.com/?q={encodedPrompt}";
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = chatGptUrl,
                        UseShellExecute = true
                    });
                }
            }
        }
        
        private void OverlayContextMenu_LookupKanji_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToLearn = !string.IsNullOrWhiteSpace(_currentContextMenuSelection) 
                    ? _currentContextMenuSelection 
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
        
        private void OverlayContextMenu_Speak_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToSpeak = !string.IsNullOrWhiteSpace(_currentContextMenuSelection)
                    ? _currentContextMenuSelection
                    : (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated)
                        ? textObj.TextTranslated
                        : textObj.Text);
                
                if (!string.IsNullOrWhiteSpace(textToSpeak))
                {
                    // Use the configured TTS service
                    string ttsService = ConfigManager.Instance.GetTtsService();
                    if (ttsService.Equals("Google Cloud TTS", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = GoogleTTSService.Instance.SpeakText(textToSpeak);
                    }
                    else // Default to ElevenLabs
                    {
                        _ = ElevenLabsService.Instance.SpeakText(textToSpeak);
                    }
                }
            }
        }
        
        private void OverlayContextMenu_SpeakSource_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null)
            {
                // Always speak the source text (ignoring selection)
                string textToSpeak = textObj.Text;
                
                if (!string.IsNullOrWhiteSpace(textToSpeak))
                {
                    // Use the configured TTS service
                    string ttsService = ConfigManager.Instance.GetTtsService();
                    if (ttsService.Equals("Google Cloud TTS", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = GoogleTTSService.Instance.SpeakText(textToSpeak);
                    }
                    else // Default to ElevenLabs
                    {
                        _ = ElevenLabsService.Instance.SpeakText(textToSpeak);
                    }
                }
            }
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Only process hotkeys at window level if global hotkeys are disabled
            // (When global hotkeys are enabled, the global hook handles them)
            if (!HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                var modifiers = System.Windows.Input.Keyboard.Modifiers;
                bool handled = HotkeyManager.Instance.HandleKeyDown(e.Key, modifiers);
                
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }
        
        private void MonitorWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update scrollbars when window size changes
            UpdateScrollViewerSettings();
        }
        
        // Handle Ctrl+MouseWheel for zooming
        private void MonitorWindow_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Only handle wheel events when Ctrl is pressed
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Determine zoom direction based on wheel delta
                if (e.Delta > 0)
                {
                    // Scroll up = Zoom in
                    currentZoom += zoomIncrement;
                }
                else if (e.Delta < 0)
                {
                    // Scroll down = Zoom out
                    currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                }
                
                // Apply the new zoom level
                ApplyZoom();
                
                // Mark the event as handled to prevent scrolling
                e.Handled = true;
            }
        }
        
        // Update the monitor with a bitmap directly (no file saving required)
        public void UpdateScreenshotFromBitmap(System.Drawing.Bitmap bitmap, bool showWindow = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                // Convert to BitmapSource on the calling thread to avoid UI thread bottleneck
                BitmapSource bitmapSource;
                try
                {
                    // Create a BitmapSource directly from the Bitmap handle - much faster than memory stream
                    IntPtr hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        
                        // Freeze for cross-thread use
                        bitmapSource.Freeze();
                    }
                    finally
                    {
                        // Always delete the HBitmap to prevent memory leaks
                        DeleteObject(hBitmap);
                    }
                    
                    // Use BeginInvoke with high priority for UI update
                    Dispatcher.BeginInvoke(new Action(() => {
                        try
                        {
                            captureImage.Source = bitmapSource;
                            
                            // Show window if needed and requested
                            if (showWindow && !IsVisible)
                            {
                                Show();
                            }
                            
                            // Update scrollbars
                            UpdateScrollViewerSettings();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error in UI thread bitmap update: {ex.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating bitmap source: {ex.Message}");
                }
                return;
            }

            // Direct UI thread path
            try
            {
                // Convert bitmap to BitmapSource - faster than BitmapImage
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    BitmapSource bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    // Set the image source
                    captureImage.Source = bitmapSource;
                }
                finally
                {
                    // Always delete the HBitmap to prevent memory leaks
                    DeleteObject(hBitmap);
                }
                
                // Show the window if not visible and requested
                if (showWindow && !IsVisible)
                {
                    Show();
                }
                
                // Make sure scroll bars appear when needed
                UpdateScrollViewerSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating screenshot from bitmap: {ex.Message}");
                UpdateStatus($"Error: {ex.Message}");
            }
        }
        
        // P/Invoke call needed for proper HBitmap cleanup
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        
        // Update the monitor with a new screenshot from file
        public void UpdateScreenshot(string imagePath)
        {
            if (!File.Exists(imagePath)) 
            {
                UpdateStatus($"File not found: {imagePath}");
                return;
            }
            
            try
            {
                lastImagePath = imagePath;
                
                // Get the absolute file path (fully qualified)
                string fullPath = Path.GetFullPath(imagePath);
                
                // Make a copy of the file to avoid access conflicts
                string tempCopyPath = Path.Combine(Path.GetTempPath(), 
                                                 $"monitor_copy_{Guid.NewGuid()}.png");
                
                // Copy the file to a temporary location
                try
                {
                    File.Copy(fullPath, tempCopyPath, true);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error copying image file: {ex.Message}");
                    // Continue with the original file if copy fails
                    tempCopyPath = fullPath;
                }
                
                // Load the image using a FileStream to avoid URI issues
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                
                // Use a FileStream instead of UriSource
                try
                {
                    using (FileStream stream = new FileStream(tempCopyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Make it thread-safe and more efficient
                    }
                    
                    // Ensure we're on the UI thread when updating the image source
                    if (!Dispatcher.CheckAccess())
                    {
                        Dispatcher.Invoke(() => { captureImage.Source = bitmap; });
                    }
                    else
                    {
                        captureImage.Source = bitmap;
                    }
                    
                    // Clean up temp file if it's not the original
                    if (tempCopyPath != fullPath && File.Exists(tempCopyPath))
                    {
                        try
                        {
                            File.Delete(tempCopyPath);
                        }
                        catch
                        {
                            // Ignore deletion errors - temp files will be cleaned up by the OS eventually
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading image: {ex.Message}");
                    UpdateStatus($"Error loading image: {ex.Message}");
                }
                
                // Clear existing overlay elements
                //textOverlayCanvas.Children.Clear();
                
                // Ensure UI updates happen on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => {
                        // Show the window if not visible
                        if (!IsVisible)
                        {
                            Show();
                            Console.WriteLine("Monitor window shown during screenshot update");
                        }
                        
                        // Make sure scroll bars appear when needed
                        UpdateScrollViewerSettings();
                    });
                }
                else
                {
                    // Show the window if not visible
                    if (!IsVisible)
                    {
                        Show();
                        Console.WriteLine("Monitor window shown during screenshot update");
                    }
                    
                    // Make sure scroll bars appear when needed
                    UpdateScrollViewerSettings();
                }
                
                //UpdateStatus($"Screenshot updated: {Path.GetFileName(imagePath)}");
                //Console.WriteLine($"Monitor window updated with screenshot: {Path.GetFileName(imagePath)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                Console.WriteLine($"Error updating monitor: {ex.Message}");
            }
        }
        
        // Ensure scroll bars appear when needed
        private void UpdateScrollViewerSettings()
        {
            if (captureImage.Source != null)
            {
                // Make sure the image and WebView are sized correctly
                if (captureImage.Source is BitmapSource bitmapSource)
                {
                    // Set the WebView size to match the image
                    textOverlayWebView.Width = bitmapSource.PixelWidth;
                    textOverlayWebView.Height = bitmapSource.PixelHeight;
                    
                    // This ensures the scrollbars will appear when the image is larger
                    // than the available space in the ScrollViewer
                    imageContainer.Width = bitmapSource.PixelWidth;
                    imageContainer.Height = bitmapSource.PixelHeight;
                }
            }
        }
        
        // Handle TextObject added event
        public void CreateMonitorOverlayFromTextObject(object? sender, TextObject textObject)
        {
            try
            {
                using IDisposable profiler = OverlayProfiler.Measure("MonitorWindow.CreateOverlayFromTextObject");
                if (textObject == null)
                {
                    Console.WriteLine("Warning: TextObject is null in CreateMonitorOverlayFromTextObject");
                    return;
                }
                
                // Store original colors if not already stored
                if (!_originalColors.ContainsKey(textObject.ID))
                {
                    _originalColors[textObject.ID] = (
                        textObject.BackgroundColor?.Clone() ?? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)),
                        textObject.TextColor?.Clone() ?? new SolidColorBrush(Colors.White)
                    );
                }
                
                // Trigger WebView update
                UpdateOverlayWebView();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding text to monitor: {ex.Message}");
                UpdateStatus($"Error adding text overlay: {ex.Message}");
            }
        }
        
        private void TextObject_TextCopied(object? sender, EventArgs e)
        {
            UpdateStatus("Text copied to clipboard");
        }

        // Zoom controls
        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom += zoomIncrement;
            ApplyZoom();
        }
        
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
            ApplyZoom();
        }
        
        // View in Browser button handler
        private void ViewInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportToBrowser();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting to browser: {ex.Message}");
                UpdateStatus($"Export failed: {ex.Message}");
            }
        }
        
        // Export current view to browser
        public void ExportToBrowser()
        {
            // Check if we have an image to export
            if (captureImage.Source == null || !(captureImage.Source is BitmapSource bitmapSource))
            {
                UpdateStatus("No image to export");
                return;
            }
            
            // Create temp directory
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            Directory.CreateDirectory(tempDir);
            
            // Generate filenames with timestamp to avoid conflicts
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string imagePath = Path.Combine(tempDir, $"monitor_image_{timestamp}.png");
            string htmlPath = Path.Combine(tempDir, $"monitor_view_{timestamp}.html");
            
            // Save the image with zoom applied
            SaveImageWithZoom(bitmapSource, imagePath);
            
            // Generate HTML
            string html = GenerateHtml(imagePath, bitmapSource.PixelWidth, bitmapSource.PixelHeight);
            
            // Save HTML
            File.WriteAllText(htmlPath, html);
            
            // Open in browser
            Process.Start(new ProcessStartInfo
            {
                FileName = htmlPath,
                UseShellExecute = true
            });
            
            UpdateStatus($"Exported to browser: {Path.GetFileName(htmlPath)}");
        }
        
        private void SaveImageWithZoom(BitmapSource source, string path)
        {
            // Create a new bitmap with zoom applied
            int width = (int)(source.PixelWidth * currentZoom);
            int height = (int)(source.PixelHeight * currentZoom);
            
            // Create a DrawingVisual to render the zoomed image
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.DrawImage(source, new Rect(0, 0, width, height));
            }
            
            // Render to bitmap
            RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(visual);
            
            // Save as PNG
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
            
            using (FileStream stream = new FileStream(path, FileMode.Create))
            {
                encoder.Save(stream);
            }
        }
        
        private string GenerateHtml(string imagePath, int originalWidth, int originalHeight)
        {
            StringBuilder html = new StringBuilder();
            
            // Get relative path for the image
            string imageFileName = Path.GetFileName(imagePath);
            
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"UTF-8\">");
            html.AppendLine("<title>UGTLive Monitor View</title>");
            html.AppendLine("<style>");
            
            // CSS styles
            html.AppendLine("body { margin: 0; padding: 0; background-color: #000; font-family: Arial, sans-serif; display: flex; flex-direction: column; align-items: center; min-height: 100vh; }");
            html.AppendLine(".content-wrapper { display: flex; flex-direction: column; align-items: center; padding-top: 60px; }");
            html.AppendLine(".container { position: relative; display: inline-block; transform: translateZ(0); will-change: auto; }");
            html.AppendLine(".monitor-image { display: block; transform: translateZ(0); will-change: auto; }");
            html.AppendLine(".text-overlay { position: absolute; box-sizing: border-box; overflow: hidden; }");
            html.AppendLine(".text-content { width: 100%; height: 100%; display: flex; align-items: center; justify-content: center; }");
            html.AppendLine(".controls-container { position: fixed; top: 10px; left: 0; right: 0; z-index: 1000; display: flex; justify-content: center; }");
            html.AppendLine(".controls { background-color: #202020; padding: 10px 20px; border-radius: 5px; display: inline-block; }");
            html.AppendLine(".controls button { padding: 10px 20px; font-size: 16px; cursor: pointer; margin-bottom: 10px; }");
            html.AppendLine(".controls label { color: white; margin-right: 15px; cursor: pointer; display: inline-block; }");
            html.AppendLine(".controls input[type='radio'] { margin-right: 5px; cursor: pointer; }");
            html.AppendLine(".footer { background-color: rgba(0,0,0,0.8); color: white; padding: 10px; text-align: center; font-size: 14px; width: 100%; }");
            html.AppendLine(".footer a { color: #00aaff; text-decoration: none; }");
            html.AppendLine(".footer a:hover { text-decoration: underline; }");
            
            // Add font imports if needed
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            // Controls container
            html.AppendLine("<div class=\"controls-container\">");
            html.AppendLine("<div class=\"controls\">");
            // Set radio button checked state based on current overlay mode
            string hideChecked = _currentOverlayMode == OverlayMode.Hide ? " checked" : "";
            string sourceChecked = _currentOverlayMode == OverlayMode.Source ? " checked" : "";
            string translatedChecked = _currentOverlayMode == OverlayMode.Translated ? " checked" : "";
            
            html.AppendLine($"<label><input type=\"radio\" name=\"overlayMode\" value=\"hide\" onchange=\"setOverlayMode('hide')\"{hideChecked}> Hide</label>");
            html.AppendLine($"<label><input type=\"radio\" name=\"overlayMode\" value=\"source\" onchange=\"setOverlayMode('source')\"{sourceChecked}> Source</label>");
            html.AppendLine($"<label><input type=\"radio\" name=\"overlayMode\" value=\"translated\" onchange=\"setOverlayMode('translated')\"{translatedChecked}> Translated</label>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            
            // Content wrapper for centering
            html.AppendLine("<div class=\"content-wrapper\">");
            
            // Container with image
            html.AppendLine("<div class=\"container\">");
            html.AppendLine($"<img class=\"monitor-image\" src=\"{imageFileName}\" width=\"{(int)(originalWidth * currentZoom)}\" height=\"{(int)(originalHeight * currentZoom)}\">");
            
            // Add text overlays from Logic
            var textObjects = Logic.Instance?.GetTextObjects();
            if (textObjects != null)
            {
                foreach (var textObj in textObjects)
                {
                    if (textObj == null) continue;
                    
                    // Get position with zoom applied
                    double left = textObj.X * currentZoom;
                    double top = textObj.Y * currentZoom;
                    
                    // Use TextObject dimensions with zoom
                    double width = textObj.Width > 0 
                        ? textObj.Width * currentZoom 
                        : 200 * currentZoom; // Default fallback width
                        
                    double height = textObj.Height > 0 
                        ? textObj.Height * currentZoom 
                        : 100 * currentZoom; // Default fallback height
                    
                    // Get colors
                    string bgColor = ColorToHex(textObj.BackgroundColor?.Color ?? Colors.Black);
                    string textColor = ColorToHex(textObj.TextColor?.Color ?? Colors.White);
                    
                    // Determine font settings based on translation state
                    bool isTranslated = !string.IsNullOrEmpty(textObj.TextTranslated);
                    string fontFamily = isTranslated
                        ? ConfigManager.Instance.GetTargetLanguageFontFamily()
                        : ConfigManager.Instance.GetSourceLanguageFontFamily();
                    bool isBold = isTranslated
                        ? ConfigManager.Instance.GetTargetLanguageFontBold()
                        : ConfigManager.Instance.GetSourceLanguageFontBold();
                    
                    // Get both source and translated text
                    string sourceText = textObj.Text;
                    string translatedText = textObj.TextTranslated;
                    
                    // Escape both texts for HTML
                    string escapedSourceText = System.Web.HttpUtility.HtmlEncode(sourceText).Replace("\n", "<br>");
                    string escapedTranslatedText = System.Web.HttpUtility.HtmlEncode(translatedText).Replace("\n", "<br>");
                    
                    // Default to source text for initial display (matching the radio button default)
                    string displayText = escapedSourceText;
                    
                    // Calculate font size with zoom
                    double fontSize = 24 * currentZoom; // Default font size with zoom
                    
                    // Generate overlay div with unique ID for text fitting and data attributes for both texts
                    html.AppendLine($"<div class=\"text-overlay\" id=\"overlay-{textObj.ID}\" " +
                        $"data-source-text=\"{System.Web.HttpUtility.HtmlAttributeEncode(sourceText)}\" " +
                        $"data-translated-text=\"{System.Web.HttpUtility.HtmlAttributeEncode(translatedText)}\" " +
                        $"data-original-font-size=\"{fontSize}\" style=\"");
                html.AppendLine($"  left: {left}px;");
                html.AppendLine($"  top: {top}px;");
                html.AppendLine($"  width: {width}px;");
                html.AppendLine($"  height: {height}px;");
                html.AppendLine($"  background-color: {bgColor};");
                html.AppendLine($"  color: {textColor};");
                html.AppendLine($"  font-family: '{fontFamily}', sans-serif;");
                html.AppendLine($"  font-weight: {(isBold ? "bold" : "normal")};");
                html.AppendLine($"  font-size: {fontSize}px;");
                    html.AppendLine($"  padding: 0;");
                    html.AppendLine($"  margin: 0;");
                    html.AppendLine($"  line-height: 1.2;");
                    
                    // Add data attributes for orientation
                    html.AppendLine($"\" data-orientation=\"{textObj.TextOrientation}\">");
                    
                    // Add inner div for text content with orientation support
                    string initialOrientation = textObj.TextOrientation;
                    
                    // Only modify orientation if we're in Translated mode AND showing translated text
                    if (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated) && textObj.TextOrientation == "vertical")
                    {
                        string targetLang = ConfigManager.Instance.GetTargetLanguage().ToLower();
                        if (!IsVerticalSupportedLanguage(targetLang))
                        {
                            initialOrientation = "horizontal";
                        }
                    }
                    
                    html.AppendLine($"<div class=\"text-content\" style=\"");
                    if (initialOrientation == "vertical")
                    {
                        html.AppendLine($"  writing-mode: vertical-rl;");
                        html.AppendLine($"  text-orientation: upright;");
                        html.AppendLine($"  display: flex;");
                        html.AppendLine($"  align-items: center;");
                        html.AppendLine($"  justify-content: center;");
                        html.AppendLine($"  width: 100%;");
                        html.AppendLine($"  height: 100%;");
                    }
                    
                    html.AppendLine($"\">");
                    html.AppendLine($"<span class=\"overlay-content\">{displayText}</span>");
                    html.AppendLine("</div>");
                    html.AppendLine("</div>");
                }
            }
            
            html.AppendLine("</div>"); // End container
            
            // Footer
            html.AppendLine("<div class=\"footer\">");
            html.AppendLine("Generated by UGTLive - ");
            html.AppendLine("<a href=\"https://github.com/SethRobinson/UGTLive\" target=\"_blank\">https://github.com/SethRobinson/UGTLive</a>");
            html.AppendLine("<br><br>");
            html.AppendLine("Studying Japanese and want a nice Chrome/Brave plugin that will explain Kanji?  Grab <a href=\"https://chromewebstore.google.com/detail/10ten-japanese-reader-rik/pnmaklegiibbioifkmfkgpfnmdehdfan\" target=\"_blank\">10ten Japanese Reader</a>.  After installing, click Manage Extensions, Details and set \"Allow access to file URLs\" to on.");
            html.AppendLine("</div>");
            
            html.AppendLine("</div>"); // End content-wrapper
            
            // JavaScript
            html.AppendLine("<script>");
            // Set initial mode based on current overlay mode
            string initialMode = _currentOverlayMode switch
            {
                OverlayMode.Hide => "hide",
                OverlayMode.Translated => "translated",
                _ => "source"
            };
            html.AppendLine($"let currentOverlayMode = '{initialMode}';");
            html.AppendLine("");
            // Add target language info for JavaScript
            string targetLangCode = ConfigManager.Instance.GetTargetLanguage().ToLower();
            bool targetSupportsVertical = IsVerticalSupportedLanguage(targetLangCode);
            html.AppendLine($"const targetSupportsVertical = {(targetSupportsVertical ? "true" : "false")};");
            html.AppendLine("");
            
            html.AppendLine("function setOverlayMode(mode) {");
            html.AppendLine("  currentOverlayMode = mode;");
            html.AppendLine("  const overlays = document.getElementsByClassName('text-overlay');");
            html.AppendLine("  ");
            html.AppendLine("  for (let overlay of overlays) {");
            html.AppendLine("    if (mode === 'hide') {");
            html.AppendLine("      overlay.style.display = 'none';");
            html.AppendLine("    } else {");
            html.AppendLine("      overlay.style.display = 'block';");
            html.AppendLine("      ");
            html.AppendLine("      // Get the appropriate text based on mode");
            html.AppendLine("      const sourceText = overlay.getAttribute('data-source-text') || '';");
            html.AppendLine("      const translatedText = overlay.getAttribute('data-translated-text') || '';");
            html.AppendLine("      const originalOrientation = overlay.getAttribute('data-orientation') || 'horizontal';");
            html.AppendLine("      const contentSpan = overlay.querySelector('.overlay-content');");
            html.AppendLine("      const textContentDiv = overlay.querySelector('.text-content');");
            html.AppendLine("      ");
            html.AppendLine("      if (contentSpan) {");
            html.AppendLine("        if (mode === 'translated' && translatedText) {");
            html.AppendLine("          contentSpan.innerHTML = translatedText.replace(/\\n/g, '<br>');");
            html.AppendLine("        } else {");
            html.AppendLine("          // Default to source for 'source' mode or when translated is empty");
            html.AppendLine("          contentSpan.innerHTML = sourceText.replace(/\\n/g, '<br>');");
            html.AppendLine("        }");
            html.AppendLine("      }");
            html.AppendLine("      ");
            html.AppendLine("      // Handle orientation");
            html.AppendLine("      if (textContentDiv) {");
            html.AppendLine("        if (mode === 'source' || (mode === 'translated' && !translatedText)) {");
            html.AppendLine("          // Show source orientation");
            html.AppendLine("          if (originalOrientation === 'vertical') {");
            html.AppendLine("            textContentDiv.style.writingMode = 'vertical-rl';");
            html.AppendLine("            textContentDiv.style.textOrientation = 'upright';");
            html.AppendLine("          } else {");
            html.AppendLine("            textContentDiv.style.writingMode = 'horizontal-tb';");
            html.AppendLine("            textContentDiv.style.textOrientation = 'mixed';");
            html.AppendLine("          }");
            html.AppendLine("        } else if (mode === 'translated') {");
            html.AppendLine("          // For translated, check if target supports vertical");
            html.AppendLine("          if (originalOrientation === 'vertical' && targetSupportsVertical) {");
            html.AppendLine("            textContentDiv.style.writingMode = 'vertical-rl';");
            html.AppendLine("            textContentDiv.style.textOrientation = 'upright';");
            html.AppendLine("          } else {");
            html.AppendLine("            textContentDiv.style.writingMode = 'horizontal-tb';");
            html.AppendLine("            textContentDiv.style.textOrientation = 'mixed';");
            html.AppendLine("          }");
            html.AppendLine("        }");
            html.AppendLine("      }");
            html.AppendLine("    }");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  // Refit text after changing content");
            html.AppendLine("  if (mode !== 'hide') {");
            html.AppendLine("    setTimeout(fitAllText, 0);");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("// Function to scale text to fit within its container");
            html.AppendLine("function fitTextToContainer(overlay) {");
            html.AppendLine("  try {");
            html.AppendLine("    const originalFontSize = parseFloat(overlay.getAttribute('data-original-font-size')) || parseFloat(getComputedStyle(overlay).fontSize);");
            html.AppendLine("    const containerWidth = overlay.offsetWidth;");
            html.AppendLine("    const containerHeight = overlay.offsetHeight;");
            html.AppendLine("    ");
            html.AppendLine("    if (containerWidth <= 0 || containerHeight <= 0) return;");
            html.AppendLine("    ");
            html.AppendLine("    // Check if this is vertical text");
            html.AppendLine("    const computedStyle = getComputedStyle(overlay);");
            html.AppendLine("    const isVertical = computedStyle.writingMode === 'vertical-rl' || computedStyle.writingMode === 'vertical-lr';");
            html.AppendLine("    ");
            html.AppendLine("    // Temporarily change overflow to auto to measure scroll dimensions");
            html.AppendLine("    const originalOverflow = overlay.style.overflow;");
            html.AppendLine("    overlay.style.overflow = 'auto';");
            html.AppendLine("    ");
            html.AppendLine("    // Check current scroll dimensions");
            html.AppendLine("    const scrollWidth = overlay.scrollWidth;");
            html.AppendLine("    const scrollHeight = overlay.scrollHeight;");
            html.AppendLine("    ");
            html.AppendLine("    // Restore overflow");
            html.AppendLine("    overlay.style.overflow = originalOverflow || 'hidden';");
            html.AppendLine("    ");
            html.AppendLine("    // Check if text already fits");
            html.AppendLine("    const fitsWidth = scrollWidth <= containerWidth;");
            html.AppendLine("    const fitsHeight = scrollHeight <= containerHeight;");
            html.AppendLine("    ");
            html.AppendLine("    if (fitsWidth && fitsHeight) {");
            html.AppendLine("      return; // Text already fits");
            html.AppendLine("    }");
            html.AppendLine("    ");
            html.AppendLine("    // Calculate scale factor needed");
            html.AppendLine("    let scaleFactor = 1;");
            html.AppendLine("    if (!fitsWidth) {");
            html.AppendLine("      scaleFactor = Math.min(scaleFactor, containerWidth / scrollWidth);");
            html.AppendLine("    }");
            html.AppendLine("    if (!fitsHeight) {");
            html.AppendLine("      scaleFactor = Math.min(scaleFactor, containerHeight / scrollHeight);");
            html.AppendLine("    }");
            html.AppendLine("    ");
            html.AppendLine("    // Apply scaled font size (with a small safety margin to prevent edge cases)");
            html.AppendLine("    const newFontSize = Math.max(1, originalFontSize * scaleFactor * 0.98);");
            html.AppendLine("    overlay.style.fontSize = newFontSize + 'px';");
            html.AppendLine("    ");
            html.AppendLine("    // Verify it fits after scaling (iterative refinement if needed)");
            html.AppendLine("    overlay.style.overflow = 'auto';");
            html.AppendLine("    let iterations = 0;");
            html.AppendLine("    while (iterations < 5 && (overlay.scrollWidth > containerWidth || overlay.scrollHeight > containerHeight)) {");
            html.AppendLine("      const currentScale = Math.min(containerWidth / overlay.scrollWidth, containerHeight / overlay.scrollHeight);");
            html.AppendLine("      const currentFontSize = parseFloat(getComputedStyle(overlay).fontSize);");
            html.AppendLine("      overlay.style.fontSize = (currentFontSize * currentScale * 0.98) + 'px';");
            html.AppendLine("      iterations++;");
            html.AppendLine("    }");
            html.AppendLine("    overlay.style.overflow = originalOverflow || 'hidden';");
            html.AppendLine("  } catch (e) {");
            html.AppendLine("    console.error('Error fitting text:', e);");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("// Fit all text overlays on page load");
            html.AppendLine("function fitAllText() {");
            html.AppendLine("  const overlays = document.getElementsByClassName('text-overlay');");
            html.AppendLine("  for (let overlay of overlays) {");
            html.AppendLine("    // Only fit if overlay has dimensions");
            html.AppendLine("    if (overlay.offsetWidth > 0 && overlay.offsetHeight > 0) {");
            html.AppendLine("      fitTextToContainer(overlay);");
            html.AppendLine("    }");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("// Run when everything is loaded");
            html.AppendLine("function initializeTextFitting() {");
            html.AppendLine("  // Wait for image to load first");
            html.AppendLine("  const img = document.querySelector('.monitor-image');");
            html.AppendLine("  if (img && !img.complete) {");
            html.AppendLine("    img.addEventListener('load', function() {");
            html.AppendLine("      setTimeout(function() {");
            html.AppendLine("        // Wait for fonts to load before fitting text");
            html.AppendLine("        if (document.fonts && document.fonts.ready) {");
            html.AppendLine("          document.fonts.ready.then(fitAllText);");
            html.AppendLine("        } else {");
            html.AppendLine("          setTimeout(fitAllText, 200);");
            html.AppendLine("        }");
            html.AppendLine("      }, 100);");
            html.AppendLine("    }, { once: true });");
            html.AppendLine("  } else {");
            html.AppendLine("    setTimeout(function() {");
            html.AppendLine("      if (document.fonts && document.fonts.ready) {");
            html.AppendLine("        document.fonts.ready.then(fitAllText);");
            html.AppendLine("      } else {");
            html.AppendLine("        setTimeout(fitAllText, 200);");
            html.AppendLine("      }");
            html.AppendLine("    }, 100);");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("// Run when DOM is ready");
            html.AppendLine("if (document.readyState === 'loading') {");
            html.AppendLine("  document.addEventListener('DOMContentLoaded', function() {");
            html.AppendLine("    initializeTextFitting();");
            html.AppendLine("    // Apply initial overlay mode");
            html.AppendLine("    setOverlayMode(currentOverlayMode);");
            html.AppendLine("  });");
            html.AppendLine("} else {");
            html.AppendLine("  initializeTextFitting();");
            html.AppendLine("  // Apply initial overlay mode");
            html.AppendLine("  setOverlayMode(currentOverlayMode);");
            html.AppendLine("}");
            html.AppendLine("</script>");
            
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
        
        private string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        // Check if a language supports vertical text
        public static bool IsVerticalSupportedLanguage(string languageCode)
        {
            // Languages that typically support vertical text (CJK languages)
            string[] verticalLanguages = { "ja", "zh", "ko", "zh-cn", "zh-tw", "zh-hk", "ja-jp", "ko-kr" };
            return verticalLanguages.Any(lang => languageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
        }
        
        // Removed ResetZoomButton_Click as it's no longer needed with the TextBox
        
        private void ApplyZoom(System.Windows.Point? mousePosition = null)
        {
            // Store old zoom for scroll calculation
            double oldZoom = imageContainer.LayoutTransform is ScaleTransform oldTransform 
                ? oldTransform.ScaleX 
                : 1.0;

            // Set the transform on the container to scale both image and overlays together
            ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
            imageContainer.LayoutTransform = scaleTransform;
            
            // Make sure scroll bars are correctly shown after zoom change
            UpdateScrollViewerSettings();
            
            // Adjust scroll position to zoom towards mouse cursor if position provided
            if (mousePosition.HasValue && oldZoom > 0)
            {
                try
                {
                    // Get mouse position relative to ScrollViewer
                    System.Windows.Point mouseInScrollViewer = imageScrollViewer.PointFromScreen(mousePosition.Value);
                    
                    // Calculate the content point under the mouse before zoom
                    double contentX = (imageScrollViewer.HorizontalOffset + mouseInScrollViewer.X) / oldZoom;
                    double contentY = (imageScrollViewer.VerticalOffset + mouseInScrollViewer.Y) / oldZoom;
                    
                    // Calculate new scroll offsets to keep the same content point under the mouse
                    double newHorizontalOffset = (contentX * currentZoom) - mouseInScrollViewer.X;
                    double newVerticalOffset = (contentY * currentZoom) - mouseInScrollViewer.Y;
                    
                    // Clamp to valid scroll ranges
                    newHorizontalOffset = Math.Max(0, Math.Min(newHorizontalOffset, imageScrollViewer.ScrollableWidth));
                    newVerticalOffset = Math.Max(0, Math.Min(newVerticalOffset, imageScrollViewer.ScrollableHeight));
                    
                    // Apply the new scroll position
                    imageScrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
                    imageScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error adjusting scroll position during zoom: {ex.Message}");
                }
            }
            
            // Update zoom textbox
            zoomTextBox.Text = ((int)(currentZoom * 100)).ToString();
            UpdateStatus($"Zoom: {(int)(currentZoom * 100)}%");
            Console.WriteLine($"Zoom level changed to {(int)(currentZoom * 100)}%");
            
            // Refresh overlays to apply new zoom factor
            RefreshOverlays();
        }
        
        // Method to refresh text overlays
        public void RefreshOverlays()
        {
            try
            {
                // Check if we need to invoke on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    // Use Invoke with high priority for immediate update
                    Dispatcher.Invoke(new Action(() => RefreshOverlays()), DispatcherPriority.Send);
                    return;
                }
                
                // Skip profiler for faster refresh
                // Trigger WebView update immediately
                UpdateOverlayWebView();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing overlays: {ex.Message}");
            }
        }
        
        public void RemoveOverlay(TextObject textObject)
        {
            if (textObject == null)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RemoveOverlay(textObject));
                return;
            }

            using IDisposable profiler = OverlayProfiler.Measure("MonitorWindow.RemoveOverlay");
            
            _originalColors.Remove(textObject.ID);
            
            // Trigger WebView update
            UpdateOverlayWebView();
        }

        public void ClearOverlays()
        {
            if (!Dispatcher.CheckAccess())
            {
                // Use Invoke with high priority for immediate update
                Dispatcher.Invoke(new Action(() => ClearOverlays()), DispatcherPriority.Send);
                return;
            }

            // Skip profiler for faster clearing
            _originalColors.Clear();
            
            // Clear the HTML cache to force WebView update even if HTML is the same
            _lastOverlayHtml = string.Empty;
            
            // Trigger WebView update immediately
            UpdateOverlayWebView();
        }

        // Update status message
        private void UpdateStatus(string message)
        {
            // Check if we need to invoke on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                // We're on a background thread, marshal the call to the UI thread
                Dispatcher.Invoke(() => UpdateStatus(message));
                return;
            }
            
            // Now we're on the UI thread, safe to update
            if (statusText != null)
                statusText.Text = message;
        }
        
        // Initialize the translation status timer
        private void InitializeTranslationStatusTimer()
        {
            _translationStatusTimer = new DispatcherTimer();
            _translationStatusTimer.Interval = TimeSpan.FromSeconds(1);
            _translationStatusTimer.Tick += TranslationStatusTimer_Tick;
        }
        
        // Update the translation status timer
        private void TranslationStatusTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _translationStartTime;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                translationStatusLabel.Text = $"Waiting for {service}... {elapsed.Minutes:D1}:{elapsed.Seconds:D2}";
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
                    translationStatusLabel.Text = $"Settling...";
                    
                    
                    translationStatusBorder.Visibility = Visibility.Visible;
                });

                    return;
            }
            
            _isShowingSettling = false;


            _translationStartTime = DateTime.Now;
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            
            Dispatcher.Invoke(() =>
            {
                translationStatusLabel.Text = $"Waiting for {service}... 0:00";
                
                
                translationStatusBorder.Visibility = Visibility.Visible;
                
                // Start the timer if not already running
                if (_translationStatusTimer == null)
                {
                    InitializeTranslationStatusTimer();
                }
                
                if (!_translationStatusTimer!.IsEnabled)
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
                translationStatusBorder.Visibility = Visibility.Collapsed;
                
                // Stop the timer
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
            
            translationStatusLabel.Text = $"{ocrMethod} active (fps: {fps:F1})";
            translationStatusBorder.Visibility = Visibility.Visible;
        }
        
        // Hide OCR status display
        public void HideOCRStatusDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => HideOCRStatusDisplay());
                return;
            }
            
            // Only hide if we're showing OCR status (not translation status)
            if (translationStatusLabel.Text.Contains("fps"))
            {
                translationStatusBorder.Visibility = Visibility.Collapsed;
            }
        }
        
        // Override closing to hide instead
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isShuttingDown)
            {
                // Allow closing during shutdown
                Console.WriteLine("Monitor window closing during shutdown");
                return;
            }
            
            e.Cancel = true;  // Cancel the close
            Hide();           // Hide the window instead
            Console.WriteLine("Monitor window closing operation converted to hide");
        }

        // Clean up the low-level hook when window is actually closed
        protected override void OnClosed(EventArgs e)
        {
            // Unhook the low-level mouse hook
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }

            base.OnClosed(e);
        }

        // Low-level mouse hook to intercept mouse events before they reach WebView2
        private IntPtr LowLevelMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                
                // Handle mouse move to detect scrollbar/titlebar regions
                if (msg == WM_MOUSEMOVE)
                {
                    try
                    {
                        // Get the mouse position from the hook structure
                        MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        
                        // Check if the target window is our MonitorWindow or a child of it
                        IntPtr targetWindow = WindowFromPoint(hookStruct.pt);
                        bool isOurWindow = false;
                        
                        if (targetWindow == _monitorWindowHandle)
                        {
                            isOurWindow = true;
                        }
                        else if (targetWindow != IntPtr.Zero)
                        {
                            IntPtr parent = GetParent(targetWindow);
                            while (parent != IntPtr.Zero)
                            {
                                if (parent == _monitorWindowHandle)
                                {
                                    isOurWindow = true;
                                    break;
                                }
                                parent = GetParent(parent);
                            }
                        }
                        
                        if (isOurWindow)
                        {
                            System.Windows.Point screenPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);
                            
                            // Use Dispatcher to check UI elements on UI thread
                            Dispatcher.BeginInvoke(() =>
                            {
                                checkMousePositionAndUpdateHitTesting(screenPoint);
                            }, DispatcherPriority.Normal);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in mouse move hook: {ex.Message}");
                    }
                }
                else if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
                {
                    try
                    {
                        // Check if Ctrl key is pressed
                        short ctrlState = GetKeyState(VK_CONTROL);
                        bool ctrlPressed = (ctrlState & 0x8000) != 0;

                        if (ctrlPressed)
                        {
                            // Get the mouse position from the hook structure
                            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                            System.Windows.Point screenPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);

                            // Check if the target window is our MonitorWindow or a child of it
                            IntPtr targetWindow = WindowFromPoint(hookStruct.pt);
                            bool isOurWindow = false;

                            // Check if it's our window or a child of our window
                            if (targetWindow == _monitorWindowHandle)
                            {
                                isOurWindow = true;
                            }
                            else if (targetWindow != IntPtr.Zero)
                            {
                                // Check if it's a child window of our window
                                IntPtr parent = GetParent(targetWindow);
                                while (parent != IntPtr.Zero)
                                {
                                    if (parent == _monitorWindowHandle)
                                    {
                                        isOurWindow = true;
                                        break;
                                    }
                                    parent = GetParent(parent);
                                }
                            }

                            if (isOurWindow)
                            {
                                // Capture delta before async call (lParam may become invalid)
                                int delta = unchecked((short)(hookStruct.mouseData >> 16));

                                // Check if mouse is over the ScrollViewer area
                                // Use Dispatcher to ensure we're on UI thread for UI element access
                                Dispatcher.BeginInvoke(() =>
                                {
                                    try
                                    {
                                        System.Windows.Point scrollViewerPoint = imageScrollViewer.PointFromScreen(screenPoint);

                                        if (scrollViewerPoint.X >= 0 && scrollViewerPoint.Y >= 0 &&
                                            scrollViewerPoint.X <= imageScrollViewer.ActualWidth &&
                                            scrollViewerPoint.Y <= imageScrollViewer.ActualHeight)
                                        {
                                            // Perform zoom towards mouse cursor
                                            if (delta > 0)
                                            {
                                                currentZoom += zoomIncrement;
                                                ApplyZoom(screenPoint);
                                            }
                                            else
                                            {
                                                currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                                                ApplyZoom(screenPoint);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error in low-level hook zoom: {ex.Message}");
                                    }
                                }, DispatcherPriority.Normal);

                                // Block the message from reaching WebView2
                                return new IntPtr(1);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in LowLevelMouseHookProc: {ex.Message}");
                    }
                }
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }
        
       
        
        // Handle Enter key press in TextBoxes
        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (sender == zoomTextBox)
                {
                    ApplyZoomFromTextBox();
                }
                
                // Remove focus from the TextBox
                System.Windows.Input.Keyboard.ClearFocus();
                
                // Mark the event as handled
                e.Handled = true;
            }
        }
        
        // Handle Overlay Radio Button selection
        private void OverlayRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            // Skip if initializing to prevent saving during load
            if (_isInitializing)
                return;
                
            if (sender is System.Windows.Controls.RadioButton radioButton && radioButton.Tag is string mode)
            {
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
                
                // Save the overlay mode to config
                ConfigManager.Instance.SetMonitorOverlayMode(mode);
                
                // Refresh the overlays with the new mode
                RefreshOverlays();
            }
        }
        
        // Handle TextChanged event for zoom TextBox
        private void ZoomTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only check format, but don't apply yet - will apply on LostFocus
            if (int.TryParse(zoomTextBox.Text, out int value))
            {
                // Valid number
                if (value < 10 || value > 1000)
                {
                    // Out of range - highlight but don't change yet
                    zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
                }
                else
                {
                    // Valid range
                    zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                }
            }
            else if (!string.IsNullOrWhiteSpace(zoomTextBox.Text))
            {
                // Invalid number
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
            }
        }
        
        // Apply zoom value when focus is lost
        private void ZoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyZoomFromTextBox();
        }
        
        // Apply zoom from TextBox value
        private void ApplyZoomFromTextBox()
        {
            if (int.TryParse(zoomTextBox.Text, out int value))
            {
                // Clamp to valid range
                value = Math.Max(10, Math.Min(1000, value));
                
                // Update value and display
                zoomTextBox.Text = value.ToString();
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply zoom
                currentZoom = value / 100.0;
                
                // Set the transform on the container to scale both image and overlays together
                ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
                imageContainer.LayoutTransform = scaleTransform;
                
                // Make sure scroll bars are correctly shown after zoom change
                UpdateScrollViewerSettings();
                
                // Update status
                UpdateStatus($"Zoom: {value}%");
                Console.WriteLine($"Zoom level changed to {value}%");
            }
            else
            {
                // Invalid input, revert to 100%
                zoomTextBox.Text = "100";
                zoomTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64));
                
                // Apply default zoom
                currentZoom = 1.0;
                ScaleTransform scaleTransform = new ScaleTransform(currentZoom, currentZoom);
                imageContainer.LayoutTransform = scaleTransform;
                
                // Update status
                UpdateStatus("Zoom reset to default (100%)");
            }
        }
        
        // Windows message constants
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int VK_CONTROL = 0x11;
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL_LL = 0x020A;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        
        // Win32 API for WDA_EXCLUDEFROMCAPTURE
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        
        // Win32 API for GetSystemMetrics - used for scrollbar and titlebar dimensions
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        
        private const int SM_CXVSCROLL = 2;  // Width of vertical scrollbar
        private const int SM_CYHSCROLL = 3;  // Height of horizontal scrollbar
        private const int SM_CYCAPTION = 4;  // Height of window caption/titlebar

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelMouseProc _mouseHookProc;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _monitorWindowHandle = IntPtr.Zero;

        // Windows message hook to intercept WM_MOUSEWHEEL before WebView2 handles it
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
            {
                try
                {
                    // Check if Ctrl key is pressed using GetKeyState
                    short ctrlState = GetKeyState(VK_CONTROL);
                    bool ctrlPressed = (ctrlState & 0x8000) != 0;
                    
                    // Also check WPF keyboard state as backup
                    if (ctrlPressed || Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        // Get mouse position (screen coordinates) - extract safely
                        int lParamValue = lParam.ToInt32();
                        int x = unchecked((short)(lParamValue & 0xFFFF));
                        int y = unchecked((short)((lParamValue >> 16) & 0xFFFF));
                        
                        // Convert screen coordinates to window coordinates
                        System.Windows.Point screenPoint = new System.Windows.Point(x, y);
                        
                        // Check if mouse is over the ScrollViewer area (not the control panel)
                        // Message hooks run on the UI thread, so we can access UI elements directly
                        System.Windows.Point scrollViewerPoint = imageScrollViewer.PointFromScreen(screenPoint);
                        
                        if (scrollViewerPoint.X >= 0 && scrollViewerPoint.Y >= 0 && 
                            scrollViewerPoint.X <= imageScrollViewer.ActualWidth && 
                            scrollViewerPoint.Y <= imageScrollViewer.ActualHeight)
                        {
                            // Get wheel delta (positive = scroll up, negative = scroll down)
                            // Extract signed 16-bit value from high word of wParam safely
                            int wParamValue = wParam.ToInt32();
                            // Shift right 16 bits and mask to get high word, then cast to short
                            int highWord = (wParamValue >> 16) & 0xFFFF;
                            int delta = unchecked((short)highWord);
                            
                            // Always prevent the message from reaching WebView2 (to prevent font scaling)
                            // and always perform zoom, regardless of what's under the mouse
                            handled = true;
                            
                            // Zoom in or out based on wheel direction, towards mouse cursor
                            if (delta > 0)
                            {
                                // Zoom in
                                currentZoom += zoomIncrement;
                                ApplyZoom(screenPoint);
                            }
                            else
                            {
                                // Zoom out
                                currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                                ApplyZoom(screenPoint);
                            }
                            
                            return IntPtr.Zero;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in WndProc: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            
            return IntPtr.Zero;
        }

        // Check if the mouse is over a WebView2 control
        private bool IsMouseOverWebView2(System.Windows.Point screenPoint)
        {
            try
            {
                // Convert screen point to window-relative point
                System.Windows.Point windowPoint = this.PointFromScreen(screenPoint);
                
                // Use HitTest to find what element is at this point
                HitTestResult hitTestResult = VisualTreeHelper.HitTest(this, windowPoint);
                if (hitTestResult != null && hitTestResult.VisualHit != null)
                {
                    // Walk up the visual tree to find if we're over a WebView2
                    DependencyObject current = hitTestResult.VisualHit;
                    while (current != null)
                    {
                        if (current is WebView2)
                        {
                            return true;
                        }
                        // Also check if it's a Border containing a WebView2
                        if (current is Border border && border.Child is WebView2)
                        {
                            return true;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking WebView2 hit test: {ex.Message}");
            }
            
            return false;
        }

        // Handle mouse wheel zoom when Ctrl is held - at Window level to capture before WebView2
        private void Window_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Check if Ctrl key is pressed
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Get the mouse position relative to the window
                System.Windows.Point mousePos = e.GetPosition(this);
                
                // Check if mouse is over the ScrollViewer area (not the control panel)
                // The control panel is in Grid.Row="0", ScrollViewer is in Grid.Row="1"
                System.Windows.Point scrollViewerPos = e.GetPosition(imageScrollViewer);
                
                // Check if the mouse is actually within the ScrollViewer bounds
                if (scrollViewerPos.X >= 0 && scrollViewerPos.Y >= 0 && 
                    scrollViewerPos.X <= imageScrollViewer.ActualWidth && 
                    scrollViewerPos.Y <= imageScrollViewer.ActualHeight)
                {
                    // Prevent default scrolling/zooming behavior (including WebView2 font scaling)
                    e.Handled = true;
                    
                    // Get mouse position in screen coordinates for zoom centering
                    System.Windows.Point screenPoint = this.PointToScreen(e.GetPosition(this));
                    
                    // Zoom in or out based on wheel direction, towards mouse cursor
                    if (e.Delta > 0)
                    {
                        // Zoom in
                        currentZoom += zoomIncrement;
                        ApplyZoom(screenPoint);
                    }
                    else
                    {
                        // Zoom out
                        currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                        ApplyZoom(screenPoint);
                    }
                }
            }
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error excluding tooltip from capture: {ex.Message}");
            }
        }
       
    }
}