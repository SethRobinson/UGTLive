using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Drawing.Imaging;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Text;
using System.Windows.Shell;
using System.Threading.Tasks;
using MessageBox = System.Windows.MessageBox;


namespace UGTLive
{
    public partial class MainWindow
    {
        private string? _currentMainWindowContextMenuTextObjectId;
        private string? _currentMainWindowContextMenuSelection;
        private string? _focusedTextObjectId;

        // Handle WebView2 web messages for context menu
        private void MainWindowOverlayWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
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
                        
                        ShowMainWindowOverlayContextMenu(textObjectId, x, y, selection);
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
                        
                        TextObject? textObj = GetMainWindowTextObjectById(textObjectId);
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
                            MonitorWindow.Instance?.RefreshOverlays();
                        }
                    }
                    else if (kind == "dragMove")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl2)
                            ? idEl2.GetString() ?? string.Empty : string.Empty;
                        double deltaX = root.TryGetProperty("deltaX", out System.Text.Json.JsonElement dxEl) ? dxEl.GetDouble() : 0;
                        double deltaY = root.TryGetProperty("deltaY", out System.Text.Json.JsonElement dyEl) ? dyEl.GetDouble() : 0;
                        
                        TextObject? textObj = GetMainWindowTextObjectById(textObjectId);
                        if (textObj != null)
                        {
                            double textScl = DisplayHelper.GetWindowsTextScaleFactor();
                            double actualDpi = GetActualDpiScale();
                            double scale = textScl * actualDpi;
                            textObj.OffsetX += deltaX * scale;
                            textObj.OffsetY += deltaY * scale;
                            MonitorWindow.Instance?.RefreshOverlays();
                        }
                    }
                    else if (kind == "dragResize")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl4)
                            ? idEl4.GetString() ?? string.Empty : string.Empty;
                        double deltaX = root.TryGetProperty("deltaX", out System.Text.Json.JsonElement dxEl2) ? dxEl2.GetDouble() : 0;
                        double deltaY = root.TryGetProperty("deltaY", out System.Text.Json.JsonElement dyEl2) ? dyEl2.GetDouble() : 0;
                        double deltaW = root.TryGetProperty("deltaW", out System.Text.Json.JsonElement dwEl) ? dwEl.GetDouble() : 0;
                        double deltaH = root.TryGetProperty("deltaH", out System.Text.Json.JsonElement dhEl) ? dhEl.GetDouble() : 0;
                        
                        TextObject? textObj = GetMainWindowTextObjectById(textObjectId);
                        if (textObj != null)
                        {
                            double textScl = DisplayHelper.GetWindowsTextScaleFactor();
                            double actualDpi = GetActualDpiScale();
                            double scale = textScl * actualDpi;
                            textObj.OffsetX += deltaX * scale;
                            textObj.OffsetY += deltaY * scale;
                            textObj.Width = Math.Max(20, textObj.Width + deltaW * scale);
                            textObj.Height = Math.Max(20, textObj.Height + deltaH * scale);
                            textObj.FontSizeOverride = null;
                            textObj.LastAutoFitSize = 0;
                            _lastOverlayHtml = string.Empty;
                            RefreshMainWindowOverlays();
                            MonitorWindow.Instance?.RefreshOverlays();
                        }
                    }
                    else if (kind == "autoFitSize")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl3)
                            ? idEl3.GetString() ?? string.Empty : string.Empty;
                        double fontSize = root.TryGetProperty("fontSize", out System.Text.Json.JsonElement fsEl) ? fsEl.GetDouble() : 0;
                        
                        TextObject? textObj = GetMainWindowTextObjectById(textObjectId);
                        if (textObj != null && fontSize > 0)
                        {
                            double textScl = DisplayHelper.GetWindowsTextScaleFactor();
                            double actualDpi = GetActualDpiScale();
                            textObj.LastAutoFitSize = fontSize * textScl * actualDpi;
                        }
                    }
                    else if (kind == "focusOverlay")
                    {
                        string textObjectId = root.TryGetProperty("textObjectId", out System.Text.Json.JsonElement idEl5)
                            ? idEl5.GetString() ?? string.Empty : string.Empty;
                        _focusedTextObjectId = textObjectId;
                        MonitorWindow.Instance?.SetFocusedTextObjectId(textObjectId);
                    }
                    else if (kind == "tabKey")
                    {
                        HotkeyManager.Instance.TriggerAction("toggle_overlay_mode");
                    }
                    else if (kind == "ctrlKey")
                    {
                        bool down = root.TryGetProperty("down", out System.Text.Json.JsonElement downEl) && downEl.GetBoolean();
                        MonitorWindow.Instance?.SendCtrlKeyToWebView(down);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling MainWindow overlay WebView message: {ex.Message}");
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
        
        private void MainWindowOverlayWebView_ContextMenuRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
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
        
        private void ShowMainWindowOverlayContextMenu(string textObjectId, double clientX, double clientY, string? selection)
        {
            try
            {
                if (string.IsNullOrEmpty(textObjectId))
                {
                    return;
                }
                
                _currentMainWindowContextMenuTextObjectId = textObjectId;
                _currentMainWindowContextMenuSelection = string.IsNullOrWhiteSpace(selection) ? null : selection.Trim();
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Windows.Point contentPoint = new System.Windows.Point(clientX, clientY);
                        System.Windows.Point relativeToWebView = textOverlayWebView.TranslatePoint(contentPoint, this);
                        System.Windows.Point screenPoint = this.PointToScreen(relativeToWebView);
                        
                        System.Windows.Controls.ContextMenu contextMenu = CreateMainWindowOverlayContextMenu();
                        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
                        contextMenu.HorizontalOffset = screenPoint.X;
                        contextMenu.VerticalOffset = screenPoint.Y;
                        contextMenu.IsOpen = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error showing MainWindow context menu: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preparing MainWindow context menu: {ex.Message}");
            }
        }
        
        private System.Windows.Controls.ContextMenu CreateMainWindowOverlayContextMenu()
        {
            System.Windows.Controls.ContextMenu contextMenu = new System.Windows.Controls.ContextMenu();
            
            // Copy menu item
            System.Windows.Controls.MenuItem copyMenuItem = new System.Windows.Controls.MenuItem();
            copyMenuItem.Header = "Copy";
            copyMenuItem.InputGestureText = "Ctrl+C";
            copyMenuItem.Click += MainWindowOverlayContextMenu_Copy_Click;
            contextMenu.Items.Add(copyMenuItem);
            
            // Copy Translated menu item (only shown when in Source mode)
            System.Windows.Controls.MenuItem copyTranslatedMenuItem = new System.Windows.Controls.MenuItem();
            copyTranslatedMenuItem.Header = "Copy Translated";
            copyTranslatedMenuItem.Click += MainWindowOverlayContextMenu_CopyTranslated_Click;
            contextMenu.Items.Add(copyTranslatedMenuItem);
            
            // Separator
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            
            // Font Size submenu
            System.Windows.Controls.MenuItem fontSizeMenu = new System.Windows.Controls.MenuItem();
            fontSizeMenu.Header = "Font Size";
            
            System.Windows.Controls.MenuItem fontSizeUp = new System.Windows.Controls.MenuItem();
            fontSizeUp.Header = "Increase (+)";
            fontSizeUp.InputGestureText = HotkeyManager.Instance.GetKeyboardComboForAction("font_size_increase");
            fontSizeUp.Click += (s, e) => mainWindowAdjustFontSize(_currentMainWindowContextMenuTextObjectId, 1.10);
            fontSizeMenu.Items.Add(fontSizeUp);
            
            System.Windows.Controls.MenuItem fontSizeDown = new System.Windows.Controls.MenuItem();
            fontSizeDown.Header = "Decrease (-)";
            fontSizeDown.InputGestureText = HotkeyManager.Instance.GetKeyboardComboForAction("font_size_decrease");
            fontSizeDown.Click += (s, e) => mainWindowAdjustFontSize(_currentMainWindowContextMenuTextObjectId, 0.90);
            fontSizeMenu.Items.Add(fontSizeDown);
            
            System.Windows.Controls.MenuItem fontSizeReset = new System.Windows.Controls.MenuItem();
            fontSizeReset.Header = "Reset (Auto)";
            fontSizeReset.Click += (s, e) =>
            {
                TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
                if (textObj != null)
                {
                    textObj.FontSizeOverride = null;
                    _lastOverlayHtml = string.Empty;
                    RefreshMainWindowOverlays();
                    MonitorWindow.Instance?.RefreshOverlays();
                }
            };
            fontSizeMenu.Items.Add(fontSizeReset);
            
            fontSizeMenu.SubmenuOpened += (s, e) => ExcludeSubmenuFromCapture(fontSizeMenu);
            contextMenu.Items.Add(fontSizeMenu);
            
            // Align submenu
            System.Windows.Controls.MenuItem alignMenu = new System.Windows.Controls.MenuItem();
            alignMenu.Header = "Align";
            
            System.Windows.Controls.MenuItem alignHLeft = new System.Windows.Controls.MenuItem();
            alignHLeft.Header = "H: Left";
            alignHLeft.Click += (s, e) => mainWindowSetAlignment(_currentMainWindowContextMenuTextObjectId, "left", null);
            alignMenu.Items.Add(alignHLeft);
            
            System.Windows.Controls.MenuItem alignHCenter = new System.Windows.Controls.MenuItem();
            alignHCenter.Header = "H: Center";
            alignHCenter.Click += (s, e) => mainWindowSetAlignment(_currentMainWindowContextMenuTextObjectId, "center", null);
            alignMenu.Items.Add(alignHCenter);
            
            System.Windows.Controls.MenuItem alignHRight = new System.Windows.Controls.MenuItem();
            alignHRight.Header = "H: Right";
            alignHRight.Click += (s, e) => mainWindowSetAlignment(_currentMainWindowContextMenuTextObjectId, "right", null);
            alignMenu.Items.Add(alignHRight);
            
            alignMenu.Items.Add(new System.Windows.Controls.Separator());
            
            System.Windows.Controls.MenuItem alignVTop = new System.Windows.Controls.MenuItem();
            alignVTop.Header = "V: Top";
            alignVTop.Click += (s, e) => mainWindowSetAlignment(_currentMainWindowContextMenuTextObjectId, null, "top");
            alignMenu.Items.Add(alignVTop);
            
            System.Windows.Controls.MenuItem alignVCenter = new System.Windows.Controls.MenuItem();
            alignVCenter.Header = "V: Center";
            alignVCenter.Click += (s, e) => mainWindowSetAlignment(_currentMainWindowContextMenuTextObjectId, null, "center");
            alignMenu.Items.Add(alignVCenter);
            
            System.Windows.Controls.MenuItem alignVBottom = new System.Windows.Controls.MenuItem();
            alignVBottom.Header = "V: Bottom";
            alignVBottom.Click += (s, e) => mainWindowSetAlignment(_currentMainWindowContextMenuTextObjectId, null, "bottom");
            alignMenu.Items.Add(alignVBottom);
            
            alignMenu.Items.Add(new System.Windows.Controls.Separator());
            
            System.Windows.Controls.MenuItem alignReset = new System.Windows.Controls.MenuItem();
            alignReset.Header = "Reset to Default";
            alignReset.Click += (s, e) =>
            {
                TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
                if (textObj != null)
                {
                    textObj.HorizontalAlignmentOverride = null;
                    textObj.VerticalAlignmentOverride = null;
                    RefreshMainWindowOverlays();
                    MonitorWindow.Instance?.RefreshOverlays();
                }
            };
            alignMenu.Items.Add(alignReset);
            
            alignMenu.SubmenuOpened += (s, e) => ExcludeSubmenuFromCapture(alignMenu);
            contextMenu.Items.Add(alignMenu);
            
            // Separator
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            
            // Lesson menu item (ChatGPT)
            System.Windows.Controls.MenuItem lessonMenuItem = new System.Windows.Controls.MenuItem();
            lessonMenuItem.Header = "Lesson";
            lessonMenuItem.Click += MainWindowOverlayContextMenu_Lesson_Click;
            contextMenu.Items.Add(lessonMenuItem);
            
            // Jisho lookup menu item (jisho.org)
            System.Windows.Controls.MenuItem lookupKanjiMenuItem = new System.Windows.Controls.MenuItem();
            lookupKanjiMenuItem.Header = "Jisho lookup";
            lookupKanjiMenuItem.Click += MainWindowOverlayContextMenu_LookupKanji_Click;
            contextMenu.Items.Add(lookupKanjiMenuItem);
            
            // Speak menu item
            System.Windows.Controls.MenuItem speakMenuItem = new System.Windows.Controls.MenuItem();
            speakMenuItem.Header = "Speak";
            speakMenuItem.Click += MainWindowOverlayContextMenu_Speak_Click;
            contextMenu.Items.Add(speakMenuItem);
            
            // Speak (source) menu item (only shown when in Translated mode)
            System.Windows.Controls.MenuItem speakSourceMenuItem = new System.Windows.Controls.MenuItem();
            speakSourceMenuItem.Header = "Speak (source)";
            speakSourceMenuItem.Click += MainWindowOverlayContextMenu_SpeakSource_Click;
            contextMenu.Items.Add(speakSourceMenuItem);
            
            // Update menu visibility when opened
            contextMenu.Opened += (s, e) =>
            {
                TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
                if (textObj != null)
                {
                    copyTranslatedMenuItem.Visibility = _currentOverlayMode == OverlayMode.Source ? Visibility.Visible : Visibility.Collapsed;
                    copyTranslatedMenuItem.IsEnabled = !string.IsNullOrEmpty(textObj.TextTranslated);
                    speakSourceMenuItem.Visibility = _currentOverlayMode == OverlayMode.Translated ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Exclude context menu popup from screen capture
                ExcludeContextMenuFromCapture(contextMenu);
            };
            
            contextMenu.Closed += (s, e) =>
            {
                _currentMainWindowContextMenuTextObjectId = null;
                _currentMainWindowContextMenuSelection = null;
            };
            
            return contextMenu;
        }
        
        private void mainWindowAdjustFontSize(string? textObjectId, double multiplier)
        {
            TextObject? textObj = GetMainWindowTextObjectById(textObjectId);
            if (textObj == null) return;
            
            double currentSize = textObj.FontSizeOverride 
                ?? (textObj.LastAutoFitSize > 0 ? textObj.LastAutoFitSize : Math.Max(8, Math.Min(128, textObj.Height * 0.7)));
            textObj.FontSizeOverride = Math.Max(4, Math.Min(200, currentSize * multiplier));
            RefreshMainWindowOverlays();
            MonitorWindow.Instance?.RefreshOverlays();
        }
        
        public void AdjustFocusedFontSize(double multiplier)
        {
            mainWindowAdjustFontSize(_focusedTextObjectId, multiplier);
        }
        
        public void SetFocusedTextObjectId(string? id)
        {
            _focusedTextObjectId = id;
        }
        
        private void mainWindowSetAlignment(string? textObjectId, string? hAlign, string? vAlign)
        {
            TextObject? textObj = GetMainWindowTextObjectById(textObjectId);
            if (textObj == null) return;
            
            if (hAlign != null) textObj.HorizontalAlignmentOverride = hAlign;
            if (vAlign != null) textObj.VerticalAlignmentOverride = vAlign;
            RefreshMainWindowOverlays();
            MonitorWindow.Instance?.RefreshOverlays();
        }
        
        private TextObject? GetMainWindowTextObjectById(string? id)
        {
            if (string.IsNullOrEmpty(id) || Logic.Instance == null)
            {
                return null;
            }
            
            var textObjects = Logic.Instance.GetTextObjects();
            return textObjects?.FirstOrDefault(t => t.ID == id);
        }
        
        private void MainWindowOverlayContextMenu_Copy_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToCopy = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection) 
                    ? _currentMainWindowContextMenuSelection 
                    : (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated) 
                        ? textObj.TextTranslated 
                        : textObj.Text);
                
                System.Windows.Forms.Clipboard.SetText(textToCopy);
                SetStatus("Text copied to clipboard");
            }
        }
        
        private void MainWindowOverlayContextMenu_CopyTranslated_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null && !string.IsNullOrEmpty(textObj.TextTranslated))
            {
                System.Windows.Forms.Clipboard.SetText(textObj.TextTranslated);
                SetStatus("Translated text copied to clipboard");
            }
        }
        
        private void MainWindowOverlayContextMenu_Lesson_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToLearn = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection) 
                    ? _currentMainWindowContextMenuSelection 
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
        
        private void MainWindowOverlayContextMenu_LookupKanji_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToLearn = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection) 
                    ? _currentMainWindowContextMenuSelection 
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
        
        private async void MainWindowOverlayContextMenu_Speak_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                string textToSpeak = !string.IsNullOrWhiteSpace(_currentMainWindowContextMenuSelection)
                    ? _currentMainWindowContextMenuSelection
                    : (_currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated)
                        ? textObj.TextTranslated
                        : textObj.Text);
                
                if (!string.IsNullOrWhiteSpace(textToSpeak))
                {
                    await TtsServiceFactory.CreateService().SpeakText(textToSpeak);
                }
            }
        }
        
        private async void MainWindowOverlayContextMenu_SpeakSource_Click(object sender, RoutedEventArgs e)
        {
            TextObject? textObj = GetMainWindowTextObjectById(_currentMainWindowContextMenuTextObjectId);
            if (textObj != null)
            {
                // Always speak the source text (ignoring selection)
                string textToSpeak = textObj.Text;
                
                if (!string.IsNullOrWhiteSpace(textToSpeak))
                {
                    await TtsServiceFactory.CreateService().SpeakText(textToSpeak);
                }
            }
        }
    }
}
