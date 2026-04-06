using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;

namespace UGTLive
{
    internal static class AudioHelper
    {
        public static void PlayAndDeleteTempFile(string filePath)
        {
            try
            {
                Task.Run(() =>
                {
                    IWavePlayer? wavePlayer = null;
                    AudioFileReader? audioFile = null;
                    ManualResetEvent playbackFinished = new ManualResetEvent(false);

                    try
                    {
                        wavePlayer = new WaveOutEvent();
                        wavePlayer.PlaybackStopped += (sender, args) =>
                        {
                            playbackFinished.Set();
                        };

                        audioFile = new AudioFileReader(filePath);
                        wavePlayer.Init(audioFile);

                        Console.WriteLine($"Starting audio playback of file: {filePath}");
                        wavePlayer.Play();

                        playbackFinished.WaitOne();
                        wavePlayer.Stop();

                        Console.WriteLine("Audio playback completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error playing audio file: {ex.Message}");

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show($"Error playing audio: {ex.Message}",
                                "Audio Playback Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }
                    finally
                    {
                        wavePlayer?.Dispose();
                        audioFile?.Dispose();

                        try
                        {
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                                Console.WriteLine($"Temp audio file deleted: {filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete temp audio file: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting audio playback thread: {ex.Message}");
            }
        }
    }
}
