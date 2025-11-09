using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                // Show the dialog
                dialog.ShowDialog();
            }
        }
        
        private ServerSetupDialog()
        {
            InitializeComponent();
            this.Loaded += ServerSetupDialog_Loaded;
            
            // Clear instance when window is closed
            this.Closed += (s, e) =>
            {
                if (_instance == this)
                {
                    _instance = null;
                }
            };
        }
        
        private async void ServerSetupDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await RunDiagnosticsAsync();
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
                // Check Conda
                await UpdateCondaStatusAsync();
                
                // Check Environment
                await UpdateEnvironmentStatusAsync();
                
                // Check Packages
                await UpdatePackagesStatusAsync();
                
                // Check Server
                await UpdateServerStatusAsync();
                
                statusMessage.Text = "Diagnostics complete";
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
                Dispatcher.Invoke(() =>
                {
                    statusMessage.Visibility = Visibility.Collapsed;
                });
            }
        }
        
        private async Task UpdateCondaStatusAsync()
        {
            var result = await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync();
            
            Dispatcher.Invoke(() =>
            {
                if (result.available)
                {
                    condaStatusIcon.Text = "✓";
                    condaStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    condaStatusText.Text = $"Available - {result.version}";
                    installCondaButton.IsEnabled = false;
                }
                else
                {
                    condaStatusIcon.Text = "✗";
                    condaStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    condaStatusText.Text = result.errorMessage;
                    installCondaButton.IsEnabled = true;
                }
            });
        }
        
        private async Task UpdateEnvironmentStatusAsync()
        {
            var result = await ServerDiagnosticsService.Instance.CheckCondaEnvironmentAsync();
            
            Dispatcher.Invoke(() =>
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
            });
        }
        
        private async Task UpdateServerStatusAsync()
        {
            var result = await ServerProcessManager.Instance.IsServerRunningAsync();
            
            Dispatcher.Invoke(() =>
            {
                if (result)
                {
                    serverStatusIcon.Text = "✓";
                    serverStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(32, 184, 20)); // Green
                    serverStatusText.Text = "Server is running on port 9999";
                    startServerButton.IsEnabled = false;
                    closeButton.IsEnabled = true;
                }
                else
                {
                    serverStatusIcon.Text = "✗";
                    serverStatusIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 0, 0)); // Red
                    serverStatusText.Text = "Server is not running";
                    
                    // Enable start button if conda and environment are available
                    var condaCheck = Task.Run(async () => 
                        await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync()).Result;
                    var envCheck = Task.Run(async () => 
                        await ServerDiagnosticsService.Instance.CheckCondaEnvironmentAsync()).Result;
                    
                    startServerButton.IsEnabled = condaCheck.available && envCheck.exists;
                }
            });
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
                
                Process.Start(psi);
                
                statusMessage.Text = "Miniconda installer started. Please complete the installation and restart UGTLive.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting installer: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusMessage.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isInstalling = false;
            }
        }
        
        private async void InstallBackendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) return;
            
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
                // Start the server process
                var startResult = await ServerProcessManager.Instance.StartServerProcessAsync();
                
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
        
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RunDiagnosticsAsync();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Only allow closing if server is running
            // User can still close via window X button if needed
            if (closeButton.IsEnabled)
            {
                this.Close();
            }
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Allow closing even if server isn't running (user might want to use Windows OCR)
            // Just warn if server isn't running
            if (!closeButton.IsEnabled)
            {
                var result = MessageBox.Show(
                    "The server is not running. Some OCR methods (EasyOCR, Manga OCR, docTR) will not be available.\n\n" +
                    "You can switch to Windows OCR in Settings, or set up the server later.\n\n" +
                    "Close this dialog?",
                    "Server Not Running",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }
            
            // Clear instance reference when closing
            if (_instance == this)
            {
                _instance = null;
            }
            
            base.OnClosing(e);
        }
    }
}

