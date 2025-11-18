using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Navigation;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using MediaColor = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace UGTLive
{
    public partial class ServerSetupDialog : Window
    {
        private static ServerSetupDialog? _instance;
        private bool _isRunningDiagnostics = false;
        private bool _fromSettings = false;
        private bool _normalClose = false;
        private ObservableCollection<ServiceItemViewModel> _serviceViewModels = new ObservableCollection<ServiceItemViewModel>();
        private Dictionary<string, ServiceItemViewModel> _serviceViewModelMap = new Dictionary<string, ServiceItemViewModel>();
        private VersionInfo? _latestVersionInfo;
        
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
        /// Checks if the ServerSetupDialog is currently open and visible
        /// </summary>
        public static bool IsDialogOpen
        {
            get
            {
                return _instance != null && IsWindowValid(_instance) && _instance.IsVisible;
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
        public static void ShowDialogSafe(bool fromSettings = false)
        {
            var dialog = Instance;
            dialog._fromSettings = fromSettings;
            
            if (dialog.IsVisible)
            {
                dialog.Activate();
                dialog.Focus();
            }
            else
            {
                dialog.ShowDialog();
            }
        }
        
        private ServerSetupDialog()
        {
            InitializeComponent();
            this.Loaded += ServerSetupDialog_Loaded;
            this.Closing += ServerSetupDialog_Closing;
            
            // Load checkbox state from config
            showServerWindowCheckBox.IsChecked = ConfigManager.Instance.GetShowServerWindow();
            skipCondaChecksCheckBox.IsChecked = ConfigManager.Instance.GetSkipCondaChecks();
            
            // Load app icon and version info
            LoadSplashBranding();
            
            // Bind services list to ItemsControl
            servicesItemsControl.ItemsSource = _serviceViewModels;
            
            // Clear instance when window is closed
            this.Closed += (s, e) =>
            {
                if (_instance == this)
                {
                    _instance = null;
                }
            };
        }
        
        private void ServerSetupDialog_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If dialog is closed via X button (not via Continue) and not from settings, shut down the app
            if (!_normalClose && !_fromSettings)
            {
                System.Windows.Application.Current.Shutdown();
            }
        }
        
        private void LoadSplashBranding()
        {
            try
            {
                // Load app icon
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "media", "Icon1.ico");
                if (File.Exists(iconPath))
                {
                    var bitmapSource = new BitmapImage(new Uri(iconPath));
                    appIconImage.Source = bitmapSource;
                }
                
                // Set version text from SplashManager
                versionTextBlock.Text = $"V{SplashManager.CurrentVersion.ToString("0.00")} by Seth A. Robinson";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading splash branding: {ex.Message}");
            }
        }
        
        private async void ServerSetupDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Reset normal close flag when dialog is loaded
            _normalClose = false;
            
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
                // Check for updates first
                await CheckForUpdatesAsync();
                
                // Check GPU
                await UpdateGpuStatusAsync();
                
                // If opened from Settings, skip conda checks
                if (_fromSettings)
                {
                    await UpdateCondaStatusSkippedAsync();
                }
                else
                {
                    // Check if user wants to skip conda/python checks
                    bool skipCondaChecks = await Dispatcher.InvokeAsync(() => skipCondaChecksCheckBox.IsChecked ?? false);
                    
                    if (skipCondaChecks)
                    {
                        await UpdateCondaStatusSkippedAsync();
                    }
                    else
                    {
                        // Check Conda
                        await UpdateCondaStatusAsync();
                    }
                }
                
                // Discover and load services
                await DiscoverAndLoadServicesAsync();
                
                statusMessage.Text = "Diagnostics complete";
                
                // Update status label based on autostart services state
                UpdateStatusLabel();
                
                // Auto-hide status message after delay
                await Task.Delay(2000);
                Dispatcher.Invoke(() =>
                {
                    statusMessage.Visibility = Visibility.Collapsed;
                    progressBar.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running diagnostics: {ex.Message}");
                statusMessage.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isRunningDiagnostics = false;
            }
        }
        
        private void UpdateStatusLabel()
        {
            try
            {
                var services = PythonServicesManager.Instance.GetAllServices();
                var autoStartServices = services.Where(s => s.AutoStart).ToList();
                
                if (autoStartServices.Count == 0)
                {
                    statusLabel.Text = "No services configured for auto-start";
                    statusLabel.Foreground = new SolidColorBrush(Colors.Orange);
                    return;
                }
                
                var allRunning = autoStartServices.All(s => s.IsRunning);
                
                if (allRunning)
                {
                    statusLabel.Text = "Everything looks good, click Continue.";
                    statusLabel.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    statusLabel.Text = "Making sure everything is ready...";
                    statusLabel.Foreground = new SolidColorBrush(Colors.Black);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating status label: {ex.Message}");
                statusLabel.Text = "Making sure everything is ready...";
                statusLabel.Foreground = new SolidColorBrush(Colors.Black);
            }
        }
        
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                updateStatusIcon.Text = "⏳";
                updateStatusText.Text = "Checking for updates...";
                
                // Use SplashManager's version checking logic
                var versionInfo = await FetchVersionInfoAsync();
                
                if (versionInfo == null)
                {
                    updateStatusIcon.Text = "⚠️";
                    updateStatusText.Text = "Could not check for updates";
                    return;
                }
                
                if (versionInfo.LatestVersion > SplashManager.CurrentVersion)
                {
                    _latestVersionInfo = versionInfo;
                    updateStatusIcon.Text = "⬇️";
                    updateStatusText.Text = $"New version {versionInfo.LatestVersion.ToString("0.00")} available!";
                    downloadUpdateButton.IsEnabled = true;
                    downloadUpdateButton.Visibility = Visibility.Visible;
                }
                else
                {
                    updateStatusIcon.Text = "✅";
                    updateStatusText.Text = "Up to date";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking updates: {ex.Message}");
                updateStatusIcon.Text = "⚠️";
                updateStatusText.Text = "Could not check for updates";
            }
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
        
        private async Task<VersionInfo?> FetchVersionInfoAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string versionCheckerUrl = "https://raw.githubusercontent.com/SethRobinson/UGTLive/refs/heads/main/media/latest_version_checker.json";
                    string json = await client.GetStringAsync(versionCheckerUrl);
                    Console.WriteLine($"Received JSON: {json}");
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    
                    var result = JsonSerializer.Deserialize<VersionInfo>(json, options);
                    Console.WriteLine($"Deserialized version: {result?.LatestVersion}, name: {result?.Name}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching version info: {ex.Message}");
                return null;
            }
        }
        
        private async Task UpdateGpuStatusAsync()
        {
            try
            {
                gpuStatusIcon.Text = "⏳";
                gpuStatusText.Text = "Checking GPU...";
                
                await Task.Run(() =>
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "nvidia-smi",
                            Arguments = "--query-gpu=name --format=csv,noheader",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };
                        
                        using (Process process = Process.Start(psi)!)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            
                            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                            {
                                string gpuName = output.Trim().Split('\n')[0];
                                Dispatcher.Invoke(() =>
                                {
                                    gpuStatusIcon.Text = "✅";
                                    gpuStatusText.Text = $"Found: {gpuName}";
                                });
                            }
                            else
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    gpuStatusIcon.Text = "⚠️";
                                    gpuStatusText.Text = "No NVIDIA GPU detected (CPU fallback will be used)";
                                });
                            }
                        }
                    }
                    catch
                    {
                        Dispatcher.Invoke(() =>
                        {
                            gpuStatusIcon.Text = "⚠️";
                            gpuStatusText.Text = "NVIDIA drivers not found (CPU fallback will be used)";
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking GPU: {ex.Message}");
                gpuStatusIcon.Text = "⚠️";
                gpuStatusText.Text = "Could not check GPU";
            }
        }
        
        private async Task UpdateCondaStatusAsync()
        {
            try
            {
                condaStatusIcon.Text = "⏳";
                condaStatusText.Text = "Checking conda...";
                
                var result = await checkCondaAvailableAsync();
                
                if (result.available)
                {
                    condaStatusIcon.Text = "✅";
                    condaStatusText.Text = $"Installed: {result.version}";
                    installCondaButton.IsEnabled = false;
                }
                else
                {
                    condaStatusIcon.Text = "❌";
                    condaStatusText.Text = "Not installed";
                    installCondaButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking conda: {ex.Message}");
                condaStatusIcon.Text = "⚠️";
                condaStatusText.Text = "Error checking conda";
            }
        }
        
        /// <summary>
        /// Checks if conda is available in the system PATH
        /// </summary>
        private async Task<(bool available, string version, string errorMessage)> checkCondaAvailableAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Try to find conda.exe or conda.bat
                    string condaPath = findCondaExecutable();
                    if (string.IsNullOrEmpty(condaPath))
                    {
                        return (false, "", "Conda is not installed or not in PATH");
                    }
                    
                    // Try to get conda version
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = condaPath,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };
                    
                    using (Process process = Process.Start(psi)!)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            string version = output.Trim();
                            return (true, version, "");
                        }
                        else
                        {
                            return (true, "Unknown", error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, "", $"Error checking conda: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Finds conda executable in common locations or PATH
        /// </summary>
        private string findCondaExecutable()
        {
            // First try "conda" command (if in PATH)
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "conda",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (Process process = Process.Start(psi)!)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        string[] paths = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string path in paths)
                        {
                            string trimmed = path.Trim();
                            if (File.Exists(trimmed))
                            {
                                return trimmed;
                            }
                        }
                    }
                }
            }
            catch { }
            
            // Try common installation locations
            string[] commonPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "miniconda3", "Scripts", "conda.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "Users", Environment.UserName, "miniconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "Users", Environment.UserName, "anaconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "ProgramData", "miniconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "ProgramData", "anaconda3", "Scripts", "conda.exe")
            };
            
            foreach (string path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            return "";
        }
        
        private async Task UpdateCondaStatusSkippedAsync()
        {
            await Task.Delay(100);
            condaStatusIcon.Text = "⏭️";
            condaStatusText.Text = "Skipped (conda checks disabled)";
            installCondaButton.IsEnabled = false;
        }
        
        private async Task DiscoverAndLoadServicesAsync()
        {
            try
            {
                // Discover services
                PythonServicesManager.Instance.DiscoverServices();
                
                var services = PythonServicesManager.Instance.GetAllServices();
                
                if (services.Count == 0)
                {
                    Console.WriteLine("No Python services found");
                    return;
                }
                
                // Create view models for each service
                foreach (var service in services)
                {
                    var viewModel = new ServiceItemViewModel
                    {
                        ServiceName = service.ServiceName,
                        Description = service.Description,
                        Port = service.Port,
                        AutoStart = service.AutoStart,
                        StatusIcon = "⏳",
                        StatusText = "Checking...",
                        StatusColor = "Gray",
                        StartStopButtonText = "Start",
                        StartStopEnabled = false,
                        InstallEnabled = false,
                        UninstallEnabled = false,
                        TestEnabled = false
                    };
                    
                    _serviceViewModels.Add(viewModel);
                    _serviceViewModelMap[service.ServiceName] = viewModel;
                }
                
                // Check status of each service
                await RefreshAllServicesStatusAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error discovering services: {ex.Message}");
            }
        }
        
        private async Task RefreshAllServicesStatusAsync()
        {
            var services = PythonServicesManager.Instance.GetAllServices();
            
            // Update all services in parallel for faster diagnostics
            var tasks = services.Select(service => UpdateServiceStatusAsync(service));
            await Task.WhenAll(tasks);
        }
        
        private async Task UpdateServiceStatusAsync(PythonService service)
        {
            if (!_serviceViewModelMap.TryGetValue(service.ServiceName, out var viewModel))
            {
                return;
            }
            
            try
            {
                // Trust IsRunning property first - only check /info if not already marked as running
                bool isRunning = service.IsRunning;
                
                if (!isRunning)
                {
                    // Only hit /info endpoint if we don't already know it's running
                    isRunning = await service.CheckIsRunningAsync();
                }
                
                if (isRunning)
                {
                    // Service is running
                    viewModel.StatusIcon = "✅";
                    viewModel.StatusText = "Running";
                    viewModel.StatusColor = "Green";
                    viewModel.StartStopButtonText = "Stop";
                    viewModel.StartStopEnabled = true;
                    viewModel.InstallEnabled = false;
                    viewModel.UninstallEnabled = true;
                }
                else
                {
                    // Service not running, check if installed
                    bool isInstalled = await service.CheckIsInstalledAsync();
                    
                    if (isInstalled)
                    {
                        // Installed but not running
                        viewModel.StatusIcon = "⏸️";
                        viewModel.StatusText = "Stopped (Installed)";
                        viewModel.StatusColor = "Orange";
                        viewModel.StartStopButtonText = "Start";
                        viewModel.StartStopEnabled = true;
                        viewModel.InstallEnabled = false;
                        viewModel.UninstallEnabled = true;
                    }
                    else
                    {
                        // Not installed
                        viewModel.StatusIcon = "❌";
                        viewModel.StatusText = "Not Installed";
                        viewModel.StatusColor = "Red";
                        viewModel.StartStopButtonText = "Start";
                        viewModel.StartStopEnabled = false;
                        viewModel.InstallEnabled = true;
                        viewModel.UninstallEnabled = false;
                    }
                }
                
                // Update status label when service status changes
                UpdateStatusLabel();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating status for {service.ServiceName}: {ex.Message}");
                viewModel.StatusIcon = "⚠️";
                viewModel.StatusText = "Error checking status";
                viewModel.StatusColor = "Gray";
                viewModel.StartStopEnabled = true; // Re-enable button even on error
            }
        }
        
        private async void ServiceStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string serviceName)
            {
                var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
                
                if (service == null) return;
                
                if (!_serviceViewModelMap.TryGetValue(serviceName, out var viewModel))
                {
                    return;
                }
                
                try
                {
                    if (service.IsRunning)
                    {
                        // Stop service
                        viewModel.StatusText = "Stopping...";
                        viewModel.StatusIcon = "⏳";
                        viewModel.StartStopEnabled = false;
                        
                        bool stopped = await service.StopAsync();
                        
                        // Always refresh status after stop attempt
                        await Task.Delay(500);
                        await UpdateServiceStatusAsync(service);
                    }
                    else
                    {
                        // Start service
                        viewModel.StatusText = "Starting...";
                        viewModel.StatusIcon = "⏳";
                        viewModel.StartStopEnabled = false;
                        
                        bool showWindow = showServerWindowCheckBox.IsChecked ?? false;
                        bool started = await service.StartAsync(showWindow);
                        
                        // Wait for service to initialize models (startup event)
                        await Task.Delay(500);
                       
                            viewModel.StatusIcon = "✅";
                            viewModel.StatusText = "Running";
                            viewModel.StatusColor = "Green";
                            viewModel.StartStopButtonText = "Stop";
                            viewModel.StartStopEnabled = true;
                            viewModel.InstallEnabled = false;
                            viewModel.UninstallEnabled = true;
                            viewModel.TestEnabled = true;
                       
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error starting/stopping service: {ex.Message}");
                    MessageBox.Show($"Error: {ex.Message}", "Service Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    // Refresh status even on error
                    await UpdateServiceStatusAsync(service);
                }
            }
        }
        
        private async void ServiceInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string serviceName)
            {
                var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
                
                if (service == null) return;
                
                if (!_serviceViewModelMap.TryGetValue(serviceName, out var viewModel))
                {
                    return;
                }
                
                try
                {
                    var installDialog = new ServiceInstallDialog();
                    installDialog.Owner = this;
                    
                    // Run installation asynchronously
                    var installTask = installDialog.RunInstallAsync(service);
                    installDialog.ShowDialog();
                    
                    // Wait for installation to complete
                    await installTask;
                    
                    // Show checking status immediately
                    viewModel.StatusText = "Checking status...";
                    viewModel.StatusIcon = "⏳";
                    viewModel.StatusColor = "Gray";
                    
                    // Refresh service status
                    await UpdateServiceStatusAsync(service);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error installing service: {ex.Message}");
                    MessageBox.Show($"Error installing service: {ex.Message}", "Installation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private async void ServiceUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string serviceName)
            {
                var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
                
                if (service == null) return;
                
                if (!_serviceViewModelMap.TryGetValue(serviceName, out var viewModel))
                {
                    return;
                }
                
                try
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to uninstall {serviceName}?\n\nThis will remove the conda environment and all installed packages.",
                        "Confirm Uninstall",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                    
                    var uninstallDialog = new ServiceInstallDialog();
                    uninstallDialog.Owner = this;
                    
                    // Run uninstallation asynchronously
                    var uninstallTask = uninstallDialog.RunUninstallAsync(service);
                    uninstallDialog.ShowDialog();
                    
                    // Wait for uninstallation to complete
                    await uninstallTask;
                    
                    // Invalidate conda env cache so it will be re-checked
                    service.InvalidateCondaEnvCache();
                    
                    // Show checking status immediately
                    viewModel.StatusText = "Checking status...";
                    viewModel.StatusIcon = "⏳";
                    viewModel.StatusColor = "Gray";
                    
                    // Refresh service status
                    await UpdateServiceStatusAsync(service);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uninstalling service: {ex.Message}");
                    MessageBox.Show($"Error uninstalling service: {ex.Message}", "Uninstallation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private async void ServiceTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string serviceName)
            {
                var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
                
                if (service == null) return;
                
                try
                {
                    var diagnosticDialog = new ServiceDiagnosticDialog();
                    diagnosticDialog.Owner = this;
                    
                    // Run diagnostics asynchronously
                    var diagnosticTask = diagnosticDialog.RunDiagnosticsAsync(service);
                    diagnosticDialog.ShowDialog();
                    
                    // Wait for diagnostics to complete
                    await diagnosticTask;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error testing service: {ex.Message}");
                    MessageBox.Show($"Error running diagnostics: {ex.Message}", "Test Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ServiceAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ServiceItemViewModel viewModel)
            {
                var service = PythonServicesManager.Instance.GetServiceByName(viewModel.ServiceName);
                
                if (service != null)
                {
                    service.AutoStart = viewModel.AutoStart;
                    PythonServicesManager.Instance.SaveAutoStartPreferences();
                }
            }
        }
        
        private async void ShowServerWindowCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool showWindow = showServerWindowCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetShowServerWindow(showWindow);
            
            // Check if any owned services are running
            var services = PythonServicesManager.Instance.GetAllServices();
            var runningOwnedServices = services.Where(s => s.IsOwnedByApp && s.IsRunning).ToList();
            
            if (runningOwnedServices.Count > 0)
            {
              //todo, apply visibility to window of the service in question

                    statusMessage.Visibility = Visibility.Collapsed;
                    progressBar.Visibility = Visibility.Collapsed;
             
            }
        }
        
        private void SkipCondaChecksCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool skipChecks = skipCondaChecksCheckBox.IsChecked ?? false;
            ConfigManager.Instance.SetSkipCondaChecks(skipChecks);
        }
        
        private void InstallCondaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string batchFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "services", "util", "InstallMiniConda.bat");
                
                if (File.Exists(batchFile))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = batchFile,
                        UseShellExecute = true
                    });
                    
                    MessageBox.Show(
                        "Miniconda installer has been launched.\n\nAfter installation completes, please restart this application.",
                        "Miniconda Installation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Installation script not found: {batchFile}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching conda installer: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Installation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestVersionInfo == null)
            {
                MessageBox.Show("Update information not available.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            try
            {
                string downloadUrl = "https://www.rtsoft.com/files/UniversalGameTranslatorLive_Windows.zip";
                
                // Format the message by replacing {VERSION_STRING} placeholder
                string message = _latestVersionInfo.Message?.Replace("{VERSION_STRING}", _latestVersionInfo.LatestVersion.ToString("0.00")) ?? "New update available!";
                
                // Show custom update dialog
                var updateDialog = new UpdateAvailableDialog(
                    _latestVersionInfo.LatestVersion,
                    message,
                    downloadUrl
                );
                updateDialog.Owner = this;
                
                bool? result = updateDialog.ShowDialog();
                
                // If user clicked Download Now, the app will have already shut down
                // If they clicked Cancel, result will be false and we just continue
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing update dialog: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening link: {ex.Message}");
            }
        }
        
        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save all autostart preferences
                PythonServicesManager.Instance.SaveAutoStartPreferences();
                
                // Start autostart services if not opened from settings
                if (!_fromSettings)
                {
                    bool showWindow = showServerWindowCheckBox.IsChecked ?? false;
                    var services = PythonServicesManager.Instance.GetAllServices();
                    var autoStartServices = services.Where(s => s.AutoStart).ToList();
                    
                    // Check if any autostart services need to be started
                    var needsStarting = autoStartServices.Where(s => !s.IsRunning).ToList();
                    
                    if (needsStarting.Count > 0)
                    {
                        // Only show progress if we actually need to start services
                        statusMessage.Visibility = Visibility.Visible;
                        progressBar.Visibility = Visibility.Visible;
                        
                        await PythonServicesManager.Instance.StartAutoStartServicesAsync(showWindow, (message) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                statusMessage.Text = message;
                            });
                        });
                        
                        progressBar.Visibility = Visibility.Collapsed;
                        statusMessage.Visibility = Visibility.Collapsed;
                    }
                    // If all services are already running, close immediately - no delay needed
                }
                
                // Mark as normal close so the Closing event doesn't shut down the app
                _normalClose = true;
                this.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in continue button: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
