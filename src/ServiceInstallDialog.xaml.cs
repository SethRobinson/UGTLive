using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class ServiceInstallDialog : Window
    {
        private Process? _process;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isComplete = false;
        
        public bool Success { get; private set; } = false;
        
        public ServiceInstallDialog()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Runs installation batch script for a service
        /// </summary>
        public async Task RunInstallAsync(PythonService service)
        {
            titleText.Text = $"Installing {service.ServiceName}";
            warningText.Text = "Installation may take up to 10 minutes. Do not close this window.";
            warningBorder.Visibility = Visibility.Visible;
            
            string batchFile = Path.Combine(service.ServiceDirectory, "SetupServerCondaEnv.bat");
            
            if (!File.Exists(batchFile))
            {
                AppendOutput($"ERROR: Installation script not found: {batchFile}");
                OnProcessComplete(false);
                return;
            }
            
            await RunBatchScriptAsync(batchFile, service.ServiceDirectory);
        }
        
        /// <summary>
        /// Runs uninstallation batch script for a service
        /// </summary>
        public async Task RunUninstallAsync(PythonService service)
        {
            titleText.Text = $"Uninstalling {service.ServiceName}";
            warningText.Text = "Uninstallation may take a few minutes. Do not close this window.";
            warningBorder.Visibility = Visibility.Visible;
            
            // Try to find RemoveServerCondaEnv.bat
            string batchFile = Path.Combine(service.ServiceDirectory, "RemoveServerCondaEnv.bat");
            
            if (!File.Exists(batchFile))
            {
                // If RemoveServerCondaEnv.bat doesn't exist, try using conda env remove directly
                AppendOutput($"RemoveServerCondaEnv.bat not found, using conda env remove command...");
                await RunCondaRemoveAsync(service.CondaEnvName);
                return;
            }
            
            await RunBatchScriptAsync(batchFile, service.ServiceDirectory);
        }
        
        /// <summary>
        /// Runs a batch script and captures output
        /// </summary>
        private async Task RunBatchScriptAsync(string batchFile, string workingDirectory)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            
            await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = batchFile,
                        Arguments = "nopause",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    
                    _process = new Process { StartInfo = psi };
                    
                    // Wire up output handlers
                    _process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            AppendOutput(e.Data);
                        }
                    };
                    
                    _process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            AppendOutput($"[ERROR] {e.Data}");
                        }
                    };
                    
                    AppendOutput($"Starting: {Path.GetFileName(batchFile)}");
                    AppendOutput($"Working directory: {workingDirectory}");
                    AppendOutput("-----------------------------------");
                    
                    _process.Start();
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                    
                    // Wait for process to exit or cancellation
                    while (!_process.HasExited)
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            _process.Kill();
                            AppendOutput("-----------------------------------");
                            AppendOutput("Installation cancelled by user");
                            OnProcessComplete(false);
                            return;
                        }
                        
                        Thread.Sleep(100);
                    }
                    
                    _process.WaitForExit();
                    
                    AppendOutput("-----------------------------------");
                    AppendOutput($"Process exited with code: {_process.ExitCode}");
                    
                    bool success = _process.ExitCode == 0;
                    
                    if (success)
                    {
                        AppendOutput("Installation completed successfully!");
                    }
                    else
                    {
                        AppendOutput("Installation failed. Please review the output above.");
                    }
                    
                    OnProcessComplete(success);
                }
                catch (Exception ex)
                {
                    AppendOutput($"ERROR: {ex.Message}");
                    OnProcessComplete(false);
                }
            }, _cancellationTokenSource.Token);
        }
        
        /// <summary>
        /// Removes conda environment using conda env remove command
        /// </summary>
        private async Task RunCondaRemoveAsync(string envName)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            
            await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "conda",
                        Arguments = $"env remove -n {envName} -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    
                    _process = new Process { StartInfo = psi };
                    
                    // Wire up output handlers
                    _process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            AppendOutput(e.Data);
                        }
                    };
                    
                    _process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            AppendOutput($"[ERROR] {e.Data}");
                        }
                    };
                    
                    AppendOutput($"Removing conda environment: {envName}");
                    AppendOutput("-----------------------------------");
                    
                    _process.Start();
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                    
                    // Wait for process to exit or cancellation
                    while (!_process.HasExited)
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            _process.Kill();
                            AppendOutput("-----------------------------------");
                            AppendOutput("Uninstallation cancelled by user");
                            OnProcessComplete(false);
                            return;
                        }
                        
                        Thread.Sleep(100);
                    }
                    
                    _process.WaitForExit();
                    
                    AppendOutput("-----------------------------------");
                    AppendOutput($"Process exited with code: {_process.ExitCode}");
                    
                    bool success = _process.ExitCode == 0;
                    
                    if (success)
                    {
                        AppendOutput("Uninstallation completed successfully!");
                    }
                    else
                    {
                        AppendOutput("Uninstallation failed. Please review the output above.");
                    }
                    
                    OnProcessComplete(success);
                }
                catch (Exception ex)
                {
                    AppendOutput($"ERROR: {ex.Message}");
                    OnProcessComplete(false);
                }
            }, _cancellationTokenSource.Token);
        }
        
        /// <summary>
        /// Appends text to the output textbox
        /// </summary>
        private void AppendOutput(string text)
        {
            Dispatcher.Invoke(() =>
            {
                outputTextBox.AppendText(text + Environment.NewLine);
                
                // Auto-scroll to bottom
                outputScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
        
        /// <summary>
        /// Called when process completes
        /// </summary>
        private void OnProcessComplete(bool success)
        {
            _isComplete = true;
            Success = success;
            
            Dispatcher.Invoke(() =>
            {
                progressBar.Visibility = Visibility.Collapsed;
                cancelButton.Visibility = Visibility.Collapsed;
                closeButton.Visibility = Visibility.Visible;
                warningBorder.Visibility = Visibility.Collapsed;
            });
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isComplete)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to cancel the installation?",
                    "Cancel Installation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _cancellationTokenSource?.Cancel();
                }
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = Success;
            this.Close();
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isComplete)
            {
                var result = MessageBox.Show(
                    "Installation is still in progress. Are you sure you want to close this window?",
                    "Installation In Progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                
                _cancellationTokenSource?.Cancel();
            }
            
            base.OnClosing(e);
        }
    }
}

