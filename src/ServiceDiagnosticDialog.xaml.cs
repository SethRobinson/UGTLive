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
    public partial class ServiceDiagnosticDialog : Window
    {
        private Process? _process;
        private bool _isComplete = false;
        
        public ServiceDiagnosticDialog()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Runs diagnostic test batch script for a service
        /// </summary>
        public async Task RunDiagnosticsAsync(PythonService service)
        {
            titleText.Text = $"Running diagnostics for {service.ServiceName}";
            
            string batchFile = Path.Combine(service.ServiceDirectory, "DiagnosticTest.bat");
            
            if (!File.Exists(batchFile))
            {
                AppendOutput($"ERROR: Diagnostic script not found: {batchFile}");
                OnProcessComplete();
                return;
            }
            
            await RunBatchScriptAsync(batchFile, service.ServiceDirectory);
        }
        
        /// <summary>
        /// Runs a batch script and captures output
        /// </summary>
        private async Task RunBatchScriptAsync(string batchFile, string workingDirectory)
        {
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
                    
                    _process.WaitForExit();
                    
                    AppendOutput("-----------------------------------");
                    
                    if (_process.ExitCode == 0)
                    {
                        AppendOutput("All diagnostic tests passed!");
                    }
                    else
                    {
                        AppendOutput($"Diagnostic tests failed (exit code: {_process.ExitCode})");
                        AppendOutput("Please review the output above.");
                    }
                    
                    OnProcessComplete();
                }
                catch (Exception ex)
                {
                    AppendOutput($"ERROR: {ex.Message}");
                    OnProcessComplete();
                }
            });
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
        private void OnProcessComplete()
        {
            _isComplete = true;
            
            Dispatcher.Invoke(() =>
            {
                progressBar.Visibility = Visibility.Collapsed;
            });
        }
        
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Kill process if still running
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                }
                catch
                {
                    // Ignore errors when killing process
                }
            }
            
            base.OnClosing(e);
        }
    }
}

