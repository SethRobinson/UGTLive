using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using System.Threading;
using Application = System.Windows.Application;

namespace UGTLive
{
    public class AudioPlaybackManager
    {
        private static AudioPlaybackManager? _instance;
        private IWavePlayer? _currentPlayer;
        private AudioFileReader? _currentAudioFile;
        private bool _isPlaying = false;
        private bool _isPlayingAll = false;
        private bool _autoPlayTriggered = false;
        private CancellationTokenSource? _playbackCancellationToken;
        private readonly object _playbackLock = new object();
        private string? _currentPlayingTextObjectId = null;
        
        // Event to notify when playback state changes
        public event EventHandler<bool>? PlayAllStateChanged;
        // Event to notify which text object is currently playing
        public event EventHandler<string?>? CurrentPlayingTextObjectChanged;
        
        public static AudioPlaybackManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AudioPlaybackManager();
                }
                return _instance;
            }
        }
        
        private AudioPlaybackManager()
        {
        }
        
        public async Task PlayAudioFileAsync(string filePath, string? textObjectId = null, bool isPartOfPlayAll = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                Console.WriteLine($"Audio file not found: {filePath}");
                return;
            }
            
            // Stop any currently playing audio first
            // Pass the new textObjectId so we can transition directly without intermediate null state
            if (!isPartOfPlayAll)
            {
                StopCurrentPlayback(textObjectId);
            }
            else
            {
                // Just stop the current player without resetting _isPlayingAll
                lock (_playbackLock)
                {
                    IWavePlayer? playerToStop = _currentPlayer;
                    if (playerToStop != null)
                    {
                        try
                        {
                            playerToStop.Stop();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error stopping playback: {ex.Message}");
                        }
                    }
                    CleanupCurrentPlayback();
                    _isPlaying = false;
                }
                // Transition directly to new playing state if provided
                lock (_playbackLock)
                {
                    _currentPlayingTextObjectId = string.IsNullOrEmpty(textObjectId) ? null : textObjectId;
                }
                OnCurrentPlayingTextObjectChanged(string.IsNullOrEmpty(textObjectId) ? null : textObjectId);
            }
            
            // Play the new audio file
            await Task.Run(() =>
            {
                IWavePlayer? player = null;
                bool playbackStarted = false;
                
                lock (_playbackLock)
                {
                    try
                    {
                        _isPlaying = true;
                        _currentPlayer = new WaveOutEvent();
                        _currentAudioFile = new AudioFileReader(filePath);
                        
                        if (_currentPlayer == null || _currentAudioFile == null)
                        {
                            Console.WriteLine("Failed to initialize audio player or file reader");
                            _isPlaying = false;
                            CleanupCurrentPlayback();
                            return;
                        }
                        
                        _currentPlayer.Init(_currentAudioFile);
                        player = _currentPlayer;
                        
                        // Check again after Init in case it failed
                        if (_currentPlayer == null)
                        {
                            Console.WriteLine("Audio player became null after Init");
                            _isPlaying = false;
                            CleanupCurrentPlayback();
                            return;
                        }
                        
                        // Capture the textObjectId for this playback session
                        string? thisSessionTextObjectId = textObjectId;
                        
                        _currentPlayer.PlaybackStopped += (sender, args) =>
                        {
                            lock (_playbackLock)
                            {
                                _isPlaying = false;
                                CleanupCurrentPlayback();
                            }
                            // Only notify that playback stopped if this was the currently playing audio
                            // (prevents old audio's stopped event from clearing new audio's playing state)
                            lock (_playbackLock)
                            {
                                if (_currentPlayingTextObjectId == thisSessionTextObjectId)
                                {
                                    _currentPlayingTextObjectId = null;
                                    OnCurrentPlayingTextObjectChanged(null);
                                }
                            }
                        };
                        
                        // Final null check before playing
                        if (_currentPlayer != null)
                        {
                            _currentPlayer.Play();
                            playbackStarted = true;
                        }
                        else
                        {
                            Console.WriteLine("Audio player is null before Play() call");
                            _isPlaying = false;
                            CleanupCurrentPlayback();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error playing audio file: {ex.Message}");
                        _isPlaying = false;
                        CleanupCurrentPlayback();
                        return;
                    }
                }
                
                // Wait for playback to complete (outside the lock to avoid blocking)
                if (playbackStarted && player != null)
                {
                    try
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"PlayAudioFileAsync: Starting wait loop for {filePath}");
                        }
                        int waitCount = 0;
                        while (true)
                        {
                            // Check cancellation token first
                            if (cancellationToken.IsCancellationRequested)
                            {
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"PlayAudioFileAsync: Cancellation requested, stopping playback");
                                }
                                lock (_playbackLock)
                                {
                                    if (_currentPlayer != null)
                                    {
                                        try
                                        {
                                            _currentPlayer.Stop();
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error stopping player on cancellation: {ex.Message}");
                                        }
                                    }
                                    _isPlaying = false;
                                    CleanupCurrentPlayback();
                                }
                                break;
                            }
                            
                            bool stillPlaying;
                            PlaybackState state;
                            
                            lock (_playbackLock)
                            {
                                stillPlaying = _isPlaying;
                                state = player.PlaybackState;
                            }
                            
                            // Log every 10 iterations (1 second) for debugging
                            if (ConfigManager.Instance.GetLogExtraDebugStuff() && waitCount % 10 == 0)
                            {
                                Console.WriteLine($"PlayAudioFileAsync: Wait loop iteration {waitCount}, stillPlaying={stillPlaying}, state={state}");
                            }
                            waitCount++;
                            
                            // Break if playback has stopped (either flag is false or state is not playing)
                            if (!stillPlaying)
                            {
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"PlayAudioFileAsync: Playback stopped (stillPlaying=false)");
                                }
                                break;
                            }
                            
                            if (state != PlaybackState.Playing)
                            {
                                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                                {
                                    Console.WriteLine($"PlayAudioFileAsync: Playback stopped (state={state})");
                                }
                                break;
                            }
                            
                            Thread.Sleep(100);
                        }
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"PlayAudioFileAsync: Wait loop completed after {waitCount * 100}ms");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error waiting for playback: {ex.Message}");
                    }
                }
            });
        }
        
        public void StopCurrentPlayback(string? nextTextObjectId = null)
        {
            bool wasPlayingAll = false;
            lock (_playbackLock)
            {
                wasPlayingAll = _isPlayingAll;
                IWavePlayer? playerToStop = _currentPlayer;
                if (playerToStop != null)
                {
                    try
                    {
                        playerToStop.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error stopping playback: {ex.Message}");
                    }
                }
                
                // Cancel any ongoing Play All operation
                if (_playbackCancellationToken != null)
                {
                    try
                    {
                        _playbackCancellationToken.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error canceling playback: {ex.Message}");
                    }
                }
                
                CleanupCurrentPlayback();
                _isPlaying = false;
                _isPlayingAll = false;
                _autoPlayTriggered = false; // Reset auto-play trigger flag when manually stopped
            }
            
            // Update current playing ID and transition state
            // This avoids race condition when switching between playing audio files
            lock (_playbackLock)
            {
                _currentPlayingTextObjectId = nextTextObjectId;
            }
            OnCurrentPlayingTextObjectChanged(nextTextObjectId);
            
            // Notify if we were playing all
            if (wasPlayingAll)
            {
                OnPlayAllStateChanged(false);
            }
        }
        
        private void CleanupCurrentPlayback()
        {
            // This method should only be called from within a lock
            if (_currentAudioFile != null)
            {
                try
                {
                    _currentAudioFile.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing audio file: {ex.Message}");
                }
                _currentAudioFile = null;
            }
            
            if (_currentPlayer != null)
            {
                try
                {
                    _currentPlayer.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing player: {ex.Message}");
                }
                _currentPlayer = null;
            }
        }
        
        public async Task PlayAllAudioAsync(List<TextObject> textObjects, string playOrder, bool useSourceAudio)
        {
            if (textObjects == null || textObjects.Count == 0)
            {
                // Reset auto-play trigger flag if no text objects
                lock (_playbackLock)
                {
                    _autoPlayTriggered = false;
                }
                return;
            }
            
            // Stop any currently playing audio (including previous Play All)
            // But don't notify about state change yet - we'll set it to playing below
            IWavePlayer? playerToStop = null;
            CancellationTokenSource? tokenToCancel = null;
            bool wasPlayingAll = false;
            
            lock (_playbackLock)
            {
                wasPlayingAll = _isPlayingAll;
                playerToStop = _currentPlayer;
                tokenToCancel = _playbackCancellationToken;
                
                // Stop the player if it's playing
                if (playerToStop != null)
                {
                    try
                    {
                        playerToStop.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error stopping playback: {ex.Message}");
                    }
                }
                
                // Cancel any ongoing Play All operation
                if (tokenToCancel != null)
                {
                    try
                    {
                        tokenToCancel.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error canceling playback: {ex.Message}");
                    }
                }
                
                CleanupCurrentPlayback();
                _isPlaying = false;
                // Don't reset _isPlayingAll here - we'll set it below
            }
            
            // Notify that playback stopped (but we're about to start new playback)
            lock (_playbackLock)
            {
                _currentPlayingTextObjectId = null;
            }
            OnCurrentPlayingTextObjectChanged(null);
            
            // Only notify if we were playing all and are stopping (not starting new)
            // We'll notify about starting below
            
            // Sort text objects based on play order
            var sortedObjects = SortTextObjectsByPlayOrder(textObjects, playOrder);
            
            // Filter objects that have audio ready
            var objectsWithAudio = sortedObjects.Where(obj =>
            {
                if (useSourceAudio)
                {
                    return obj.SourceAudioReady && !string.IsNullOrEmpty(obj.SourceAudioFilePath);
                }
                else
                {
                    return obj.TargetAudioReady && !string.IsNullOrEmpty(obj.TargetAudioFilePath);
                }
            }).ToList();
            
            if (objectsWithAudio.Count == 0)
            {
                Console.WriteLine("No audio files available to play");
                // Reset auto-play trigger flag if no audio files available
                lock (_playbackLock)
                {
                    _autoPlayTriggered = false;
                }
                return;
            }
            
            // Set playing all state and notify
            lock (_playbackLock)
            {
                _isPlayingAll = true;
            }
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"PlayAllAudio: Setting playing all state to true, notifying UI");
            }
            OnPlayAllStateChanged(true);
            
            // Create cancellation token for playback
            _playbackCancellationToken = new CancellationTokenSource();
            var cancellationToken = _playbackCancellationToken.Token;
            
            try
            {
                // Play each audio file sequentially (not in Task.Run to ensure proper async/await)
                foreach (var textObj in objectsWithAudio)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    
                    string? audioPath = useSourceAudio ? textObj.SourceAudioFilePath : textObj.TargetAudioFilePath;
                    if (string.IsNullOrEmpty(audioPath) || !System.IO.File.Exists(audioPath))
                    {
                        continue;
                    }
                    
                    try
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"PlayAllAudio: Playing audio for text object {textObj.ID}");
                        }
                        await PlayAudioFileAsync(audioPath, textObj.ID, isPartOfPlayAll: true, cancellationToken);
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"PlayAllAudio: Finished playing audio for text object {textObj.ID}");
                        }
                        
                        // Check cancellation token again after each file
                        if (cancellationToken.IsCancellationRequested)
                        {
                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                            {
                                Console.WriteLine("PlayAllAudio: Cancellation requested after file playback");
                            }
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error playing audio for text object {textObj.ID}: {ex.Message}");
                        // Notify that this item stopped playing due to error
                        lock (_playbackLock)
                        {
                            _currentPlayingTextObjectId = null;
                        }
                        OnCurrentPlayingTextObjectChanged(null);
                    }
                }
            }
            finally
            {
                // Reset playing all state and notify
                lock (_playbackLock)
                {
                    _isPlayingAll = false;
                    _autoPlayTriggered = false; // Reset auto-play trigger flag when playback completes
                }
                OnPlayAllStateChanged(false);
            }
        }
        
        private void OnPlayAllStateChanged(bool isPlaying)
        {
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"OnPlayAllStateChanged: isPlaying={isPlaying}, invoking on UI thread");
            }
            // Invoke on UI thread to update buttons - use Send priority to ensure immediate update
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                // Already on UI thread, fire event directly
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"OnPlayAllStateChanged: Already on UI thread, firing event directly, isPlaying={isPlaying}");
                }
                PlayAllStateChanged?.Invoke(this, isPlaying);
            }
            else
            {
                // Not on UI thread, invoke asynchronously
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"OnPlayAllStateChanged: Firing event on UI thread, isPlaying={isPlaying}");
                    }
                    PlayAllStateChanged?.Invoke(this, isPlaying);
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }
        
        private void OnCurrentPlayingTextObjectChanged(string? textObjectId)
        {
            // Invoke on UI thread to update icons
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                CurrentPlayingTextObjectChanged?.Invoke(this, textObjectId);
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }
        
        public bool IsPlayingAll()
        {
            lock (_playbackLock)
            {
                return _isPlayingAll;
            }
        }
        
        private List<TextObject> SortTextObjectsByPlayOrder(List<TextObject> textObjects, string playOrder)
        {
            var sorted = new List<TextObject>(textObjects);
            
            // Get vertical overlap threshold in pixels
            double pixelThreshold = ConfigManager.Instance.GetTtsVerticalOverlapThreshold();
            
            if (playOrder == "Top down, left to right")
            {
                sorted.Sort((a, b) =>
                {
                    // Check if rectangles are on the same line (within threshold pixels vertically)
                    if (areOnSameLine(a, b, pixelThreshold))
                    {
                        // On same line, sort by X (left to right)
                        return a.X.CompareTo(b.X);
                    }
                    // Different lines, sort by Y (top to bottom)
                    return a.Y.CompareTo(b.Y);
                });
            }
            else if (playOrder == "Top down, right to left")
            {
                sorted.Sort((a, b) =>
                {
                    // Check if rectangles are on the same line (within threshold pixels vertically)
                    if (areOnSameLine(a, b, pixelThreshold))
                    {
                        // On same line, sort by X (right to left)
                        return b.X.CompareTo(a.X);
                    }
                    // Different lines, sort by Y (top to bottom)
                    return a.Y.CompareTo(b.Y);
                });
            }
            
            return sorted;
        }
        
        private bool areOnSameLine(TextObject a, TextObject b, double pixelThreshold)
        {
            // Two rectangles are considered on the same line if the vertical distance
            // between their top-middle points is within the threshold
            
            // Calculate top-middle Y coordinate for each rectangle
            double aTopMiddleY = a.Y;
            double bTopMiddleY = b.Y;
            
            // Calculate vertical distance between top-middle points
            double verticalDistance = Math.Abs(aTopMiddleY - bTopMiddleY);
            
            // If distance is within threshold, they're on the same line
            return verticalDistance <= pixelThreshold;
        }
        
        public bool IsPlaying()
        {
            return _isPlaying;
        }
        
        public void ResetAutoPlayTrigger()
        {
            lock (_playbackLock)
            {
                _autoPlayTriggered = false;
            }
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine("AudioPlaybackManager: Auto-play trigger flag reset");
            }
        }
        
        public void CheckAndTriggerAutoPlay()
        {
            if (!ConfigManager.Instance.IsTtsAutoPlayAllEnabled())
            {
                return;
            }
            
            // Prevent multiple simultaneous auto-play triggers
            lock (_playbackLock)
            {
                // If already playing all or auto-play already triggered, don't trigger again
                if (_isPlayingAll || _autoPlayTriggered)
                {
                    Console.WriteLine("CheckAndTriggerAutoPlay: Already playing or auto-play already triggered, skipping");
                    return;
                }
                
                // Set flag to prevent duplicate triggers
                _autoPlayTriggered = true;
            }
            
            // Get current preload mode
            string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
            if (preloadMode == "Off")
            {
                lock (_playbackLock)
                {
                    _autoPlayTriggered = false;
                }
                return;
            }
            
            // Get all text objects
            var textObjects = Logic.Instance?.GetTextObjects();
            if (textObjects == null || textObjects.Count == 0)
            {
                lock (_playbackLock)
                {
                    _autoPlayTriggered = false;
                }
                return;
            }
            
            // Determine which audio to play based on overlay mode
            // If overlay is None or Source, play source audio
            // If overlay is Translation, play target audio
            string overlayMode = ConfigManager.Instance.GetMainWindowOverlayMode();
            bool useSourceAudio = overlayMode != "Translated";
            
            // Check if we should play source or target based on what's available
            bool hasSourceAudio = textObjects.Any(obj => obj.SourceAudioReady);
            bool hasTargetAudio = textObjects.Any(obj => obj.TargetAudioReady && !string.IsNullOrEmpty(obj.TextTranslated));
            
            // Determine which to play
            if (useSourceAudio && hasSourceAudio)
            {
                // Play source audio
                string playOrder = ConfigManager.Instance.GetTtsPlayOrder();
                _ = PlayAllAudioAsync(textObjects.ToList(), playOrder, useSourceAudio: true);
            }
            else if (!useSourceAudio && hasTargetAudio)
            {
                // Play target audio
                string playOrder = ConfigManager.Instance.GetTtsPlayOrder();
                _ = PlayAllAudioAsync(textObjects.ToList(), playOrder, useSourceAudio: false);
            }
            else
            {
                // No audio to play, reset flag
                lock (_playbackLock)
                {
                    _autoPlayTriggered = false;
                }
            }
        }
    }
}

