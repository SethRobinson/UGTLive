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
    public partial class MonitorWindow
    {
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
            }
        }
        
        private void PlayAllAudioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If currently playing all, stop it
                if (AudioPlaybackManager.Instance.IsPlayingAll())
                {
                    AudioPlaybackManager.Instance.StopCurrentPlayback();
                    return;
                }
                
                var textObjects = Logic.Instance?.GetTextObjects();
                if (textObjects == null || textObjects.Count == 0)
                {
                    // Play no_audio.wav when there's no audio to play
                    string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string noAudioPath = System.IO.Path.Combine(appDirectory, "audio", "no_audio.wav");
                    if (System.IO.File.Exists(noAudioPath))
                    {
                        _ = AudioPlaybackManager.Instance.PlayAudioFileAsync(noAudioPath);
                    }
                    return;
                }
                
                // Determine which audio to play based on overlay mode
                string overlayMode = ConfigManager.Instance.GetMonitorOverlayMode();
                bool useSourceAudio = overlayMode != "Translated";
                
                // Get play order setting
                string playOrder = ConfigManager.Instance.GetTtsPlayOrder();
                
                // Play all audio
                _ = AudioPlaybackManager.Instance.PlayAllAudioAsync(textObjects.ToList(), playOrder, useSourceAudio);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in PlayAllAudioButton_Click: {ex.Message}");
            }
        }
        
        private void AudioPlaybackManager_PlayAllStateChanged(object? sender, bool isPlaying)
        {
            try
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"MonitorWindow: AudioPlaybackManager_PlayAllStateChanged: isPlaying={isPlaying}, updating button");
                }
                if (playAllAudioButton != null)
                {
                    if (isPlaying)
                    {
                        playAllAudioButton.Content = "🔇 Stop";
                        playAllAudioButton.ToolTip = "Stop playing all audio";
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"MonitorWindow: Play All button updated to: 🔇 Stop");
                        }
                    }
                    else
                    {
                        playAllAudioButton.Content = "🔊 All";
                        playAllAudioButton.ToolTip = "Play all audio files in order";
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"MonitorWindow: Play All button updated to: 🔊 All");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("MonitorWindow: playAllAudioButton is null!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Play All button: {ex.Message}");
            }
        }
        
        private void AudioPlaybackManager_CurrentPlayingTextObjectChanged(object? sender, string? textObjectId)
        {
            try
            {
                if (textOverlayWebView?.CoreWebView2 != null)
                {
                    // Update all icons and overlays - set playing one to stop icon with playing class
                    string script = $@"
                        (function() {{
                            const allOverlays = document.querySelectorAll('.text-overlay');
                            allOverlays.forEach(overlay => {{
                                const icon = overlay.querySelector('.audio-icon');
                                if (icon) {{
                                    const overlayId = overlay.id.replace('overlay-', '');
                                    if (overlayId === '{textObjectId ?? ""}') {{
                                        icon.textContent = '⏹️';
                                        icon.classList.remove('loading');
                                        overlay.classList.add('playing');
                                    }} else {{
                                        const isReady = icon.getAttribute('data-is-ready') === 'true';
                                        icon.textContent = isReady ? '{ConfigManager.ICON_SPEAKER_READY}' : '{ConfigManager.ICON_SPEAKER_NOT_READY}';
                                        if (!isReady) icon.classList.add('loading');
                                        else icon.classList.remove('loading');
                                        overlay.classList.remove('playing');
                                    }}
                                }}
                            }});
                        }})();
                    ";
                    textOverlayWebView.CoreWebView2.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating playing icon: {ex.Message}");
            }
        }
        
        // Export current view to browser
        public void ExportToBrowser()
        {
            // Always read overlay mode from config to ensure we have the latest value
            // (especially important if window hasn't been opened yet)
            string overlayMode = ConfigManager.Instance.GetMonitorOverlayMode();
            _currentOverlayMode = overlayMode switch
            {
                "Hide" => OverlayMode.Hide,
                "Source" => OverlayMode.Source,
                "Translated" => OverlayMode.Translated,
                _ => OverlayMode.Translated
            };
            
            // Check if we have an image to export
            if (captureImage.Source == null || !(captureImage.Source is BitmapSource bitmapSource))
            {
                return;
            }
            
            // Create temp/html directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string htmlDir = Path.Combine(appDirectory, "temp", "html");
            Directory.CreateDirectory(htmlDir);
            
            // Generate filenames with timestamp to avoid conflicts
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string imagePath = Path.Combine(htmlDir, $"monitor_image_{timestamp}.png");
            string htmlPath = Path.Combine(htmlDir, $"monitor_view_{timestamp}.html");
            
            // Copy audio files to export directory (same as HTML file)
            CopyAudioFilesForExport(htmlDir);
            
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
        
        private void CopyAudioFilesForExport(string htmlDir)
        {
            try
            {
                var textObjects = Logic.Instance?.GetTextObjects();
                if (textObjects == null || textObjects.Count == 0)
                {
                    return;
                }
                
                // Copy audio files to html directory (same location as HTML file)
                foreach (var textObj in textObjects)
                {
                    if (textObj == null) continue;
                    
                    // Copy source audio if available
                    if (!string.IsNullOrEmpty(textObj.SourceAudioFilePath) && File.Exists(textObj.SourceAudioFilePath))
                    {
                        string fileName = Path.GetFileName(textObj.SourceAudioFilePath);
                        string destPath = Path.Combine(htmlDir, fileName);
                        // Always copy to ensure latest version is available
                        File.Copy(textObj.SourceAudioFilePath, destPath, overwrite: true);
                    }
                    
                    // Copy target audio if available
                    if (!string.IsNullOrEmpty(textObj.TargetAudioFilePath) && File.Exists(textObj.TargetAudioFilePath))
                    {
                        string fileName = Path.GetFileName(textObj.TargetAudioFilePath);
                        string destPath = Path.Combine(htmlDir, fileName);
                        // Always copy to ensure latest version is available
                        File.Copy(textObj.TargetAudioFilePath, destPath, overwrite: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying audio files for export: {ex.Message}");
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
            int borderRadiusInline = ConfigManager.Instance.GetMonitorTextOverlayBorderRadius();
            html.AppendLine($".text-overlay {{ position: absolute; box-sizing: border-box; overflow: visible; display: flex; align-items: center; justify-content: flex-start; padding: 2px; border-radius: {borderRadiusInline}px; }}");
            html.AppendLine(".text-content { flex: 1; width: 100%; height: 100%; display: flex; align-items: center; justify-content: center; }");
            html.AppendLine(".controls-container { position: fixed; top: 10px; left: 0; right: 0; z-index: 1000; display: flex; justify-content: center; }");
            html.AppendLine(".controls { background-color: #202020; padding: 10px 20px; border-radius: 5px; display: inline-block; }");
            html.AppendLine(".controls button { padding: 10px 20px; font-size: 16px; cursor: pointer; margin-bottom: 10px; }");
            html.AppendLine(".controls label { color: white; margin-right: 15px; cursor: pointer; display: inline-block; }");
            html.AppendLine(".controls input[type='radio'] { margin-right: 5px; cursor: pointer; }");
            html.AppendLine(".footer { background-color: rgba(0,0,0,0.8); color: white; padding: 10px; text-align: center; font-size: 14px; width: 100%; }");
            html.AppendLine(".footer a { color: #00aaff; text-decoration: none; }");
            html.AppendLine(".footer a:hover { text-decoration: underline; }");
            html.AppendLine(".audio-icon {");
            html.AppendLine("  position: absolute;");
            html.AppendLine("  top: 0px;"); // Align with top of text box
            html.AppendLine("  left: -24px;"); // Position outside to the left of the overlay
            html.AppendLine("  width: 20px;");
            html.AppendLine("  height: 20px;");
            html.AppendLine("  cursor: pointer;");
            html.AppendLine("  font-size: 16px;");
            html.AppendLine("  z-index: 10;");
            html.AppendLine("  background: rgba(0, 0, 0, 0.5);");
            html.AppendLine("  border-radius: 3px;");
            html.AppendLine("  display: flex;");
            html.AppendLine("  align-items: center;");
            html.AppendLine("  justify-content: center;");
            html.AppendLine("  pointer-events: auto;");
            html.AppendLine("  flex: 0 0 auto;"); // Explicitly remove from flex flow
            html.AppendLine("  margin: 0;"); // Ensure no margin affects positioning
            html.AppendLine("}");
            html.AppendLine(".audio-icon:hover {");
            html.AppendLine("  background: rgba(0, 0, 0, 0.7);");
            html.AppendLine("}");
            html.AppendLine(".audio-icon.loading {");
            html.AppendLine("  background: transparent;");
            html.AppendLine("  color: #cc0000 !important;");
            html.AppendLine("  font-size: 20px !important;");
            html.AppendLine("  line-height: 1;");
            html.AppendLine("}");
            html.AppendLine(".audio-icon.loading:hover {");
            html.AppendLine("  background: transparent;");
            html.AppendLine("  color: #ff0000 !important;");
            html.AppendLine("  font-size: 20px !important;");
            html.AppendLine("  line-height: 1;");
            html.AppendLine("}");
            html.AppendLine(".audio-icon:not(.loading) {");
            html.AppendLine("  filter: grayscale(0.7) sepia(0.3) hue-rotate(10deg) saturate(0.6) brightness(1.1);");
            html.AppendLine("}");
            html.AppendLine(".text-overlay.playing {");
            html.AppendLine("  animation: playingPulse 1.2s ease-in-out infinite;");
            html.AppendLine("}");
            html.AppendLine("@keyframes playingPulse {");
            html.AppendLine("  0%, 100% { filter: drop-shadow(0 0 28px rgba(120, 220, 255, 0.9)) drop-shadow(0 0 12px rgba(100, 200, 255, 0.7)); }");
            html.AppendLine("  50% { filter: drop-shadow(0 0 50px rgba(140, 230, 255, 1.0)) drop-shadow(0 0 25px rgba(120, 220, 255, 0.9)); }");
            html.AppendLine("}");
            
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
            // Compensate for DPI and text scale (WebView2 CSS pixels are scaled by both)
            double dpiScale = GetActualDpiScale();
            double textScale = DisplayHelper.GetWindowsTextScaleFactor();
            double combinedScale = dpiScale * textScale;
            
            html.AppendLine("<div class=\"container\">");
            html.AppendLine($"<img class=\"monitor-image\" src=\"{imageFileName}\" width=\"{(int)((originalWidth * currentZoom) / combinedScale)}\" height=\"{(int)((originalHeight * currentZoom) / combinedScale)}\">");
            
            // Add text overlays from Logic
            appendOverlayDivs(html, currentZoom / combinedScale, _currentOverlayMode, true);
            
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
            html.AppendLine("      overlay.style.display = 'flex';");
            html.AppendLine("      ");
            html.AppendLine("      // Get the appropriate text based on mode");
            html.AppendLine("      const sourceText = overlay.getAttribute('data-source-text') || '';");
            html.AppendLine("      const translatedText = overlay.getAttribute('data-translated-text') || '';");
            html.AppendLine("      const originalOrientation = overlay.getAttribute('data-orientation') || 'horizontal';");
            html.AppendLine("      const contentSpan = overlay.querySelector('.text-content');");
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
            html.AppendLine("      if (contentSpan) {");
            html.AppendLine("        if (mode === 'source' || (mode === 'translated' && !translatedText)) {");
            html.AppendLine("          // Show source orientation");
            html.AppendLine("          if (originalOrientation === 'vertical') {");
            html.AppendLine("            contentSpan.style.writingMode = 'vertical-rl';");
            html.AppendLine("            contentSpan.style.textOrientation = 'upright';");
            html.AppendLine("          } else {");
            html.AppendLine("            contentSpan.style.writingMode = 'horizontal-tb';");
            html.AppendLine("            contentSpan.style.textOrientation = 'mixed';");
            html.AppendLine("          }");
            html.AppendLine("        } else if (mode === 'translated') {");
            html.AppendLine("          // For translated, check if target supports vertical");
            html.AppendLine("          if (originalOrientation === 'vertical' && targetSupportsVertical) {");
            html.AppendLine("            contentSpan.style.writingMode = 'vertical-rl';");
            html.AppendLine("            contentSpan.style.textOrientation = 'upright';");
            html.AppendLine("          } else {");
            html.AppendLine("            contentSpan.style.writingMode = 'horizontal-tb';");
            html.AppendLine("            contentSpan.style.textOrientation = 'mixed';");
            html.AppendLine("          }");
            html.AppendLine("        }");
            html.AppendLine("      }");
            html.AppendLine("      ");
            html.AppendLine("      // Update speaker icon based on mode");
            html.AppendLine("      const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine("      if (icon) {");
            html.AppendLine("        const sourceReady = overlay.getAttribute('data-source-ready') === 'true';");
            html.AppendLine("        const targetReady = overlay.getAttribute('data-target-ready') === 'true';");
            html.AppendLine("        let audioIsReady = false;");
            html.AppendLine("        ");
            html.AppendLine("        if (mode === 'translated') {");
            html.AppendLine("          // In translated mode, check if target audio is ready");
            html.AppendLine("          audioIsReady = targetReady;");
            html.AppendLine("        } else {");
            html.AppendLine("          // In source mode, check if source audio is ready");
            html.AppendLine("          audioIsReady = sourceReady;");
            html.AppendLine("        }");
            html.AppendLine("        ");
            html.AppendLine("        // Update icon appearance");
            html.AppendLine($"        icon.textContent = audioIsReady ? '{ConfigManager.ICON_SPEAKER_READY}' : '{ConfigManager.ICON_SPEAKER_NOT_READY}';");
            html.AppendLine("        if (audioIsReady) {");
            html.AppendLine("          icon.classList.remove('loading');");
            html.AppendLine("        } else {");
            html.AppendLine("          icon.classList.add('loading');");
            html.AppendLine("        }");
            html.AppendLine("        ");
            html.AppendLine("        // Update data-is-ready attribute for future reference");
            html.AppendLine("        icon.setAttribute('data-is-ready', audioIsReady.toString());");
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
            html.AppendLine("function handleAudioIconClick(textObjectId, isSource) {");
            html.AppendLine("  const overlay = document.getElementById('overlay-' + textObjectId);");
            html.AppendLine("  if (!overlay) return;");
            html.AppendLine("  ");
            html.AppendLine("  // Determine which audio to play based on current overlay mode");
            html.AppendLine("  // Ignore the isSource parameter and check current mode instead");
            html.AppendLine("  let useSourceAudio = false;");
            html.AppendLine("  if (currentOverlayMode === 'source') {");
            html.AppendLine("    useSourceAudio = true;");
            html.AppendLine("  } else if (currentOverlayMode === 'translated') {");
            html.AppendLine("    useSourceAudio = false;");
            html.AppendLine("  } else {");
            html.AppendLine("    // Fallback: use the isSource parameter if mode is not set");
            html.AppendLine("    useSourceAudio = isSource;");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  // Get the appropriate audio path");
            html.AppendLine("  let audioPath = useSourceAudio ? overlay.getAttribute('data-source-audio') : overlay.getAttribute('data-target-audio');");
            html.AppendLine("  ");
            html.AppendLine("  // If the preferred audio is not available, try the other one");
            html.AppendLine("  if (!audioPath || audioPath === '') {");
            html.AppendLine("    audioPath = useSourceAudio ? overlay.getAttribute('data-target-audio') : overlay.getAttribute('data-source-audio');");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  if (!audioPath || audioPath === '') return;");
            html.AppendLine("  ");
            html.AppendLine("  // Check if audio is already playing");
            html.AppendLine("  const existingAudio = overlay._currentAudio;");
            html.AppendLine("  if (existingAudio && !existingAudio.paused) {");
            html.AppendLine("    // Stop playing");
            html.AppendLine("    existingAudio.pause();");
            html.AppendLine("    existingAudio.currentTime = 0;");
            html.AppendLine("    overlay._currentAudio = null;");
            html.AppendLine("    const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine($"    if (icon) icon.textContent = '{ConfigManager.ICON_SPEAKER_READY}';");
            html.AppendLine("    return;");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  // Create audio element and play");
            html.AppendLine("  const audio = new Audio(audioPath);");
            html.AppendLine("  overlay._currentAudio = audio;");
            html.AppendLine("  ");
            html.AppendLine("  // Update icon to stop icon while playing");
            html.AppendLine("  const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine("  if (icon) {");
            html.AppendLine("    icon.textContent = '⏸️';");
            html.AppendLine("    ");
            html.AppendLine("    audio.addEventListener('ended', function() {");
            html.AppendLine($"      icon.textContent = '{ConfigManager.ICON_SPEAKER_READY}';");
            html.AppendLine("      overlay._currentAudio = null;");
            html.AppendLine("    });");
            html.AppendLine("    ");
            html.AppendLine("    audio.addEventListener('error', function() {");
            html.AppendLine($"      icon.textContent = '{ConfigManager.ICON_SPEAKER_READY}';");
            html.AppendLine("      overlay._currentAudio = null;");
            html.AppendLine("      console.error('Error playing audio:', audioPath);");
            html.AppendLine("    });");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  audio.play().catch(err => {");
            html.AppendLine("    console.error('Error playing audio:', err);");
            html.AppendLine("    const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine($"    if (icon) icon.textContent = '{ConfigManager.ICON_SPEAKER_READY}';");
            html.AppendLine("    overlay._currentAudio = null;");
            html.AppendLine("  });");
            html.AppendLine("}");
            html.AppendLine("");
            appendFitTextJavaScript(html);
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("function initializeTextFitting() {");
            html.AppendLine("  const img = document.querySelector('.monitor-image');");
            html.AppendLine("  function doFit() {");
            html.AppendLine("    if (document.fonts && document.fonts.ready) { document.fonts.ready.then(fitAllText); }");
            html.AppendLine("    else { setTimeout(fitAllText, 200); }");
            html.AppendLine("  }");
            html.AppendLine("  if (img && !img.complete) {");
            html.AppendLine("    img.addEventListener('load', function() { setTimeout(doFit, 100); }, {once:true});");
            html.AppendLine("  } else {");
            html.AppendLine("    setTimeout(doFit, 100);");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("if (document.readyState === 'loading') {");
            html.AppendLine("  document.addEventListener('DOMContentLoaded', function() {");
            html.AppendLine("    initializeTextFitting();");
            html.AppendLine("    setOverlayMode(currentOverlayMode);");
            html.AppendLine("  });");
            html.AppendLine("} else {");
            html.AppendLine("  initializeTextFitting();");
            html.AppendLine("  setOverlayMode(currentOverlayMode);");
            html.AppendLine("}");
            html.AppendLine("</script>");
            
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
        
        /// <summary>
        /// Shared helper that emits overlay &lt;div&gt;s for every text object.
        /// Used by both GenerateHtml (browser export) and GenerateScreenshotHtml.
        /// All pixel dimensions are multiplied by <paramref name="scale"/>.
        /// </summary>
        private void appendOverlayDivs(StringBuilder html, double scale, OverlayMode mode, bool includeAudio)
        {
            var textObjects = Logic.Instance?.GetTextObjects();
            if (textObjects == null) return;

            string defaultHAlign = ConfigManager.Instance.GetTextOverlayHorizontalAlignment();
            string defaultVAlign = ConfigManager.Instance.GetTextOverlayVerticalAlignment();

            foreach (var textObj in textObjects)
            {
                if (textObj == null) continue;

                double left = (textObj.X + textObj.OffsetX) * scale;
                double top = (textObj.Y + textObj.OffsetY) * scale;
                double width = (textObj.Width > 0 ? textObj.Width : 200) * scale;
                double height = (textObj.Height > 0 ? textObj.Height : 100) * scale;

                Color bgC;
                if (ConfigManager.Instance.IsMonitorOverrideBgColorEnabled())
                    bgC = ConfigManager.Instance.GetMonitorOverrideBgColor();
                else
                    bgC = textObj.BackgroundColor?.Color ?? Colors.Black;

                double bgOpacity = ConfigManager.Instance.GetMonitorBgOpacity();
                byte alphaValue = (byte)(bgOpacity * 255);
                bgC = Color.FromArgb(alphaValue, bgC.R, bgC.G, bgC.B);

                Color textC;
                if (ConfigManager.Instance.IsMonitorOverrideFontColorEnabled())
                    textC = ConfigManager.Instance.GetMonitorOverrideFontColor();
                else
                    textC = textObj.TextColor?.Color ?? Colors.White;

                string bgColor = ColorToHex(bgC);
                string textColor = ColorToHex(textC);

                bool isTranslated = !string.IsNullOrEmpty(textObj.TextTranslated);
                string fontFamily = isTranslated
                    ? ConfigManager.Instance.GetTargetLanguageFontFamily()
                    : ConfigManager.Instance.GetSourceLanguageFontFamily();
                bool isBold = isTranslated
                    ? ConfigManager.Instance.GetTargetLanguageFontBold()
                    : ConfigManager.Instance.GetSourceLanguageFontBold();

                string sourceText = textObj.Text;
                string translatedText = textObj.TextTranslated;
                string escapedSourceText = System.Web.HttpUtility.HtmlEncode(sourceText).Replace("\n", "<br>");
                string escapedTranslatedText = System.Web.HttpUtility.HtmlEncode(translatedText).Replace("\n", "<br>");

                string displayText;
                if (mode == OverlayMode.Translated && !string.IsNullOrEmpty(translatedText))
                    displayText = escapedTranslatedText;
                else
                    displayText = escapedSourceText;

                double fontSize = (24) * scale;

                string fontSizeOverrideAttr = "";
                if (textObj.FontSizeOverride.HasValue)
                {
                    double overrideSize = textObj.FontSizeOverride.Value * scale;
                    fontSizeOverrideAttr = FormattableString.Invariant($" data-font-size-override=\"{overrideSize}\"");
                    fontSize = overrideSize;
                }

                string initialOrientation = textObj.TextOrientation;
                if (mode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated) && textObj.TextOrientation == "vertical")
                {
                    string targetLang = ConfigManager.Instance.GetTargetLanguage().ToLower();
                    if (!IsVerticalSupportedLanguage(targetLang))
                        initialOrientation = "horizontal";
                }

                string bubbleVAlign = textObj.VerticalAlignmentOverride ?? defaultVAlign;
                string bubbleHAlign = textObj.HorizontalAlignmentOverride ?? defaultHAlign;
                string bubbleVAlignCss = bubbleVAlign == "top" ? "flex-start" : bubbleVAlign == "bottom" ? "flex-end" : "center";
                string bubbleHAlignFlex = bubbleHAlign == "left" ? "flex-start" : bubbleHAlign == "right" ? "flex-end" : "center";

                string fontFamilyCss = string.Join(", ", fontFamily.Split(',').Select(f => $"'{f.Trim()}'")) + ", sans-serif";

                html.AppendLine($"<div class=\"text-overlay\" id=\"overlay-{textObj.ID}\" " +
                    $"data-source-text=\"{System.Web.HttpUtility.HtmlAttributeEncode(sourceText)}\" " +
                    $"data-translated-text=\"{System.Web.HttpUtility.HtmlAttributeEncode(translatedText)}\" " +
                    FormattableString.Invariant($"data-original-font-size=\"{fontSize}\"") + fontSizeOverrideAttr +
                    $" data-orientation=\"{textObj.TextOrientation}\"" +
                    " style=\"");
                html.AppendLine(FormattableString.Invariant($"  left: {left:F1}px;"));
                html.AppendLine(FormattableString.Invariant($"  top: {top:F1}px;"));
                html.AppendLine(FormattableString.Invariant($"  width: {width:F1}px;"));
                html.AppendLine(FormattableString.Invariant($"  height: {height:F1}px;"));
                html.AppendLine($"  box-shadow: inset 0 0 0 1000px {bgColor};");
                html.AppendLine($"  background-color: transparent;");
                html.AppendLine($"  color: {textColor};");
                html.AppendLine($"  font-family: {fontFamilyCss};");
                html.AppendLine($"  font-weight: {(isBold ? "bold" : "normal")};");
                html.AppendLine(FormattableString.Invariant($"  font-size: {fontSize:F1}px;"));
                html.AppendLine($"  padding: 0;");
                html.AppendLine($"  margin: 0;");
                html.AppendLine($"  line-height: 1.2;");

                string initialDisplay = mode == OverlayMode.Hide ? "none" : "flex";
                html.AppendLine($"  display: {initialDisplay};");
                html.AppendLine($"  align-items: {bubbleVAlignCss};");
                html.AppendLine("\">");

                if (includeAudio)
                {
                    bool isTtsPreloadEnabled = ConfigManager.Instance.IsTtsPreloadEnabled();
                    string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                    bool preloadEnabled = isTtsPreloadEnabled && preloadMode != "Off";

                    if (preloadEnabled && !ConfigManager.Instance.IsTextBelowTtsMinChars(textObj.Text))
                    {
                        string sourceAudioPath = "";
                        string targetAudioPath = "";
                        if (!string.IsNullOrEmpty(textObj.SourceAudioFilePath) && File.Exists(textObj.SourceAudioFilePath))
                            sourceAudioPath = Path.GetFileName(textObj.SourceAudioFilePath);
                        if (!string.IsNullOrEmpty(textObj.TargetAudioFilePath) && File.Exists(textObj.TargetAudioFilePath))
                            targetAudioPath = Path.GetFileName(textObj.TargetAudioFilePath);

                        bool audioIsReady = false;
                        bool isSource = true;

                        if (mode == OverlayMode.Translated)
                        {
                            if (textObj.TargetAudioReady && !string.IsNullOrEmpty(textObj.TargetAudioFilePath))
                            {
                                audioIsReady = true;
                                isSource = false;
                            }
                            else
                            {
                                isSource = textObj.SourceAudioReady ? true : false;
                            }
                        }
                        else
                        {
                            if (textObj.SourceAudioReady && !string.IsNullOrEmpty(textObj.SourceAudioFilePath))
                            {
                                audioIsReady = true;
                                isSource = true;
                            }
                        }

                        string iconEmoji = audioIsReady ? ConfigManager.ICON_SPEAKER_READY : ConfigManager.ICON_SPEAKER_NOT_READY;
                        string iconClass = audioIsReady ? "audio-icon" : "audio-icon loading";
                        html.AppendLine($"<div class=\"{iconClass}\" data-is-ready=\"{audioIsReady.ToString().ToLower()}\" onclick=\"handleAudioIconClick('{textObj.ID}', {isSource.ToString().ToLower()})\">{iconEmoji}</div>");
                    }
                }

                html.AppendLine($"<span class=\"text-content\" style=\"");
                html.AppendLine($"  flex: 1;");
                html.AppendLine($"  display: flex;");
                html.AppendLine($"  align-items: {bubbleVAlignCss};");
                html.AppendLine($"  justify-content: {bubbleHAlignFlex};");
                html.AppendLine($"  text-align: {bubbleHAlign};");
                html.AppendLine($"  width: 100%;");
                html.AppendLine($"  height: 100%;");
                if (initialOrientation == "vertical")
                {
                    html.AppendLine($"  writing-mode: vertical-rl;");
                    html.AppendLine($"  text-orientation: upright;");
                }
                html.AppendLine($"\">{displayText}</span>");
                html.AppendLine("</div>");
            }
        }

        /// <summary>
        /// Emits the fitTextToContainer function and the beginning of fitAllText
        /// (everything up to but not including the closing brace of fitAllText).
        /// Callers append any extra logic (e.g. postMessage) then close with "}".
        /// </summary>
        private void appendFitTextJavaScript(StringBuilder html)
        {
            html.AppendLine("function fitTextToContainer(element, container) {");
            html.AppendLine("  try {");
            html.AppendLine("    const sizeRef = container || element;");
            html.AppendLine("    const minSize = 8;");
            html.AppendLine("    const maxSize = 128;");
            html.AppendLine("    const computedStyle = window.getComputedStyle(sizeRef);");
            html.AppendLine("    const isVertical = computedStyle.writingMode === 'vertical-rl' || computedStyle.writingMode === 'vertical-lr';");
            html.AppendLine("    const origAlignItems = element.style.alignItems;");
            html.AppendLine("    const origJustifyContent = element.style.justifyContent;");
            html.AppendLine("    element.style.alignItems = 'flex-start';");
            html.AppendLine("    element.style.justifyContent = 'flex-start';");
            html.AppendLine("    const boxHeight = sizeRef.clientHeight;");
            html.AppendLine("    const boxWidth = sizeRef.clientWidth;");
            html.AppendLine("    let estimatedSize;");
            html.AppendLine("    if (isVertical) { estimatedSize = Math.floor(boxWidth * 0.7); }");
            html.AppendLine("    else { estimatedSize = Math.floor(boxHeight * 0.7); }");
            html.AppendLine("    estimatedSize = Math.max(minSize, Math.min(maxSize, estimatedSize));");
            html.AppendLine("    let bestSize = minSize;");
            html.AppendLine("    let low = minSize, high = maxSize;");
            html.AppendLine("    while (high - low > 0.5) {");
            html.AppendLine("      const mid = (low + high) / 2;");
            html.AppendLine("      element.style.fontSize = mid + 'px';");
            html.AppendLine("      const fitsHeight = element.scrollHeight <= element.clientHeight;");
            html.AppendLine("      const fitsWidth = element.scrollWidth <= element.clientWidth;");
            html.AppendLine("      if (fitsHeight && fitsWidth) { bestSize = mid; low = mid; }");
            html.AppendLine("      else { high = mid; }");
            html.AppendLine("    }");
            html.AppendLine("    element.style.fontSize = bestSize + 'px';");
            html.AppendLine("    element.style.alignItems = origAlignItems;");
            html.AppendLine("    element.style.justifyContent = origJustifyContent;");
            html.AppendLine("  } catch(e) { console.error('Error fitting text:', e); }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("function fitAllText() {");
            html.AppendLine("  const overlays = document.getElementsByClassName('text-overlay');");
            html.AppendLine("  for (let overlay of overlays) {");
            html.AppendLine("    if (overlay.hasAttribute('data-font-size-override')) {");
            html.AppendLine("      const overrideSize = parseFloat(overlay.getAttribute('data-font-size-override'));");
            html.AppendLine("      const textContent = overlay.querySelector('.text-content');");
            html.AppendLine("      if (textContent) textContent.style.fontSize = overrideSize + 'px';");
            html.AppendLine("      else overlay.style.fontSize = overrideSize + 'px';");
            html.AppendLine("      continue;");
            html.AppendLine("    }");
            html.AppendLine("    if (overlay.offsetWidth > 0 && overlay.offsetHeight > 0) {");
            html.AppendLine("      const textContent = overlay.querySelector('.text-content');");
            html.AppendLine("      if (textContent) { fitTextToContainer(textContent, overlay); }");
            html.AppendLine("      else { fitTextToContainer(overlay); }");
            html.AppendLine("    }");
            html.AppendLine("  }");
        }

        /// <summary>
        /// Emits the image-load / fonts-ready initialization that calls fitAllText.
        /// </summary>
        private void appendFitTextInitJavaScript(StringBuilder html)
        {
            html.AppendLine("function initializeTextFitting() {");
            html.AppendLine("  const img = document.querySelector('.monitor-image');");
            html.AppendLine("  function doFit() {");
            html.AppendLine("    if (document.fonts && document.fonts.ready) { document.fonts.ready.then(fitAllText); }");
            html.AppendLine("    else { setTimeout(fitAllText, 200); }");
            html.AppendLine("  }");
            html.AppendLine("  if (img && !img.complete) {");
            html.AppendLine("    img.addEventListener('load', function() { setTimeout(doFit, 50); }, {once:true});");
            html.AppendLine("  } else {");
            html.AppendLine("    setTimeout(doFit, 50);");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("if (document.readyState === 'loading') {");
            html.AppendLine("  document.addEventListener('DOMContentLoaded', initializeTextFitting);");
            html.AppendLine("} else {");
            html.AppendLine("  initializeTextFitting();");
            html.AppendLine("}");
        }

        public string GenerateScreenshotHtml(string base64ImageSrc, int width, int height, double cssScale)
        {
            StringBuilder html = new StringBuilder();
            
            double cw = width / cssScale;
            double ch = height / cssScale;
            
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"UTF-8\">");
            html.AppendLine("<style>");
            html.AppendLine("html, body { margin: 0; padding: 0; overflow: hidden; background: transparent; }");
            html.AppendLine(FormattableString.Invariant($".container {{ position: relative; display: inline-block; }}"));
            html.AppendLine(FormattableString.Invariant($".monitor-image {{ display: block; width: {cw:F1}px; height: {ch:F1}px; }}"));
            int borderRadius = ConfigManager.Instance.GetMonitorTextOverlayBorderRadius();
            html.AppendLine($".text-overlay {{ position: absolute; box-sizing: border-box; overflow: visible; display: flex; align-items: center; justify-content: flex-start; padding: 2px; border-radius: {borderRadius}px; }}");
            html.AppendLine(".text-content { flex: 1; width: 100%; height: 100%; display: flex; align-items: center; justify-content: center; }");
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            html.AppendLine("<div class=\"container\">");
            html.AppendLine($"<img class=\"monitor-image\" src=\"{base64ImageSrc}\">");
            
            appendOverlayDivs(html, 1.0 / cssScale, OverlayMode.Translated, false);
            
            html.AppendLine("</div>");
            
            // JavaScript for font auto-fitting, then signal ready
            html.AppendLine("<script>");
            appendFitTextJavaScript(html);
            html.AppendLine("  if (window.chrome && window.chrome.webview) {");
            html.AppendLine("    window.chrome.webview.postMessage('screenshotReady');");
            html.AppendLine("  }");
            html.AppendLine("}");
            appendFitTextInitJavaScript(html);
            html.AppendLine("</script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
        
        private string ColorToHex(Color color)
        {
            // Use rgba() format to support transparency
            // Convert alpha from 0-255 to 0.0-1.0 for CSS
            double alpha = color.A / 255.0;
            string result = FormattableString.Invariant($"rgba({color.R}, {color.G}, {color.B}, {alpha:F3})");
            Console.WriteLine($"ColorToHex: R:{color.R} G:{color.G} B:{color.B} A:{color.A} -> {result}");
            return result;
        }
        
        // Check if a language supports vertical text
        public static bool IsVerticalSupportedLanguage(string languageCode)
        {
            // Languages that typically support vertical text (CJK languages)
            string[] verticalLanguages = { "ja", "zh", "ko", "zh-cn", "zh-tw", "zh-hk", "ja-jp", "ko-kr" };
            return verticalLanguages.Any(lang => languageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
        }
    }
}
