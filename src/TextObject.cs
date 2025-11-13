using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Media;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public class TextObject : IDisposable
    {
        // Properties
        public string Text { get; set; }
        public string ID { get; set; } = Guid.NewGuid().ToString();  // Initialize with a unique ID
        public string TextTranslated { get; set; } = string.Empty;  // Initialize with empty string
        public string TextOrientation { get; set; } = "horizontal";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public SolidColorBrush TextColor { get; set; }
        public SolidColorBrush BackgroundColor { get; set; }
        public UIElement? UIElement { get; set; }
        public WebView2? WebView { get; private set; }
        public Border Border { get; private set; } = new Border();

        public event EventHandler? TextCopied;
        private bool _webViewInitialized;
        private string? _pendingWebViewHtml;
        private string? _lastRenderedHtml;
        private string? _currentSelectionText;
        private double _currentFontSize = DefaultFontSize;
        private bool _isDisposed;

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
            double captureY = 0,
            string textOrientation = "horizontal")
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            TextColor = textColor ?? new SolidColorBrush(Colors.White);
            BackgroundColor = backgroundColor ?? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)); // Fully opaque black
            CaptureX = captureX;
            CaptureY = captureY;
            TextOrientation = textOrientation;

            // Initialize sound player if not already initialized
            _soundPlayer ??= new SoundPlayer();

            // Create the UI element that will be added to the overlay
            //UIElement = CreateUIElement();
        }

        // Create a UI element with the current properties
        // Public so it can be used by MonitorWindow
        public UIElement CreateUIElement(bool useRelativePosition = true)
        {
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.CreateUIElement");
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

            CreateWebViewChild();

            Border.ContextMenu = CreateContextMenu();

            // Intercept Ctrl+Mouse wheel to prevent WebView2 from scaling fonts
            // Let the event bubble up to MonitorWindow for zoom handling
            Border.PreviewMouseWheel += Border_PreviewMouseWheel;

            Border.Loaded += async (s, e) =>
            {
                await EnsureWebViewReadyAsync();
                // Update with current overlay mode
                OverlayMode currentMode = MonitorWindow.Instance?.CurrentOverlayMode ?? OverlayMode.Source;
                
                string textToRender;
                bool isTranslated = false;
                string displayOrientation = TextOrientation;
                
                if (currentMode == OverlayMode.Source)
                {
                    textToRender = Text;
                }
                else if (currentMode == OverlayMode.Translated && !string.IsNullOrEmpty(TextTranslated))
                {
                    textToRender = TextTranslated;
                    isTranslated = true;
                    // Check if target supports vertical
                    if (TextOrientation == "vertical")
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
                    textToRender = Text;
                }
                
                await UpdateWebViewContentAsync(textToRender, isTranslated, displayOrientation);
            };

            UIElement = Border;
            return Border;
        }

        private void CreateWebViewChild()
        {
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.CreateWebViewChild");
            WebView2? webView;

            if (WebViewPool.TryRent(out var pooledWebView) && pooledWebView != null)
            {
                webView = pooledWebView;
                OverlayProfiler.Record("TextObject.WebView2RentFromPool", 0);
            }
            else
            {
                Stopwatch constructorStopwatch = Stopwatch.StartNew();
                webView = new WebView2();
                constructorStopwatch.Stop();
                OverlayProfiler.Record("TextObject.WebView2Constructor", constructorStopwatch.ElapsedMilliseconds);
            }

            webView.Margin = new Thickness(0);
            webView.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            webView.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;

            webView.Loaded -= WebView_Loaded;
            webView.Loaded += WebView_Loaded;

            WebView = webView;
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
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.EnsureWebViewReadyAsync");
            if (WebView == null)
            {
                return;
            }

            if (_webViewInitialized && WebView.CoreWebView2 != null)
            {
                return;
            }

            if (WebView.CoreWebView2 != null)
            {
                ConfigureCoreWebView2();
                _webViewInitialized = true;

                if (!string.IsNullOrEmpty(_pendingWebViewHtml))
                {
                    Stopwatch navigateStopwatch = Stopwatch.StartNew();
                    WebView.CoreWebView2.NavigateToString(_pendingWebViewHtml);
                    navigateStopwatch.Stop();
                    OverlayProfiler.Record("TextObject.NavigatePendingHtml", navigateStopwatch.ElapsedMilliseconds);
                    _pendingWebViewHtml = null;
                }

                return;
            }

            try
            {
                CoreWebView2Environment? sharedEnvironment = await WebViewEnvironmentManager.GetEnvironmentAsync();

                Stopwatch ensureStopwatch = Stopwatch.StartNew();
                if (sharedEnvironment != null)
                {
                    await WebView.EnsureCoreWebView2Async(sharedEnvironment);
                }
                else
                {
                    await WebView.EnsureCoreWebView2Async();
                }
                ensureStopwatch.Stop();
                OverlayProfiler.Record("TextObject.EnsureCoreWebView2Async", ensureStopwatch.ElapsedMilliseconds);

                ConfigureCoreWebView2();

                _webViewInitialized = true;

                if (!string.IsNullOrEmpty(_pendingWebViewHtml) && WebView.CoreWebView2 != null)
                {
                    Stopwatch navigateStopwatch = Stopwatch.StartNew();
                    WebView.CoreWebView2.NavigateToString(_pendingWebViewHtml);
                    navigateStopwatch.Stop();
                    OverlayProfiler.Record("TextObject.NavigatePendingHtml", navigateStopwatch.ElapsedMilliseconds);
                    _pendingWebViewHtml = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing WebView2: {ex.Message}");
            }
        }

        private void ConfigureCoreWebView2()
        {
            if (WebView?.CoreWebView2 == null)
            {
                return;
            }

            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            WebView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;
            WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;

            WebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
            WebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
        }

        private async Task UpdateWebViewContentAsync(string? textToRender = null, bool? isTranslated = null, string? overrideOrientation = null)
        {
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.UpdateWebViewContentAsync");
            if (WebView == null)
            {
                return;
            }

            try
            {
                // Use provided text or default
                if (string.IsNullOrEmpty(textToRender))
                {
                    textToRender = !string.IsNullOrEmpty(TextTranslated) ? TextTranslated : Text;
                }
                
                // Determine if translated for font selection
                if (isTranslated == null)
                {
                    isTranslated = !string.IsNullOrEmpty(TextTranslated) && textToRender == TextTranslated;
                }
                
                string normalized = NormalizeContent(textToRender);
                string encoded = EncodeContentForHtml(normalized);
                string textColorCss = BrushToCss(TextColor);
                
                // Get appropriate font settings
                string fontFamily = isTranslated.Value
                    ? ConfigManager.Instance.GetTargetLanguageFontFamily()
                    : ConfigManager.Instance.GetSourceLanguageFontFamily();
                bool isBold = isTranslated.Value
                    ? ConfigManager.Instance.GetTargetLanguageFontBold()
                    : ConfigManager.Instance.GetSourceLanguageFontBold();
                
                // Use override orientation if provided, otherwise use current TextOrientation
                string displayOrientation = !string.IsNullOrEmpty(overrideOrientation) 
                    ? overrideOrientation 
                    : TextOrientation;
                
                string html = BuildWebViewDocument(encoded, textColorCss, _currentFontSize, fontFamily, isBold, displayOrientation);

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

                Stopwatch navigateStopwatch = Stopwatch.StartNew();
                WebView.CoreWebView2.NavigateToString(html);
                navigateStopwatch.Stop();
                OverlayProfiler.Record("TextObject.NavigateToString", navigateStopwatch.ElapsedMilliseconds);
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

        private string BuildWebViewDocument(string encodedContent, string textColorCss, double fontSize, string? fontFamily = null, bool? isBold = null, string? orientation = null)
        {
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.BuildWebViewDocument");
            if (string.IsNullOrWhiteSpace(encodedContent))
            {
                encodedContent = "&nbsp;";
            }

            string fontSizeCss = fontSize.ToString(CultureInfo.InvariantCulture);
            
            // Use provided font settings or defaults
            if (string.IsNullOrEmpty(fontFamily))
            {
                fontFamily = !string.IsNullOrEmpty(TextTranslated) 
                    ? ConfigManager.Instance.GetTargetLanguageFontFamily()
                    : ConfigManager.Instance.GetSourceLanguageFontFamily();
            }
            
            if (isBold == null)
            {
                isBold = !string.IsNullOrEmpty(TextTranslated)
                    ? ConfigManager.Instance.GetTargetLanguageFontBold()
                    : ConfigManager.Instance.GetSourceLanguageFontBold();
            }
            
            // Use provided orientation or default to current TextOrientation
            string effectiveOrientation = orientation ?? TextOrientation;

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
            if (effectiveOrientation == "vertical")
            {
                builder.AppendLine("#container {");
                builder.AppendLine("  position: relative;");
                builder.AppendLine("  width: 100%;");
                builder.AppendLine("  height: 100%;");
                builder.AppendLine("  overflow: hidden;");
                builder.AppendLine("  display: flex;");
                builder.AppendLine("  align-items: center;");
                builder.AppendLine("  justify-content: center;");
                builder.AppendLine("  flex-direction: row;");
                builder.AppendLine("}");
                builder.AppendLine("#content {");
                builder.AppendLine("  writing-mode: vertical-rl;");
                builder.AppendLine("  text-orientation: upright;");
                builder.AppendLine("  white-space: pre-wrap;");
                builder.AppendLine("  position: relative;");
                builder.AppendLine("  width: auto;");
                builder.AppendLine("  height: auto;");
                builder.AppendLine("  max-width: 100%;");
                builder.AppendLine("  max-height: 100%;");
                builder.AppendLine("  flex-shrink: 0;");
            }
            else
            {
                builder.AppendLine("#container {");
                builder.AppendLine("  position: relative;");
                builder.AppendLine("  width: 100%;");
                builder.AppendLine("  height: 100%;");
                builder.AppendLine("  overflow: hidden;");
                builder.AppendLine("}");
                builder.AppendLine("#content {");
                builder.AppendLine("  writing-mode: horizontal-tb;");
                builder.AppendLine("  white-space: normal;");
                builder.AppendLine("  text-align: left;");
                builder.AppendLine("  position: relative;");
                builder.AppendLine("  width: 100%;");
                builder.AppendLine("  height: 100%;");
                builder.AppendLine("  overflow-wrap: break-word;");
            }
            
            // Escape font family names for CSS (handle comma-separated lists)
            string fontFamilyCss = string.Join(", ", fontFamily!.Split(',').Select(f => $"\"{f.Trim()}\""));
            string fontWeightCss = isBold!.Value ? "bold" : "normal";
            
            builder.AppendLine($"  font-family: {fontFamilyCss};");
            builder.AppendLine($"  font-weight: {fontWeightCss};");
            builder.AppendLine($"  font-size: {fontSizeCss}px;");
            builder.AppendLine("  line-height: 1.25;");
            builder.AppendLine("  letter-spacing: 0.08em;");
            builder.AppendLine("  column-gap: 0.12em;");
            builder.AppendLine("  column-fill: auto;");
            builder.AppendLine("  box-sizing: border-box;");
            builder.AppendLine("  padding: 0;");
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
            builder.AppendLine("");
            builder.AppendLine("    // Check if this is vertical text");
            builder.AppendLine($"    const isVertical = {(effectiveOrientation == "vertical" ? "true" : "false")};");
            builder.AppendLine("");
            builder.AppendLine("    const baseFont = parseFloat(content.dataset.baseFontSize || '24');");
            builder.AppendLine("    let minSize = Math.max(8, baseFont * 0.4);");
            builder.AppendLine("    let maxSize = Math.min(220, baseFont * 4);");
            builder.AppendLine("    let bestSize = minSize;");
            builder.AppendLine("    let foundFit = false;");
            builder.AppendLine("");
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
            builder.AppendLine("");
            builder.AppendLine("    if (!foundFit) {");
            builder.AppendLine("        content.style.fontSize = maxSize + 'px';");
            builder.AppendLine("    } else {");
            builder.AppendLine("        content.style.fontSize = bestSize + 'px';");
            builder.AppendLine("        content.dataset.lastComputedFontSize = bestSize.toFixed(2);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("document.addEventListener('contextmenu', function(event) {");
            builder.AppendLine("    try {");
            builder.AppendLine("        const selection = window.getSelection();");
            builder.AppendLine("        const message = {");
            builder.AppendLine("            kind: 'contextmenu',");
            builder.AppendLine("            x: event.clientX,");
            builder.AppendLine("            y: event.clientY,");
            builder.AppendLine("            selection: selection ? selection.toString() : ''");
            builder.AppendLine("        };");
            builder.AppendLine("        if (window.chrome && window.chrome.webview) {");
            builder.AppendLine("            window.chrome.webview.postMessage(JSON.stringify(message));");
            builder.AppendLine("        }");
            builder.AppendLine("        event.preventDefault();");
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
            // Prevent Ctrl+wheel zoom in WebView2 - let MonitorWindow handle zoom instead
            builder.AppendLine("document.addEventListener('wheel', function(event) {");
            builder.AppendLine("    if (event.ctrlKey) {");
            builder.AppendLine("        event.preventDefault();");
            builder.AppendLine("        event.stopPropagation();");
            builder.AppendLine("        return false;");
            builder.AppendLine("    }");
            builder.AppendLine("}, { passive: false, capture: true });");
            builder.AppendLine("");
            builder.AppendLine("// Call fitContent when the page loads");
            builder.AppendLine("window.addEventListener('DOMContentLoaded', function() {");
            builder.AppendLine("    setTimeout(fitContent, 10);");
            builder.AppendLine("});");
            builder.AppendLine("window.addEventListener('load', function() {");
            builder.AppendLine("    setTimeout(fitContent, 50);");
            builder.AppendLine("});");
            builder.AppendLine("</script>");
            builder.AppendLine("</head>");
            builder.AppendLine("<body onload=\"fitContent()\">");
            builder.AppendLine($"<div id=\"container\"><div id=\"content\" data-base-font-size=\"{fontSizeCss}\" data-orientation=\"{effectiveOrientation}\">{encodedContent}</div></div>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");

            return builder.ToString();
        }

        private double CalculateWebViewFontSize(string text, string? orientation = null)
        {
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.CalculateWebViewFontSize");
            try
            {
                if (Height <= 0)
                {
                    return DefaultFontSize;
                }

                // Use provided orientation or fall back to TextOrientation
                string effectiveOrientation = orientation ?? TextOrientation;

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
                // For vertical text, maxSize should be based on width, not height
                double maxSize = effectiveOrientation == "vertical" 
                    ? Math.Min(WebViewMaxFontSize, availableWidth)
                    : Math.Min(WebViewMaxFontSize, availableHeight);
                double bestSize = minSize;

                for (int i = 0; i < 12; i++)
                {
                    double testSize = (minSize + maxSize) / 2.0;
                    if (DoesFontFit(testSize, normalized, availableWidth, availableHeight, effectiveOrientation))
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

        private bool DoesFontFit(double fontSize, string normalizedText, double availableWidth, double availableHeight, string? orientation = null)
        {
            using IDisposable profiler = OverlayProfiler.Measure("TextObject.DoesFontFit");
            // Use provided orientation or fall back to TextOrientation
            string effectiveOrientation = orientation ?? TextOrientation;
            bool isVertical = effectiveOrientation == "vertical";
            
            if (isVertical)
            {
                // For vertical text, swap the logic - height becomes width and width becomes height
                double charWidth = fontSize;
                if (charWidth <= 0)
                {
                    return false;
                }

                int charsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / charWidth));
                if (charsPerRow <= 0)
                {
                    return false;
                }

                string[] segments = normalizedText.Split('\n');
                int rowsNeeded = 0;

                foreach (string segment in segments)
                {
                    int length = Math.Max(1, segment.Length);
                    rowsNeeded += (int)Math.Ceiling(length / (double)charsPerRow);
                }

                if (rowsNeeded <= 0)
                {
                    rowsNeeded = 1;
                }

                double rowHeight = fontSize * WebViewLineHeightFactor;
                double rowGap = fontSize * WebViewColumnGapFactor;
                double totalHeight = rowsNeeded * rowHeight + (rowsNeeded - 1) * rowGap;

                return totalHeight <= availableHeight;
            }
            else
            {
                // Original horizontal text logic
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
                    return;
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(message);
                    JsonElement root = document.RootElement;
                    if (root.TryGetProperty("kind", out JsonElement kindElement) &&
                        kindElement.GetString() == "contextmenu")
                    {
                        double x = root.TryGetProperty("x", out JsonElement xElement) ? xElement.GetDouble() : 0;
                        double y = root.TryGetProperty("y", out JsonElement yElement) ? yElement.GetDouble() : 0;
                        string selection = root.TryGetProperty("selection", out JsonElement selectionElement)
                            ? selectionElement.GetString() ?? string.Empty
                            : string.Empty;

                        ShowContextMenuFromWebView(x, y, selection);
                    }
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"Error parsing WebView message: {jsonEx.Message}");
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error suppressing default WebView2 context menu: {ex.Message}");
            }
        }

        // Intercept Ctrl+Mouse wheel to prevent WebView2 from scaling fonts
        // The event will bubble up to MonitorWindow for zoom handling
        private void Border_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // If Ctrl is pressed, prevent WebView2 from handling the event
            // Let it bubble up to MonitorWindow for zoom handling
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
            }
        }

        // Intercept Ctrl+Mouse wheel directly on WebView2 to prevent font scaling
        private void WebView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // If Ctrl is pressed, prevent WebView2 from handling the event
            // Let it bubble up to MonitorWindow for zoom handling
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
            }
        }

        private void CopyToClipboard()
        {
            try
            {
                // Get text based on current overlay mode
                string textToCopy = GetPrimaryText();
                
                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);
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
        
        private void CopySourceToClipboard()
        {
            CopyToClipboard();
        }

        private async void WebView_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not WebView2 webView || webView != WebView)
            {
                return;
            }

            using IDisposable loadScope = OverlayProfiler.Measure("TextObject.WebViewLoadedHandler");
            await EnsureWebViewReadyAsync();
            await UpdateWebViewContentAsync();
        }

        public void SetFontSize(double fontSize)
        {
            if (fontSize <= 0)
            {
                return;
            }

            double clamped = Math.Max(WebViewMinFontSize, Math.Min(WebViewMaxFontSize, fontSize));
            if (Math.Abs(_currentFontSize - clamped) > 0.1)
            {
                _currentFontSize = clamped;
            }
            _ = UpdateWebViewContentAsync();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            using IDisposable profiler = OverlayProfiler.Measure("TextObject.Dispose");
            try
            {
                if (Border != null)
                {
                    _originalBackgrounds.Remove(Border);
                    Border.PreviewMouseWheel -= Border_PreviewMouseWheel;
                    if (Border.Child == WebView)
                    {
                        Border.Child = null;
                    }
                    Border.ContextMenu = null;
                }

                if (WebView != null)
                {
                    try
                    {
                        WebView.Loaded -= WebView_Loaded;

                        if (WebView.CoreWebView2 != null)
                        {
                            WebView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;
                            WebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error detaching WebView2 events: {ex.Message}");
                    }

                    WebViewPool.Return(WebView);
                    OverlayProfiler.Record("TextObject.WebViewReturnToPool", 0);
                }
            }
            finally
            {
                WebView = null;
                UIElement = null;
                _webViewInitialized = false;
                _pendingWebViewHtml = null;
                _lastRenderedHtml = null;
                _isDisposed = true;
            }
        }

        // Update the UI element with current properties
        public void UpdateUIElement(OverlayMode? overlayMode = null)
        {
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateUIElement(overlayMode));
                return;
            }

            using IDisposable profiler = OverlayProfiler.Measure("TextObject.UpdateUIElement");
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

            // Determine overlay mode
            OverlayMode currentMode = overlayMode ?? 
                (MonitorWindow.Instance != null ? MonitorWindow.Instance.CurrentOverlayMode : OverlayMode.Translated);
                
            string textForRender;
            bool isShowingTranslated = false;
            
            if (currentMode == OverlayMode.Source)
            {
                textForRender = Text;
            }
            else if (currentMode == OverlayMode.Translated && !string.IsNullOrEmpty(TextTranslated))
            {
                textForRender = TextTranslated;
                isShowingTranslated = true;
            }
            else
            {
                // Fall back to source if no translation available
                textForRender = Text;
            }

            // Determine display orientation
            string displayOrientation = TextOrientation; // Always start with original
            
            // Only modify orientation if we're showing translated text
            if (currentMode == OverlayMode.Translated && isShowingTranslated && TextOrientation == "vertical")
            {
                // Check if target language supports vertical
                string targetLang = ConfigManager.Instance.GetTargetLanguage().ToLower();
                if (!IsVerticalSupportedLanguage(targetLang))
                {
                    displayOrientation = "horizontal";
                }
            }

            if (ConfigManager.Instance.IsAutoSizeTextBlocksEnabled())
            {
                double calculatedSize = CalculateWebViewFontSize(textForRender, displayOrientation);
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
            
            // Update WebView with appropriate text and orientation
            _ = UpdateWebViewContentAsync(textForRender, isShowingTranslated, displayOrientation);
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
            
            // Copy menu item (was "Copy Source")
            MenuItem copyMenuItem = new MenuItem();
            copyMenuItem.Header = "Copy";
            copyMenuItem.Click += CopyMenuItem_Click;
            contextMenu.Items.Add(copyMenuItem);
            
            // Copy Translated menu item (only shown when in Source mode)
            MenuItem copyTranslatedMenuItem = new MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += CopyTranslatedMenuItem_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Add a separator
            contextMenu.Items.Add(new Separator());
            
            // Learn menu item
            MenuItem learnMenuItem = new MenuItem();
            learnMenuItem.Header = "Learn";
            learnMenuItem.Click += LearnMenuItem_Click;
            contextMenu.Items.Add(learnMenuItem);
            
            // Speak menu item (was "Speak Source")
            MenuItem speakMenuItem = new MenuItem();
            speakMenuItem.Header = "Speak";
            speakMenuItem.Click += SpeakMenuItem_Click;
            contextMenu.Items.Add(speakMenuItem);
            
            // Update menu item states when context menu is opened
            contextMenu.Opened += (s, e) => {
                // Get current overlay mode from MonitorWindow
                OverlayMode mode = MonitorWindow.Instance?.CurrentOverlayMode ?? OverlayMode.Source;
                
                // Only show "Copy Translated" option when in Source mode
                copyTranslatedMenuItem.Visibility = mode == OverlayMode.Source ? Visibility.Visible : Visibility.Collapsed;
                copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(this.TextTranslated);
            };

            contextMenu.Closed += ContextMenu_Closed;
            
            return contextMenu;
        }
        
        // Click handler for Copy menu item
        private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard();
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
        
        // Click handler for Learn menu item
        private void LearnMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string textToLearn = GetPrimarySourceText();

                if (!string.IsNullOrEmpty(textToLearn))
                {
                    string chatGptPrompt = $"Create a lesson to help me learn about this text and its translation: {textToLearn}";
                    string encodedPrompt = System.Web.HttpUtility.UrlEncode(chatGptPrompt);
                    string chatGptUrl = $"https://chat.openai.com/?q={encodedPrompt}";

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chatGptUrl,
                        UseShellExecute = true
                    });

                    Console.WriteLine($"Opening ChatGPT with text: {textToLearn.Substring(0, Math.Min(50, textToLearn.Length))}...");
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
        
        // Click handler for Speak menu item
        private async void SpeakMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string textToSpeak = GetPrimaryText();

                if (!string.IsNullOrEmpty(textToSpeak))
                {
                    string text = textToSpeak.Trim();
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

        private string GetPrimaryText()
        {
            // Get current overlay mode from MonitorWindow
            OverlayMode mode = MonitorWindow.Instance?.CurrentOverlayMode ?? OverlayMode.Source;
            
            // Check for selection first
            string? selection = _currentSelectionText;
            if (!string.IsNullOrWhiteSpace(selection))
            {
                return selection.Trim();
            }
            
            // Return text based on mode
            if (mode == OverlayMode.Translated && !string.IsNullOrEmpty(TextTranslated))
            {
                return TextTranslated.Trim();
            }
            
            // Default to source text (for Source mode or when translated is not available)
            return string.IsNullOrWhiteSpace(Text) ? string.Empty : Text.Trim();
        }
        
        private string GetPrimarySourceText()
        {
            // For Learn functionality, always use source text
            string? selection = _currentSelectionText;
            if (!string.IsNullOrWhiteSpace(selection))
            {
                return selection.Trim();
            }

            return string.IsNullOrWhiteSpace(Text) ? string.Empty : Text.Trim();
        }

        private void ContextMenu_Closed(object? sender, RoutedEventArgs e)
        {
            _currentSelectionText = null;
        }

        private void ShowContextMenuFromWebView(double clientX, double clientY, string? selection)
        {
            try
            {
                if (Border.ContextMenu == null || WebView == null)
                {
                    return;
                }

                string trimmedSelection = string.IsNullOrWhiteSpace(selection) ? string.Empty : selection.Trim();
                _currentSelectionText = string.IsNullOrEmpty(trimmedSelection) ? null : trimmedSelection;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Border.ContextMenu == null || WebView == null)
                    {
                        return;
                    }

                    try
                    {
                        Point contentPoint = new Point(clientX, clientY);
                        Point relativeToBorder = WebView.TranslatePoint(contentPoint, Border);

                        Console.WriteLine($"Opening custom context menu at ({relativeToBorder.X},{relativeToBorder.Y}) with selection length {(_currentSelectionText?.Length ?? 0)}");

                        var menu = Border.ContextMenu;
                        menu.PlacementTarget = Border;
                        menu.Placement = PlacementMode.RelativePoint;
                        menu.HorizontalOffset = relativeToBorder.X;
                        menu.VerticalOffset = relativeToBorder.Y;

                        if (menu.IsOpen)
                        {
                            menu.IsOpen = false;
                        }

                        menu.IsOpen = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error opening custom context menu: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preparing custom context menu: {ex.Message}");
            }
        }
        
        // Check if a language supports vertical text
        private bool IsVerticalSupportedLanguage(string languageCode)
        {
            // Languages that typically support vertical text (CJK languages)
            string[] verticalLanguages = { "ja", "zh", "ko", "zh-cn", "zh-tw", "zh-hk", "ja-jp", "ko-kr" };
            return verticalLanguages.Any(lang => languageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
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