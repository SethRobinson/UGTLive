using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace UGTLive
{
    public class BatchConvertItem
    {
        public string FilePath { get; set; } = "";
        public bool IsPdf => FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        public int PageCount { get; set; } = 1;
        public string Status { get; set; } = "Pending";
        public string DisplayText
        {
            get
            {
                string name = System.IO.Path.GetFileName(FilePath);
                return IsPdf ? $"{name}  ({PageCount} pages)" : name;
            }
        }
    }

    public class BatchProgressEventArgs : EventArgs
    {
        public int CurrentFileIndex { get; set; }
        public int TotalFiles { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public string StatusText { get; set; } = "";
        public BitmapSource? PreviewImage { get; set; }
        public double OverallProgress { get; set; }
    }

    public class BatchConverterService
    {
        public event EventHandler<BatchProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? LogMessage;

        private Window? _offscreenWindow;
        private WebView2? _offscreenWebView;
        private bool _offscreenReady;

        public async Task<(int succeeded, int failed)> ConvertFilesAsync(
            List<BatchConvertItem> items,
            CancellationToken cancellationToken)
        {
            int succeeded = 0;
            int failed = 0;
            int totalUnits = items.Sum(i => i.IsPdf ? i.PageCount : 1);
            int completedUnits = 0;

            await initOffscreenWebViewAsync();

            for (int fileIdx = 0; fileIdx < items.Count; fileIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[fileIdx];

                try
                {
                    if (item.IsPdf)
                    {
                        reportProgress(fileIdx, items.Count, 0, item.PageCount, $"Opening PDF: {Path.GetFileName(item.FilePath)}", null, completedUnits, totalUnits);
                        await convertPdfAsync(item, fileIdx, items.Count, completedUnits, totalUnits, cancellationToken);
                        completedUnits += item.PageCount;
                    }
                    else
                    {
                        reportProgress(fileIdx, items.Count, 0, 1, $"Processing: {Path.GetFileName(item.FilePath)}", null, completedUnits, totalUnits);
                        await convertImageAsync(item.FilePath, fileIdx, items.Count, completedUnits, totalUnits, cancellationToken);
                        completedUnits += 1;
                    }
                    item.Status = "Done";
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "Cancelled";
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = $"Error: {ex.Message}";
                    failed++;
                    log($"Error processing {Path.GetFileName(item.FilePath)}: {ex.Message}");
                }
            }

            return (succeeded, failed);
        }

        private async Task convertImageAsync(string imagePath, int fileIdx, int totalFiles, int completedUnits, int totalUnits, CancellationToken ct)
        {
            using var bitmap = new Bitmap(imagePath);
            log($"Loaded image: {bitmap.Width}x{bitmap.Height} - {Path.GetFileName(imagePath)}");
            var translated = await processImageAsync(bitmap, ct);

            if (translated == null)
            {
                log($"Skipped {Path.GetFileName(imagePath)} - see above for details.");
                return;
            }

            reportProgress(fileIdx, totalFiles, 0, 1, $"Saving: {Path.GetFileName(imagePath)}", translated, completedUnits, totalUnits);

            string targetLang = ConfigManager.Instance.GetTargetLanguage();
            string dir = Path.GetDirectoryName(imagePath) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(imagePath);
            string ext = Path.GetExtension(imagePath);
            string outPath = Path.Combine(dir, $"{baseName}_converted_{targetLang}{ext}");

            saveBitmapSourceAsPng(translated, outPath);
            log($"Saved: {outPath}");
        }

        private async Task convertPdfAsync(BatchConvertItem item, int fileIdx, int totalFiles, int completedUnits, int totalUnits, CancellationToken ct)
        {
            string targetLang = ConfigManager.Instance.GetTargetLanguage();
            string dir = Path.GetDirectoryName(item.FilePath) ?? ".";
            string baseName = Path.GetFileNameWithoutExtension(item.FilePath);
            string outPath = Path.Combine(dir, $"{baseName}_converted_{targetLang}.pdf");

            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.FilePath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(storageFile);
            item.PageCount = (int)pdfDoc.PageCount;

            var translatedPages = new List<(string tempPath, double widthPt, double heightPt)>();
            string tempDir = Path.Combine(Path.GetTempPath(), "UGTLive_BatchConvert_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);

            try
            {
                for (int page = 0; page < pdfDoc.PageCount; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    reportProgress(fileIdx, totalFiles, page, item.PageCount,
                        $"Processing: {Path.GetFileName(item.FilePath)} - Page {page + 1}/{item.PageCount}",
                        null, completedUnits + page, totalUnits);

                    using var pdfPage = pdfDoc.GetPage((uint)page);
                    double widthPt = pdfPage.Size.Width;
                    double heightPt = pdfPage.Size.Height;

                    uint renderWidth = (uint)(widthPt * 200.0 / 72.0);
                    uint renderHeight = (uint)(heightPt * 200.0 / 72.0);
                    log($"Page {page + 1}: {widthPt:F0}x{heightPt:F0}pt -> {renderWidth}x{renderHeight}px at 200 DPI");

                    using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    var renderOptions = new Windows.Data.Pdf.PdfPageRenderOptions
                    {
                        DestinationWidth = renderWidth,
                        DestinationHeight = renderHeight
                    };
                    await pdfPage.RenderToStreamAsync(stream, renderOptions);
                    stream.Seek(0);

                    using var netStream = stream.AsStreamForRead();
                    using var bitmap = new Bitmap(netStream);

                    var translated = await processImageAsync(bitmap, ct);
                    string tempFile = Path.Combine(tempDir, $"page_{page}.png");

                    if (translated != null)
                    {
                        reportProgress(fileIdx, totalFiles, page, item.PageCount,
                            $"Saving page {page + 1}/{item.PageCount}: {Path.GetFileName(item.FilePath)}",
                            translated, completedUnits + page, totalUnits);
                        saveBitmapSourceAsPng(translated, tempFile);
                    }
                    else
                    {
                        saveBitmapAsPng(bitmap, tempFile);
                    }

                    translatedPages.Add((tempFile, widthPt, heightPt));
                }

                reportProgress(fileIdx, totalFiles, item.PageCount, item.PageCount,
                    $"Assembling PDF: {Path.GetFileName(outPath)}", null, completedUnits + item.PageCount, totalUnits);

                assemblePdf(translatedPages, outPath);
                log($"Saved: {outPath}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private async Task<BitmapSource?> processImageAsync(Bitmap bitmap, CancellationToken ct)
        {
            string ocrJson = await runOcrAsync(bitmap, ct);
            if (string.IsNullOrEmpty(ocrJson))
            {
                log("OCR returned no results.");
                return null;
            }

            var textObjects = OcrResultParser.ParseOcrJsonToTextObjects(ocrJson);
            if (textObjects.Count == 0)
            {
                log("OCR found no text blocks.");
                return null;
            }
            log($"OCR found {textObjects.Count} text block(s).");

            await applyColorDetectionAsync(bitmap, textObjects);

            string? translatedJson = await runTranslationAsync(textObjects, ct);
            if (string.IsNullOrEmpty(translatedJson))
            {
                log("Translation returned empty response.");
                return null;
            }

            applyTranslation(textObjects, translatedJson);

            int translatedCount = textObjects.Count(t => !string.IsNullOrEmpty(t.TextTranslated));
            if (translatedCount == 0)
            {
                log("Translation produced no translated text blocks.");
                return null;
            }
            log($"Translated {translatedCount}/{textObjects.Count} text block(s). Rendering overlay...");

            return await renderOverlayAsync(bitmap, textObjects);
        }

        private async Task<string> runOcrAsync(Bitmap bitmap, CancellationToken ct)
        {
            string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            string sourceLanguage = ConfigManager.Instance.GetSourceLanguage();

            if (ocrMethod == "Windows OCR")
            {
                var lines = await WindowsOCRManager.Instance.GetOcrLinesFromBitmapAsync(bitmap, sourceLanguage);
                return WindowsOCRManager.Instance.FormatOcrLinesToJson(lines);
            }
            else if (ocrMethod == "Google Vision")
            {
                var textObjects = await GoogleVisionOCRService.Instance.ProcessImageAsync(bitmap, sourceLanguage);
                return GoogleVisionOCRService.Instance.FormatResultsToJson(textObjects);
            }
            else
            {
                var service = PythonServicesManager.Instance.GetServiceByName(ocrMethod);
                if (service == null)
                    throw new InvalidOperationException($"OCR service '{ocrMethod}' not found. Check Settings.");

                if (!service.IsRunning)
                {
                    bool isRunning = await service.CheckIsRunningAsync();
                    if (!isRunning)
                        throw new InvalidOperationException($"OCR service '{ocrMethod}' is not running. Start it in the GPU Service Console first.");
                }

                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }
                string? result = await Logic.Instance.ProcessImageWithHttpServiceAsync(imageBytes, ocrMethod, sourceLanguage, suppressErrorUI: true);
                if (result == null)
                    throw new InvalidOperationException($"OCR service '{ocrMethod}' returned no result. Check if the service is running and healthy.");
                return result;
            }
        }

        private async Task<string?> runTranslationAsync(List<TextObject> textObjects, CancellationToken ct)
        {
            var textsToTranslate = new List<object>();
            for (int i = 0; i < textObjects.Count; i++)
            {
                var t = textObjects[i];
                textsToTranslate.Add(new
                {
                    id = t.ID,
                    text = t.Text,
                    rect = new { x = t.X, y = t.Y, width = t.Width, height = t.Height }
                });
            }

            string sourceLanguage = ConfigManager.Instance.GetSourceLanguage();
            string targetLanguage = ConfigManager.Instance.GetTargetLanguage();
            string gameInfo = ConfigManager.Instance.GetGameInfo();

            var ocrData = new
            {
                source_language = sourceLanguage,
                target_language = targetLanguage,
                text_blocks = textsToTranslate,
                previous_context = new List<string>(),
                game_info = gameInfo
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string jsonToTranslate = JsonSerializer.Serialize(ocrData, jsonOptions);

            string prompt = ConfigManager.Instance.GetLlmPrompt();
            string sourceLanguageName = Logic.GetLanguageName(sourceLanguage ?? "en");
            string targetLanguageName = Logic.GetLanguageName(targetLanguage);
            prompt = prompt.Replace("{SOURCE_LANG}", sourceLanguageName);
            prompt = prompt.Replace("{TARGET_LANG}", targetLanguageName);

            ITranslationService translationService = TranslationServiceFactory.CreateService();
            return await translationService.TranslateAsync(jsonToTranslate, prompt, ct);
        }

        private void applyTranslation(List<TextObject> textObjects, string translationResponse)
        {
            try
            {
                string currentService = ConfigManager.Instance.GetCurrentTranslationService();
                string? textBlocksJson = extractTextBlocksJson(translationResponse, currentService);
                if (textBlocksJson == null) return;

                using JsonDocument doc = JsonDocument.Parse(textBlocksJson);
                if (!doc.RootElement.TryGetProperty("text_blocks", out JsonElement textBlocksEl) ||
                    textBlocksEl.ValueKind != JsonValueKind.Array)
                    return;

                for (int i = 0; i < textBlocksEl.GetArrayLength(); i++)
                {
                    var block = textBlocksEl[i];
                    if (!block.TryGetProperty("id", out JsonElement idEl)) continue;
                    string id = idEl.GetString() ?? "";
                    if (!block.TryGetProperty("text", out JsonElement textEl)) continue;
                    string translatedText = textEl.GetString() ?? "";

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(translatedText)) continue;

                    var match = textObjects.FirstOrDefault(t => t.ID == id);
                    if (match != null)
                    {
                        match.TextTranslated = translatedText;
                    }
                    else if (id.StartsWith("text_") && int.TryParse(id.Substring(5), out int idx) && idx >= 0 && idx < textObjects.Count)
                    {
                        textObjects[idx].TextTranslated = translatedText;
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Error applying translation: {ex.Message}");
            }
        }

        private string? extractTextBlocksJson(string response, string service)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(response);

                if (service == "Google Translate")
                {
                    if (doc.RootElement.TryGetProperty("translations", out JsonElement translations))
                    {
                        var blocks = new List<object>();
                        for (int i = 0; i < translations.GetArrayLength(); i++)
                        {
                            var t = translations[i];
                            blocks.Add(new
                            {
                                id = t.GetProperty("id").GetString(),
                                text = t.GetProperty("translated_text").GetString()
                            });
                        }
                        return JsonSerializer.Serialize(new { text_blocks = blocks });
                    }
                    return null;
                }

                string? rawText = null;

                if (service == "ChatGPT" || service == "llama.cpp")
                {
                    if (doc.RootElement.TryGetProperty("translated_text", out JsonElement translatedTextEl))
                        rawText = translatedTextEl.GetString();
                }
                else if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) && candidates.GetArrayLength() > 0)
                {
                    rawText = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                }

                if (rawText != null)
                    return extractJsonObject(rawText);
            }
            catch (Exception ex)
            {
                log($"Error extracting text blocks: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Extracts the first complete JSON object from a string using brace matching.
        /// Handles LLM responses that wrap JSON in markdown fences or add extra text.
        /// </summary>
        private string? extractJsonObject(string text)
        {
            int start = text.IndexOf('{');
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (escape) { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return text.Substring(start, i - start + 1); }
            }
            return null;
        }

        private async Task<BitmapSource?> renderOverlayAsync(Bitmap sourceBitmap, List<TextObject> textObjects)
        {
            if (!_offscreenReady || _offscreenWebView?.CoreWebView2 == null || _offscreenWindow == null)
                return null;

            int imgWidth = sourceBitmap.Width;
            int imgHeight = sourceBitmap.Height;

            string? tempImgPath = null;
            string? tempHtmlPath = null;

            try
            {
                double dpiScale = VisualTreeHelper.GetDpi(_offscreenWindow).DpiScaleX;
                if (dpiScale <= 0) dpiScale = 1.0;

                double neededLogicalW = imgWidth / dpiScale;
                double neededLogicalH = imgHeight / dpiScale;

                // The OS restricts offscreen windows to the virtual screen size.
                // If the image is larger, scale content down to fit the viewport.
                double maxLogicalW = SystemParameters.VirtualScreenWidth;
                double maxLogicalH = SystemParameters.VirtualScreenHeight;
                double fitScale = Math.Min(1.0, Math.Min(maxLogicalW / neededLogicalW, maxLogicalH / neededLogicalH));

                _offscreenWindow.Width = neededLogicalW * fitScale;
                _offscreenWindow.Height = neededLogicalH * fitScale;
                await Task.Delay(50);

                double textScale = DisplayHelper.GetWindowsTextScaleFactor();
                // Adjust cssScale so content shrinks proportionally when the window is capped
                double cssScale = (dpiScale * textScale) / fitScale;

                // Write image to a temp file instead of base64 to avoid NavigateToString's ~2MB limit
                string tempDir = Path.Combine(Path.GetTempPath(), "UGTLive_Render");
                Directory.CreateDirectory(tempDir);
                tempImgPath = Path.Combine(tempDir, $"render_{Guid.NewGuid():N}.png");
                sourceBitmap.Save(tempImgPath, ImageFormat.Png);

                string imgFileUri = new Uri(tempImgPath).AbsoluteUri;
                string html = MonitorWindow.GenerateScreenshotHtmlForTextObjects(imgFileUri, imgWidth, imgHeight, cssScale, textObjects);

                tempHtmlPath = Path.Combine(tempDir, $"render_{Guid.NewGuid():N}.html");
                File.WriteAllText(tempHtmlPath, html, Encoding.UTF8);

                var readyTcs = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2WebMessageReceivedEventArgs> handler = null!;
                handler = (s, e) =>
                {
                    if (e.TryGetWebMessageAsString() == "screenshotReady")
                    {
                        _offscreenWebView.CoreWebView2.WebMessageReceived -= handler;
                        readyTcs.TrySetResult(true);
                    }
                };
                _offscreenWebView.CoreWebView2.WebMessageReceived += handler;
                _offscreenWebView.CoreWebView2.Navigate(new Uri(tempHtmlPath).AbsoluteUri);

                var timeout = Task.Delay(10000);
                if (await Task.WhenAny(readyTcs.Task, timeout) == timeout)
                {
                    _offscreenWebView.CoreWebView2.WebMessageReceived -= handler;
                    log("Warning: WebView2 render timed out waiting for screenshotReady signal.");
                }

                await Task.Delay(200);

                BitmapImage overlayBitmap;
                using (var ms = new MemoryStream())
                {
                    await _offscreenWebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                    ms.Position = 0;
                    overlayBitmap = new BitmapImage();
                    overlayBitmap.BeginInit();
                    overlayBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    overlayBitmap.StreamSource = ms;
                    overlayBitmap.EndInit();
                }
                overlayBitmap.Freeze();
                return overlayBitmap;
            }
            catch (Exception ex)
            {
                log($"Render error: {ex.Message}");
                Console.WriteLine($"[BatchConverter] Render stack trace: {ex.StackTrace}");
                return null;
            }
            finally
            {
                try { if (tempImgPath != null) File.Delete(tempImgPath); } catch { }
                try { if (tempHtmlPath != null) File.Delete(tempHtmlPath); } catch { }
            }
        }

        private async Task initOffscreenWebViewAsync()
        {
            if (_offscreenReady) return;

            var environment = await WebViewEnvironmentManager.GetEnvironmentAsync();
            if (environment == null) return;

            _offscreenWindow = new Window
            {
                Width = 1, Height = 1,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Opacity = 0,
                Left = -10000, Top = -10000
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
            }
        }

        public void Cleanup()
        {
            if (_offscreenWebView != null)
            {
                _offscreenWebView.Dispose();
                _offscreenWebView = null;
            }
            if (_offscreenWindow != null)
            {
                _offscreenWindow.Close();
                _offscreenWindow = null;
            }
            _offscreenReady = false;
        }

        private async Task applyColorDetectionAsync(Bitmap bitmap, List<TextObject> textObjects)
        {
            var service = PythonServicesManager.Instance.GetServiceByName("EasyOCR");
            if (service == null || !service.IsRunning)
            {
                bool isRunning = service != null && await service.CheckIsRunningAsync();
                if (!isRunning)
                {
                    log("EasyOCR not running - skipping color detection, using defaults.");
                    return;
                }
            }

            int detected = 0;
            foreach (var textObj in textObjects)
            {
                int cropX = Math.Max(0, (int)textObj.X);
                int cropY = Math.Max(0, (int)textObj.Y);
                int cropW = Math.Min((int)textObj.Width, bitmap.Width - cropX);
                int cropH = Math.Min((int)textObj.Height, bitmap.Height - cropY);

                if (cropW <= 0 || cropH <= 0) continue;

                try
                {
                    using var crop = bitmap.Clone(new Rectangle(cropX, cropY, cropW, cropH), bitmap.PixelFormat);
                    var colorInfo = await Logic.Instance.GetColorAnalysisAsync(crop);

                    if (colorInfo.HasValue)
                    {
                        if (colorInfo.Value.TryGetProperty("foreground_color", out var fgEl) &&
                            fgEl.TryGetProperty("rgb", out var fgRgb) &&
                            fgRgb.ValueKind == System.Text.Json.JsonValueKind.Array && fgRgb.GetArrayLength() >= 3)
                        {
                            byte r = (byte)Math.Clamp(fgRgb[0].GetInt32(), 0, 255);
                            byte g = (byte)Math.Clamp(fgRgb[1].GetInt32(), 0, 255);
                            byte b = (byte)Math.Clamp(fgRgb[2].GetInt32(), 0, 255);
                            textObj.TextColor = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                        }
                        if (colorInfo.Value.TryGetProperty("background_color", out var bgEl) &&
                            bgEl.TryGetProperty("rgb", out var bgRgb) &&
                            bgRgb.ValueKind == System.Text.Json.JsonValueKind.Array && bgRgb.GetArrayLength() >= 3)
                        {
                            byte r = (byte)Math.Clamp(bgRgb[0].GetInt32(), 0, 255);
                            byte g = (byte)Math.Clamp(bgRgb[1].GetInt32(), 0, 255);
                            byte b = (byte)Math.Clamp(bgRgb[2].GetInt32(), 0, 255);
                            textObj.BackgroundColor = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                        }
                        detected++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BatchConverter] Color detection failed for block: {ex.Message}");
                }
            }
            log($"Color detection: {detected}/{textObjects.Count} blocks analyzed.");
        }

        private string bitmapToBase64DataUri(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
        }

        private void saveBitmapSourceAsPng(BitmapSource source, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }

        private void saveBitmapAsPng(Bitmap bitmap, string path)
        {
            bitmap.Save(path, ImageFormat.Png);
        }

        private void assemblePdf(List<(string tempPath, double widthPt, double heightPt)> pages, string outputPath)
        {
            var document = new PdfDocument();

            foreach (var (tempPath, widthPt, heightPt) in pages)
            {
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(widthPt);
                page.Height = XUnit.FromPoint(heightPt);

                using var gfx = XGraphics.FromPdfPage(page);
                using var image = XImage.FromFile(tempPath);
                gfx.DrawImage(image, 0, 0, page.Width, page.Height);
            }

            document.Save(outputPath);
        }

        public static async Task<int> GetPdfPageCountAsync(string filePath)
        {
            try
            {
                var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
                var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(storageFile);
                return (int)pdfDoc.PageCount;
            }
            catch
            {
                return 0;
            }
        }

        private void reportProgress(int fileIdx, int totalFiles, int page, int totalPages, string status, BitmapSource? preview, int completedUnits, int totalUnits)
        {
            double overall = totalUnits > 0 ? (double)completedUnits / totalUnits * 100.0 : 0;
            ProgressChanged?.Invoke(this, new BatchProgressEventArgs
            {
                CurrentFileIndex = fileIdx,
                TotalFiles = totalFiles,
                CurrentPage = page,
                TotalPages = totalPages,
                StatusText = status,
                PreviewImage = preview,
                OverallProgress = overall
            });
        }

        private void log(string message)
        {
            Console.WriteLine($"[BatchConverter] {message}");
            LogMessage?.Invoke(this, message);
        }
    }
}
