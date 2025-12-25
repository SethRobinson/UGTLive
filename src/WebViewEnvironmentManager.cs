using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace UGTLive
{
    public static class WebViewEnvironmentManager
    {
        private static readonly object _lock = new object();
        private static Task<CoreWebView2Environment?>? _environmentTask;
        private static bool _errorShown = false;

        public static Task<CoreWebView2Environment?> GetEnvironmentAsync()
        {
            lock (_lock)
            {
                _environmentTask ??= CreateEnvironmentAsync();
                return _environmentTask;
            }
        }

        private static async Task<CoreWebView2Environment?> CreateEnvironmentAsync()
        {
            try
            {
                using IDisposable profiler = OverlayProfiler.Measure("WebViewEnvironmentManager.CreateEnvironment");
                var environment = await CoreWebView2Environment.CreateAsync();
                Console.WriteLine("[WebViewEnvironmentManager] CoreWebView2 environment created");
                return environment;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebViewEnvironmentManager] Failed to create CoreWebView2 environment: {ex.Message}");
                
                // Show user-friendly error message for access denied errors
                if (!_errorShown)
                {
                    _errorShown = true;
                    
                    string errorMessage;
                    string errorTitle;
                    
                    // Check for access denied error (0x80070005 / E_ACCESSDENIED)
                    if (ex.Message.Contains("0x80070005") || 
                        ex.Message.Contains("E_ACCESSDENIED") || 
                        ex.Message.Contains("Access is denied"))
                    {
                        errorTitle = "WebView2 Access Denied";
                        errorMessage = "Failed to initialize WebView2: Access is denied.\n\n" +
                            "This can happen when:\n" +
                            "• Another instance of the app is already running\n" +
                            "• The WebView2 cache folder is locked by another process\n" +
                            "• Running from a debugger with different permissions\n\n" +
                            "Try these solutions:\n" +
                            "1. Close any other instances of this application\n" +
                            "2. Delete the WebView2 cache folder (next to the .exe)\n" +
                            "3. Run as Administrator\n" +
                            "4. If debugging, try running the .exe directly instead";
                    }
                    else
                    {
                        errorTitle = "WebView2 Initialization Failed";
                        errorMessage = $"Failed to initialize WebView2 component.\n\n" +
                            $"Error: {ex.Message}\n\n" +
                            "The text overlay features will not work.\n\n" +
                            "Make sure WebView2 Runtime is installed:\n" +
                            "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
                    }
                    
                    // Show on UI thread
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show(
                            errorMessage,
                            errorTitle,
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    });
                }
                
                return null;
            }
        }
    }
}
