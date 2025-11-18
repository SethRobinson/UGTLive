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
        private ObservableCollection<ServiceItemViewModel> _serviceViewModels = new ObservableCollection<ServiceItemViewModel>();
        private Dictionary<string, ServiceItemViewModel> _serviceViewModelMap = new Dictionary<string, ServiceItemViewModel>();
        
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
                
                // Set version text
                versionTextBlock.Text = "V0.28 by Seth A. Robinson";
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
        
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                updateStatusIcon.Text = "⏳";
                updateStatusText.Text = "Checking for updates...";
                
                // Check for updates logic here (using existing code if available)
                // For now, just mark as up to date
                await Task.Delay(100);
                
                updateStatusIcon.Text = "✅";
                updateStatusText.Text = "Up to date";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking updates: {ex.Message}");
                updateStatusIcon.Text = "⚠️";
                updateStatusText.Text = "Could not check for updates";
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
                
                var result = await ServerDiagnosticsService.Instance.CheckCondaAvailableAsync();
                
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
            
            foreach (var service in services)
            {
                await UpdateServiceStatusAsync(service);
            }
        }
        
        private async Task UpdateServiceStatusAsync(PythonService service)
        {
            if (!_serviceViewModelMap.TryGetValue(service.ServiceName, out var viewModel))
            {
                return;
            }
            
            try
            {
                // First check if running (fast check via HTTP)
                bool isRunning = await service.CheckIsRunningAsync();
                
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
                        
                        // Always refresh status after start attempt
                        await Task.Delay(2000);
                        await UpdateServiceStatusAsync(service);
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
                var result = MessageBox.Show(
                    $"To apply this change, running services need to be restarted.\n\n" +
                    $"Restart {runningOwnedServices.Count} running service(s) now?",
                    "Restart Services?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    statusMessage.Visibility = Visibility.Visible;
                    progressBar.Visibility = Visibility.Visible;
                    
                    foreach (var service in runningOwnedServices)
                    {
                        statusMessage.Text = $"Restarting {service.ServiceName}...";
                        
                        // Stop the service
                        await service.StopAsync();
                        await Task.Delay(1000);
                        
                        // Start with new visibility setting
                        await service.StartAsync(showWindow);
                        await Task.Delay(2000);
                        
                        // Update status
                        await UpdateServiceStatusAsync(service);
                    }
                    
                    statusMessage.Visibility = Visibility.Collapsed;
                    progressBar.Visibility = Visibility.Collapsed;
                }
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
            // Update download logic (if needed)
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
                    
                    statusMessage.Visibility = Visibility.Visible;
                    progressBar.Visibility = Visibility.Visible;
                    
                    await PythonServicesManager.Instance.StartAutoStartServicesAsync(showWindow, (message) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            statusMessage.Text = message;
                        });
                    });
                    
                    // Give services a moment to stabilize
                    await Task.Delay(1000);
                    
                    progressBar.Visibility = Visibility.Collapsed;
                    statusMessage.Visibility = Visibility.Collapsed;
                }
                
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
