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
        private string? _currentContextMenuTextObjectId;
        private string? _currentContextMenuSelection;
        private string? _focusedTextObjectId;
        
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
                
                if (root.TryGetProperty("kind", out System.Text.Json.JsonElement kindElement))
                {
                    string kind = kindElement.GetString() ?? "";
                    
                    if (kind == "contextmenu")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idElement) 
                            ? idElement.GetString() ?? string.Empty : string.Empty;
                        double x = root.TryGetProperty("x", out System.Text.Json.JsonElement xElement) ? xElement.GetDouble() : 0;
                        double y = root.TryGetProperty("y", out System.Text.Json.JsonElement yElement) ? yElement.GetDouble() : 0;
                        string selection = root.TryGetProperty("selection", out System.Text.Json.JsonElement selectionElement)
                            ? selectionElement.GetString() ?? string.Empty : string.Empty;
                        
                        ShowOverlayContextMenu(textObjectId, x, y, selection);
                    }
                    else if (kind == "playAudio")
                    {
                        string audioPath = root.TryGetProperty("audioPath", out System.Text.Json.JsonElement audioPathElement)
                            ? audioPathElement.GetString() ?? string.Empty : string.Empty;
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement textObjectIdElement)
                            ? textObjectIdElement.GetString() ?? string.Empty : string.Empty;
                        
                        if (!string.IsNullOrEmpty(audioPath))
                        {
                            // Play audio - the AudioPlaybackManager event will update icons
                            _ = PlayAudioAndUpdateIcon(audioPath, textObjectId);
                        }
                    }
                    else if (kind == "stopAudio")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement textObjectIdElement)
                            ? textObjectIdElement.GetString() ?? string.Empty : string.Empty;
                        
                        AudioPlaybackManager.Instance.StopCurrentPlayback();
                        
                        if (!string.IsNullOrEmpty(textObjectId))
                        {
                            UpdateAudioIconInWebView(textObjectId, false);
                        }
                    }
                    else if (kind == "editText")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl)
                            ? idEl.GetString() ?? string.Empty : string.Empty;
                        string newText = root.TryGetProperty("newText", out System.Text.Json.JsonElement textEl)
                            ? textEl.GetString() ?? string.Empty : string.Empty;
                        
                        TextObject? textObj = GetTextObjectById(textObjectId);
                        if (textObj != null && !string.IsNullOrEmpty(newText))
                        {
                            if (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated))
                            {
                                textObj.TextTranslated = newText.Trim();
                            }
                            else
                            {
                                textObj.Text = newText.Trim();
                            }
                            MainWindow.Instance?.RefreshMainWindowOverlays();
                        }
                    }
                    else if (kind == "dragMove")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl2)
                            ? idEl2.GetString() ?? string.Empty : string.Empty;
                        double deltaX = root.TryGetProperty("deltaX", out System.Text.Json.JsonElement dxEl) ? dxEl.GetDouble() : 0;
                        double deltaY = root.TryGetProperty("deltaY", out System.Text.Json.JsonElement dyEl) ? dyEl.GetDouble() : 0;
                        
                        TextObject? textObj = GetTextObjectById(textObjectId);
                        if (textObj != null)
                        {
                            textObj.OffsetX += deltaX;
                            textObj.OffsetY += deltaY;
                            MainWindow.Instance?.RefreshMainWindowOverlays();
                        }
                    }
                    else if (kind == "dragResize")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl3)
                            ? idEl3.GetString() ?? string.Empty : string.Empty;
                        double deltaX = root.TryGetProperty("deltaX", out System.Text.Json.JsonElement dxEl2) ? dxEl2.GetDouble() : 0;
                        double deltaY = root.TryGetProperty("deltaY", out System.Text.Json.JsonElement dyEl2) ? dyEl2.GetDouble() : 0;
                        double deltaW = root.TryGetProperty("deltaW", out System.Text.Json.JsonElement dwEl) ? dwEl.GetDouble() : 0;
                        double deltaH = root.TryGetProperty("deltaH", out System.Text.Json.JsonElement dhEl) ? dhEl.GetDouble() : 0;
                        
                        TextObject? textObj = GetTextObjectById(textObjectId);
                        if (textObj != null)
                        {
                            textObj.OffsetX += deltaX;
                            textObj.OffsetY += deltaY;
                            textObj.Width = Math.Max(20, textObj.Width + deltaW);
                            textObj.Height = Math.Max(20, textObj.Height + deltaH);
                            textObj.FontSizeOverride = null;
                            textObj.LastAutoFitSize = 0;
                            _lastOverlayHtml = null;
                            RefreshOverlays();
                            MainWindow.Instance?.RefreshMainWindowOverlays();
                        }
                    }
                    else if (kind == "autoFitSize")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl3)
                            ? idEl3.GetString() ?? string.Empty : string.Empty;
                        double fontSize = root.TryGetProperty("fontSize", out System.Text.Json.JsonElement fsEl) ? fsEl.GetDouble() : 0;
                        
                        TextObject? textObj = GetTextObjectById(textObjectId);
                        if (textObj != null && fontSize > 0)
                        {
                            textObj.LastAutoFitSize = fontSize;
                        }
                    }
                    else if (kind == "focusOverlay")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl4)
                            ? idEl4.GetString() ?? string.Empty : string.Empty;
                        _focusedTextObjectId = textObjectId;
                        MainWindow.Instance?.SetFocusedTextObjectId(textObjectId);
                    }
                    else if (kind == "tabKey")
                    {
                        HotkeyManager.Instance.TriggerAction("toggle_overlay_mode");
                    }
                    else if (kind == "ctrlKey")
                    {
                        bool down = root.TryGetProperty("down", out System.Text.Json.JsonElement downEl) && downEl.GetBoolean();
                        MainWindow.Instance?.SendCtrlKeyToWebView(down);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling overlay WebView message: {ex.Message}");
            }
        }
        
        private void UpdateAudioIconInWebView(string textObjectId, bool isPlaying)
        {
            try
            {
                if (textOverlayWebView?.CoreWebView2 != null)
                {
                    string script = $"updateAudioIcon('{textObjectId}', {isPlaying.ToString().ToLower()});";
                    textOverlayWebView.CoreWebView2.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating audio icon in WebView: {ex.Message}");
            }
        }

        public void UpdateAudioReadyState(string textObjectId, bool isSourceUpdate, string audioPath)
        {
            try
            {
                // Ensure UI thread
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => UpdateAudioReadyState(textObjectId, isSourceUpdate, audioPath));
                    return;
                }

                if (textOverlayWebView?.CoreWebView2 != null)
                {
                     // Find the text object
                     var textObjects = Logic.Instance?.GetTextObjects();
                     var textObj = textObjects?.FirstOrDefault(t => t.ID == textObjectId);
                     
                     if (textObj == null) return;

                     if (ConfigManager.Instance.IsTextBelowTtsMinChars(textObj.Text))
                         return;
                     
                     // Determine display state
                     bool isTranslated = _currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated);
                     
                     bool audioIsReady = false;
                     bool isSourceForClick = true;
                     
                     if (isTranslated)
                     {
                         // In translated mode, only show speaker if target audio is ready
                         if (textObj.TargetAudioReady && !string.IsNullOrEmpty(textObj.TargetAudioFilePath))
                         {
                             audioIsReady = true;
                             isSourceForClick = false;
                         }
                         else
                         {
                             // Show hourglass but set isSource for fallback playback if user clicks
                             isSourceForClick = textObj.SourceAudioReady ? true : false;
                         }
                     }
                     else
                     {
                         // In source mode, only show speaker if source audio is ready
                         if (textObj.SourceAudioReady && !string.IsNullOrEmpty(textObj.SourceAudioFilePath))
                         {
                             audioIsReady = true;
                             isSourceForClick = true;
                         }
                     }
                     
                     string iconReady = ConfigManager.ICON_SPEAKER_READY;
                     string iconNotReady = ConfigManager.ICON_SPEAKER_NOT_READY;
                     
                     // Escape backslashes for JavaScript string
                     string escapedAudioPath = audioPath.Replace("\\", "\\\\").Replace("'", "\\'");
                     string script = $"setAudioState('{textObjectId}', {audioIsReady.ToString().ToLower()}, {isSourceForClick.ToString().ToLower()}, '{escapedAudioPath}', {isSourceUpdate.ToString().ToLower()}, '{iconReady}', '{iconNotReady}');";
                     
                     textOverlayWebView.CoreWebView2.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating audio ready state: {ex.Message}");
            }
        }
        
        private async Task PlayAudioAndUpdateIcon(string audioPath, string textObjectId)
        {
            try
            {
                await AudioPlaybackManager.Instance.PlayAudioFileAsync(audioPath, textObjectId);
                
                // Update icon back to speaker when playback finishes
                if (!string.IsNullOrEmpty(textObjectId))
                {
                    UpdateAudioIconInWebView(textObjectId, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing audio: {ex.Message}");
                if (!string.IsNullOrEmpty(textObjectId))
                {
                    UpdateAudioIconInWebView(textObjectId, false);
                }
            }
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
            copyMenuItem.InputGestureText = "Ctrl+C";
            copyMenuItem.Click += OverlayContextMenu_Copy_Click;
            contextMenu.Items.Add(copyMenuItem);
            
            // Copy Translated menu item (only shown when in Source mode)
            MenuItem copyTranslatedMenuItem = new MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += OverlayContextMenu_CopyTranslated_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Separator
            contextMenu.Items.Add(new Separator());
            
            // Font Size submenu
            MenuItem fontSizeMenu = new MenuItem();
            fontSizeMenu.Header = "Font Size";
            
            MenuItem fontSizeUp = new MenuItem();
            fontSizeUp.Header = "Increase (+)";
            fontSizeUp.InputGestureText = HotkeyManager.Instance.GetKeyboardComboForAction("font_size_increase");
            fontSizeUp.Click += (s, e) => adjustFontSizeForTextObject(_currentContextMenuTextObjectId, 1.10);
            fontSizeMenu.Items.Add(fontSizeUp);
            
            MenuItem fontSizeDown = new MenuItem();
            fontSizeDown.Header = "Decrease (-)";
            fontSizeDown.InputGestureText = HotkeyManager.Instance.GetKeyboardComboForAction("font_size_decrease");
            fontSizeDown.Click += (s, e) => adjustFontSizeForTextObject(_currentContextMenuTextObjectId, 0.90);
            fontSizeMenu.Items.Add(fontSizeDown);
            
            MenuItem fontSizeReset = new MenuItem();
            fontSizeReset.Header = "Reset (Auto)";
            fontSizeReset.Click += (s, e) => resetFontSizeForTextObject(_currentContextMenuTextObjectId);
            fontSizeMenu.Items.Add(fontSizeReset);
            
            contextMenu.Items.Add(fontSizeMenu);
            
            // Align submenu
            MenuItem alignMenu = new MenuItem();
            alignMenu.Header = "Align";
            
            MenuItem alignHLeft = new MenuItem();
            alignHLeft.Header = "H: Left";
            alignHLeft.Click += (s, e) => setAlignmentForTextObject(_currentContextMenuTextObjectId, "left", null);
            alignMenu.Items.Add(alignHLeft);
            
            MenuItem alignHCenter = new MenuItem();
            alignHCenter.Header = "H: Center";
            alignHCenter.Click += (s, e) => setAlignmentForTextObject(_currentContextMenuTextObjectId, "center", null);
            alignMenu.Items.Add(alignHCenter);
            
            MenuItem alignHRight = new MenuItem();
            alignHRight.Header = "H: Right";
            alignHRight.Click += (s, e) => setAlignmentForTextObject(_currentContextMenuTextObjectId, "right", null);
            alignMenu.Items.Add(alignHRight);
            
            alignMenu.Items.Add(new Separator());
            
            MenuItem alignVTop = new MenuItem();
            alignVTop.Header = "V: Top";
            alignVTop.Click += (s, e) => setAlignmentForTextObject(_currentContextMenuTextObjectId, null, "top");
            alignMenu.Items.Add(alignVTop);
            
            MenuItem alignVCenter = new MenuItem();
            alignVCenter.Header = "V: Center";
            alignVCenter.Click += (s, e) => setAlignmentForTextObject(_currentContextMenuTextObjectId, null, "center");
            alignMenu.Items.Add(alignVCenter);
            
            MenuItem alignVBottom = new MenuItem();
            alignVBottom.Header = "V: Bottom";
            alignVBottom.Click += (s, e) => setAlignmentForTextObject(_currentContextMenuTextObjectId, null, "bottom");
            alignMenu.Items.Add(alignVBottom);
            
            alignMenu.Items.Add(new Separator());
            
            MenuItem alignReset = new MenuItem();
            alignReset.Header = "Reset to Default";
            alignReset.Click += (s, e) =>
            {
                TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
                if (textObj != null)
                {
                    textObj.HorizontalAlignmentOverride = null;
                    textObj.VerticalAlignmentOverride = null;
                    RefreshOverlays();
                    MainWindow.Instance?.RefreshMainWindowOverlays();
                }
            };
            alignMenu.Items.Add(alignReset);
            
            contextMenu.Items.Add(alignMenu);
            
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
        
        private void adjustFontSizeForTextObject(string? textObjectId, double multiplier)
        {
            TextObject? textObj = GetTextObjectById(textObjectId);
            if (textObj == null) return;
            
            double currentSize = textObj.FontSizeOverride 
                ?? (textObj.LastAutoFitSize > 0 ? textObj.LastAutoFitSize : Math.Max(8, Math.Min(128, textObj.Height * 0.7)));
            textObj.FontSizeOverride = Math.Max(4, Math.Min(200, currentSize * multiplier));
            RefreshOverlays();
            MainWindow.Instance?.RefreshMainWindowOverlays();
        }
        
        private void resetFontSizeForTextObject(string? textObjectId)
        {
            TextObject? textObj = GetTextObjectById(textObjectId);
            if (textObj == null) return;
            
            textObj.FontSizeOverride = null;
            _lastOverlayHtml = null;
            RefreshOverlays();
            MainWindow.Instance?.RefreshMainWindowOverlays();
        }
        
        public void AdjustFocusedFontSize(double multiplier)
        {
            adjustFontSizeForTextObject(_focusedTextObjectId, multiplier);
        }
        
        public void SetFocusedTextObjectId(string? id)
        {
            _focusedTextObjectId = id;
        }
        
        private void setAlignmentForTextObject(string? textObjectId, string? hAlign, string? vAlign)

        {
            TextObject? textObj = GetTextObjectById(textObjectId);
            if (textObj == null) return;
            
            if (hAlign != null) textObj.HorizontalAlignmentOverride = hAlign;
            if (vAlign != null) textObj.VerticalAlignmentOverride = vAlign;
            RefreshOverlays();
            MainWindow.Instance?.RefreshMainWindowOverlays();
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
            }
        }
        
        private void OverlayContextMenu_CopyTranslated_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetTextObjectById(_currentContextMenuTextObjectId);
            if (textObj != null && !string.IsNullOrEmpty(textObj.TextTranslated))
            {
                System.Windows.Forms.Clipboard.SetText(textObj.TextTranslated);
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
                    // Get prompt and URL templates from config
                    string promptTemplate = ConfigManager.Instance.GetLessonPromptTemplate();
                    string urlTemplate = ConfigManager.Instance.GetLessonUrlTemplate();
                    
                    // Format the prompt with the text to learn
                    string lessonPrompt = string.Format(promptTemplate, textToLearn);
                    string encodedPrompt = Uri.EscapeDataString(lessonPrompt);
                    
                    // Format the URL with the encoded prompt
                    string lessonUrl = string.Format(urlTemplate, encodedPrompt);
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = lessonUrl,
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
                    _ = TtsServiceFactory.CreateService().SpeakText(textToSpeak);
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
                    _ = TtsServiceFactory.CreateService().SpeakText(textToSpeak);
                }
            }
        }
        
        // Handler for application-level keyboard shortcuts
        private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            
            if (e.Key == System.Windows.Input.Key.LeftCtrl || e.Key == System.Windows.Input.Key.RightCtrl)
            {
                SendCtrlKeyToWebView(true);
                MainWindow.Instance?.SendCtrlKeyToWebView(true);
            }
            
            if (HotkeyManager.Instance.GetGlobalHotkeysEnabled())
            {
                bool handled = HotkeyManager.Instance.HandleKeyDownLocal(e.Key, modifiers);
                if (handled || e.Key == System.Windows.Input.Key.Tab)
                {
                    e.Handled = true;
                }
            }
            else
            {
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
                MainWindow.Instance?.SendCtrlKeyToWebView(false);
            }
        }
    }
}
