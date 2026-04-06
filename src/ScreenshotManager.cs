using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace UGTLive
{
    public class ScreenshotManager
    {

        private static ScreenshotManager? _instance;

        public static ScreenshotManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ScreenshotManager();
                }
                return _instance;
            }
        }

        private ScreenshotManager() { }

        private Window? _offscreenWindow;
        private WebView2? _offscreenWebView;
        private bool _offscreenReady;

        public async void SaveScreenshot()
        {
            try
            {
                BitmapSource? source = MonitorWindow.Instance.GetCurrentCaptureBitmapSource();
                if (source == null)
                {
                    ToastOverlayWindow.ShowToast("Screenshot failed: No capture available");
                    return;
                }

                string folder = resolveFolder();
                Directory.CreateDirectory(folder);

                string filenameBase = resolveFilenameBase();
                string screenshotType = ConfigManager.Instance.GetScreenshotType();

                var textObjects = Logic.Instance.GetTextObjects();
                bool hasOverlays = textObjects.Any(t => !string.IsNullOrEmpty(t.TextTranslated));

                List<string> savedFiles = new List<string>();
                string warning = "";

                string sourceLang = ConfigManager.Instance.GetSourceLanguage();
                string targetLang = ConfigManager.Instance.GetTargetLanguage();

                if (screenshotType == "Source" || screenshotType == "Both")
                {
                    string suffix = screenshotType == "Both" ? $"{sourceLang}_" : "";
                    string path = getNextAvailablePath(folder, filenameBase + suffix);
                    saveBitmapAsPng(source, path);
                    savedFiles.Add(Path.GetFileName(path));
                }

                if (screenshotType == "Target" || screenshotType == "Both")
                {
                    string suffix = screenshotType == "Both" ? $"{targetLang}_" : "";
                    string path = getNextAvailablePath(folder, filenameBase + suffix);

                    if (hasOverlays)
                    {
                        BitmapSource? composited = await renderTargetImageAsync(source);
                        saveBitmapAsPng(composited ?? source, path);
                    }
                    else
                    {
                        saveBitmapAsPng(source, path);
                        warning = "\n(Warning: no text overlays exist)";
                    }
                    savedFiles.Add(Path.GetFileName(path));
                }

                string message = "Saved: " + string.Join(" + ", savedFiles)
                    + "\nFolder: " + folder
                    + warning;

                ToastOverlayWindow.ShowToast(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Screenshot save error: {ex.Message}");
                ToastOverlayWindow.ShowToast($"Screenshot failed: {ex.Message}");
            }
        }

        private async Task initOffscreenWebViewAsync()
        {
            if (_offscreenReady)
                return;

            var environment = await WebViewEnvironmentManager.GetEnvironmentAsync();
            if (environment == null)
                return;

            _offscreenWindow = new Window
            {
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Opacity = 0,
                Left = -10000,
                Top = -10000
            };
            _offscreenWindow.Show();

            WindowCaptureHelper.ExcludeFromCaptureByHandle(new WindowInteropHelper(_offscreenWindow).Handle);

            _offscreenWebView = new WebView2();
            _offscreenWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            _offscreenWindow.Content = _offscreenWebView;

            await _offscreenWebView.EnsureCoreWebView2Async(environment);

            if (_offscreenWebView.CoreWebView2 != null)
            {
                _offscreenWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _offscreenWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _offscreenWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _offscreenReady = true;
                Console.WriteLine("[ScreenshotManager] Offscreen WebView2 initialized");
            }
        }

        private async Task<BitmapSource?> renderTargetImageAsync(BitmapSource source)
        {
            await initOffscreenWebViewAsync();

            if (!_offscreenReady || _offscreenWebView?.CoreWebView2 == null || _offscreenWindow == null)
                return source;

            int imgWidth = source.PixelWidth;
            int imgHeight = source.PixelHeight;

            try
            {
                // Resize the offscreen window to match the source image dimensions.
                // WPF sizes are in logical pixels; WebView2 renders at device pixel ratio.
                // To get a 1:1 pixel capture we need the WPF logical size such that
                // logicalSize * dpiScale = imgPixels  →  logicalSize = imgPixels / dpiScale.
                double dpiScale = VisualTreeHelper.GetDpi(_offscreenWindow).DpiScaleX;
                if (dpiScale <= 0) dpiScale = 1.0;
                _offscreenWindow.Width = imgWidth / dpiScale;
                _offscreenWindow.Height = imgHeight / dpiScale;

                // Let the layout propagate
                await Task.Delay(50);

                // Convert source image to base64 data URI
                string base64Image = bitmapSourceToBase64DataUri(source);

                // The WebView2 device pixel ratio = dpiScale * textScale.
                // CSS pixels in the HTML must be divided by this so the content
                // fits exactly into the device-pixel viewport.
                double textScale = DisplayHelper.GetWindowsTextScaleFactor();
                double cssScale = dpiScale * textScale;

                // Generate the self-contained HTML
                string html = MonitorWindow.Instance.GenerateScreenshotHtml(base64Image, imgWidth, imgHeight, cssScale);

                // Set up a TaskCompletionSource to wait for the "screenshotReady" signal from JS
                var readyTcs = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2WebMessageReceivedEventArgs> messageHandler = null!;
                messageHandler = (s, e) =>
                {
                    string msg = e.TryGetWebMessageAsString();
                    if (msg == "screenshotReady")
                    {
                        _offscreenWebView.CoreWebView2.WebMessageReceived -= messageHandler;
                        readyTcs.TrySetResult(true);
                    }
                };
                _offscreenWebView.CoreWebView2.WebMessageReceived += messageHandler;

                // Navigate to the HTML
                _offscreenWebView.CoreWebView2.NavigateToString(html);

                // Wait for screenshotReady or timeout after 5 seconds
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(readyTcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("[ScreenshotManager] Timed out waiting for screenshotReady");
                    _offscreenWebView.CoreWebView2.WebMessageReceived -= messageHandler;
                }

                // Brief extra delay for final paint
                await Task.Delay(100);

                // Capture the WebView2 content
                BitmapImage overlayBitmap;
                using (var ms = new MemoryStream())
                {
                    await _offscreenWebView.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, ms);

                    ms.Position = 0;
                    overlayBitmap = new BitmapImage();
                    overlayBitmap.BeginInit();
                    overlayBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    overlayBitmap.StreamSource = ms;
                    overlayBitmap.EndInit();
                }
                overlayBitmap.Freeze();

                Console.WriteLine($"[ScreenshotManager] Captured overlay: {overlayBitmap.PixelWidth}x{overlayBitmap.PixelHeight}, target: {imgWidth}x{imgHeight}");

                return overlayBitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScreenshotManager] renderTargetImageAsync error: {ex.Message}");
                return source;
            }
        }

        private string bitmapSourceToBase64DataUri(BitmapSource source)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                string base64 = Convert.ToBase64String(ms.ToArray());
                return $"data:image/png;base64,{base64}";
            }
        }

        private string resolveFolder()
        {
            string folder = ConfigManager.Instance.GetScreenshotFolder();
            if (!Path.IsPathRooted(folder))
            {
                folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder);
            }
            return folder;
        }

        private string resolveFilenameBase()
        {
            string pattern = ConfigManager.Instance.GetScreenshotFilename();
            DateTime now = DateTime.Now;
            string dateStr = FormattableString.Invariant($"{now.Year}_{now.Month:D2}_{now.Day:D2}");
            string timeStr = FormattableString.Invariant($"{now.Hour:D2}_{now.Minute:D2}_{now.Second:D2}_{now.Millisecond:D3}");
            return pattern.Replace("{DATE}", dateStr).Replace("{TIME}", timeStr);
        }

        private string getNextAvailablePath(string folder, string filenameBase)
        {
            int counter = 0;
            while (true)
            {
                string filename = $"{filenameBase}{counter}.png";
                string fullPath = Path.Combine(folder, filename);
                if (!File.Exists(fullPath))
                {
                    return fullPath;
                }
                counter++;
            }
        }

        private void saveBitmapAsPng(BitmapSource source, string path)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (FileStream stream = new FileStream(path, FileMode.Create))
            {
                encoder.Save(stream);
            }
        }

    }
}
