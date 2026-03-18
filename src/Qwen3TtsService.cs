using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public class Qwen3TtsService : ITtsService
    {
        private static Qwen3TtsService? _instance;
        private readonly HttpClient _httpClient;

        public static readonly Dictionary<string, string> AvailableVoices = new Dictionary<string, string>
        {
            { "Ono Anna (Japanese)", "ono_anna" },
            { "Ryan (English)", "ryan" },
            { "Aiden (English)", "aiden" },
            { "Vivian (Chinese)", "vivian" },
            { "Serena (Chinese)", "serena" },
            { "Uncle Fu (Chinese)", "uncle_fu" },
            { "Dylan (Chinese)", "dylan" },
            { "Eric (Chinese)", "eric" },
            { "Sohee (Korean)", "sohee" },
        };

        public static Qwen3TtsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Qwen3TtsService();
                }
                return _instance;
            }
        }

        private Qwen3TtsService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
        }

        private string GetBaseUrl()
        {
            string url = ConfigManager.Instance.GetQwen3TtsUrl();
            string port = ConfigManager.Instance.GetQwen3TtsPort();
            return $"{url}:{port}";
        }

        public async Task<bool> SpeakText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot speak empty text");
                    return false;
                }

                string voice = ConfigManager.Instance.GetQwen3TtsVoice();
                if (string.IsNullOrWhiteSpace(voice))
                {
                    voice = "ono_anna";
                }

                string baseUrl = GetBaseUrl();

                var requestData = new
                {
                    text = text,
                    voice = voice
                };

                string jsonRequest = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                string url = $"{baseUrl}/tts";

                Console.WriteLine($"Sending TTS request to Qwen3-TTS for text: {text.Substring(0, Math.Min(50, text.Length))}...");

                return await Task.Run(async () =>
                {
                    try
                    {
                        HttpResponseMessage response = await _httpClient.PostAsync(url, content);

                        if (response.IsSuccessStatusCode)
                        {
                            string contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                            {
                                Console.WriteLine($"Qwen3-TTS request successful, content type: {contentType}");
                            }

                            using Stream audioStream = await response.Content.ReadAsStreamAsync();

                            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", "cache");
                            Directory.CreateDirectory(tempDir);

                            string extension = ".wav";
                            if (contentType.Contains("audio/mpeg") || contentType.Contains("audio/mp3"))
                            {
                                extension = ".mp3";
                            }
                            else if (contentType.Contains("audio/ogg"))
                            {
                                extension = ".ogg";
                            }

                            string audioFile = Path.Combine(tempDir, $"tts_qwen3_{DateTime.Now.Ticks}{extension}");

                            using (FileStream fileStream = File.Create(audioFile))
                            {
                                await audioStream.CopyToAsync(fileStream);
                            }

                            Console.WriteLine($"Audio saved to {audioFile}, playing...");

                            PlayAudioFile(audioFile);

                            return true;
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"Qwen3-TTS request failed: {response.StatusCode}. Details: {errorContent}");
                            return false;
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"Qwen3-TTS connection error: {ex.Message}. Is the Qwen3-TTS service running?");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("Cannot connect to Qwen3-TTS service. Please make sure it is installed and running from the Services tab.",
                                "Qwen3-TTS Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during Qwen3-TTS request: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initiating Qwen3-TTS: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> GenerateAudioFileAsync(string text, string voiceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot generate audio for empty text");
                    return null;
                }

                string voice = voiceId;
                if (string.IsNullOrWhiteSpace(voice))
                {
                    voice = "ono_anna";
                }

                string baseUrl = GetBaseUrl();

                var requestData = new
                {
                    text = text,
                    voice = voice
                };

                string jsonRequest = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                string url = $"{baseUrl}/tts";

                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Generating Qwen3-TTS audio for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                }

                return await Task.Run(async () =>
                {
                    try
                    {
                        HttpResponseMessage response = await _httpClient.PostAsync(url, content);

                        if (response.IsSuccessStatusCode)
                        {
                            string contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                            if (ConfigManager.Instance.GetLogExtraDebugStuff())
                            {
                                Console.WriteLine($"Qwen3-TTS request successful, content type: {contentType}");
                            }

                            using Stream audioStream = await response.Content.ReadAsStreamAsync();

                            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", "cache");
                            Directory.CreateDirectory(tempDir);

                            string extension = ".wav";
                            if (contentType.Contains("audio/mpeg") || contentType.Contains("audio/mp3"))
                            {
                                extension = ".mp3";
                            }
                            else if (contentType.Contains("audio/ogg"))
                            {
                                extension = ".ogg";
                            }

                            string audioFile = Path.Combine(tempDir, $"tts_qwen3_{DateTime.Now.Ticks}{extension}");

                            using (FileStream fileStream = File.Create(audioFile))
                            {
                                await audioStream.CopyToAsync(fileStream);
                            }

                            Console.WriteLine($"Qwen3-TTS audio file generated: {audioFile}");
                            return audioFile;
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"Qwen3-TTS request failed: {response.StatusCode}. Details: {errorContent}");

                            throw new HttpRequestException($"Qwen3-TTS request failed: {response.StatusCode}. Details: {errorContent}")
                            {
                                Data = { ["StatusCode"] = response.StatusCode }
                            };
                        }
                    }
                    catch (HttpRequestException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during Qwen3-TTS request: {ex.Message}");
                        return null;
                    }
                });
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initiating Qwen3-TTS audio generation: {ex.Message}");
                return null;
            }
        }

        private void PlayAudioFile(string filePath)
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

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Error playing audio: {ex.Message}",
                                "Audio Playback Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }
                    finally
                    {
                        if (wavePlayer != null)
                        {
                            wavePlayer.Dispose();
                        }

                        if (audioFile != null)
                        {
                            audioFile.Dispose();
                        }

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
