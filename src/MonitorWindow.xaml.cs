using System;
using System.Collections.Generic;
using System.Globalization;
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
        
        // Flag to prevent saving during initialization
        private static bool _isInitializing = true;
        
        public MonitorWindow()
        {
            // Make sure the initialization flag is set before anything else
            _isInitializing = true;
            Console.WriteLine("MonitorWindow constructor: Setting _isInitializing to true");
            
            InitializeComponent();
            
            // Set high-res icon
            IconHelper.SetWindowIcon(this);
            
            Console.WriteLine("MonitorWindow constructor started");
            
            // Subscribe to Play All state changes
            AudioPlaybackManager.Instance.PlayAllStateChanged += AudioPlaybackManager_PlayAllStateChanged;
            // Subscribe to current playing text object changes
            AudioPlaybackManager.Instance.CurrentPlayingTextObjectChanged += AudioPlaybackManager_CurrentPlayingTextObjectChanged;

            PopulateOcrMethodOptions();
            if (ocrMethodComboBox.Items.Count > 0)
            {
                ocrMethodComboBox.SelectedIndex = 0;
            }
            
            // Subscribe to TextObject events from Logic
            //Logic.Instance.TextObjectAdded += CreateMonitorOverlayFromTextObject;
             
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


            // Set default size if not already set
            if (this.Width == 0)
                this.Width = 600;
            if (this.Height == 0)
                this.Height = 500;
                
            // Register application-wide keyboard shortcut handler
            this.PreviewKeyDown += Application_KeyDown;
            this.PreviewKeyUp += Application_KeyUp;
            
            // Subscribe to centralized status updates
            TranslationStatus.StatusChanged += OnStatusChanged;

            Console.WriteLine("MonitorWindow constructor completed");
        }
        
        // Handle status message changes from centralized TranslationStatus
        private void OnStatusChanged(object? sender, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnStatusChanged(sender, message));
                return;
            }
            
            if (statusLabel != null)
            {
                statusLabel.Text = message;
            }
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
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine("MonitorWindow_Loaded: Starting initialization");
            }
            
            // Set initialization flag to true to prevent saving during setup
            _isInitializing = true;
            
            // Make sure keyboard shortcuts work from this window too
            PreviewKeyDown -= Application_KeyDown;
            PreviewKeyDown += Application_KeyDown;
            PreviewKeyUp -= Application_KeyUp;
            PreviewKeyUp += Application_KeyUp;
            
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
                GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName ?? ""), 0);
            
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
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"MonitorWindow_Loaded: Loading OCR method from config: '{ocrMethod}'");
                }
                
                // Temporarily remove the event handler to prevent triggering
                // a new connection while initializing
                ocrMethodComboBox.SelectionChanged -= OcrMethodComboBox_SelectionChanged;
                
                // Find the matching ComboBoxItem by Tag (internal ID)
                bool foundMatch = false;
                foreach (ComboBoxItem comboItem in ocrMethodComboBox.Items)
                {
                    string itemId = comboItem.Tag?.ToString() ?? "";
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Comparing OCR method: '{itemId}' with config value: '{ocrMethod}'");
                    }
                    
                    if (string.Equals(itemId, ocrMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"Found matching OCR method: '{itemId}'");
                        }
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
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"OCR ComboBox is now set to: '{selectedItem.Tag}' (display: '{selectedItem.Content}')");
                    }
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
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"MonitorWindow_Loaded: Loading overlay mode from config: '{overlayMode}'");
                }
                
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
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine("MonitorWindow initialization complete. Settings changes will now be saved.");
                }
                
                // Force the OCR method to match the config again
                // This ensures the config value is preserved and not overwritten
                string configOcrMethod = ConfigManager.Instance.GetOcrMethod();
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Ensuring config OCR method is preserved: {configOcrMethod}");
                }
                
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
            
            // Subscribe to scroll events to sync overlay position with scrolled image
            imageScrollViewer.ScrollChanged += ImageScrollViewer_ScrollChanged;
            
            // Initialize the overlay WebView2
            InitializeOverlayWebView();
            
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine("MonitorWindow initialization complete");
            }
        }
        
        private void MonitorWindow_SourceInitialized(object? sender, EventArgs e)
        {
            WindowCaptureHelper.SetExcludeFromCapture(this, "Monitor");
        }
        
        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            SyncOverlayScrollOffset();
            UpdateWebViewMarginForScrollbars();
        }
        
        private void SyncOverlayScrollOffset()
        {
            if (!_overlayWebViewInitialized || textOverlayWebView?.CoreWebView2 == null)
                return;
            
            try
            {
                double textScale = DisplayHelper.GetWindowsTextScaleFactor();
                double scaleFactor = currentZoom / textScale;

                System.Windows.Point imageOffset = imageContainer.TranslatePoint(new System.Windows.Point(0, 0), imageScrollViewer);
                double offsetX = imageOffset.X / textScale;
                double offsetY = imageOffset.Y / textScale;

                textOverlayWebView.CoreWebView2.ExecuteScriptAsync(
                    $"updateScrollOffset({offsetX:F2}, {offsetY:F2}, {scaleFactor:F4})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing overlay scroll offset: {ex.Message}");
            }
        }
        
        private void UpdateWebViewMarginForScrollbars()
        {
            try
            {
                double rightMargin = imageScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
                    ? _scrollbarWidth : 0;
                double bottomMargin = imageScrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                    ? _scrollbarHeight : 0;
                
                textOverlayWebView.Margin = new Thickness(0, 0, rightMargin, bottomMargin);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating WebView margin for scrollbars: {ex.Message}");
            }
        }
        
        public void UpdateCaptureExclusion()
        {
            WindowCaptureHelper.SetExcludeFromCapture(this, "Monitor");

            if (_overlayWebViewInitialized && textOverlayWebView?.CoreWebView2 != null)
            {
                WindowCaptureHelper.SetWebView2ExcludeFromCapture(textOverlayWebView, "Monitor");
            }
        }
        
        private void initializeScrollbarDimensions()
        {
            // Get scrollbar dimensions from Windows system metrics
            _scrollbarWidth = GetSystemMetrics(SM_CXVSCROLL);
            _scrollbarHeight = GetSystemMetrics(SM_CYHSCROLL);
            _titleBarHeight = GetSystemMetrics(SM_CYCAPTION);
        }
        
        private void MonitorWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update scrollbars when window size changes
            UpdateScrollViewerSettings();
            UpdateWebViewMarginForScrollbars();
        }
        
        // Update the monitor with a bitmap directly (no file saving required)
        public BitmapSource? GetCurrentCaptureBitmapSource()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => GetCurrentCaptureBitmapSource());
            }
            return captureImage.Source as BitmapSource;
        }

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
            }
        }
        
        // P/Invoke call needed for proper HBitmap cleanup
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        
        // Update the monitor with a new screenshot from file
        public void UpdateScreenshot(string imagePath)
        {
            if (!File.Exists(imagePath)) 
            {
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
                }
                
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
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating monitor: {ex.Message}");
            }
        }
        
        // Ensure scroll bars appear when needed
        private void UpdateScrollViewerSettings()
        {
            if (captureImage.Source != null)
            {
                if (captureImage.Source is BitmapSource bitmapSource)
                {
                    // WebView2 is now viewport-sized (Stretch alignment outside ScrollViewer),
                    // so we only size the imageContainer for scrollbar calculation
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
            }
        }
        
        private void TextObject_TextCopied(object? sender, EventArgs e)
        {
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
        
        // Get actual DPI scale factor using Win32 API
        private double GetActualDpiScale()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                    if (monitor != IntPtr.Zero)
                    {
                        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
                        {
                            return dpiX / 96.0;
                        }
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
            }
        }
        
        // Override closing to hide instead
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isShuttingDown || System.Windows.Application.Current?.MainWindow == null)
            {
                // Allow closing during shutdown
                Console.WriteLine("Monitor window closing during shutdown");
                return;
            }
            
            e.Cancel = true;  // Cancel the close
            Hide();           // Hide the window instead
            Console.WriteLine("Monitor window closing operation converted to hide");
            MainWindow.Instance?.UpdateMonitorButtonState(false);
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
                WindowCaptureHelper.ExcludeTooltipFromCapture(fullEnumeration: false);
            }), DispatcherPriority.Background);
        }
       
    }
}
