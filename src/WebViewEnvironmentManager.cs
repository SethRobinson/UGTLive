using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace UGTLive
{
    public static class WebViewEnvironmentManager
    {
        private static readonly object _lock = new object();
        private static Task<CoreWebView2Environment?>? _environmentTask;

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
                return null;
            }
        }
    }
}

