using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public partial class MonitorWindow : Window
    {
        private double currentZoom = 1.0;
        private const double zoomIncrement = 0.1;
        private string lastImagePath = string.Empty;
        private readonly Dictionary<string, Border> _overlayElements = new();
        private readonly Dictionary<string, (SolidColorBrush bgColor, SolidColorBrush textColor)> _originalColors = new();
        
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
             
            // Add loaded event handler
            this.Loaded += MonitorWindow_Loaded;
            
            // Add size changed handler to update scrollbars
            this.SizeChanged += MonitorWindow_SizeChanged;
            
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
                ocrMethodComboBox.Items.Add(new ComboBoxItem { Content = method });
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
            
            string? ocrMethod = (ocrMethodComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
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
            else
            {
                // Using EasyOCR, check connection status first
                _ = Task.Run(async () => 
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
                
                // Find the matching ComboBoxItem
                bool foundMatch = false;
                foreach (ComboBoxItem comboItem in ocrMethodComboBox.Items)
                {
                    string itemText = comboItem.Content.ToString() ?? "";
                    Console.WriteLine($"Comparing OCR method: '{itemText}' with config value: '{ocrMethod}'");
                    
                    if (string.Equals(itemText, ocrMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Found matching OCR method: '{itemText}'");
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
                        Console.WriteLine($"  - '{listItem.Content}'");
                    }
                }
                
                // Log what we actually set
                if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    Console.WriteLine($"OCR ComboBox is now set to: '{selectedItem.Content}'");
                }
                
                // Re-attach the event handler
                ocrMethodComboBox.SelectionChanged += OcrMethodComboBox_SelectionChanged;
                
                // Make sure MainWindow has the same OCR method
                if (ocrMethodComboBox.SelectedItem is ComboBoxItem selectedComboItem)
                {
                    string selectedOcrMethod = selectedComboItem.Content.ToString() ?? "";
                    MainWindow.Instance.SetOcrMethod(selectedOcrMethod);
                }
                
                // Get auto-translate state from MainWindow
                bool isTranslateEnabled = MainWindow.Instance.GetTranslateEnabled();
                
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
            
            Console.WriteLine("MonitorWindow initialization complete");
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Forward to the central keyboard shortcuts handler
            KeyboardShortcuts.HandleKeyDown(e);
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
        public void UpdateScreenshotFromBitmap(System.Drawing.Bitmap bitmap)
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
                            
                            // Show window if needed
                            if (!IsVisible)
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
                
                // Show the window if not visible
                if (!IsVisible)
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
                // Make sure the image and canvas are sized correctly
                if (captureImage.Source is BitmapSource bitmapSource)
                {
                    // Set the canvas size to match the image
                    textOverlayCanvas.Width = bitmapSource.PixelWidth;
                    textOverlayCanvas.Height = bitmapSource.PixelHeight;
                    
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
                // Check for null references
                if (textObject == null || textOverlayCanvas == null)
                {
                    Console.WriteLine("Warning: TextObject or Canvas is null in OnTextObjectAdded");
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
                
                // Get original colors
                var (originalBgColor, originalTextColor) = _originalColors[textObject.ID];
                
                // Apply override colors if enabled, otherwise restore originals
                if (ConfigManager.Instance.IsMonitorOverrideBgColorEnabled())
                {
                    Color overrideBgColor = ConfigManager.Instance.GetMonitorOverrideBgColor();
                    textObject.BackgroundColor = new SolidColorBrush(overrideBgColor);
                }
                else
                {
                    // Restore original background color
                    textObject.BackgroundColor = originalBgColor.Clone();
                }
                
                if (ConfigManager.Instance.IsMonitorOverrideFontColorEnabled())
                {
                    Color overrideFontColor = ConfigManager.Instance.GetMonitorOverrideFontColor();
                    textObject.TextColor = new SolidColorBrush(overrideFontColor);
                }
                else
                {
                    // Restore original text color
                    textObject.TextColor = originalTextColor.Clone();
                }
                
                // We need to create a NEW UI element with positioning appropriate for Canvas
                // but we'll use the existing Border and TextBlock references so updates work
                Border? border = textObject.Border;
                if (border == null || border.Child == null)
                {
                    textObject.CreateUIElement(useRelativePosition: false);
                    border = textObject.Border;
                }
                else
                {
                    // Update existing UI element with current colors (override or original)
                    border.Background = textObject.BackgroundColor;
                    
                    // Update text color in TextBlock or WebView
                    if (textObject.TextBlock != null)
                    {
                        textObject.TextBlock.Foreground = textObject.TextColor;
                    }
                    // For WebView, we need to update the content
                    textObject.UpdateUIElement();
                }

                if (border == null)
                {
                    Console.WriteLine("Warning: TextObject.Border is null");
                    return;
                }

                border.Margin = new Thickness(0);

                textObject.TextCopied -= TextObject_TextCopied;
                textObject.TextCopied += TextObject_TextCopied;

                if (!_overlayElements.TryGetValue(textObject.ID, out Border? existingBorder) || existingBorder != border)
                {
                    if (existingBorder != null)
                    {
                        textOverlayCanvas.Children.Remove(existingBorder);
                    }

                    if (!textOverlayCanvas.Children.Contains(border))
                    {
                        textOverlayCanvas.Children.Add(border);
                    }

                    _overlayElements[textObject.ID] = border;
                }

                Canvas.SetLeft(border, textObject.X);
                Canvas.SetTop(border, textObject.Y);
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
        private void ExportToBrowser()
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
            html.AppendLine("body { margin: 0; padding: 0; background-color: #000; font-family: Arial, sans-serif; }");
            html.AppendLine(".container { position: relative; display: inline-block; }");
            html.AppendLine(".monitor-image { display: block; }");
            html.AppendLine(".text-overlay { position: absolute; box-sizing: border-box; overflow: hidden; }");
            html.AppendLine(".controls { position: fixed; top: 10px; right: 10px; z-index: 1000; }");
            html.AppendLine(".controls button { padding: 10px 20px; font-size: 16px; cursor: pointer; }");
            html.AppendLine(".footer { position: fixed; bottom: 0; left: 0; right: 0; background-color: rgba(0,0,0,0.8); color: white; padding: 10px; text-align: center; font-size: 14px; }");
            html.AppendLine(".footer a { color: #00aaff; text-decoration: none; }");
            html.AppendLine(".footer a:hover { text-decoration: underline; }");
            
            // Add font imports if needed
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            // Controls
            html.AppendLine("<div class=\"controls\">");
            html.AppendLine("<button onclick=\"toggleOverlays()\">Hide overlay</button>");
            html.AppendLine("</div>");
            
            // Container with image
            html.AppendLine("<div class=\"container\">");
            html.AppendLine($"<img class=\"monitor-image\" src=\"{imageFileName}\" width=\"{(int)(originalWidth * currentZoom)}\" height=\"{(int)(originalHeight * currentZoom)}\">");
            
            // Add text overlays
            foreach (var kvp in _overlayElements)
            {
                string textObjectId = kvp.Key;
                Border border = kvp.Value;
                
                // Get the corresponding TextObject
                TextObject? textObj = Logic.Instance.GetTextObjects().FirstOrDefault(t => t.ID == textObjectId);
                if (textObj == null) continue;
                
                // Get position with zoom applied
                double left = Canvas.GetLeft(border) * currentZoom;
                double top = Canvas.GetTop(border) * currentZoom;
                double width = border.ActualWidth * currentZoom;
                double height = border.ActualHeight * currentZoom;
                
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
                
                // Get the text content
                string text = isTranslated ? textObj.TextTranslated : textObj.Text;
                string escapedText = System.Web.HttpUtility.HtmlEncode(text).Replace("\n", "<br>");
                
                // Calculate font size with zoom
                double fontSize = 24 * currentZoom; // Default font size with zoom
                if (textObj.TextBlock != null)
                {
                    fontSize = textObj.TextBlock.FontSize * currentZoom;
                }
                
                // Generate overlay div
                html.AppendLine($"<div class=\"text-overlay\" style=\"");
                html.AppendLine($"  left: {left}px;");
                html.AppendLine($"  top: {top}px;");
                html.AppendLine($"  width: {width}px;");
                html.AppendLine($"  height: {height}px;");
                html.AppendLine($"  background-color: {bgColor};");
                html.AppendLine($"  color: {textColor};");
                html.AppendLine($"  font-family: '{fontFamily}', sans-serif;");
                html.AppendLine($"  font-weight: {(isBold ? "bold" : "normal")};");
                html.AppendLine($"  font-size: {fontSize}px;");
                html.AppendLine($"  padding: 10px;");
                html.AppendLine($"  line-height: 1.2;");
                
                // Add vertical text support
                if (textObj.TextOrientation == "vertical")
                {
                    html.AppendLine($"  writing-mode: vertical-rl;");
                    html.AppendLine($"  text-orientation: upright;");
                    html.AppendLine($"  display: flex;");
                    html.AppendLine($"  align-items: center;");
                    html.AppendLine($"  justify-content: center;");
                }
                
                html.AppendLine($"\">");
                html.AppendLine(escapedText);
                html.AppendLine("</div>");
            }
            
            html.AppendLine("</div>"); // End container
            
            // Footer
            html.AppendLine("<div class=\"footer\">");
            html.AppendLine("Generated by UGTLive - Created by Seth A. Robinson | ");
            html.AppendLine("<a href=\"https://github.com/SethRobinson/UGTLive\" target=\"_blank\">https://github.com/SethRobinson/UGTLive</a>");
            html.AppendLine("</div>");
            
            // JavaScript
            html.AppendLine("<script>");
            html.AppendLine("let overlaysVisible = true;");
            html.AppendLine("function toggleOverlays() {");
            html.AppendLine("  overlaysVisible = !overlaysVisible;");
            html.AppendLine("  const overlays = document.getElementsByClassName('text-overlay');");
            html.AppendLine("  const button = document.querySelector('.controls button');");
            html.AppendLine("  for (let overlay of overlays) {");
            html.AppendLine("    overlay.style.display = overlaysVisible ? 'block' : 'none';");
            html.AppendLine("  }");
            html.AppendLine("  button.textContent = overlaysVisible ? 'Hide overlay' : 'Show overlay';");
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
        }
        
        // Method to refresh text overlays
        public void RefreshOverlays()
        {
            try
            {
                // Check if we need to invoke on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    // Use Invoke to ensure we wait for completion
                    Dispatcher.Invoke(() => RefreshOverlays());
                    return;
                }
                
                // Check if canvas is initialized
                if (textOverlayCanvas == null)
                {
                    Console.WriteLine("Warning: textOverlayCanvas is null. Overlay refresh skipped.");
                    return;
                }
                
                // Now we're on the UI thread, safe to update UI elements
                
                var remainingIds = new HashSet<string>(_overlayElements.Keys);

                // Check if Logic is initialized
                if (Logic.Instance == null)
                {
                    Console.WriteLine("Warning: Logic.Instance is null. Cannot refresh text objects.");
                    return;
                }
                
                var textObjects = Logic.Instance.GetTextObjects();
                if (textObjects == null)
                {
                    Console.WriteLine("Warning: Text objects collection is null.");
                    return;
                }
                
                // Re-add all current text objects
                foreach (TextObject textObj in textObjects)
                {
                    if (textObj != null)
                    {
                        // Call our OnTextObjectAdded method to add it to the canvas
                        CreateMonitorOverlayFromTextObject(this, textObj);
                        remainingIds.Remove(textObj.ID);
                    }
                }

                foreach (string id in remainingIds)
                {
                    if (_overlayElements.TryGetValue(id, out Border? border))
                    {
                        textOverlayCanvas.Children.Remove(border);
                        _overlayElements.Remove(id);
                    }
                    // Clean up stored original colors
                    _originalColors.Remove(id);
                }
                
                //UpdateStatus("Text overlays refreshed");
                //Console.WriteLine($"Monitor window refreshed {textOverlayCanvas.Children.Count} text overlays");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing overlays: {ex.Message}");
            }
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
                Dispatcher.Invoke(() =>
                {
                    translationStatusLabel.Text = $"Settling...";
                    translationStatusBorder.Visibility = Visibility.Visible;
                });

                    return;
            }


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
            Dispatcher.Invoke(() =>
            {
                translationStatusBorder.Visibility = Visibility.Collapsed;
                
                // Stop the timer
                if (_translationStatusTimer != null && _translationStatusTimer.IsEnabled)
                {
                    _translationStatusTimer.Stop();
                }
            });
        }
        
        // Override closing to hide instead
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
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

        // Low-level mouse hook to intercept WM_MOUSEWHEEL before it reaches WebView2
        private IntPtr LowLevelMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
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
        
       
    }
}