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
        private bool isListening = false;
        private OpenAIRealtimeAudioServiceWhisper? openAIRealtimeAudioService = null;

        public bool IsListening => isListening;

        public void RestartListenIfActive()
        {
            if (!isListening) return;

            Console.WriteLine("Restarting Listen service due to settings change...");
            openAIRealtimeAudioService?.Stop();

            if (openAIRealtimeAudioService == null)
                openAIRealtimeAudioService = new OpenAIRealtimeAudioServiceWhisper();

            openAIRealtimeAudioService.StartRealtimeAudioService(
                OnOpenAITranscriptReceived_Initial,
                OnOpenAITranslationUpdate_WithId,
                OnOpenAIPartialTranscript,
                false,
                OnOpenAIListenUpsert);
        }

        public void HandleListenButton()
        {
            var btn = listenButton;
            if (isListening)
            {
                isListening = false;
                if (btn != null)
                {
                    btn.Content = "Listen";
                    btn.Background = new SolidColorBrush(Color.FromRgb(95, 95, 95));
                }
                openAIRealtimeAudioService?.Stop();
            }
            else
            {
                isListening = true;
                if (btn != null)
                {
                    btn.Content = "Stop Listening";
                    btn.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
                }

                var chatBoxWin = ChatBoxWindow.Instance;
                if (chatBoxWin == null || !chatBoxWin.IsVisible)
                {
                    MessageBox.Show(this, "The Listen button listens for audio and shows the detected dialog in the Transcript window. Please open it to see the detected dialog.", "Transcript Not Visible", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (openAIRealtimeAudioService == null)
                    openAIRealtimeAudioService = new OpenAIRealtimeAudioServiceWhisper();
                
                openAIRealtimeAudioService.StartRealtimeAudioService(
                    OnOpenAITranscriptReceived_Initial,
                    OnOpenAITranslationUpdate_WithId,
                    OnOpenAIPartialTranscript,
                    false,
                    OnOpenAIListenUpsert);
            }
        }

        private string OnOpenAITranscriptReceived_Initial(string text, string initialTranslation)
        {
            const string audioPrefix = "🎤 ";
            string idToReturn = string.Empty;
            Dispatcher.Invoke(() =>
            {
                string originalWithIcon = string.IsNullOrWhiteSpace(text) ? string.Empty : audioPrefix + text;
                string translatedWithIcon = string.IsNullOrWhiteSpace(initialTranslation) ? string.Empty : audioPrefix + initialTranslation;

                // Replace the last partial entry if one exists (streaming partial -> final transition)
                if (_translationHistory.Count > 0)
                {
                    var lastEntry = _translationHistory[_translationHistory.Count - 1];
                    if (string.IsNullOrEmpty(lastEntry.TranslatedText) &&
                        lastEntry.OriginalText != null &&
                        lastEntry.OriginalText.StartsWith(audioPrefix) &&
                        lastEntry.OriginalText.EndsWith("..."))
                    {
                        lastEntry.OriginalText = originalWithIcon;
                        lastEntry.TranslatedText = translatedWithIcon;
                        lastEntry.Timestamp = DateTime.Now;
                        idToReturn = lastEntry.Id;
                        ChatBoxWindow.Instance?.UpdateChatHistory();
                        return;
                    }
                }

                idToReturn = AddTranslationToHistory(originalWithIcon, translatedWithIcon);
            });
            return idToReturn;
        }
        
        // Dual-mode interleave sink (streaming upsert). Empty id => create a new
        // standalone line and return its id; non-empty id => update that line's
        // text in place so it streams live. isTranslation routes the text to the
        // source or translated column so the ChatBox colors the two streams
        // differently (and source-only / translated-only display modes work).
        // Distinct emoji prefixes (not just color) keep the streams separable
        // for colorblind users: 🎤 = heard source, 🌐 = translation.
        private string OnOpenAIListenUpsert(string id, string text, bool isTranslation)
        {
            const string sourcePrefix = "🎤 ";
            const string translationPrefix = "🌐 ";
            string body = (text ?? string.Empty).Trim();

            return Dispatcher.Invoke(() =>
            {
                string original = isTranslation ? "" : sourcePrefix + body;
                string translated = isTranslation ? translationPrefix + body : "";

                if (!string.IsNullOrEmpty(id))
                {
                    var existing = _translationHistory.FirstOrDefault(en => en.Id == id);
                    if (existing != null)
                    {
                        if (isTranslation) existing.TranslatedText = translated;
                        else existing.OriginalText = original;
                        existing.Timestamp = DateTime.Now;
                        ChatBoxWindow.Instance?.UpdateChatHistory();
                        return existing.Id;
                    }
                    // Line scrolled out of the capped history while streaming;
                    // fall through and start a fresh line.
                }

                var entry = new TranslationEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    OriginalText = original,
                    TranslatedText = translated,
                    Timestamp = DateTime.Now
                };
                _translationHistory.Add(entry);

                int maxHistorySize = ConfigManager.Instance.GetChatBoxHistorySize();
                while (_translationHistory.Count > maxHistorySize)
                {
                    _translationHistory.RemoveAt(0);
                }

                ChatBoxWindow.Instance?.UpdateChatHistory();
                return entry.Id;
            });
        }

        private void OnOpenAIPartialTranscript(string partialText)
        {
            if (string.IsNullOrWhiteSpace(partialText)) return;

            const string audioPrefix = "🎤 ";
            Dispatcher.Invoke(() =>
            {
                string displayText = audioPrefix + partialText + "...";
                // Update the last entry if it's a partial, or add a new one
                if (_translationHistory.Count > 0)
                {
                    var lastEntry = _translationHistory[_translationHistory.Count - 1];
                    // Only update if the last entry looks like a partial (empty translation, same prefix)
                    if (string.IsNullOrEmpty(lastEntry.TranslatedText) &&
                        lastEntry.OriginalText != null &&
                        lastEntry.OriginalText.StartsWith(audioPrefix) &&
                        lastEntry.OriginalText.EndsWith("..."))
                    {
                        lastEntry.OriginalText = displayText;
                        lastEntry.Timestamp = DateTime.Now;
                        ChatBoxWindow.Instance?.UpdateChatHistory();
                        return;
                    }
                }
                // Add new partial entry
                var entry = new TranslationEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    OriginalText = displayText,
                    TranslatedText = "",
                    Timestamp = DateTime.Now
                };
                _translationHistory.Add(entry);

                int maxHistorySize = ConfigManager.Instance.GetChatBoxHistorySize();
                while (_translationHistory.Count > maxHistorySize)
                {
                    _translationHistory.RemoveAt(0);
                }

                ChatBoxWindow.Instance?.UpdateChatHistory();
            });
        }

        // Callback to handle translation updates via ID
        private void OnOpenAITranslationUpdate_WithId(string lineId, string originalText, string translatedText)
        {
            if (string.IsNullOrEmpty(lineId))
            {
                Console.WriteLine("OnOpenAITranslationUpdate_WithId called with empty lineId.");
                return; // Can't update if no ID
            }

            const string audioPrefix = "🎤 ";
            Dispatcher.Invoke(() =>
            {
                string originalWithIcon = string.IsNullOrWhiteSpace(originalText) ? string.Empty : audioPrefix + originalText;
                string translatedWithIcon = string.IsNullOrWhiteSpace(translatedText) ? string.Empty : audioPrefix + translatedText;
                // Update both columns so the source (e.g. Japanese) and the
                // translation grow on the same line at the same time.
                UpdateEntryInHistory(lineId, originalWithIcon, translatedWithIcon);
            });
        }

        // --- Floating Toolbar Management ---

        private double _toolbarOffsetX = 5;  // default: 5px right of main window's right edge
        private double _toolbarOffsetY = 0;  // default: aligned with main window top

        private void CreateAndShowToolbar()
        {
            _toolbarWindow = new ToolbarWindow();
            _toolbarWindow.Owner = this;
            _toolbarWindow.Show();

            // Load persisted offset
            _toolbarOffsetX = ConfigManager.Instance.GetToolbarOffsetX();
            _toolbarOffsetY = ConfigManager.Instance.GetToolbarOffsetY();

            UpdateToolbarPosition();
        }

        private void UpdateToolbarPosition()
        {
            if (_toolbarWindow == null || _isToolbarDragging)
                return;

            double newLeft = this.Left + this.Width + _toolbarOffsetX;
            double newTop = this.Top + _toolbarOffsetY;

            double tbWidth = _toolbarWindow.ActualWidth > 0 ? _toolbarWindow.ActualWidth : _toolbarWindow.Width;
            double tbHeight = _toolbarWindow.ActualHeight > 0 ? _toolbarWindow.ActualHeight : _toolbarWindow.Height;
            if (double.IsNaN(tbWidth) || tbWidth <= 0) tbWidth = 60;
            if (double.IsNaN(tbHeight) || tbHeight <= 0) tbHeight = 400;

            if (!ConfigManager.IsWindowBoundsValid(newLeft, newTop, tbWidth, tbHeight, minVisiblePixels: 40))
            {
                _toolbarOffsetX = 5;
                _toolbarOffsetY = 0;
                newLeft = this.Left + this.Width + _toolbarOffsetX;
                newTop = this.Top + _toolbarOffsetY;

                if (!ConfigManager.IsWindowBoundsValid(newLeft, newTop, tbWidth, tbHeight, minVisiblePixels: 40))
                {
                    newLeft = this.Left - tbWidth - 5;
                    newTop = this.Top;

                    if (!ConfigManager.IsWindowBoundsValid(newLeft, newTop, tbWidth, tbHeight, minVisiblePixels: 40))
                    {
                        var workArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
                        if (workArea.HasValue)
                        {
                            newLeft = workArea.Value.Right - tbWidth - 10;
                            newTop = workArea.Value.Top + 10;
                        }
                        else
                        {
                            newLeft = 100;
                            newTop = 100;
                        }
                    }

                    _toolbarOffsetX = newLeft - (this.Left + this.Width);
                    _toolbarOffsetY = newTop - this.Top;
                }

                ConfigManager.Instance.SetToolbarOffset(_toolbarOffsetX, _toolbarOffsetY);
                Console.WriteLine($"Toolbar position reset (was off-screen): Left={newLeft}, Top={newTop}");
            }

            _toolbarWindow.Left = newLeft;
            _toolbarWindow.Top = newTop;
        }

        public void SaveToolbarOffset()
        {
            if (_toolbarWindow == null)
                return;

            _toolbarOffsetX = _toolbarWindow.Left - (this.Left + this.Width);
            _toolbarOffsetY = _toolbarWindow.Top - this.Top;

            ConfigManager.Instance.SetToolbarOffset(_toolbarOffsetX, _toolbarOffsetY);
        }

        public void ResetToolbarPosition()
        {
            _toolbarOffsetX = 5;
            _toolbarOffsetY = 0;
            ConfigManager.Instance.SetToolbarOffset(_toolbarOffsetX, _toolbarOffsetY);
            UpdateToolbarPosition();
        }
    }
}
