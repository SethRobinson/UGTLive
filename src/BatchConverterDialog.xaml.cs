using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace UGTLive
{
    public partial class BatchConverterDialog : Window
    {
        private readonly ObservableCollection<BatchConvertItem> _items = new();
        private CancellationTokenSource? _cts;
        private BatchConverterService? _service;
        private bool _isProcessing;
        private string? _lastOutputFolder;

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
        private static readonly string[] PdfExtensions = { ".pdf" };
        private static readonly string[] AllExtensions = ImageExtensions.Concat(PdfExtensions).ToArray();

        private bool _autoStart;
        private bool _autoQuit;

        public BatchConverterDialog()
        {
            InitializeComponent();
            fileListBox.ItemsSource = _items;
            refreshSettingsDisplay();
            updateFileCount();
        }

        /// <summary>
        /// Add files/folders from command-line args and optionally auto-start + auto-quit.
        /// Call after the dialog is shown and the app is ready.
        /// </summary>
        public void SetupCommandLine(IEnumerable<string> paths, bool autoQuit)
        {
            _autoStart = true;
            _autoQuit = autoQuit;

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => AllExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f);
                    foreach (string file in files)
                        addFile(file);
                }
                else if (File.Exists(path))
                {
                    addFile(path);
                }
            }

            Loaded += async (s, e) =>
            {
                await Task.Delay(200);
                if (_autoStart && _items.Count > 0)
                    StartButton_Click(this, new RoutedEventArgs());
            };
        }

        private void refreshSettingsDisplay()
        {
            string src = ConfigManager.Instance.GetSourceLanguage();
            string tgt = ConfigManager.Instance.GetTargetLanguage();
            string ocr = ConfigManager.Instance.GetOcrMethod();
            string trans = ConfigManager.Instance.GetCurrentTranslationService();

            settingsSourceLang.Text = $"Source: {Logic.GetLanguageName(src)}";
            settingsTargetLang.Text = $"Target: {Logic.GetLanguageName(tgt)}";
            settingsOcrMethod.Text = $"OCR: {ocr}";
            settingsTransService.Text = $"Translation: {trans}";
        }

        private void updateFileCount()
        {
            int files = _items.Count;
            int pages = _items.Sum(i => i.IsPdf ? i.PageCount : 1);
            fileCountText.Text = files == 0 ? "No files" : $"{files} file(s), ~{pages} page(s)";
        }

        private void AddFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images and PDFs|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif;*.pdf|Images|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|PDF Files|*.pdf|All Files|*.*",
                Title = "Select images or PDFs to convert"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (string file in dlg.FileNames)
                    addFile(file);
            }
        }

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing images/PDFs"
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var files = Directory.EnumerateFiles(dlg.SelectedPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => AllExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f);

                foreach (string file in files)
                    addFile(file);
            }
        }

        private async void addFile(string filePath)
        {
            if (_items.Any(i => i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                return;

            var item = new BatchConvertItem { FilePath = filePath };

            if (item.IsPdf)
            {
                int pages = await BatchConverterService.GetPdfPageCountAsync(filePath);
                item.PageCount = pages > 0 ? pages : 1;
            }

            _items.Add(item);
            updateFileCount();
        }

        private void FileListBox_DragOver(object sender, DragEventArgs e)
        {
            if (_isProcessing)
            {
                e.Effects = DragDropEffects.None;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void FileListBox_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_isProcessing) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[]? paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null) return;

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => AllExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f);
                    foreach (string file in files)
                        addFile(file);
                }
                else if (File.Exists(path) && AllExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                {
                    addFile(path);
                }
            }
        }

        private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = fileListBox.SelectedItems.Cast<BatchConvertItem>().ToList();
            foreach (var item in selected)
                _items.Remove(item);
            updateFileCount();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            updateFileCount();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                MessageBox.Show("Add some files first.", "No Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            setProcessingState(true);
            _cts = new CancellationTokenSource();
            _service = new BatchConverterService();
            _service.ProgressChanged += onProgress;
            _service.LogMessage += onLog;

            foreach (var item in _items)
                item.Status = "Pending";
            fileListBox.Items.Refresh();

            try
            {
                var (succeeded, failed) = await _service.ConvertFilesAsync(_items.ToList(), _cts.Token);
                statusText.Text = $"Complete! {succeeded} succeeded, {failed} failed.";
                progressBar.Value = 100;

                if (succeeded > 0 && _items.Count > 0)
                {
                    _lastOutputFolder = Path.GetDirectoryName(_items[0].FilePath);
                    openFolderButton.Visibility = Visibility.Visible;
                }

                if (_autoQuit)
                {
                    Console.WriteLine($"[BatchConverter] Auto-quit: {succeeded} succeeded, {failed} failed.");
                    await Task.Delay(500);
                    System.Windows.Application.Current.Shutdown(failed > 0 ? 1 : 0);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "Cancelled by user.";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _service.ProgressChanged -= onProgress;
                _service.LogMessage -= onLog;
                _service.Cleanup();
                _service = null;
                _cts?.Dispose();
                _cts = null;
                setProcessingState(false);
                fileListBox.Items.Refresh();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                statusText.Text = "Cancelling...";
            }
            else
            {
                Close();
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastOutputFolder) && Directory.Exists(_lastOutputFolder))
            {
                System.Diagnostics.Process.Start("explorer.exe", _lastOutputFolder);
            }
        }

        private void setProcessingState(bool processing)
        {
            _isProcessing = processing;

            // Hide buttons instead of disabling them (disabled WPF buttons turn white-on-white)
            var fileButtons = new[] { addFilesButton, addFolderButton, removeSelectedButton, clearAllButton, startButton };
            foreach (var btn in fileButtons)
                btn.Visibility = processing ? Visibility.Collapsed : Visibility.Visible;

            cancelButton.Content = processing ? "Cancel" : "Close";

            if (processing)
            {
                openFolderButton.Visibility = Visibility.Collapsed;
                logTextBox.Text = "";
            }
        }

        private void onProgress(object? sender, BatchProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                statusText.Text = e.StatusText;
                progressBar.Value = e.OverallProgress;
                if (e.PreviewImage != null)
                    previewImage.Source = e.PreviewImage;

                if (e.CurrentFileIndex < _items.Count)
                {
                    _items[e.CurrentFileIndex].Status = e.TotalPages > 1
                        ? $"Pg {e.CurrentPage + 1}/{e.TotalPages}"
                        : "Working...";
                    fileListBox.Items.Refresh();
                }
            });
        }

        private void onLog(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                logTextBox.AppendText(message + "\n");
                logTextBox.ScrollToEnd();
            });
        }
    }
}
