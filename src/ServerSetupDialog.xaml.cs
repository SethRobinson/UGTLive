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
                // Check if any services are running that need to be shut down
                var services = PythonServicesManager.Instance.GetAllServices();
                var runningServices = services.Where(s => s.IsRunning).ToList();
                
                if (runningServices.Count > 0)
                {
                    // Cancel the close for now
                    e.Cancel = true;
                    
                    // Show shutdown dialog and perform graceful shutdown
                    PerformGracefulShutdown();
                }
                else
                {
                    // No services running, just shut down immediately
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }
        
        private async void PerformGracefulShutdown()
        {
            try
            {
                // Show shutdown dialog
                ShutdownDialog shutdownDialog = new ShutdownDialog();
                shutdownDialog.Show();
                shutdownDialog.UpdateStatus("Stopping services...");
                
                // Process UI messages to ensure dialog is visible
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(100);
                
                // Stop Python services
                await PythonServicesManager.Instance.StopOwnedServicesAsync();
                
                // Close shutdown dialog
                shutdownDialog.Close();
                
                // Now shut down the application
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during graceful shutdown: {ex.Message}");
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
                
                // Discover and load services
                await DiscoverAndLoadServicesAsync();
                
                // Auto-start services if needed
                await CheckAndStartAutoStartServicesAsync();
                
                statusMessage.Text = "Diagnostics complete";
                
                // Mark diagnostics as done so the status label can update correctly
                _isRunningDiagnostics = false;
                
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
        
        private async Task CheckAndStartAutoStartServicesAsync()
        {
            var services = PythonServicesManager.Instance.GetAllServices();
            var autoStartServices = services.Where(s => s.AutoStart && !s.IsRunning && s.IsInstalled).ToList();
            
            if (autoStartServices.Count > 0)
            {
                statusMessage.Text = "Starting services...";
                bool showWindow = showServerWindowCheckBox.IsChecked ?? false;
                
                await PythonServicesManager.Instance.StartAutoStartServicesAsync(showWindow, (msg) => 
                {
                    Dispatcher.Invoke(() => statusMessage.Text = msg);
                });
                
                // Refresh status for all services
                await RefreshAllServicesStatusAsync();
            }
        }
        
        private void UpdateStatusLabel()
        {
            // Don't show ready state if we're still running initial diagnostics
            if (_isRunningDiagnostics)
            {
                statusLabel.Text = "Checking system status...";
                statusLabel.Foreground = new SolidColorBrush(Colors.Black);
                return;
            }

            try
            {
                var services = PythonServicesManager.Instance.GetAllServices();
                var autoStartServices = services.Where(s => s.AutoStart).ToList();
                var runningServices = services.Where(s => s.IsRunning).ToList();
                
                if (runningServices.Count > 0)
                {
                    statusLabel.Text = "Services ready. Click Continue.";
                    statusLabel.Foreground = new SolidColorBrush(Colors.Green);
                    return;
                }
                
                if (autoStartServices.Count == 0)
                {
                    statusLabel.Text = "No services configured for auto-start";
                    statusLabel.Foreground = new SolidColorBrush(Colors.Orange);
                    return;
                }
                
                // If we are here, we have auto-start services but none are running yet
                statusLabel.Text = "Waiting for services to start...";
                statusLabel.Foreground = new SolidColorBrush(Colors.Black);
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
                                    gpuStatusText.Text = "No NVIDIA GPU detected";
                                });
                            }
                        }
                    }
                    catch
                    {
                        Dispatcher.Invoke(() =>
                        {
                            gpuStatusIcon.Text = "⚠️";
                            gpuStatusText.Text = "NVIDIA drivers not found";
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
                        InstallButtonText = "Install",
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
                // Always force a check to ensure we catch external closures
                bool isRunning = await service.CheckIsRunningAsync(forceCheck: true);
                
                if (isRunning)
                {
                    // Service is running
                    viewModel.StatusIcon = "✅";
                    viewModel.StatusText = "Running";
                    viewModel.StatusColor = "Green";
                    viewModel.StartStopButtonText = "Stop";
                    viewModel.StartStopEnabled = true;
                    viewModel.InstallButtonText = "Install/Reinstall";
                    viewModel.InstallEnabled = true; // Allow clicking, but will prompt to stop first
                    viewModel.UninstallEnabled = false; // Cannot uninstall while running
                }
                else
                {
                    // Service not running, check if installed
                    bool isInstalled = await service.CheckIsInstalledAsync();
                    
                    if (isInstalled)
                    {
                        // Installed but not running
                        viewModel.StatusIcon = "⏹️";
                        viewModel.StatusText = "Stopped";
                        viewModel.StatusColor = "Red";
                        viewModel.StartStopButtonText = "Start";
                        viewModel.StartStopEnabled = true;
                        viewModel.InstallButtonText = "Install/Reinstall";
                        viewModel.InstallEnabled = true; // Allow reinstall
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
                        viewModel.InstallButtonText = "Install";
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
                        
                        if (started)
                        {
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
                        else
                        {
                            // Service failed to start, refresh status to show correct state
                            Console.WriteLine($"Service {service.ServiceName} failed to start");
                            await UpdateServiceStatusAsync(service);
                        }
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
                
                // Disable button IMMEDIATELY to prevent multiple clicks
                // This must happen BEFORE any async operations
                viewModel.InstallEnabled = false;
                viewModel.StatusText = "Starting installation...";
                viewModel.StatusIcon = "⏳";
                viewModel.StatusColor = "Gray";
                
                // Check if service is currently running
                bool isRunning = await service.CheckIsRunningAsync(forceCheck: true);
                if (isRunning)
                {
                    MessageBox.Show(
                        $"Please stop the {serviceName} service first by clicking the \"Stop\" button.",
                        "Service Running",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    // Re-enable button since we're not proceeding
                    await UpdateServiceStatusAsync(service);
                    return;
                }
                
                try
                {
                    // Check if already installed (Reinstall case)
                    bool isInstalled = await service.CheckIsInstalledAsync();
                    
                    if (isInstalled)
                    {
                        var result = MessageBox.Show(
                            $"This will reinstall {serviceName}. Continue?",
                            "Confirm Reinstall",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                            
                        if (result != MessageBoxResult.Yes)
                        {
                            // User cancelled, refresh status to restore proper button state
                            await UpdateServiceStatusAsync(service);
                            return;
                        }
                        
                        // Note: Install.bat handles removing the existing venv, so we don't need to uninstall explicitly
                        service.InvalidateVenvCache();
                        service.MarkAsNotRunning();
                    }
                
                    viewModel.StatusText = "Installing...";
                    
                    var installDialog = new ServiceInstallDialog();
                    installDialog.Owner = this;
                    
                    // Run installation asynchronously
                    var installTask = installDialog.RunInstallAsync(service);
                    installDialog.ShowDialog();
                    
                    // Wait for installation to complete
                    await installTask;
                    
                    // Invalidate venv cache so it will be re-checked
                    service.InvalidateVenvCache();
                    
                    // Show checking status immediately
                    viewModel.StatusText = "Checking status...";
                    viewModel.StatusIcon = "⏳";
                    viewModel.StatusColor = "Gray";
                    
                    // Refresh service status (this will re-enable buttons appropriately)
                    await UpdateServiceStatusAsync(service);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error installing service: {ex.Message}");
                    MessageBox.Show($"Error installing service: {ex.Message}", "Installation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    // Refresh status to restore proper button state
                    await UpdateServiceStatusAsync(service);
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
                        $"Are you sure you want to uninstall {serviceName}?\n\nThis will remove the virtual environment and all installed packages.",
                        "Confirm Uninstall",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                    
                    // Disable button immediately to prevent multiple clicks
                    viewModel.UninstallEnabled = false;
                    viewModel.StatusText = "Preparing uninstall...";
                    viewModel.StatusIcon = "⏳";
                    
                    var uninstallDialog = new ServiceInstallDialog();
                    uninstallDialog.Owner = this;
                    
                    // Run uninstallation asynchronously
                    var uninstallTask = uninstallDialog.RunUninstallAsync(service);
                    uninstallDialog.ShowDialog();
                    
                    // Wait for uninstallation to complete
                    await uninstallTask;
                    
                    // Invalidate venv cache so it will be re-checked
                    service.InvalidateVenvCache();
                    service.MarkAsNotRunning(); // Ensure it's marked as not running
                    
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
                    
                    // Re-enable button on error
                    await UpdateServiceStatusAsync(service);
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
        
        private async void ServiceAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ServiceItemViewModel viewModel)
            {
                var service = PythonServicesManager.Instance.GetServiceByName(viewModel.ServiceName);
                
                if (service != null)
                {
                    service.AutoStart = viewModel.AutoStart;
                    PythonServicesManager.Instance.SaveAutoStartPreferences();
                    
                    // If checked and not running, start it
                    if (service.AutoStart && !service.IsRunning)
                    {
                        // Check if installed first
                        bool isInstalled = await service.CheckIsInstalledAsync();
                        if (isInstalled)
                        {
                            viewModel.StatusText = "Starting...";
                            viewModel.StatusIcon = "⏳";
                            viewModel.StartStopEnabled = false;
                            
                            bool showWindow = showServerWindowCheckBox.IsChecked ?? false;
                            bool started = await service.StartAsync(showWindow);
                            
                            // Wait for service to initialize
                            await Task.Delay(500);
                            await UpdateServiceStatusAsync(service);
                        }
                        else
                        {
                            // Not installed - ask user if they want to install
                            var result = MessageBox.Show(
                                $"{service.ServiceName} is not installed. Do you want to install it now?",
                                "Install Service",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);
                                
                            if (result == MessageBoxResult.Yes)
                            {
                                // Trigger install logic
                                // We can reuse the ServiceInstall_Click logic by simulating a click or extracting the method
                                // For simplicity, let's just call the button click handler with a dummy button
                                var dummyButton = new Button { Tag = service.ServiceName };
                                ServiceInstall_Click(dummyButton, new RoutedEventArgs());
                            }
                            else
                            {
                                // User said no, uncheck autostart
                                viewModel.AutoStart = false;
                                service.AutoStart = false;
                                PythonServicesManager.Instance.SaveAutoStartPreferences();
                            }
                        }
                    }
                    
                    UpdateStatusLabel();
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
                // Apply visibility changes to all running owned services
                foreach (var service in runningOwnedServices)
                {
                    // Give the window-finding logic time to locate the console window
                    await Task.Delay(300);
                    service.SetWindowVisibility(showWindow);
                }
                
                Console.WriteLine($"Applied window visibility ({(showWindow ? "SHOW" : "HIDE")}) to {runningOwnedServices.Count} running service(s)");
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
            if (sender is Button continueBtn)
            {
                continueBtn.IsEnabled = false;
            }
            
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
                
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                }
            }
        }
    }
}
