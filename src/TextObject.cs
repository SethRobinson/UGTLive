using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Media;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Clipboard = System.Windows.Forms.Clipboard;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;

namespace UGTLive
{
    public class TextObject
    {
        // Properties
        public string Text { get; set; }
        public string ID { get; set; } = Guid.NewGuid().ToString();  // Initialize with a unique ID
        public string TextTranslated { get; set; } = string.Empty;  // Initialize with empty string
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public SolidColorBrush TextColor { get; set; }
        public SolidColorBrush BackgroundColor { get; set; }
        public UIElement? UIElement { get; set; }
        public TextBlock? TextBlock { get; private set; }
        public WebView2? WebView { get; private set; }
        public Border Border { get; private set; } = new Border();

        public event EventHandler? TextCopied;

        private readonly bool _useWebViewOverlay;
        private bool _webViewInitialized;
        private string? _pendingWebViewHtml;
        private string? _lastRenderedHtml;
        private double _currentFontSize = DefaultFontSize;

        private const double WebViewMinFontSize = 12;
        private const double WebViewMaxFontSize = 64;
        private const double WebViewLineHeightFactor = 1.25;
        private const double WebViewColumnGapFactor = 0.12; // Relative to font size

        private const double DefaultFontSize = 24;
        
        // Store the original capture position
        public double CaptureX { get; set; }
        public double CaptureY { get; set; }

        // Audio player for click sound
        private static SoundPlayer? _soundPlayer;

        // Constructor with default parameters
        public TextObject(
            string text = "New text added!",
            double x = 0,
            double y = 0,
            double width = 0,
            double height = 0,
            SolidColorBrush? textColor = null,
            SolidColorBrush? backgroundColor = null,
            double captureX = 0,
            double captureY = 0)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            TextColor = textColor ?? new SolidColorBrush(Colors.White);
            BackgroundColor = backgroundColor ?? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)); // Half-transparent black
            CaptureX = captureX;
            CaptureY = captureY;

            // Initialize sound player if not already initialized
            _soundPlayer ??= new SoundPlayer();

            _useWebViewOverlay = ConfigManager.Instance.IsWebViewOverlayEnabled();

            // Create the UI element that will be added to the overlay
            //UIElement = CreateUIElement();
        }

        // Create a UI element with the current properties
        // Public so it can be used by MonitorWindow
        public UIElement CreateUIElement(bool useRelativePosition = true)
        {
            Border = new Border
            {
                Background = BackgroundColor,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };
            
            if (useRelativePosition)
            {
                Border.Margin = new Thickness(X, Y, 0, 0);
            }

            ApplySizeConstraints();

            if (_useWebViewOverlay)
            {
                CreateWebViewChild();
            }
            else
            {
                CreateTextBlockChild();
            }

            Border.MouseLeftButtonDown += Border_MouseLeftButtonDown;
            Border.ContextMenu = CreateContextMenu();

            if (_useWebViewOverlay)
            {
                Border.Loaded += async (s, e) =>
                {
                    await EnsureWebViewReadyAsync();
                    await UpdateWebViewContentAsync();
                };
            }
            else if (TextBlock != null)
            {
                Border.Loaded += (s, e) => AdjustFontSize(Border, TextBlock);
            }

            UIElement = Border;
            return Border;
        }

        private void CreateTextBlockChild()
        {
            WebView = null;
            TextBlock = new TextBlock
            {
                Text = Text,
                Foreground = TextColor,
                FontWeight = FontWeights.Normal,
                FontSize = DefaultFontSize,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                FontStretch = FontStretches.Normal,
                FontFamily = new FontFamily("Noto Sans JP, MS Gothic, Yu Gothic, Microsoft YaHei, Arial Unicode MS, Arial"),
                Margin = new Thickness(0),
                TextTrimming = TextTrimming.None
            };

            if (Width > 0)
            {
                TextBlock.MaxWidth = Width;
                Border.MaxWidth = Width + 20;
            }
            else
            {
                TextBlock.ClearValue(TextBlock.MaxWidthProperty);
                Border.ClearValue(Border.MaxWidthProperty);
            }

            if (Height > 0)
            {
                Border.Height = Height;
            }
            else
            {
                Border.ClearValue(Border.HeightProperty);
            }

            Border.Child = TextBlock;
            _currentFontSize = DefaultFontSize;
        }

        private void CreateWebViewChild()
        {
            TextBlock = null;
            WebView = new WebView2
            {
                Margin = new Thickness(0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            WebView.Loaded += async (s, e) =>
            {
                await EnsureWebViewReadyAsync();
                await UpdateWebViewContentAsync();
            };

            Border.Child = WebView;
        }

        private void ApplySizeConstraints()
        {
            if (Width > 0)
            {
                Border.Width = Width;
                Border.MaxWidth = Width;
            }
            else
            {
                Border.ClearValue(Border.WidthProperty);
                Border.ClearValue(Border.MaxWidthProperty);
            }

            if (Height > 0)
            {
                Border.Height = Height;
                Border.MaxHeight = Height;
            }
            else
            {
                Border.ClearValue(Border.HeightProperty);
                Border.ClearValue(Border.MaxHeightProperty);
            }
        }

        private async Task EnsureWebViewReadyAsync()
        {
            if (!_useWebViewOverlay || WebView == null)
            {
                return;
            }

            if (_webViewInitialized && WebView.CoreWebView2 != null)
            {
                return;
            }

            try
            {
                await WebView.EnsureCoreWebView2Async();

                if (WebView.CoreWebView2 != null)
                {
                    WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);
                    WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                    WebView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;
                    WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;

                    WebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
                    WebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
                }

                _webViewInitialized = true;

                if (!string.IsNullOrEmpty(_pendingWebViewHtml) && WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.NavigateToString(_pendingWebViewHtml);
                    _pendingWebViewHtml = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing WebView2: {ex.Message}");
            }
        }

        private async Task UpdateWebViewContentAsync()
        {
            if (!_useWebViewOverlay || WebView == null)
            {
                return;
            }

            try
            {
                string textToRender = !string.IsNullOrEmpty(TextTranslated) ? TextTranslated : Text;
                string normalized = NormalizeContent(textToRender);
                string encoded = EncodeContentForHtml(normalized);
                string textColorCss = BrushToCss(TextColor);
                string html = BuildWebViewDocument(encoded, textColorCss, _currentFontSize);

                if (!_webViewInitialized || WebView.CoreWebView2 == null)
                {
                    _pendingWebViewHtml = html;
                    _lastRenderedHtml = html;
                    await EnsureWebViewReadyAsync();
                    return;
                }

                if (string.Equals(html, _lastRenderedHtml, StringComparison.Ordinal))
                {
                    return;
                }

                _lastRenderedHtml = html;

                WebView.CoreWebView2.NavigateToString(html);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating WebView2 content: {ex.Message}");
            }
        }

        private static string NormalizeContent(string content)
        {
            string normalized = content?.Replace("\r\n", "\n").Replace("\r", "\n") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "\u00A0"; // Non-breaking space to preserve height
            }

            return normalized;
        }

        private static string EncodeContentForHtml(string normalized)
        {
            return System.Web.HttpUtility.HtmlEncode(normalized).Replace("\n", "<br/>");
        }

        private string BuildWebViewDocument(string encodedContent, string textColorCss, double fontSize)
        {
            if (string.IsNullOrWhiteSpace(encodedContent))
            {
                encodedContent = "&nbsp;";
            }

            string fontSizeCss = fontSize.ToString(CultureInfo.InvariantCulture);

            var builder = new StringBuilder();
            builder.AppendLine("<!DOCTYPE html>");
            builder.AppendLine("<html>");
            builder.AppendLine("<head>");
            builder.AppendLine("<meta charset=\"utf-8\" />");
            builder.AppendLine("<style>");
            builder.AppendLine("html, body {");
            builder.AppendLine("  margin: 0;");
            builder.AppendLine("  padding: 0;");
            builder.AppendLine("  background: transparent;");
            builder.AppendLine("  width: 100%;");
            builder.AppendLine("  height: 100%;");
            builder.AppendLine("  overflow: hidden;");
            builder.AppendLine($"  color: {textColorCss};");
            builder.AppendLine("}");
            builder.AppendLine("#container {");
            builder.AppendLine("  position: relative;");
            builder.AppendLine("  width: 100%;");
            builder.AppendLine("  height: 100%;");
            builder.AppendLine("  overflow: hidden;");
            builder.AppendLine("}");
            builder.AppendLine("#content {");
            builder.AppendLine("  writing-mode: vertical-rl;");
            builder.AppendLine("  text-orientation: upright;");
            builder.AppendLine("  font-family: \"Yu Mincho\", \"Yu Gothic\", \"Noto Serif JP\", \"Noto Sans JP\", \"MS PGothic\", serif;");
            builder.AppendLine($"  font-size: {fontSizeCss}px;");
            builder.AppendLine("  line-height: 1.25;");
            builder.AppendLine("  letter-spacing: 0.08em;");
            builder.AppendLine("  column-gap: 0.12em;");
            builder.AppendLine("  column-fill: auto;");
            builder.AppendLine("  white-space: pre-wrap;");
            builder.AppendLine("  box-sizing: border-box;");
            builder.AppendLine("  padding: 0;");
            builder.AppendLine("  position: absolute;");
            builder.AppendLine("  top: 0;");
            builder.AppendLine("  right: 0;");
            builder.AppendLine("  width: auto;");
            builder.AppendLine("  height: auto;");
            builder.AppendLine("  max-width: 100%;");
            builder.AppendLine("  max-height: 100%;");
            builder.AppendLine($"  color: {textColorCss};");
            builder.AppendLine("}");
            builder.AppendLine("body {");
            builder.AppendLine("  user-select: text;");
            builder.AppendLine("}");
            builder.AppendLine("</style>");
            builder.AppendLine("<script>");
            builder.AppendLine("function fits(container, content) {");
            builder.AppendLine("    return content.scrollWidth <= container.clientWidth + 0.5 && content.scrollHeight <= container.clientHeight + 0.5;");
            builder.AppendLine("}");

            builder.AppendLine("function fitContent() {");
            builder.AppendLine("    const container = document.getElementById('container');");
            builder.AppendLine("    const content = document.getElementById('content');");
            builder.AppendLine("    if (!container || !content) { return; }");

            builder.AppendLine("    const baseFont = parseFloat(content.dataset.baseFontSize || '24');");
            builder.AppendLine("    let minSize = Math.max(8, baseFont * 0.4);");
            builder.AppendLine("    let maxSize = Math.min(220, baseFont * 4);");
            builder.AppendLine("    let bestSize = minSize;");
            builder.AppendLine("    let foundFit = false;");

            builder.AppendLine("    while (maxSize - minSize > 0.25) {");
            builder.AppendLine("        const testSize = (minSize + maxSize) / 2;");
        builder.AppendLine("        content.style.fontSize = testSize + 'px';");
            builder.AppendLine("        if (fits(container, content)) {");
            builder.AppendLine("            bestSize = testSize;");
            builder.AppendLine("            minSize = testSize;");
            builder.AppendLine("            foundFit = true;");
            builder.AppendLine("        } else {");
            builder.AppendLine("            maxSize = testSize;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");

            builder.AppendLine("    if (!foundFit) {");
            builder.AppendLine("        content.style.fontSize = maxSize + 'px';");
            builder.AppendLine("    } else {");
            builder.AppendLine("        content.style.fontSize = bestSize + 'px';");
            builder.AppendLine("        content.dataset.lastComputedFontSize = bestSize.toFixed(2);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("window.addEventListener('pointerup', function(event) {");
            builder.AppendLine("    try {");
            builder.AppendLine("        const selection = window.getSelection();");
            builder.AppendLine("        if (selection && selection.toString().trim().length > 0) {");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine("        if (window.chrome && window.chrome.webview) {");
            builder.AppendLine("            window.chrome.webview.postMessage('copy');");
            builder.AppendLine("        }");
            builder.AppendLine("    } catch (error) {");
            builder.AppendLine("        console.error(error);");
            builder.AppendLine("    }");
            builder.AppendLine("});");
            builder.AppendLine("window.addEventListener('load', function() {");
            builder.AppendLine("    fitContent();");
            builder.AppendLine("    setTimeout(fitContent, 0);");
            builder.AppendLine("});");
            builder.AppendLine("window.addEventListener('resize', fitContent);");
            builder.AppendLine("if (window.ResizeObserver) {");
            builder.AppendLine("    const resizeObserver = new ResizeObserver(fitContent);");
            builder.AppendLine("    resizeObserver.observe(document.body);");
            builder.AppendLine("}");
            builder.AppendLine("if (document.fonts && document.fonts.ready) {");
            builder.AppendLine("    document.fonts.ready.then(fitContent);");
            builder.AppendLine("}");
            builder.AppendLine("</script>");
            builder.AppendLine("</head>");
            builder.AppendLine("<body>");
            builder.AppendLine($"<div id=\"container\"><div id=\"content\" data-base-font-size=\"{fontSizeCss}\">{encodedContent}</div></div>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");

            return builder.ToString();
        }

        private double CalculateWebViewFontSize(string text)
        {
            try
            {
                if (Height <= 0)
                {
                    return DefaultFontSize;
                }

                double availableWidth = Width > 0 ? Width : double.MaxValue;
                double availableHeight = Height;

                if (double.IsInfinity(availableWidth) || double.IsInfinity(availableHeight) ||
                    double.IsNaN(availableWidth) || double.IsNaN(availableHeight) ||
                    availableWidth <= 0 || availableHeight <= 0)
                {
                    return DefaultFontSize;
                }

                string normalized = NormalizeContent(text);
                if (string.IsNullOrWhiteSpace(normalized) || normalized == "\u00A0")
                {
                    return DefaultFontSize;
                }

                double minSize = WebViewMinFontSize;
                double maxSize = Math.Min(WebViewMaxFontSize, availableHeight);
                double bestSize = minSize;

                for (int i = 0; i < 12; i++)
                {
                    double testSize = (minSize + maxSize) / 2.0;
                    if (DoesFontFit(testSize, normalized, availableWidth, availableHeight))
                    {
                        bestSize = testSize;
                        minSize = testSize;
                    }
                    else
                    {
                        maxSize = testSize;
                    }

                    if (Math.Abs(maxSize - minSize) < 0.5)
                    {
                        break;
                    }
                }

                return Math.Max(WebViewMinFontSize, Math.Min(WebViewMaxFontSize, bestSize));
            }
            catch
            {
                return DefaultFontSize;
            }
        }

        private bool DoesFontFit(double fontSize, string normalizedText, double availableWidth, double availableHeight)
        {
            double charHeight = fontSize * WebViewLineHeightFactor;
            if (charHeight <= 0)
            {
                return false;
            }

            int charsPerColumn = Math.Max(1, (int)Math.Floor(availableHeight / charHeight));
            if (charsPerColumn <= 0)
            {
                return false;
            }

            string[] segments = normalizedText.Split('\n');
            int columnsNeeded = 0;

            foreach (string segment in segments)
            {
                int length = Math.Max(1, segment.Length);
                columnsNeeded += (int)Math.Ceiling(length / (double)charsPerColumn);
            }

            if (columnsNeeded <= 0)
            {
                columnsNeeded = 1;
            }

            double columnWidth = fontSize;
            double columnGap = fontSize * WebViewColumnGapFactor;
            double totalWidth = columnsNeeded * columnWidth + (columnsNeeded - 1) * columnGap;

            return totalWidth <= availableWidth;
        }

        private static string BrushToCss(SolidColorBrush brush)
        {
            Color color = brush.Color;
            if (color.A >= 255)
            {
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }

            double alpha = color.A / 255.0;
            return $"rgba({color.R}, {color.G}, {color.B}, {alpha.ToString(CultureInfo.InvariantCulture)})";
        }

        private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                if (string.Equals(message, "copy", StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(CopySourceToClipboard);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling WebView2 message: {ex.Message}");
            }
        }

        private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                e.Handled = true;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Border.ContextMenu != null)
                    {
                        Border.ContextMenu.PlacementTarget = Border;
                        Border.ContextMenu.IsOpen = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing WebView2 context menu: {ex.Message}");
            }
        }

        private void CopySourceToClipboard()
        {
            try
            {
                if (!string.IsNullOrEmpty(Text))
                {
                    Clipboard.SetText(Text);
                    PlayClickSound();

                    if (Border != null)
                    {
                        AnimateBorderOnClick(Border);
                    }

                    TextCopied?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying text to clipboard: {ex.Message}");
            }
        }

        public void SetFontSize(double fontSize)
        {
            if (fontSize <= 0)
            {
                return;
            }

            if (_useWebViewOverlay)
            {
                double clamped = Math.Max(WebViewMinFontSize, Math.Min(WebViewMaxFontSize, fontSize));
                if (Math.Abs(_currentFontSize - clamped) > 0.1)
                {
                    _currentFontSize = clamped;
                }
                _ = UpdateWebViewContentAsync();
            }
            else if (TextBlock != null)
            {
                TextBlock.FontSize = fontSize;
            }
        }

        // Update the UI element with current properties
        public void UpdateUIElement()
        {
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(UpdateUIElement);
                return;
            }

            if (Border == null || Border.Child == null)
            {
                CreateUIElement();
            }

            Border? border = Border;
            if (border == null)
            {
                return;
            }

            ApplySizeConstraints();

            border.Background = BackgroundColor;
            border.Margin = new Thickness(X, Y, 0, 0);

            if (_useWebViewOverlay)
            {
                string textForRender = !string.IsNullOrEmpty(TextTranslated) ? TextTranslated : Text;

                if (ConfigManager.Instance.IsAutoSizeTextBlocksEnabled())
                {
                    double calculatedSize = CalculateWebViewFontSize(textForRender);
                    if (!double.IsNaN(calculatedSize) && Math.Abs(calculatedSize - _currentFontSize) > 0.1)
                    {
                        _currentFontSize = calculatedSize;
                    }
                }

                if (WebView != null)
                {
                    if (Width > 0)
                    {
                        WebView.Width = Width;
                    }
                    else
                    {
                        WebView.ClearValue(FrameworkElement.WidthProperty);
                    }

                    if (Height > 0)
                    {
                        WebView.Height = Height;
                    }
                    else
                    {
                        WebView.ClearValue(FrameworkElement.HeightProperty);
                    }
                }

                _ = UpdateWebViewContentAsync();
                return;
            }

            if (TextBlock == null)
            {
                return;
            }

            TextBlock.Text = !string.IsNullOrEmpty(TextTranslated) ? TextTranslated : Text;
            TextBlock.Foreground = TextColor;

                if (Width > 0)
                {
                    TextBlock.MaxWidth = Width;
                border.MaxWidth = Width + 20;
            }
            else
            {
                TextBlock.ClearValue(TextBlock.MaxWidthProperty);
                border.ClearValue(Border.MaxWidthProperty);
                }

                if (Height > 0)
                {
                border.Height = Height;
            }
            else
            {
                border.ClearValue(Border.HeightProperty);
            }

            TextBlock.FontSize = DefaultFontSize;
            AdjustFontSize(border, TextBlock);
        }

        // Static cache for font size calculations
        private static readonly Dictionary<string, double> _fontSizeCache = new Dictionary<string, double>();
        
        // Adjust font size to fit within the container using binary search
        private void AdjustFontSize(Border border, TextBlock textBlock)
        {
            try
            {
                // Check if auto sizing is enabled
                if (!ConfigManager.Instance.IsAutoSizeTextBlocksEnabled())
                {
                    // Just set default font size and exit
                    textBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }
                
                // Exit early if dimensions aren't set or text is empty
                if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    // Reset to defaults and exit
                    textBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }

                // Basic text settings
                textBlock.TextWrapping = TextWrapping.Wrap;
                textBlock.VerticalAlignment = VerticalAlignment.Center;
                textBlock.TextAlignment = TextAlignment.Left;
                
                // Create a cache key based on text length, width and height
                // Using length instead of full text to increase cache hits for similar-sized texts
                string cacheKey = $"{textBlock.Text.Length}_{Width}_{Height}";
                
                // Check if we have a cached font size for similar dimensions
                if (_fontSizeCache.TryGetValue(cacheKey, out double cachedFontSize))
                {
                    textBlock.FontSize = cachedFontSize;
                    textBlock.LayoutTransform = Transform.Identity;
                    return;
                }
                
                // Binary search for the best font size
                double minSize = 10;
                double maxSize = 48; // Increased from 36 to 48 to allow for larger text
                double currentSize = 24; // Increased from 18 to 24 for better initial size
                int maxIterations = 6; // Reduced from 10 to 6 iterations for performance
                double lastDiff = double.MaxValue;
                
                for (int i = 0; i < maxIterations; i++)
                {
                    textBlock.FontSize = currentSize;
                    textBlock.Measure(new Size(Width * 0.95, Double.PositiveInfinity));
                    
                    double currentDiff = Math.Abs(textBlock.DesiredSize.Height - Height);
                    
                    // Early termination if we're close enough
                    if (currentDiff < 2 || Math.Abs(lastDiff - currentDiff) < 0.5)
                    {
                        break;
                    }
                    
                    lastDiff = currentDiff;
                    
                    // If text is too tall, decrease font size more aggressively
                    if (textBlock.DesiredSize.Height > Height * 0.90)
                    {
                        maxSize = currentSize;
                        currentSize = (minSize + currentSize) / 2;
                    }
                    // If text is too short, increase font size
                    // Using 0.85 for a more balanced fit that prevents overflow
                    else if (textBlock.DesiredSize.Height < Height * 0.85)
                    {
                        minSize = currentSize;
                        currentSize = (currentSize + maxSize) / 2;
                    }
                    // Good enough fit
                    else
                    {
                        break;
                    }
                }
                
                // Verify final size is within min/max range
                double finalSize = Math.Max(minSize, Math.Min(maxSize, currentSize));
                textBlock.FontSize = finalSize;
                textBlock.LayoutTransform = Transform.Identity;
                _currentFontSize = finalSize;
                
                // Cache the result for future use
                if (_fontSizeCache.Count > 100) // Limit cache size
                {
                    _fontSizeCache.Clear(); // Simple strategy: clear all when too many entries
                }
                _fontSizeCache[cacheKey] = finalSize;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AdjustFontSize: {ex.Message}");
                // Reset to defaults in case of any exception
                textBlock.FontSize = 24; // Increased from 18 to 24 for better initial size
                textBlock.LayoutTransform = Transform.Identity;
            }
        }

        // Handle click event
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                // Copy text to clipboard using shared helper
                CopySourceToClipboard();

                e.Handled = true;
            }
        }

        // Play a sound when clicked
        private void PlayClickSound()
        {
            // Set the sound file path
            string soundFile = "audio\\clipboard.wav";

            // Check if file exists
            if (System.IO.File.Exists(soundFile))
            {
                _soundPlayer!.SoundLocation = soundFile;
                _soundPlayer.Play();
            }
        }

        // Store original background color for each border to ensure we restore properly
        private static readonly ConditionalWeakTable<Border, SolidColorBrush> _originalBackgrounds = 
            new ConditionalWeakTable<Border, SolidColorBrush>();
            
        // Create the context menu with Copy Source, Copy Translated, Speak Source, and Learn Source options
        private ContextMenu CreateContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();
            
            // Copy Source menu item
            MenuItem copySourceMenuItem = new MenuItem();
            copySourceMenuItem.Header = "Copy Source";
            copySourceMenuItem.Click += CopySourceMenuItem_Click;
            contextMenu.Items.Add(copySourceMenuItem);
            
            // Copy Translated menu item
            MenuItem copyTranslatedMenuItem = new MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += CopyTranslatedMenuItem_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Add a separator
            contextMenu.Items.Add(new Separator());
            
            // Learn Source menu item
            MenuItem learnSourceMenuItem = new MenuItem();
            learnSourceMenuItem.Header = "Learn Source";
            learnSourceMenuItem.Click += LearnSourceMenuItem_Click;
            contextMenu.Items.Add(learnSourceMenuItem);
            
            // Speak Source menu item
            MenuItem speakSourceMenuItem = new MenuItem();
            speakSourceMenuItem.Header = "Speak Source";
            speakSourceMenuItem.Click += SpeakSourceMenuItem_Click;
            contextMenu.Items.Add(speakSourceMenuItem);
            
            // Update menu item states when context menu is opened
            contextMenu.Opened += (s, e) => {
                copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(this.TextTranslated);
            };
            
            return contextMenu;
        }
        
        // Click handler for Copy Source menu item
        private void CopySourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CopySourceToClipboard();
        }
        
        // Click handler for Copy Translated menu item
        private void CopyTranslatedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(this.TextTranslated))
            {
                Clipboard.SetText(this.TextTranslated);
                PlayClickSound();
            }
        }
        
        // Click handler for Learn Source menu item
        private void LearnSourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(this.Text))
                {
                    // Construct the ChatGPT URL with the selected text and instructions
                    string chatGptPrompt = $"Create a lesson to help me learn about this text and its translation: {this.Text}";
                    string encodedPrompt = System.Web.HttpUtility.UrlEncode(chatGptPrompt);
                    string chatGptUrl = $"https://chat.openai.com/?q={encodedPrompt}";
                    
                    // Open in default browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chatGptUrl,
                        UseShellExecute = true
                    });
                    
                    Console.WriteLine($"Opening ChatGPT with text: {this.Text.Substring(0, Math.Min(50, this.Text.Length))}...");
                }
                else
                {
                    Console.WriteLine("No text available for Learn function");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Learn function: {ex.Message}");
            }
        }
        
        // Click handler for Speak Source menu item
        private async void SpeakSourceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(this.Text))
                {
                    string text = this.Text.Trim();
                    Console.WriteLine($"Speak function called with text: {text.Substring(0, Math.Min(50, text.Length))}...");
                    
                    // Check if TTS is enabled in config
                    if (ConfigManager.Instance.IsTtsEnabled())
                    {
                        string ttsService = ConfigManager.Instance.GetTtsService();
                        
                        try
                        {
                            bool success = false;
                            
                            if (ttsService == "ElevenLabs")
                            {
                                success = await ElevenLabsService.Instance.SpeakText(text);
                            }
                            else if (ttsService == "Google Cloud TTS")
                            {
                                success = await GoogleTTSService.Instance.SpeakText(text);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show($"Text-to-Speech service '{ttsService}' is not supported yet.",
                                    "Unsupported Service", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            
                            if (!success)
                            {
                                System.Windows.MessageBox.Show($"Failed to generate speech using {ttsService}. Please check the API key and settings.",
                                    "Text-to-Speech Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"TTS error: {ex.Message}");
                            System.Windows.MessageBox.Show($"Text-to-Speech error: {ex.Message}", 
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Text-to-Speech is disabled in settings. Please enable it first.",
                            "TTS Disabled", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    Console.WriteLine("No text available for Speak function");
                    System.Windows.MessageBox.Show("No text available to speak.",
                        "No Text Available", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Speak function: {ex.Message}");
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Text-to-Speech Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Animate the border when clicked
        private void AnimateBorderOnClick(Border border)
        {
            try
            {
                // Get or store the original background color
                if (!_originalBackgrounds.TryGetValue(border, out SolidColorBrush? originalBrush))
                {
                    // Only store the original background on first click
                    originalBrush = border.Background.Clone() as SolidColorBrush;
                    if (originalBrush != null)
                    {
                        // Freeze it to ensure it doesn't change
                        originalBrush.Freeze();
                        _originalBackgrounds.Add(border, originalBrush);
                    }
                }

                // Get the color to return to (use stored value, not current which might be mid-animation)
                Color targetColor = originalBrush?.Color ?? ((SolidColorBrush)border.Background).Color;
                
                // Create a new transform for this animation
                var scaleTransform = new ScaleTransform(1.0, 1.0);
                border.RenderTransform = scaleTransform;
                border.RenderTransformOrigin = new Point(0.5, 0.5);

                // Create background color animation with proper completion action
                ColorAnimation colorAnimation = new ColorAnimation()
                {
                    From = Colors.Yellow,
                    To = targetColor,
                    Duration = TimeSpan.FromSeconds(0.3),
                    // Important: Ensure animation completes and doesn't get stuck
                    FillBehavior = FillBehavior.Stop
                };

                // Create scale animation
                DoubleAnimation scaleAnimation = new DoubleAnimation()
                {
                    From = 1.1,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.2),
                    FillBehavior = FillBehavior.Stop
                };

                // Create a new brush for the animation
                SolidColorBrush animationBrush = new SolidColorBrush(Colors.Yellow);
                
                // When the color animation completes, reset to original background
                colorAnimation.Completed += (s, e) => 
                {
                    border.Background = originalBrush?.Clone() ?? new SolidColorBrush(targetColor);
                };

                // Start animations
                border.Background = animationBrush;
                animationBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Animation error: {ex.Message}");
                // In case of error, try to restore the original background
                if (_originalBackgrounds.TryGetValue(border, out SolidColorBrush? originalBrush) && originalBrush != null)
                {
                    border.Background = originalBrush.Clone();
                }
            }
        }
    }
}