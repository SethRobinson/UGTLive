using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace UGTLive
{
    public partial class SetupLogViewerWindow : Window
    {
        private string _logFilePath;
        private FileSystemWatcher? _fileWatcher;
        private DispatcherTimer? _refreshTimer;
        private DateTime _lastModified = DateTime.MinValue;
        private bool _autoScroll = true;
        
        public SetupLogViewerWindow(string logFilePath, string serviceName, Window? owner = null)
        {
            InitializeComponent();
            _logFilePath = logFilePath;
            
            // Set owner so this window stays above the install dialog
            if (owner != null)
            {
                Owner = owner;
            }
            
            titleText.Text = $"📋 Setup Log - {serviceName}";
            filePathTextBox.Text = logFilePath;
            
            // Initial load
            LoadLogContent();
            
            // Set up file watcher
            SetupFileWatcher();
            
            // Set up refresh timer as backup (in case FileSystemWatcher misses events)
            SetupRefreshTimer();
        }
        
        private void SetupFileWatcher()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_logFilePath);
                string fileName = Path.GetFileName(_logFilePath);
                
                if (string.IsNullOrEmpty(directory))
                {
                    return;
                }
                
                _fileWatcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                
                _fileWatcher.Changed += OnLogFileChanged;
                _fileWatcher.Created += OnLogFileChanged;
                _fileWatcher.Deleted += OnLogFileDeleted;
                
                UpdateStatus("Watching for changes...", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up file watcher: {ex.Message}");
                UpdateStatus("File watcher failed, using timer only", false);
            }
        }
        
        private void SetupRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (s, e) => LoadLogContent();
            _refreshTimer.Start();
        }
        
        private void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LoadLogContent();
            });
        }
        
        private void OnLogFileDeleted(object sender, FileSystemEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                logTextBox.Text = "(Log file was deleted or reset - waiting for new content...)";
                _lastModified = DateTime.MinValue;
                UpdateStatus("Log file deleted, waiting...", false);
            });
        }
        
        private void LoadLogContent()
        {
            try
            {
                if (!File.Exists(_logFilePath))
                {
                    if (logTextBox.Text != "(Log file not found yet - waiting for installation to create it...)")
                    {
                        logTextBox.Text = "(Log file not found yet - waiting for installation to create it...)";
                        UpdateStatus("Waiting for log file...", false);
                    }
                    return;
                }
                
                // Check if file was modified
                var fileInfo = new FileInfo(_logFilePath);
                if (fileInfo.LastWriteTime <= _lastModified)
                {
                    return; // No changes
                }
                
                _lastModified = fileInfo.LastWriteTime;
                
                // Read the file with shared access (so we don't block the installer)
                string content;
                using (var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    logTextBox.Text = "(Log file is empty)";
                    UpdateStatus("Log file empty", true);
                    return;
                }
                
                logTextBox.Text = content;
                UpdateStatus($"Updated: {DateTime.Now:HH:mm:ss}", true);
                
                // Auto-scroll to bottom
                if (_autoScroll)
                {
                    logScrollViewer.ScrollToEnd();
                }
            }
            catch (IOException)
            {
                // File might be locked, will retry on next timer tick
                UpdateStatus("File temporarily locked, retrying...", false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading log file: {ex.Message}");
                UpdateStatus($"Error reading file", false);
            }
        }
        
        private void UpdateStatus(string text, bool isOk)
        {
            statusText.Text = text;
            statusIndicator.Fill = isOk 
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))  // Green
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00)); // Orange
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Clean up resources
            _refreshTimer?.Stop();
            _refreshTimer = null;
            
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
            
            base.OnClosing(e);
        }
    }
}

