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
        private CancellationTokenSource? _playbackCancellationToken;
        private readonly object _playbackLock = new object();
        
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
        
        public async Task PlayAudioFileAsync(string filePath, string? textObjectId = null)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                Console.WriteLine($"Audio file not found: {filePath}");
                return;
            }
            
            // Stop any currently playing audio first (but don't reset the playing text object notification yet)
            StopCurrentPlayback();
            
            // Notify which text object is playing (if provided) - do this after stopping to avoid race conditions
            if (!string.IsNullOrEmpty(textObjectId))
            {
                OnCurrentPlayingTextObjectChanged(textObjectId);
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
                        
                        _currentPlayer.PlaybackStopped += (sender, args) =>
                        {
                            lock (_playbackLock)
                            {
                                _isPlaying = false;
                                CleanupCurrentPlayback();
                            }
                            // Notify that playback stopped
                            OnCurrentPlayingTextObjectChanged(null);
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
                        Console.WriteLine($"PlayAudioFileAsync: Starting wait loop for {filePath}");
                        int waitCount = 0;
                        while (true)
                        {
                            bool stillPlaying;
                            PlaybackState state;
                            
                            lock (_playbackLock)
                            {
                                stillPlaying = _isPlaying;
                                state = player.PlaybackState;
                            }
                            
                            // Log every 10 iterations (1 second) for debugging
                            if (waitCount % 10 == 0)
                            {
                                Console.WriteLine($"PlayAudioFileAsync: Wait loop iteration {waitCount}, stillPlaying={stillPlaying}, state={state}");
                            }
                            waitCount++;
                            
                            // Break if playback has stopped (either flag is false or state is not playing)
                            if (!stillPlaying)
                            {
                                Console.WriteLine($"PlayAudioFileAsync: Playback stopped (stillPlaying=false)");
                                break;
                            }
                            
                            if (state != PlaybackState.Playing)
                            {
                                Console.WriteLine($"PlayAudioFileAsync: Playback stopped (state={state})");
                                break;
                            }
                            
                            Thread.Sleep(100);
                        }
                        Console.WriteLine($"PlayAudioFileAsync: Wait loop completed after {waitCount * 100}ms");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error waiting for playback: {ex.Message}");
                    }
                }
            });
        }
        
        public void StopCurrentPlayback()
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
            }
            
            // Notify that playback stopped (but only if we're not starting a new one)
            // This will be set by the new PlayAudioFileAsync call if needed
            OnCurrentPlayingTextObjectChanged(null);
            
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
                return;
            }
            
            // Set playing all state and notify
            lock (_playbackLock)
            {
                _isPlayingAll = true;
            }
            Console.WriteLine($"PlayAllAudio: Setting playing all state to true, notifying UI");
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
                        Console.WriteLine($"PlayAllAudio: Playing audio for text object {textObj.ID}");
                        await PlayAudioFileAsync(audioPath, textObj.ID);
                        Console.WriteLine($"PlayAllAudio: Finished playing audio for text object {textObj.ID}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error playing audio for text object {textObj.ID}: {ex.Message}");
                        // Notify that this item stopped playing due to error
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
                }
                OnPlayAllStateChanged(false);
            }
        }
        
        private void OnPlayAllStateChanged(bool isPlaying)
        {
            Console.WriteLine($"OnPlayAllStateChanged: isPlaying={isPlaying}, invoking on UI thread");
            // Invoke on UI thread to update buttons
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Console.WriteLine($"OnPlayAllStateChanged: Firing event, isPlaying={isPlaying}");
                PlayAllStateChanged?.Invoke(this, isPlaying);
            }, System.Windows.Threading.DispatcherPriority.Normal);
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
            
            if (playOrder == "Top down, left to right")
            {
                sorted.Sort((a, b) =>
                {
                    // First sort by Y (top to bottom)
                    int yCompare = a.Y.CompareTo(b.Y);
                    if (yCompare != 0)
                    {
                        return yCompare;
                    }
                    // Then sort by X (left to right)
                    return a.X.CompareTo(b.X);
                });
            }
            else if (playOrder == "Top down, right to left")
            {
                sorted.Sort((a, b) =>
                {
                    // First sort by Y (top to bottom)
                    int yCompare = a.Y.CompareTo(b.Y);
                    if (yCompare != 0)
                    {
                        return yCompare;
                    }
                    // Then sort by X (right to left)
                    return b.X.CompareTo(a.X);
                });
            }
            
            return sorted;
        }
        
        public bool IsPlaying()
        {
            return _isPlaying;
        }
        
        public void CheckAndTriggerAutoPlay()
        {
            if (!ConfigManager.Instance.IsTtsAutoPlayAllEnabled())
            {
                return;
            }
            
            // Get current preload mode
            string preloadMode = ConfigManager.Instance.GetTtsPreloadMode();
            if (preloadMode == "Off")
            {
                return;
            }
            
            // Get all text objects
            var textObjects = Logic.Instance?.GetTextObjects();
            if (textObjects == null || textObjects.Count == 0)
            {
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
        }
    }
}

