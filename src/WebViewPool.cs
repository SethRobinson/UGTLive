using System.Collections.Generic;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace UGTLive
{
    public static class WebViewPool
    {
        private static readonly Queue<WebView2> _available = new Queue<WebView2>();
        private static readonly object _lock = new object();

        public static bool TryRent(out WebView2? webView)
        {
            lock (_lock)
            {
                if (_available.Count > 0)
                {
                    webView = _available.Dequeue();
                    return true;
                }
            }

            webView = null;
            return false;
        }

        public static void Return(WebView2 webView)
        {
            if (webView == null)
            {
                return;
            }

            // Detach from any visual parent if it somehow still has one
            if (webView.Parent is Border parentBorder && parentBorder.Child == webView)
            {
                parentBorder.Child = null;
            }

            lock (_lock)
            {
                _available.Enqueue(webView);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                while (_available.Count > 0)
                {
                    var webView = _available.Dequeue();
                    try
                    {
                        webView.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}

