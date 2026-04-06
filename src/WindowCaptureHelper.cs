using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;

namespace UGTLive
{
    internal static class WindowCaptureHelper
    {
        public static void SetExcludeFromCapture(Window window, string windowName)
        {
            try
            {
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();

                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;

                if (hwnd != IntPtr.Zero)
                {
                    uint affinity = visibleInScreenshots ? NativeMethods.WDA_NONE : NativeMethods.WDA_EXCLUDEFROMCAPTURE;
                    bool success = NativeMethods.SetWindowDisplayAffinity(hwnd, affinity);

                    if (success)
                    {
                        Console.WriteLine($"{windowName} window {(visibleInScreenshots ? "included in" : "excluded from")} screen capture successfully (HWND: {hwnd})");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to set {windowName} window capture mode. Last error: {Marshal.GetLastWin32Error()}");
                    }
                }
                else
                {
                    Console.WriteLine($"{windowName} window HWND is null, cannot set capture mode");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting {windowName} window capture mode: {ex.Message}");
            }
        }

        public static void ExcludeFromCaptureByHandle(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            }
        }

        public static void ExcludeTooltipFromCapture(bool fullEnumeration = true)
        {
            try
            {
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();
                if (visibleInScreenshots)
                    return;

                var tooltipWindows = System.Windows.Application.Current.Windows.OfType<Window>()
                    .Where(w => w.GetType().Name.Contains("ToolTip") || w.GetType().Name.Contains("Popup"));

                foreach (var window in tooltipWindows)
                {
                    var helper = new WindowInteropHelper(window);
                    IntPtr hwnd = helper.Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    }
                }

                if (fullEnumeration)
                {
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (System.Diagnostics.ProcessThread thread in currentProcess.Threads)
                    {
                        try
                        {
                            NativeMethods.EnumThreadWindows((uint)thread.Id, (hWnd, lParam) =>
                            {
                                var className = new StringBuilder(256);
                                NativeMethods.GetClassName(hWnd, className, className.Capacity);
                                string cls = className.ToString();

                                if (cls.Contains("Popup") || cls.Contains("ToolTip") || cls.Contains("HwndWrapper"))
                                {
                                    NativeMethods.SetWindowDisplayAffinity(hWnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                                }

                                return true;
                            }, IntPtr.Zero);
                        }
                        catch
                        {
                            // Thread may have terminated
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error excluding tooltip from capture: {ex.Message}");
            }
        }

        public static void SetWebView2ExcludeFromCapture(WebView2? webView, string windowName)
        {
            try
            {
                bool visibleInScreenshots = ConfigManager.Instance.GetWindowsVisibleInScreenshots();

                if (webView?.CoreWebView2 != null)
                {
                    var presentationSource = PresentationSource.FromVisual(webView);
                    if (presentationSource is HwndSource hwndSource)
                    {
                        IntPtr webViewHwnd = hwndSource.Handle;

                        if (webViewHwnd != IntPtr.Zero)
                        {
                            uint affinity = visibleInScreenshots ? NativeMethods.WDA_NONE : NativeMethods.WDA_EXCLUDEFROMCAPTURE;
                            bool success = NativeMethods.SetWindowDisplayAffinity(webViewHwnd, affinity);

                            if (!success)
                            {
                                Console.WriteLine($"Failed to set {windowName} WebView2 capture mode. Last error: {Marshal.GetLastWin32Error()}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"{windowName} WebView2 HWND is null");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{windowName} WebView2: Could not get HwndSource, WebView2 may share parent window HWND");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting {windowName} WebView2 capture mode: {ex.Message}");
            }
        }
    }
}
