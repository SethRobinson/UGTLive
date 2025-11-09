using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using MediaColor = System.Windows.Media.Color;

namespace UGTLive
{
    public partial class ServerSetupDialog : Window
    {
        private static ServerSetupDialog? _instance;
        private bool _isRunningDiagnostics = false;
        private bool _isInstalling = false;
        
        /// <summary>
        /// Gets or creates the singleton instance of ServerSetupDialog
        /// </summary>
        public static ServerSetupDialog Instance
        {
            get
            {
                if (_instance == null || !IsWindowValid(_instance))
                {
                    _instance = new ServerSetupDialog();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Checks if a window instance is still valid (not disposed or closed)
        /// </summary>
        private static bool IsWindowValid(Window? window)
        {
            if (window == null) return false;
            try
            {
                // Try to access a property to see if window is still valid
                var _ = window.IsVisible;
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Shows the dialog if not already visible, or brings existing instance to front
        /// </summary>
        public static void ShowDialogSafe()
        {
            var dialog = Instance;
            
            if (dialog.IsVisible)
            {
                // Dialog is already open, bring it to front
                dialog.Activate();
                dialog.Focus();
            }
            else
            {
                // Show the dialog (modal when called from settings, non-modal for startup)
                dialog.ShowDialog();
            }
        }
        
        private ServerSetupDialog()
        {
            InitializeComponent();
            this.Loaded += ServerSetupDialog_Loaded;
            
            // Load checkbox states from config
            showServerWindowCheckBox.IsChecked = ConfigManager.Instance.GetShowServerWindow();
            skipCondaChecksCheckBox.IsChecked = ConfigManager.Instance.GetSkipCondaChecks();
            
            // Load app icon and version info
            LoadSplashBranding();
            
            // Clear instance when window is closed
            this.Closed += (s, e) =>
            {
                if (_instance == this)
                {
                    _instance = null;
                }
            };
        }
        
        private void LoadSplashBranding()
        {
            try
            {
                // Load app icon
                System.Uri iconUri = new System.Uri("pack://application:,,,/media/Icon1.ico", UriKind.RelativeOrAbsolute);
                IconBitmapDecoder decoder = new IconBitmapDecoder(
                    iconUri,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                
                BitmapSource bigFrame = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth)
                    .First();
                
                appIconImage.Source = bigFrame;
                
                // Set version text
                versionTextBlock.Text = $"V{SplashManager.CurrentVersion} by Seth A. Robinson";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading splash branding: {ex.Message}");
            }
        }
        
        private async void ServerSetupDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await RunDiagnosticsAsync();
        }
        
        private void ShowServerWindowCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Save checkbox state to config
            bool isChecked = showServerWindowCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetShowServerWindow(isChecked);
            
            // If server is running, toggle window visibility
            ServerProcessManager.Instance.SetServerWindowVisibility(isChecked);
        }
        
        private void SkipCondaChecksCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Save checkbox state to config
            bool isChecked = skipCondaChecksCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetSkipCondaChecks(isChecked);
        }
        
        private class VersionInfo
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string? Name { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("latest_version")]
            public double LatestVersion { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("message")]
            public string? Message { get; set; }
        }
        
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    updateStatusIcon.Text = "⏳";
                    updateStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(128, 128, 128)); // Gray
                    updateStatusText.Text = "Checking for updates...";
                });
                
                VersionInfo? versionInfo = await FetchVersionInfoAsync();
                
                await Dispatcher.InvokeAsync(() =>
                {
                    if (versionInfo == null)
                    {
                        updateStatusIcon.Text = "⚠";
                        updateStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 165, 0)); // Orange
                        updateStatusText.Text = "Could not check for updates";
                        return;
                    }
                    
                    if (versionInfo.LatestVersion > SplashManager.CurrentVersion)
                    {
                        updateStatusIcon.Text = "⚠";
                        updateStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 165, 0)); // Orange
                        updateStatusText.Text = $"New version V{versionInfo.LatestVersion} available!";
                        downloadUpdateButton.IsEnabled = true;
                        downloadUpdateButton.Visibility = Visibility.Visible;
                        
                        // Store version info for download button
                        _updateVersionInfo = versionInfo;
                    }
                    else
                    {
                        updateStatusIcon.Text = "✓";
                        updateStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                        updateStatusText.Text = "You have the latest version";
                    }
                    
                    // Scroll to show update status (first check)
                    ScrollToElement(updateStatusIcon);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking for updates: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    updateStatusIcon.Text = "⚠";
                    updateStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 165, 0)); // Orange
                    updateStatusText.Text = "Update check failed";
                });
            }
        }
        
        private VersionInfo? _updateVersionInfo;
        private const string VersionCheckerUrl = "https://raw.githubusercontent.com/SethRobinson/UGTLive/refs/heads/main/media/latest_version_checker.json";
        private const string DownloadUrl = "https://www.rtsoft.com/files/UniversalGameTranslatorLive_Windows.zip";
        
        private async Task<VersionInfo?> FetchVersionInfoAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5); // 5 second timeout
                    string json = await client.GetStringAsync(VersionCheckerUrl);
                    Console.WriteLine($"Received JSON: {json}");
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    
                    var result = JsonSerializer.Deserialize<VersionInfo>(json, options);
                    Console.WriteLine($"Deserialized version: {result?.LatestVersion}, name: {result?.Name}, message: {result?.Message}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching version info: {ex.Message}");
                return null;
            }
        }
        
        private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_updateVersionInfo != null)
                {
                    string message = _updateVersionInfo.Message?.Replace("{VERSION_STRING}", _updateVersionInfo.LatestVersion.ToString()) 
                        ?? $"A new version (V{_updateVersionInfo.LatestVersion}) is available. Would you like to download it?";
                    
                    MessageBoxResult result = MessageBox.Show(message, "Update Available", 
                        MessageBoxButton.YesNo, MessageBoxImage.Information);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Open the download URL in the default browser
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open download page: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"Error downloading update: {ex.Message}");
            }
        }
        
        private async Task RunDiagnosticsAsync()
        {
            if (_isRunningDiagnostics) return;
            
            _isRunningDiagnostics = true;
            statusMessage.Visibility = Visibility.Visible;
            statusMessage.Text = "Running diagnostics...";
            progressBar.Visibility = Visibility.Visible;
            
            try
            {
                // Check for updates first
                await CheckForUpdatesAsync();
                
                // Check GPU (right after version check)
                await UpdateGpuStatusAsync();
                
                // Check if user wants to skip conda/python checks
                bool skipCondaChecks = await Dispatcher.InvokeAsync(() => skipCondaChecksCheckBox.IsChecked ?? false);
                
                if (skipCondaChecks)
                {
                    // Skip conda/python checks and show "Skipped" status
                    await UpdateCondaStatusSkippedAsync();
                    await UpdateEnvironmentStatusSkippedAsync();
                    await UpdatePackagesStatusSkippedAsync();
                }
                else
                {
                    // Check Conda
                    var condaResult = await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync();
                    await UpdateCondaStatusAsync();
                    
                    // Check Environment
                    var envResult = await ServerDiagnosticsService.Instance.CheckCondaEnvironmentAsync();
                    await UpdateEnvironmentStatusAsync();
                    
                    // Check Packages
                    var packagesResult = await ServerDiagnosticsService.Instance.CheckPythonPackagesAsync();
                    await UpdatePackagesStatusAsync();
                }
                
                // Check Server (always check this)
                await UpdateServerStatusAsync();
                
                statusMessage.Text = "Diagnostics complete";
                
                // Check if server is running for auto-start logic
                bool serverRunning = await ServerProcessManager.Instance.IsServerRunningAsync();
                
                // Auto-start server if it's not running
                if (!serverRunning)
                {
                    bool shouldAutoStart = false;
                    
                    if (skipCondaChecks)
                    {
                        // If checks are skipped, assume everything is set up and try to auto-start
                        shouldAutoStart = true;
                    }
                    else
                    {
                        // If checks aren't skipped, validate conda/environment/packages before auto-starting
                        var condaResult = await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync();
                        var envResult = await ServerDiagnosticsService.Instance.CheckCondaEnvironmentAsync();
                        var packagesResult = await ServerDiagnosticsService.Instance.CheckPythonPackagesAsync();
                        
                        if (condaResult.available && envResult.exists)
                        {
                            // Check if all key packages are installed
                            string[] keyPackages = { "torch", "easyocr", "manga-ocr", "python-doctr", "ultralytics", "opencv-python" };
                            bool allPackagesInstalled = keyPackages.All(pkg => 
                                packagesResult.ContainsKey(pkg) && packagesResult[pkg].installed);
                            
                            shouldAutoStart = allPackagesInstalled;
                        }
                    }
                    
                    if (shouldAutoStart)
                    {
                        // Check if user wants to see the server window
                        bool showWindow = await Dispatcher.InvokeAsync(() => (bool)(showServerWindowCheckBox.IsChecked ?? false));
                        
                        // Disable Start Server button and show visual indication
                        await Dispatcher.InvokeAsync(() =>
                        {
                            startServerButton.IsEnabled = false;
                            startServerButton.Content = "Auto-starting...";
                            startServerButton.Opacity = 0.6; // Gray it out
                            statusMessage.Visibility = Visibility.Visible;
                            statusMessage.Text = "Auto-starting server... (This may take a few seconds)";
                        });
                        
                        // Auto-start the server
                        await AutoStartServerAsync(showWindow);
                    }
                }
            }
            catch (Exception ex)
            {
                statusMessage.Text = $"Error running diagnostics: {ex.Message}";
                Console.WriteLine($"Diagnostics error: {ex.Message}");
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                _isRunningDiagnostics = false;
                
                // Hide status message after a delay
                await Task.Delay(2000);
                await Dispatcher.InvokeAsync(() =>
                {
                    statusMessage.Visibility = Visibility.Collapsed;
                });
            }
        }
        
        private async Task AutoStartServerAsync(bool showWindow)
        {
            try
            {
                statusMessage.Visibility = Visibility.Visible;
                statusMessage.Text = "Auto-starting server...";
                progressBar.Visibility = Visibility.Visible;
                
                // Start the server process
                var startResult = await ServerProcessManager.Instance.StartServerProcessAsync(showWindow);
                
                if (!startResult.success)
                {
                    statusMessage.Text = $"Auto-start failed: {startResult.errorMessage}";
                    return;
                }
                
                // Poll for server to become available (check every second for up to 30 seconds)
                statusMessage.Text = "Waiting for server to start...";
                bool serverReady = false;
                int maxAttempts = 30; // 30 seconds
                int attempts = 0;
                
                while (attempts < maxAttempts && !serverReady)
                {
                    await Task.Delay(1000); // Wait 1 second between checks
                    attempts++;
                    
                    serverReady = await ServerProcessManager.Instance.IsServerRunningAsync();
                    
                    if (!serverReady)
                    {
                        statusMessage.Text = $"Waiting for server to start... ({attempts}/{maxAttempts} seconds)";
                    }
                }
                
                if (serverReady)
                {
                    statusMessage.Text = "Server started successfully!";
                    await UpdateServerStatusAsync();
                }
                else
                {
                    statusMessage.Text = "Server did not start in time. Please check for errors.";
                }
            }
            catch (Exception ex)
            {
                statusMessage.Text = $"Auto-start error: {ex.Message}";
                Console.WriteLine($"Auto-start error: {ex.Message}");
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                
                // Re-enable Start Server button if auto-start failed
                bool serverRunning = await ServerProcessManager.Instance.IsServerRunningAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!serverRunning)
                    {
                        startServerButton.IsEnabled = true;
                        startServerButton.Content = "Start Server";
                        startServerButton.Opacity = 1.0;
                    }
                });
            }
        }
        
        private async Task UpdateCondaStatusAsync()
        {
            var result = await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync();
            
            await Dispatcher.InvokeAsync(() =>
            {
                if (result.available)
                {
                    condaStatusIcon.Text = "✓";
                    condaStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    condaStatusText.Text = $"Available - {result.version}";
                    // Keep button enabled to allow reinstallation if needed
                    installCondaButton.IsEnabled = true;
                }
                else
                {
                    condaStatusIcon.Text = "✗";
                    condaStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    condaStatusText.Text = result.errorMessage;
                    installCondaButton.IsEnabled = true;
                }
                
                // Scroll to show this section
                ScrollToElement(condaStatusIcon);
            });
        }
        
        private async Task UpdateCondaStatusSkippedAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                condaStatusIcon.Text = "—";
                condaStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(128, 128, 128)); // Gray
                condaStatusText.Text = "Skipped";
                installCondaButton.IsEnabled = false;
                
                // Scroll to show this section
                ScrollToElement(condaStatusIcon);
            });
        }
        
        private async Task UpdateEnvironmentStatusAsync()
        {
            var result = await ServerDiagnosticsService.Instance.CheckCondaEnvironmentAsync();
            
            await Dispatcher.InvokeAsync(() =>
            {
                if (result.exists)
                {
                    envStatusIcon.Text = "✓";
                    envStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    envStatusText.Text = $"Environment exists - Python {result.pythonVersion}";
                }
                else
                {
                    envStatusIcon.Text = "✗";
                    envStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    envStatusText.Text = result.errorMessage;
                }
                
                // Scroll to show this section
                ScrollToElement(envStatusIcon);
            });
        }
        
        private async Task UpdateEnvironmentStatusSkippedAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                envStatusIcon.Text = "—";
                envStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(128, 128, 128)); // Gray
                envStatusText.Text = "Skipped";
                
                // Scroll to show this section
                ScrollToElement(envStatusIcon);
            });
        }
        
        private async Task UpdatePackagesStatusAsync()
        {
            var packages = await ServerDiagnosticsService.Instance.CheckPythonPackagesAsync();
            
            Dispatcher.Invoke(() =>
            {
                int installedCount = 0;
                int totalCount = packages.Count;
                List<string> packageDetails = new List<string>();
                
                // Key packages to check
                string[] keyPackages = { "torch", "easyocr", "manga-ocr", "python-doctr", "ultralytics", "opencv-python" };
                
                foreach (string pkg in keyPackages)
                {
                    if (packages.ContainsKey(pkg))
                    {
                        var (installed, version) = packages[pkg];
                        if (installed)
                        {
                            installedCount++;
                            packageDetails.Add($"  ✓ {pkg} - {version}");
                        }
                        else
                        {
                            packageDetails.Add($"  ✗ {pkg} - Not installed");
                        }
                    }
                }
                
                // Check CuPy (optional)
                bool cupyFound = false;
                foreach (string cupyKey in new[] { "cupy", "cupy-cuda11x", "cupy-cuda12x" })
                {
                    if (packages.ContainsKey(cupyKey) && packages[cupyKey].installed)
                    {
                        cupyFound = true;
                        packageDetails.Add($"  ✓ {cupyKey} - {packages[cupyKey].version}");
                        break;
                    }
                }
                if (!cupyFound)
                {
                    packageDetails.Add($"  ⚠ CuPy - Not installed (optional for GPU)");
                }
                
                packagesList.ItemsSource = packageDetails;
                
                if (installedCount == keyPackages.Length)
                {
                    packagesStatusIcon.Text = "✓";
                    packagesStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    packagesStatusText.Text = $"All packages installed ({installedCount}/{keyPackages.Length})";
                    installBackendButton.IsEnabled = true; // Always enabled so user can reinstall if needed
                }
                else
                {
                    packagesStatusIcon.Text = "✗";
                    packagesStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    packagesStatusText.Text = $"Missing packages ({installedCount}/{keyPackages.Length})";
                    installBackendButton.IsEnabled = true;
                }
                
                // Scroll to show this section
                ScrollToElement(packagesStatusIcon);
            });
        }
        
        private async Task UpdatePackagesStatusSkippedAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                packagesStatusIcon.Text = "—";
                packagesStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(128, 128, 128)); // Gray
                packagesStatusText.Text = "Skipped";
                packagesList.ItemsSource = null;
                installBackendButton.IsEnabled = false;
                
                // Scroll to show this section
                ScrollToElement(packagesStatusIcon);
            });
        }
        
        private async Task UpdateGpuStatusAsync()
        {
            var result = await ServerDiagnosticsService.Instance.CheckNvidiaGpuAsync();
            
            await Dispatcher.InvokeAsync(() =>
            {
                if (result.found)
                {
                    gpuStatusIcon.Text = "✓";
                    gpuStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    gpuStatusText.Text = result.modelName;
                }
                else
                {
                    gpuStatusIcon.Text = "✗";
                    gpuStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    gpuStatusText.Text = "These scripts currently require a modern NVidia GPU to work out of the box!";
                }
                
                // Scroll to show this section
                ScrollToElement(gpuStatusIcon);
            });
        }
        
        private async Task UpdateServerStatusAsync()
        {
            var result = await ServerProcessManager.Instance.IsServerRunningAsync();
            
            await Dispatcher.InvokeAsync(() =>
            {
                if (result)
                {
                    serverStatusIcon.Text = "✓";
                    serverStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    serverStatusText.Text = "UGTLive Server detected";
                    startServerButton.IsEnabled = false;
                    startServerButton.Content = "Start Server"; // Reset text in case it was "Auto-starting..."
                    startServerButton.Opacity = 1.0; // Reset opacity
                    stopServerButton.IsEnabled = true;
                    
                    // Update status label to show everything is ready
                    statusLabel.Text = "Everything looks good. Let's go!";
                    statusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    
                    // Scroll to show server status section
                    ScrollToServerStatus();
                }
                else
                {
                    serverStatusIcon.Text = "✗";
                    serverStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    serverStatusText.Text = "Server is not running";
                    stopServerButton.IsEnabled = false;
                    
                    // Keep status label as "Making sure everything is ready..."
                    statusLabel.Text = "Making sure everything is ready...";
                    statusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 0, 0)); // Black
                    
                    // Scroll to show server status section
                    ScrollToServerStatus();
                    
                    // Enable start button if conda and environment are available (check async without blocking)
                    _ = CheckAndEnableStartButtonAsync();
                }
            });
        }
        
        private ScrollViewer? _diagnosticsScrollViewer;
        
        private void ScrollToElement(FrameworkElement element)
        {
            if (_diagnosticsScrollViewer == null)
            {
                _diagnosticsScrollViewer = FindVisualChild<ScrollViewer>(this);
            }
            
            if (_diagnosticsScrollViewer != null && element != null)
            {
                // Bring the element into view
                element.BringIntoView();
                
                // Small delay to ensure smooth scrolling
                Task.Delay(100).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Ensure element is visible
                        element.BringIntoView();
                    });
                });
            }
        }
        
        private void ScrollToServerStatus()
        {
            if (_diagnosticsScrollViewer == null)
            {
                _diagnosticsScrollViewer = FindVisualChild<ScrollViewer>(this);
            }
            
            if (_diagnosticsScrollViewer != null)
            {
                // Scroll to bottom to show server status and buttons
                _diagnosticsScrollViewer.ScrollToEnd();
            }
        }
        
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }
        
        private async void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) return;
            
            _isInstalling = true;
            stopServerButton.IsEnabled = false;
            statusMessage.Visibility = Visibility.Visible;
            statusMessage.Text = "Stopping server...";
            progressBar.Visibility = Visibility.Visible;
            
            try
            {
                var result = await ServerProcessManager.Instance.ForceStopServerAsync();
                
                if (result.success)
                {
                    statusMessage.Text = "Server stopped successfully!";
                    // Server stopped, update UI
                    await Task.Delay(1000); // Wait a moment for port to be released
                    await UpdateServerStatusAsync();
                }
                else
                {
                    statusMessage.Text = $"Failed to stop server: {result.errorMessage}";
                    MessageBox.Show($"Failed to stop server:\n\n{result.errorMessage}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                statusMessage.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Error stopping server: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                _isInstalling = false;
                
                // Hide status message after delay
                await Task.Delay(2000);
                Dispatcher.Invoke(() =>
                {
                    statusMessage.Visibility = Visibility.Collapsed;
                });
            }
        }
        
        private async void InstallCondaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) return;
            
            MessageBoxResult result = MessageBox.Show(
                "This will download and install Miniconda. The installer window will open.\n\n" +
                "IMPORTANT: Make sure to check 'Add Miniconda3 to my PATH environment variable' during installation.\n\n" +
                "After installation, close this dialog and restart UGTLive.\n\n" +
                "Continue?",
                "Install Miniconda",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;
            
            _isInstalling = true;
            installCondaButton.IsEnabled = false;
            statusMessage.Visibility = Visibility.Visible;
            statusMessage.Text = "Starting Miniconda installer...";
            
            try
            {
                string webserverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webserver");
                string installBatch = Path.Combine(webserverPath, "InstallMiniConda.bat");
                
                if (!File.Exists(installBatch))
                {
                    MessageBox.Show($"InstallMiniConda.bat not found at {installBatch}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = installBatch,
                    WorkingDirectory = webserverPath,
                    UseShellExecute = true
                };
                
                Process process = Process.Start(psi)!;
                
                statusMessage.Text = "Installing Miniconda... Please wait.";
                progressBar.Visibility = Visibility.Visible;
                
                // Wait for installer to complete
                await Task.Run(() => process.WaitForExit());
                
                progressBar.Visibility = Visibility.Collapsed;
                statusMessage.Text = "Miniconda installation complete.";
                
                // Show message that app will close and user needs to restart manually
                MessageBox.Show(
                    "Miniconda installation is complete.\n\n" +
                    "UGTLive will now close automatically.\n\n" +
                    "Please restart UGTLive manually to continue setup.\n\n" +
                    "PATH environment changes require a full restart of the application\n" +
                    "to take effect. After restarting, you can continue with the backend setup.",
                    "Restart Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                // Close the application
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting installer: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusMessage.Text = $"Error: {ex.Message}";
                progressBar.Visibility = Visibility.Collapsed;
            }
            finally
            {
                // Only clean up if app is still running (not shutting down)
                try
                {
                    if (System.Windows.Application.Current != null)
                    {
                        progressBar.Visibility = Visibility.Collapsed;
                        installCondaButton.IsEnabled = true;
                        _isInstalling = false;
                    }
                }
                catch
                {
                    // Ignore errors during shutdown
                }
            }
        }
        
        private async void InstallBackendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) return;
            
            // Show Conda Terms of Service acceptance dialog
            CondaTosDialog tosDialog = new CondaTosDialog
            {
                Owner = this
            };
            
            bool? tosResult = tosDialog.ShowDialog();
            if (tosResult != true || !tosDialog.TosAccepted)
            {
                return; // User cancelled or didn't accept ToS
            }
            
            MessageBoxResult result = MessageBox.Show(
                "This will install or reinstall the UGTLive backend environment.\n\n" +
                "This process may take several minutes and will:\n" +
                "- Create/update the 'ocrstuff' conda environment\n" +
                "- Install PyTorch, EasyOCR, Manga OCR, docTR, and other dependencies\n" +
                "- Download AI models\n\n" +
                "A command window will open showing progress.\n\n" +
                "Continue?",
                "Install/Reinstall Backend",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;
            
            _isInstalling = true;
            installBackendButton.IsEnabled = false;
            statusMessage.Visibility = Visibility.Visible;
            statusMessage.Text = "Starting backend installation...";
            progressBar.Visibility = Visibility.Visible;
            
            try
            {
                string webserverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webserver");
                string setupBatch = Path.Combine(webserverPath, "SetupServerCondaEnvNVidia.bat");
                
                if (!File.Exists(setupBatch))
                {
                    MessageBox.Show($"SetupServerCondaEnvNVidia.bat not found at {setupBatch}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = setupBatch,
                    WorkingDirectory = webserverPath,
                    UseShellExecute = true
                };
                
                Process process = Process.Start(psi)!;
                
                // Wait for process to complete (this may take a while)
                await Task.Run(() => process.WaitForExit());
                
                statusMessage.Text = "Backend installation complete. Refreshing diagnostics...";
                
                // Refresh diagnostics
                await RunDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during installation: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusMessage.Text = $"Error: {ex.Message}";
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                _isInstalling = false;
            }
        }
        
        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) return;
            
            _isInstalling = true;
            startServerButton.IsEnabled = false;
            statusMessage.Visibility = Visibility.Visible;
            statusMessage.Text = "Starting server...";
            progressBar.Visibility = Visibility.Visible;
            
            try
            {
                // Check if user wants to see the server window
                bool showWindow = Dispatcher.Invoke(() => showServerWindowCheckBox.IsChecked ?? false);
                
                // Start the server process
                var startResult = await ServerProcessManager.Instance.StartServerProcessAsync(showWindow);
                
                if (!startResult.success)
                {
                    statusMessage.Text = $"Failed to start server: {startResult.errorMessage}";
                    MessageBox.Show($"Failed to start server:\n\n{startResult.errorMessage}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Poll for server to become available (check every second for up to 30 seconds)
                statusMessage.Text = "Waiting for server to start...";
                bool serverReady = false;
                int maxAttempts = 30; // 30 seconds
                int attempts = 0;
                
                while (attempts < maxAttempts && !serverReady)
                {
                    await Task.Delay(1000); // Wait 1 second between checks
                    attempts++;
                    
                    serverReady = await ServerProcessManager.Instance.IsServerRunningAsync();
                    
                    if (!serverReady)
                    {
                        statusMessage.Text = $"Waiting for server to start... ({attempts}/{maxAttempts} seconds)";
                    }
                }
                
                if (serverReady)
                {
                    statusMessage.Text = "Server started successfully!";
                    await UpdateServerStatusAsync();
                }
                else
                {
                    statusMessage.Text = "Server did not start in time. Please check the server window for errors.";
                    MessageBox.Show(
                        "The server process started but did not become available on port 9999 within 30 seconds.\n\n" +
                        "Please check the server window for error messages.",
                        "Server Startup Timeout",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                statusMessage.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Error starting server: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                _isInstalling = false;
                
                // Hide status message after delay
                await Task.Delay(2000);
                Dispatcher.Invoke(() =>
                {
                    statusMessage.Visibility = Visibility.Collapsed;
                });
            }
        }
        
        private async Task CheckAndEnableStartButtonAsync()
        {
            try
            {
                var condaCheck = await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync();
                var envCheck = await ServerDiagnosticsService.Instance.CheckCondaEnvironmentAsync();
                
                Dispatcher.Invoke(() =>
                {
                    startServerButton.IsEnabled = condaCheck.available && envCheck.exists;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking start button enable state: {ex.Message}");
            }
        }
        
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Clear instance reference when closing
            if (_instance == this)
            {
                _instance = null;
            }
            
            base.OnClosing(e);
        }
    }
}

