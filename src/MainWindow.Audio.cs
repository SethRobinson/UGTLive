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
        public void HandlePlayAllAudioButton()
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
                string overlayMode = ConfigManager.Instance.GetMainWindowOverlayMode();
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
                    Console.WriteLine($"AudioPlaybackManager_PlayAllStateChanged: isPlaying={isPlaying}, updating button");
                }
                if (playAllAudioButton != null)
                {
                    if (isPlaying)
                    {
                        playAllAudioButton.Content = "🔇 Stop";
                        playAllAudioButton.ToolTip = "Stop playing all audio";
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"Play All button updated to: 🔇 Stop");
                        }
                    }
                    else
                    {
                        playAllAudioButton.Content = "🔊 All";
                        playAllAudioButton.ToolTip = "Play all audio files in order";
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"Play All button updated to: 🔊 All");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("playAllAudioButton is null!");
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
                     var textObjects = Logic.Instance?.GetTextObjects();
                     var textObj = textObjects?.FirstOrDefault(t => t.ID == textObjectId);
                     
                     if (textObj == null) return;

                     if (ConfigManager.Instance.IsTextBelowTtsMinChars(textObj.Text))
                         return;
                     
                     bool isTranslated = _currentOverlayMode == OverlayMode.Translated && !string.IsNullOrEmpty(textObj.TextTranslated);
                     
                     bool audioIsReady = false;
                     bool isSourceForClick = true;
                     
                     if (isTranslated)
                     {
                         if (textObj.TargetAudioReady && !string.IsNullOrEmpty(textObj.TargetAudioFilePath))
                         {
                             audioIsReady = true;
                             isSourceForClick = false;
                         }
                         else
                         {
                             isSourceForClick = textObj.SourceAudioReady ? true : false;
                         }
                     }
                     else
                     {
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
                Console.WriteLine($"Error updating MainWindow audio ready state: {ex.Message}");
            }
        }

        private void PlayCompletionSoundIfEnabled()
        {
            if (!ConfigManager.Instance.IsCompletionSoundEnabled())
                return;

            try
            {
                string soundPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "audio", "translation_complete.wav");

                if (System.IO.File.Exists(soundPath))
                {
                    var player = new System.Media.SoundPlayer(soundPath);
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing completion sound: {ex.Message}");
            }
        }

        // Keep translation history even when ChatBox is closed
        private List<TranslationEntry> _translationHistory = new List<TranslationEntry>();
        
        // Accessor for ChatBoxWindow to get the translation history
        public List<TranslationEntry> GetTranslationHistory()
        {
            return _translationHistory;
        }
        
        // Method to clear translation history
        public void ClearTranslationHistory()
        {
            _translationHistory.Clear();
            Console.WriteLine("Translation history cleared from MainWindow");
        }

        // **** MODIFIED: Returns the ID of the added/updated entry ****
        public string AddTranslationToHistory(string originalText, string translatedText)
        {
            string entryId = string.Empty;
            bool entryUpdated = false;

            try
            {
                // Check if the new text is essentially the same as the last entry's original text
                // and if the new translated text is non-empty (avoid overwriting translation with empty)
                if (_translationHistory.Count > 0 && !string.IsNullOrEmpty(translatedText))
                {
                    var lastEntry = _translationHistory[_translationHistory.Count - 1]; // More direct access with List
                    
                    // Check if original texts match (case-insensitive, trimmed)
                    if (string.Equals(lastEntry.OriginalText?.Trim(), originalText?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        // Update existing entry ONLY if the new translation is different
                        // This prevents unnecessary updates if only the transcript arrived
                        if (!string.Equals(lastEntry.TranslatedText?.Trim(), translatedText?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            lastEntry.TranslatedText = translatedText ?? "";
                            lastEntry.Timestamp = DateTime.Now;
                            Console.WriteLine($"Updated last translation entry ID: {lastEntry.Id}");
                            entryId = lastEntry.Id;
                            entryUpdated = true;
                        }
                        else
                        {
                             // Texts match, but translation is the same. Return existing ID but mark as not needing UI refresh yet.
                             entryId = lastEntry.Id;
                             // entryUpdated remains false - UI doesn't need immediate full refresh
                             if (ConfigManager.Instance.GetLogExtraDebugStuff())
                             {
                                 Console.WriteLine($"Skipping update, translation same for ID: {lastEntry.Id}");
                             }
                        }
                    }
                }

                if (!entryUpdated && !string.IsNullOrEmpty(originalText)) // If not updated, it's a new entry (and original text isn't empty)
                {
                    var entry = new TranslationEntry
                    {
                        Id = Guid.NewGuid().ToString(), // Assign new ID
                        OriginalText = originalText,
                        TranslatedText = translatedText ?? "",
                        Timestamp = DateTime.Now
                    };
                    _translationHistory.Add(entry); // Use Add for List
                    entryId = entry.Id; // Store the new ID
                    entryUpdated = true; // Mark that we've handled this (new entry requires UI refresh)
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Added new translation entry ID: {entryId}");
                    }
                }

                // Keep history size limited
                int maxHistorySize = ConfigManager.Instance.GetChatBoxHistorySize();
                while (_translationHistory.Count > maxHistorySize)
                {
                    _translationHistory.RemoveAt(0); // Remove oldest entry
                }

                // Update ChatBoxWindow if an entry was actually added or updated
                if (entryUpdated)
                {
                    ChatBoxWindow.Instance?.UpdateChatHistory();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding/updating translation history: {ex.Message}");
            }
            
            return entryId; // Return the ID of the added or updated entry
        }
        
        // **** NEW: Method to update a specific entry by ID ****
        public void UpdateTranslationInHistory(string id, string newTranslatedText)
        {
            if (string.IsNullOrEmpty(id))
            {
                Console.WriteLine("UpdateTranslationInHistory called with empty ID.");
                return;
            }

            try
            {
                TranslationEntry? entryToUpdate = null;
                // Use LINQ to find the entry efficiently
                entryToUpdate = _translationHistory.FirstOrDefault(entry => entry.Id == id);

                if (entryToUpdate != null)
                {
                    // Update the translation and timestamp
                    entryToUpdate.TranslatedText = newTranslatedText;
                    entryToUpdate.Timestamp = DateTime.Now; // Update timestamp on modification
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        Console.WriteLine($"Updated translation for entry ID: {id}");

                    // Refresh the ChatBox UI
                    ChatBoxWindow.Instance?.UpdateChatHistory();
                }
                else
                {
                    Console.WriteLine($"Could not find translation entry with ID: {id} to update.");
                }
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Error updating translation history by ID: {ex.Message}");
            }
        }

        // Update BOTH the original and translated text of an entry by ID.
        // Used by the streaming translate session so the Japanese source and
        // the translation grow on the same line simultaneously.
        public void UpdateEntryInHistory(string id, string newOriginalText, string newTranslatedText)
        {
            if (string.IsNullOrEmpty(id)) return;

            try
            {
                var entryToUpdate = _translationHistory.FirstOrDefault(entry => entry.Id == id);
                if (entryToUpdate != null)
                {
                    if (!string.IsNullOrEmpty(newOriginalText))
                        entryToUpdate.OriginalText = newOriginalText;
                    if (!string.IsNullOrEmpty(newTranslatedText))
                        entryToUpdate.TranslatedText = newTranslatedText;
                    entryToUpdate.Timestamp = DateTime.Now;
                    ChatBoxWindow.Instance?.UpdateChatHistory();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateEntryInHistory for ID {id}: {ex.Message}");
            }
        }

        // Handle translation events from Logic
        private void Logic_TranslationCompleted(object? sender, TranslationEventArgs e)
        {
            AddTranslationToHistory(e.OriginalText, e.TranslatedText);
            PlayCompletionSoundIfEnabled();
        }

        // Load language settings from config (no UI updates needed, ConfigManager handles everything)
        private void LoadLanguageSettingsFromConfig()
        {
            try
            {
                string savedSourceLanguage = ConfigManager.Instance.GetSourceLanguage();
                string savedTargetLanguage = ConfigManager.Instance.GetTargetLanguage();
                
                Console.WriteLine($"Loaded languages from config - Source: {savedSourceLanguage}, Target: {savedTargetLanguage}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading language settings from config: {ex.Message}");
            }
        }
    }
}
