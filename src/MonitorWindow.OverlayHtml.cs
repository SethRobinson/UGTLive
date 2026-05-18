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
        private bool _overlayWebViewInitialized = false;
        
        private async void InitializeOverlayWebView()
        {
            try
            {
                var environment = await WebViewEnvironmentManager.GetEnvironmentAsync();
                
                // CRITICAL: Set WebView2 background to transparent BEFORE initializing
                // This enables CSS rgba() transparency to work properly
                textOverlayWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                Console.WriteLine($"[WEBVIEW2] Set DefaultBackgroundColor to Transparent");
                
                await textOverlayWebView.EnsureCoreWebView2Async(environment);
                
                if (textOverlayWebView.CoreWebView2 != null)
                {
                    Console.WriteLine($"[WEBVIEW2] CoreWebView2 initialized successfully");
                    Console.WriteLine($"[WEBVIEW2] DefaultBackgroundColor is: {textOverlayWebView.DefaultBackgroundColor}");
                    
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
                    
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            WindowCaptureHelper.SetWebView2ExcludeFromCapture(textOverlayWebView, "Monitor");
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

        // While the user is middle-mouse panning, suppress overlay re-navigation
        // (NavigateToString reloads the document and would destroy the in-page
        // pan state / pointer capture, breaking the drag every OCR cycle when
        // Auto is on). The failsafe time guards against a missed panEnd.
        private volatile bool _overlayPanInProgress = false;
        private DateTime _lastPanActivityUtc = DateTime.MinValue;
        private static readonly TimeSpan _panSuppressFailsafe = TimeSpan.FromSeconds(30);
        
        // Method to force clear the HTML cache (for when settings change)
        public void ClearOverlayCache()
        {
            Console.WriteLine("[MONITOR] ClearOverlayCache called - forcing HTML regeneration");
            _lastOverlayHtml = string.Empty;
        }
        
        private void UpdateOverlayWebView()
        {
            if (!_overlayWebViewInitialized || textOverlayWebView?.CoreWebView2 == null)
            {
                return;
            }
            
            // Don't reload the document mid-pan; it would kill the JS pan state
            // and pointer capture. The pending update is applied when the pan
            // ends (see panEnd handler). Failsafe in case panEnd is missed.
            if (_overlayPanInProgress &&
                (DateTime.UtcNow - _lastPanActivityUtc) < _panSuppressFailsafe)
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
                
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"[WEBVIEW2] Updating overlay HTML ({html.Length} chars)");
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
            html.AppendLine("  overflow: visible;"); // Allow audio icon to show outside box
            html.AppendLine("  white-space: normal;");
            html.AppendLine("  word-wrap: break-word;");
            html.AppendLine("  padding: 2px;"); // Minimal padding for visual spacing
            html.AppendLine("  margin: 0;");
            html.AppendLine("  line-height: 1.1;"); // Slightly increase line height for better readability
            html.AppendLine("  display: flex;");
            
            // Apply default vertical alignment from config
            string defaultVAlign = ConfigManager.Instance.GetTextOverlayVerticalAlignment();
            string vAlignCss = defaultVAlign == "top" ? "flex-start" : defaultVAlign == "bottom" ? "flex-end" : "center";
            html.AppendLine($"  align-items: {vAlignCss};");
            html.AppendLine("  justify-content: flex-start;");
            html.AppendLine("  pointer-events: auto;");
            html.AppendLine("  user-select: text;");
            int borderRadius = ConfigManager.Instance.GetMonitorTextOverlayBorderRadius();
            html.AppendLine($"  border-radius: {borderRadius}px;"); // Rounded corners to better fit speech bubbles
            html.AppendLine("}");
            html.AppendLine(".vertical-text {");
            html.AppendLine("  writing-mode: vertical-rl;");
            html.AppendLine("  text-orientation: upright;");
            html.AppendLine("  align-items: flex-start;");
            html.AppendLine("  justify-content: center;");
            html.AppendLine("}");
            html.AppendLine(".text-content {");
            html.AppendLine("  flex: 1;"); // Take up all available space
            html.AppendLine("  display: flex;");
            html.AppendLine("  align-items: inherit;");
            
            // Apply default horizontal alignment from config
            string defaultHAlign = ConfigManager.Instance.GetTextOverlayHorizontalAlignment();
            string hAlignFlex = defaultHAlign == "left" ? "flex-start" : defaultHAlign == "right" ? "flex-end" : "center";
            html.AppendLine($"  justify-content: {hAlignFlex};");
            html.AppendLine($"  text-align: {defaultHAlign};");
            html.AppendLine("  width: 100%;");
            html.AppendLine("  height: 100%;");
            html.AppendLine("  outline: none;");
            html.AppendLine("}");
            
            // Edit mode styles - always emit so Ctrl-drag works even when edit mode is off
            bool editMode = ConfigManager.Instance.GetEditModeEnabled();
            {
                html.AppendLine(".text-overlay.edit-mode {");
                html.AppendLine("  border: 1px dashed rgba(255, 255, 0, 0.7);");
                html.AppendLine("}");
                html.AppendLine(".text-overlay.edit-mode.drag-active {");
                html.AppendLine("  cursor: move;");
                html.AppendLine("  user-select: none;");
                html.AppendLine("}");
                html.AppendLine(".text-overlay.edit-mode.drag-active .text-content {");
                html.AppendLine("  pointer-events: none;");
                html.AppendLine("}");
                html.AppendLine(".text-overlay.edit-mode:hover {");
                html.AppendLine("  border-color: rgba(255, 255, 0, 1.0);");
                html.AppendLine("}");
                html.AppendLine(".text-overlay.edit-mode.dragging {");
                html.AppendLine("  opacity: 0.8;");
                html.AppendLine("  z-index: 9999;");
                html.AppendLine("}");
                html.AppendLine(".resize-handle {");
                html.AppendLine("  position: absolute;");
                html.AppendLine("  width: 8px;");
                html.AppendLine("  height: 8px;");
                html.AppendLine("  background: rgba(255, 255, 0, 0.9);");
                html.AppendLine("  border: 1px solid rgba(0, 0, 0, 0.5);");
                html.AppendLine("  z-index: 10;");
                html.AppendLine("  pointer-events: none;");
                html.AppendLine("  display: none;");
                html.AppendLine("}");
                html.AppendLine(".edit-mode .resize-handle {");
                html.AppendLine("  display: block;");
                html.AppendLine("  pointer-events: auto;");
                html.AppendLine("}");
                html.AppendLine(".resize-handle.nw { top: -4px; left: -4px; cursor: nw-resize; }");
                html.AppendLine(".resize-handle.ne { top: -4px; right: -4px; cursor: ne-resize; }");
                html.AppendLine(".resize-handle.sw { bottom: -4px; left: -4px; cursor: sw-resize; }");
                html.AppendLine(".resize-handle.se { bottom: -4px; right: -4px; cursor: se-resize; }");
                html.AppendLine(".text-overlay.edit-mode.focused {");
                html.AppendLine("  border-color: rgba(0, 200, 255, 1.0);");
                html.AppendLine("  box-shadow: 0 0 6px rgba(0, 200, 255, 0.6);");
                html.AppendLine("}");
            }
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
            html.AppendLine("  user-select: none;");
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
            html.AppendLine("#scroll-container {");
            html.AppendLine("  position: absolute;");
            html.AppendLine("  top: 0;");
            html.AppendLine("  left: 0;");
            html.AppendLine("  transform-origin: 0 0;");
            html.AppendLine("  pointer-events: none;");
            html.AppendLine("}");
            html.AppendLine("#scroll-container > * {");
            html.AppendLine("  pointer-events: auto;");
            html.AppendLine("}");
            html.AppendLine("</style>");
            html.AppendLine("<script>");
            html.AppendLine("function fitTextToBox(element, container) {");
            html.AppendLine("  const minSize = 8;");
            html.AppendLine("  const maxSize = 128;"); // Increased from 64 to allow larger text
            html.AppendLine("  ");
            html.AppendLine("  // Use container for size if provided, otherwise use element");
            html.AppendLine("  const sizeRef = container || element;");
            html.AppendLine("  ");
            html.AppendLine("  // Get computed style to check for padding and vertical text");
            html.AppendLine("  const computedStyle = window.getComputedStyle(sizeRef);");
            html.AppendLine("  const isVertical = computedStyle.writingMode === 'vertical-rl' || computedStyle.writingMode === 'vertical-lr';");
            html.AppendLine("  ");
            html.AppendLine("  // Temporarily force align-items to flex-start during measurement.");
            html.AppendLine("  // When align-items is flex-end/center, overflow goes in the negative");
            html.AppendLine("  // direction and scrollHeight doesn't detect it, breaking the binary search.");
            html.AppendLine("  const origAlignItems = element.style.alignItems;");
            html.AppendLine("  const origJustifyContent = element.style.justifyContent;");
            html.AppendLine("  element.style.alignItems = 'flex-start';");
            html.AppendLine("  element.style.justifyContent = 'flex-start';");
            html.AppendLine("  ");
            html.AppendLine("  // Calculate initial font size based on box dimensions");
            html.AppendLine("  // Use height for horizontal text, width for vertical text");
            html.AppendLine("  const boxHeight = sizeRef.clientHeight;");
            html.AppendLine("  const boxWidth = sizeRef.clientWidth;");
            html.AppendLine("  let estimatedSize;");
            html.AppendLine("  if (isVertical) {");
            html.AppendLine("    estimatedSize = Math.floor(boxWidth * 0.7); // 70% of width for vertical");
            html.AppendLine("  } else {");
            html.AppendLine("    estimatedSize = Math.floor(boxHeight * 0.7); // 70% of height for horizontal");
            html.AppendLine("  }");
            html.AppendLine("  estimatedSize = Math.max(minSize, Math.min(maxSize, estimatedSize));");
            html.AppendLine("  ");
            html.AppendLine("  let bestSize = minSize;");
            html.AppendLine("  ");
            html.AppendLine("  // Binary search for the best font size, starting from estimated size");
            html.AppendLine("  let low = minSize;");
            html.AppendLine("  let high = maxSize;");
            html.AppendLine("  ");
            html.AppendLine("  while (high - low > 0.5) {");
            html.AppendLine("    const mid = (low + high) / 2;");
            html.AppendLine("    element.style.fontSize = mid + 'px';");
            html.AppendLine("    ");
            html.AppendLine("    // Check if content fits (including scrollable content)");
            html.AppendLine("    const fitsHeight = element.scrollHeight <= element.clientHeight;");
            html.AppendLine("    const fitsWidth = element.scrollWidth <= element.clientWidth;");
            html.AppendLine("    ");
            html.AppendLine("    if (fitsHeight && fitsWidth) {");
            html.AppendLine("      bestSize = mid;");
            html.AppendLine("      low = mid;");
            html.AppendLine("    } else {");
            html.AppendLine("      high = mid;");
            html.AppendLine("    }");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  // Apply the best size found and restore alignment");
            html.AppendLine("  element.style.fontSize = bestSize + 'px';");
            html.AppendLine("  element.style.alignItems = origAlignItems;");
            html.AppendLine("  element.style.justifyContent = origJustifyContent;");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("window.addEventListener('load', function() {");
            html.AppendLine("  const overlays = document.querySelectorAll('.text-overlay');");
            html.AppendLine("  overlays.forEach(overlay => {");
            html.AppendLine("    const textContent = overlay.querySelector('.text-content');");
            html.AppendLine("    if (overlay.hasAttribute('data-font-size-override')) {");
            html.AppendLine("      const overrideSize = parseFloat(overlay.getAttribute('data-font-size-override'));");
            html.AppendLine("      if (textContent) textContent.style.fontSize = overrideSize + 'px';");
            html.AppendLine("      else overlay.style.fontSize = overrideSize + 'px';");
            html.AppendLine("    } else {");
            html.AppendLine("      if (textContent) fitTextToBox(textContent, overlay);");
            html.AppendLine("      else fitTextToBox(overlay);");
            html.AppendLine("      const el = textContent || overlay;");
            html.AppendLine("      const computedSize = parseFloat(window.getComputedStyle(el).fontSize);");
            html.AppendLine("      if (computedSize && window.chrome && window.chrome.webview) {");
            html.AppendLine("        window.chrome.webview.postMessage(JSON.stringify({");
            html.AppendLine("          kind: 'autoFitSize',");
            html.AppendLine("          textObjectId: overlay.id.replace('overlay-', ''),");
            html.AppendLine("          fontSize: computedSize");
            html.AppendLine("        }));");
            html.AppendLine("      }");
            html.AppendLine("    }");
            html.AppendLine("  });");
            html.AppendLine("});");
            html.AppendLine("");
            html.AppendLine("let currentlyPlayingId = null;");
            html.AppendLine("");
            html.AppendLine("function handleAudioIconClick(textObjectId, isSource) {");
            html.AppendLine("  const overlay = document.getElementById('overlay-' + textObjectId);");
            html.AppendLine("  if (!overlay) return;");
            html.AppendLine("  ");
            html.AppendLine("  const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine("  if (!icon) return;");
            html.AppendLine("  ");
            html.AppendLine("  // Check if this audio is currently playing");
            html.AppendLine("  if (currentlyPlayingId === textObjectId) {");
            html.AppendLine("    // Stop playing");
            html.AppendLine("    const message = {");
            html.AppendLine("      kind: 'stopAudio',");
            html.AppendLine("      textObjectId: textObjectId");
            html.AppendLine("    };");
            html.AppendLine("    if (window.chrome && window.chrome.webview) {");
            html.AppendLine("      window.chrome.webview.postMessage(JSON.stringify(message));");
            html.AppendLine("    }");
            html.AppendLine("    return;");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  // Try to get the preferred audio path (source or target based on isSource)");
            html.AppendLine("  let audioPath = isSource ? overlay.getAttribute('data-source-audio') : overlay.getAttribute('data-target-audio');");
            html.AppendLine("  ");
            html.AppendLine("  // If preferred audio is not available, fallback to the other type");
            html.AppendLine("  if (!audioPath || audioPath === '') {");
            html.AppendLine("    audioPath = isSource ? overlay.getAttribute('data-target-audio') : overlay.getAttribute('data-source-audio');");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  // If no audio is available at all, return");
            html.AppendLine("  if (!audioPath || audioPath === '') return;");
            html.AppendLine("  ");
            html.AppendLine("  // Update icon to stop icon and add playing class to overlay");
            html.AppendLine("  icon.textContent = '⏹️';");
            html.AppendLine("  icon.classList.remove('loading');");
            html.AppendLine("  overlay.classList.add('playing');");
            html.AppendLine("  currentlyPlayingId = textObjectId;");
            html.AppendLine("  ");
            html.AppendLine("  const message = {");
            html.AppendLine("    kind: 'playAudio',");
            html.AppendLine("    textObjectId: textObjectId,");
            html.AppendLine("    audioPath: audioPath,");
            html.AppendLine("    isSource: isSource");
            html.AppendLine("  };");
            html.AppendLine("  ");
            html.AppendLine("  if (window.chrome && window.chrome.webview) {");
            html.AppendLine("    window.chrome.webview.postMessage(JSON.stringify(message));");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("// Function to update icon when playback stops");
            html.AppendLine("function updateAudioIcon(textObjectId, isPlaying) {");
            html.AppendLine("  const overlay = document.getElementById('overlay-' + textObjectId);");
            html.AppendLine("  if (!overlay) return;");
            html.AppendLine("  const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine("  if (!icon) return;");
            html.AppendLine("  ");
            html.AppendLine("  if (isPlaying) {");
            html.AppendLine("    icon.textContent = '⏹️';");
            html.AppendLine("    icon.classList.remove('loading');");
            html.AppendLine("    overlay.classList.add('playing');");
            html.AppendLine("    currentlyPlayingId = textObjectId;");
            html.AppendLine("  } else {");
            html.AppendLine("    // Check if audio is ready");
            html.AppendLine("    const isReady = icon.getAttribute('data-is-ready') === 'true';");
            html.AppendLine($"    icon.textContent = isReady ? '{ConfigManager.ICON_SPEAKER_READY}' : '{ConfigManager.ICON_SPEAKER_NOT_READY}';");
            html.AppendLine("    if (!isReady) icon.classList.add('loading');");
            html.AppendLine("    else icon.classList.remove('loading');");
            html.AppendLine("    overlay.classList.remove('playing');");
            html.AppendLine("    if (currentlyPlayingId === textObjectId) {");
            html.AppendLine("      currentlyPlayingId = null;");
            html.AppendLine("    }");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("");
            html.AppendLine("function setAudioState(textObjectId, isReady, isSourceForClick, audioPath, isSourceUpdate, iconReady, iconNotReady) {");
            html.AppendLine("  const overlay = document.getElementById('overlay-' + textObjectId);");
            html.AppendLine("  if (!overlay) return;");
            html.AppendLine("  ");
            html.AppendLine("  // Update the audio path attribute");
            html.AppendLine("  if (isSourceUpdate) {");
            html.AppendLine("    overlay.setAttribute('data-source-audio', audioPath || '');");
            html.AppendLine("    overlay.setAttribute('data-source-ready', 'true');");
            html.AppendLine("  } else {");
            html.AppendLine("    overlay.setAttribute('data-target-audio', audioPath || '');");
            html.AppendLine("    overlay.setAttribute('data-target-ready', 'true');");
            html.AppendLine("  }");
            html.AppendLine("  ");
            html.AppendLine("  const icon = overlay.querySelector('.audio-icon');");
            html.AppendLine("  if (!icon) return;");
            html.AppendLine("  ");
            html.AppendLine("  // Update visual state");
            html.AppendLine("  icon.setAttribute('data-is-ready', isReady);");
            html.AppendLine("  icon.textContent = isReady ? iconReady : iconNotReady;");
            html.AppendLine("  if (!isReady) icon.classList.add('loading');");
            html.AppendLine("  else icon.classList.remove('loading');");
            html.AppendLine("  ");
            html.AppendLine("  // Update click handler");
            html.AppendLine("  icon.setAttribute('onclick', 'handleAudioIconClick(\"' + textObjectId + '\", ' + isSourceForClick + ')');");
            html.AppendLine("}");
            html.AppendLine("");
            
            // Editable text: on blur, send updated text back to C#
            html.AppendLine("document.addEventListener('blur', function(event) {");
            html.AppendLine("  if (!event.target.classList.contains('text-content') || !event.target.isContentEditable) return;");
            html.AppendLine("  let overlay = event.target.closest('.text-overlay');");
            html.AppendLine("  if (!overlay) return;");
            html.AppendLine("  const message = {");
            html.AppendLine("    kind: 'editText',");
            html.AppendLine("    textObjectId: overlay.id.replace('overlay-', ''),");
            html.AppendLine("    newText: event.target.innerText");
            html.AppendLine("  };");
            html.AppendLine("  if (window.chrome && window.chrome.webview) {");
            html.AppendLine("    window.chrome.webview.postMessage(JSON.stringify(message));");
            html.AppendLine("  }");
            html.AppendLine("}, true);");
            html.AppendLine("");
            
            // Intercept Tab key to always cycle overlay mode instead of focus-cycling contenteditable elements
            html.AppendLine("document.addEventListener('keydown', function(e) {");
            html.AppendLine("  if (e.key === 'Tab') {");
            html.AppendLine("    e.preventDefault();");
            html.AppendLine("    e.stopPropagation();");
            html.AppendLine("    if (window.chrome && window.chrome.webview) {");
            html.AppendLine("      window.chrome.webview.postMessage(JSON.stringify({ kind: 'tabKey' }));");
            html.AppendLine("    }");
            html.AppendLine("  }");
            html.AppendLine("}, true);");
            
            // Focus tracking: when a text overlay gets focus (click/tap), notify C# and highlight it
            html.AppendLine("document.addEventListener('mousedown', function(event) {");
            html.AppendLine("  let overlay = event.target.closest('.text-overlay');");
            html.AppendLine("  document.querySelectorAll('.text-overlay.focused').forEach(el => el.classList.remove('focused'));");
            html.AppendLine("  if (overlay) {");
            html.AppendLine("    overlay.classList.add('focused');");
            html.AppendLine("    const message = { kind: 'focusOverlay', textObjectId: overlay.id.replace('overlay-', '') };");
            html.AppendLine("    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(JSON.stringify(message));");
            html.AppendLine("  }");
            html.AppendLine("});");
            html.AppendLine("");
            // Ctrl+drag to move, corner handles to resize, normal click to edit text
            // Always emitted: when edit mode is off, holding Ctrl temporarily enables drag
            {
                html.AppendLine($"let persistentEditMode = {(editMode ? "true" : "false")};");
                html.AppendLine("let ctrlHeld = false;");
                html.AppendLine("document.addEventListener('keydown', function(e) {");
                html.AppendLine("  if (e.key === 'Control' && !ctrlHeld) {");
                html.AppendLine("    ctrlHeld = true;");
                html.AppendLine("    if (!e._synthetic && window.chrome && window.chrome.webview)");
                html.AppendLine("      window.chrome.webview.postMessage(JSON.stringify({ kind: 'ctrlKey', down: true }));");
                html.AppendLine("    if (!persistentEditMode) {");
                html.AppendLine("      document.querySelectorAll('.text-overlay').forEach(el => el.classList.add('edit-mode'));");
                html.AppendLine("    }");
                html.AppendLine("    document.querySelectorAll('.text-overlay.edit-mode').forEach(el => el.classList.add('drag-active'));");
                html.AppendLine("  }");
                html.AppendLine("});");
                html.AppendLine("document.addEventListener('keyup', function(e) {");
                html.AppendLine("  if (e.key === 'Control') {");
                html.AppendLine("    ctrlHeld = false;");
                html.AppendLine("    if (!e._synthetic && window.chrome && window.chrome.webview)");
                html.AppendLine("      window.chrome.webview.postMessage(JSON.stringify({ kind: 'ctrlKey', down: false }));");
                html.AppendLine("    if (!dragState) {");
                html.AppendLine("      document.querySelectorAll('.text-overlay.edit-mode').forEach(el => el.classList.remove('drag-active'));");
                html.AppendLine("      if (!persistentEditMode) {");
                html.AppendLine("        document.querySelectorAll('.text-overlay').forEach(el => el.classList.remove('edit-mode'));");
                html.AppendLine("      }");
                html.AppendLine("    }");
                html.AppendLine("  }");
                html.AppendLine("});");
                html.AppendLine("let dragState = null;");
                html.AppendLine("document.addEventListener('mousedown', function(event) {");
                html.AppendLine("  const handle = event.target.closest('.resize-handle');");
                html.AppendLine("  const overlay = event.target.closest('.text-overlay.edit-mode');");
                html.AppendLine("  if (!overlay) return;");
                html.AppendLine("  if (!handle && !ctrlHeld) return;");
                html.AppendLine("  event.preventDefault();");
                html.AppendLine("  event.stopPropagation();");
                html.AppendLine("  const container = document.getElementById('scroll-container');");
                html.AppendLine("  const scale = container ? parseFloat(container.style.transform.match(/scale\\(([^)]+)\\)/)?.[1] || 1) : 1;");
                html.AppendLine("  dragState = {");
                html.AppendLine("    el: overlay,");
                html.AppendLine("    scale: scale,");
                html.AppendLine("    startX: event.clientX,");
                html.AppendLine("    startY: event.clientY,");
                html.AppendLine("    origLeft: parseFloat(overlay.style.left),");
                html.AppendLine("    origTop: parseFloat(overlay.style.top),");
                html.AppendLine("    origWidth: parseFloat(overlay.style.width),");
                html.AppendLine("    origHeight: parseFloat(overlay.style.height),");
                html.AppendLine("    mode: handle ? handle.dataset.dir : 'move'");
                html.AppendLine("  };");
                html.AppendLine("  overlay.classList.add('dragging');");
                html.AppendLine("});");
                html.AppendLine("document.addEventListener('mousemove', function(event) {");
                html.AppendLine("  if (!dragState) return;");
                html.AppendLine("  const dx = (event.clientX - dragState.startX) / dragState.scale;");
                html.AppendLine("  const dy = (event.clientY - dragState.startY) / dragState.scale;");
                html.AppendLine("  const m = dragState.mode;");
                html.AppendLine("  if (m === 'move') {");
                html.AppendLine("    dragState.el.style.left = (dragState.origLeft + dx) + 'px';");
                html.AppendLine("    dragState.el.style.top = (dragState.origTop + dy) + 'px';");
                html.AppendLine("  } else {");
                html.AppendLine("    let l = dragState.origLeft, t = dragState.origTop, w = dragState.origWidth, h = dragState.origHeight;");
                html.AppendLine("    if (m.includes('e')) w = Math.max(20, w + dx);");
                html.AppendLine("    if (m.includes('s')) h = Math.max(20, h + dy);");
                html.AppendLine("    if (m.includes('w')) { l += dx; w = Math.max(20, w - dx); }");
                html.AppendLine("    if (m.includes('n')) { t += dy; h = Math.max(20, h - dy); }");
                html.AppendLine("    dragState.el.style.left = l + 'px'; dragState.el.style.top = t + 'px';");
                html.AppendLine("    dragState.el.style.width = w + 'px'; dragState.el.style.height = h + 'px';");
                html.AppendLine("  }");
                html.AppendLine("});");
                html.AppendLine("document.addEventListener('mouseup', function(event) {");
                html.AppendLine("  if (!dragState) return;");
                html.AppendLine("  dragState.el.classList.remove('dragging');");
                html.AppendLine("  if (!ctrlHeld) {");
                html.AppendLine("    document.querySelectorAll('.text-overlay.edit-mode').forEach(el => el.classList.remove('drag-active'));");
                html.AppendLine("    if (!persistentEditMode) {");
                html.AppendLine("      document.querySelectorAll('.text-overlay').forEach(el => el.classList.remove('edit-mode'));");
                html.AppendLine("    }");
                html.AppendLine("  }");
                html.AppendLine("  const newLeft = parseFloat(dragState.el.style.left);");
                html.AppendLine("  const newTop = parseFloat(dragState.el.style.top);");
                html.AppendLine("  const newW = parseFloat(dragState.el.style.width);");
                html.AppendLine("  const newH = parseFloat(dragState.el.style.height);");
                html.AppendLine("  const dxPos = newLeft - dragState.origLeft;");
                html.AppendLine("  const dyPos = newTop - dragState.origTop;");
                html.AppendLine("  const dw = newW - dragState.origWidth;");
                html.AppendLine("  const dh = newH - dragState.origHeight;");
                html.AppendLine("  if (Math.abs(dxPos) > 1 || Math.abs(dyPos) > 1 || Math.abs(dw) > 1 || Math.abs(dh) > 1) {");
                html.AppendLine("    const message = {");
                html.AppendLine("      kind: dragState.mode === 'move' ? 'dragMove' : 'dragResize',");
                html.AppendLine("      textObjectId: dragState.el.id.replace('overlay-', ''),");
                html.AppendLine("      deltaX: dxPos, deltaY: dyPos, deltaW: dw, deltaH: dh");
                html.AppendLine("    };");
                html.AppendLine("    if (window.chrome && window.chrome.webview) {");
                html.AppendLine("      window.chrome.webview.postMessage(JSON.stringify(message));");
                html.AppendLine("    }");
                html.AppendLine("  }");
                html.AppendLine("  dragState = null;");
                html.AppendLine("});");
            }
            
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
            html.AppendLine("");
            // Middle-mouse-button grab/hand panning of the underlying image.
            // Handled here in the overlay because the WebView2 reliably receives
            // these pointer events; we preventDefault to kill Chromium's built-in
            // middle-click autoscroll and post incremental deltas to C# which
            // scrolls the WPF ScrollViewer.
            html.AppendLine("let panState = null;");
            html.AppendLine("window.addEventListener('pointerdown', function(e) {");
            html.AppendLine("  if (e.button !== 1) return;"); // 1 == middle
            html.AppendLine("  e.preventDefault();");
            html.AppendLine("  e.stopPropagation();");
            html.AppendLine("  panState = { id: e.pointerId, lastX: e.screenX, lastY: e.screenY };");
            html.AppendLine("  try { document.documentElement.setPointerCapture(e.pointerId); } catch (err) {}");
            html.AppendLine("  document.documentElement.style.cursor = 'grabbing';");
            html.AppendLine("  if (window.chrome && window.chrome.webview) {");
            html.AppendLine("    window.chrome.webview.postMessage(JSON.stringify({ kind: 'panStart' }));");
            html.AppendLine("  }");
            html.AppendLine("}, true);");
            html.AppendLine("window.addEventListener('pointermove', function(e) {");
            html.AppendLine("  if (!panState || e.pointerId !== panState.id) return;");
            html.AppendLine("  e.preventDefault();");
            html.AppendLine("  const dx = e.screenX - panState.lastX;");
            html.AppendLine("  const dy = e.screenY - panState.lastY;");
            html.AppendLine("  panState.lastX = e.screenX;");
            html.AppendLine("  panState.lastY = e.screenY;");
            html.AppendLine("  if ((dx || dy) && window.chrome && window.chrome.webview) {");
            html.AppendLine("    window.chrome.webview.postMessage(JSON.stringify({ kind: 'pan', dx: dx, dy: dy }));");
            html.AppendLine("  }");
            html.AppendLine("}, true);");
            html.AppendLine("function endPan(e) {");
            html.AppendLine("  if (!panState) return;");
            html.AppendLine("  if (e && e.pointerId !== undefined && e.pointerId !== panState.id) return;");
            html.AppendLine("  try { document.documentElement.releasePointerCapture(panState.id); } catch (err) {}");
            html.AppendLine("  panState = null;");
            html.AppendLine("  document.documentElement.style.cursor = '';");
            html.AppendLine("  if (window.chrome && window.chrome.webview) {");
            html.AppendLine("    window.chrome.webview.postMessage(JSON.stringify({ kind: 'panEnd' }));");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("window.addEventListener('pointerup', endPan, true);");
            html.AppendLine("window.addEventListener('pointercancel', endPan, true);");
            html.AppendLine("window.addEventListener('lostpointercapture', endPan, true);");
            // Also swallow the middle-button mousedown/auxclick so Chromium never
            // enters autoscroll mode (belt-and-suspenders with pointerdown).
            html.AppendLine("window.addEventListener('mousedown', function(e) { if (e.button === 1) { e.preventDefault(); } }, true);");
            html.AppendLine("window.addEventListener('auxclick', function(e) { if (e.button === 1) { e.preventDefault(); } }, true);");
            html.AppendLine("");
            html.AppendLine("function updateScrollOffset(offsetX, offsetY, scaleFactor) {");
            html.AppendLine("  const container = document.getElementById('scroll-container');");
            html.AppendLine("  if (container) {");
            html.AppendLine("    container.style.transform = 'translate(' + offsetX + 'px, ' + offsetY + 'px) scale(' + scaleFactor + ')';");
            html.AppendLine("  }");
            html.AppendLine("}");
            html.AppendLine("</script>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            double initTextScale = DisplayHelper.GetWindowsTextScaleFactor();
            double initScaleFactor = currentZoom / initTextScale;
            System.Windows.Point initOffset = imageContainer.TranslatePoint(new System.Windows.Point(0, 0), imageScrollViewer);
            double initOffsetX = initOffset.X / initTextScale;
            double initOffsetY = initOffset.Y / initTextScale;
            html.AppendLine(FormattableString.Invariant($"<div id='scroll-container' style='transform: translate({initOffsetX}px, {initOffsetY}px) scale({initScaleFactor:F4})'>"));
            
            // Add all text overlays if mode is not Hide
            if (_currentOverlayMode != OverlayMode.Hide && Logic.Instance != null)
            {
                // If keeping translation visible, use old text objects instead of current (empty) ones
                var textObjects = Logic.Instance.GetKeepingTranslationVisible() 
                    ? Logic.Instance.GetTextObjectsOld() 
                    : Logic.Instance.GetTextObjects();
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
                        
                        // Apply opacity setting to background color
                        double bgOpacity = ConfigManager.Instance.GetMonitorBgOpacity();
                        byte alphaValue = (byte)(bgOpacity * 255);
                        bgColor = Color.FromArgb(alphaValue, bgColor.R, bgColor.G, bgColor.B);
                        
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
                            .Replace("\r\n", "<br>")
                            .Replace("\r", "<br>")
                            .Replace("\n", "<br>");
                        
                        // Position overlays in raw image-pixel coordinates;
                        // zoom + DPI/text-scale mapping is handled by the scroll-container CSS transform
                        double left = textObj.X + textObj.OffsetX;
                        double top = textObj.Y + textObj.OffsetY;
                        double width = textObj.Width;
                        double height = textObj.Height;
                        
                        // Calculate initial font size based on box height (will be refined by JavaScript)
                        // Use 70% of height as a starting point, ensuring it's reasonable
                        double initialFontSize = Math.Max(8, Math.Min(128, height * 0.7));
                        
                        // Per-bubble alignment overrides
                        string bubbleVAlign = textObj.VerticalAlignmentOverride ?? defaultVAlign;
                        string bubbleHAlign = textObj.HorizontalAlignmentOverride ?? defaultHAlign;
                        string bubbleVAlignCss = bubbleVAlign == "top" ? "flex-start" : bubbleVAlign == "bottom" ? "flex-end" : "center";
                        string bubbleHAlignFlex = bubbleHAlign == "left" ? "flex-start" : bubbleHAlign == "right" ? "flex-end" : "center";
                        
                        // Build the inline style string with box-shadow for semi-transparent background
                        // (WebView2 doesn't support rgba() on background-color, but DOES on box-shadow)
                        string rgbaString = FormattableString.Invariant($"rgba({bgColor.R},{bgColor.G},{bgColor.B},{bgColor.A / 255.0:F3})");
                        string fontFamilyCss = string.Join(", ", fontFamily.Split(',').Select(f => $"\"{f.Trim()}\""));
                        string styleAttr = FormattableString.Invariant($"left: {left}px; top: {top}px; width: {width}px; height: {height}px; ") +
                            $"box-shadow: inset 0 0 0 1000px {rgbaString}; " +
                            $"background-color: transparent; " +
                            $"color: rgb({textColor.R},{textColor.G},{textColor.B}); " +
                            $"font-family: {fontFamilyCss}; " +
                            $"font-weight: {(isBold ? "bold" : "normal")}; " +
                            $"align-items: {bubbleVAlignCss}; " +
                            FormattableString.Invariant($"font-size: {initialFontSize}px;");
                        
                        string cssClass = displayOrientation == "vertical" ? "text-overlay vertical-text" : "text-overlay";
                        if (editMode)
                        {
                            cssClass += " edit-mode";
                        }
                        html.Append($"<div id='overlay-{textObj.ID}' class='{cssClass}' style='{styleAttr}' ");
                        html.Append($"data-source-audio='{System.Web.HttpUtility.HtmlAttributeEncode(textObj.SourceAudioFilePath ?? "")}' ");
                        html.Append($"data-target-audio='{System.Web.HttpUtility.HtmlAttributeEncode(textObj.TargetAudioFilePath ?? "")}' ");
                        html.Append($"data-source-ready='{textObj.SourceAudioReady.ToString().ToLower()}' ");
                        html.Append($"data-target-ready='{textObj.TargetAudioReady.ToString().ToLower()}'");
                        if (textObj.FontSizeOverride.HasValue)
                        {
                            html.Append(FormattableString.Invariant($" data-font-size-override='{textObj.FontSizeOverride.Value}'"));
                        }
                        html.Append(">");
                        
                        // Add speaker icon - show if preload is enabled
                        bool isTtsPreloadEnabled = ConfigManager.Instance.IsTtsPreloadEnabled();
                        string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
                        bool preloadEnabled = isTtsPreloadEnabled && preloadMode != "Off";
                        
                        if (preloadEnabled)
                        {
                            if (!ConfigManager.Instance.IsTextBelowTtsMinChars(textObj.Text))
                            {
                                // Determine which audio should be shown for current mode
                                bool audioIsReady = false;
                                bool isSource = true;

                                // Only show speaker icon if the EXPECTED audio for current mode is ready
                                // (no visual fallback - but clicking will still try fallback audio)
                                if (isTranslated)
                                {
                                    // In translated mode, only show speaker if target audio is ready
                                    if (textObj.TargetAudioReady && !string.IsNullOrEmpty(textObj.TargetAudioFilePath))
                                    {
                                        audioIsReady = true;
                                        isSource = false;
                                    }
                                    else
                                    {
                                        // Show hourglass but set isSource for fallback playback if user clicks
                                        isSource = textObj.SourceAudioReady ? true : false;
                                    }
                                }
                                else
                                {
                                    // In source mode, only show speaker if source audio is ready
                                    if (textObj.SourceAudioReady && !string.IsNullOrEmpty(textObj.SourceAudioFilePath))
                                    {
                                        audioIsReady = true;
                                        isSource = true;
                                    }
                                }

                                // Show icon with appropriate state
                                string iconEmoji = audioIsReady ? ConfigManager.ICON_SPEAKER_READY : ConfigManager.ICON_SPEAKER_NOT_READY;
                                string iconClass = audioIsReady ? "audio-icon" : "audio-icon loading";
                                html.Append($"<div class='{iconClass}' data-is-ready='{audioIsReady.ToString().ToLower()}' onclick='handleAudioIconClick(\"{textObj.ID}\", {isSource.ToString().ToLower()})'>{iconEmoji}</div>");
                            }
                        }
                        
                        // Wrap text in span with per-bubble alignment and contenteditable
                        string contentEditableAttr = " contenteditable='true'";
                        string spanStyle = $"justify-content: {bubbleHAlignFlex}; text-align: {bubbleHAlign};";
                        html.Append($"<span class='text-content' style='{spanStyle}'{contentEditableAttr}>{encodedText}</span>");
                        
                        // Resize handles - always in DOM, shown/hidden via CSS based on .edit-mode class
                        html.Append("<div class='resize-handle nw' data-dir='nw'></div>");
                        html.Append("<div class='resize-handle ne' data-dir='ne'></div>");
                        html.Append("<div class='resize-handle sw' data-dir='sw'></div>");
                        html.Append("<div class='resize-handle se' data-dir='se'></div>");
                        html.AppendLine("</div>");
                    }
                }
            }
            
            html.AppendLine("</div>"); // close scroll-container
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
        
        private void checkMousePositionAndUpdateHitTesting(System.Windows.Point screenPoint)
        {
            // Check if mouse is over scrollbars to disable WebView2 hit testing
            // (Title bar check no longer needed: WebView2 is viewport-sized outside ScrollViewer)
            
            try
            {
                System.Windows.Point mousePosScrollViewer = imageScrollViewer.PointFromScreen(screenPoint);
                bool shouldDisableHitTesting = false;
                string currentRegion = "content";
                
                // Check scrollbars using ScrollViewer coordinates
                if (mousePosScrollViewer.X >= 0 && mousePosScrollViewer.Y >= 0 &&
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
    }
}
