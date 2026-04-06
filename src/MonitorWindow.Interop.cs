using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using Color = System.Windows.Media.Color;
using System.Diagnostics;
using System.Text;


namespace UGTLive
{
    public partial class MonitorWindow
    {
        // Windows message constants
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int VK_CONTROL = 0x11;
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL_LL = 0x020A;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
        
        
        // Win32 API for GetSystemMetrics - used for scrollbar and titlebar dimensions
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        
        private const int SM_CXVSCROLL = 2;  // Width of vertical scrollbar
        private const int SM_CYHSCROLL = 3;  // Height of horizontal scrollbar
        private const int SM_CYCAPTION = 4;  // Height of window caption/titlebar

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelMouseProc _mouseHookProc;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _monitorWindowHandle = IntPtr.Zero;

        // Low-level mouse hook to intercept mouse events before they reach WebView2
        private IntPtr LowLevelMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                
                // Handle mouse move to detect scrollbar/titlebar regions
                if (msg == WM_MOUSEMOVE)
                {
                    try
                    {
                        // Get the mouse position from the hook structure
                        MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        
                        // Check if the target window is our MonitorWindow or a child of it
                        IntPtr targetWindow = WindowFromPoint(hookStruct.pt);
                        bool isOurWindow = false;
                        
                        if (targetWindow == _monitorWindowHandle)
                        {
                            isOurWindow = true;
                        }
                        else if (targetWindow != IntPtr.Zero)
                        {
                            IntPtr parent = GetParent(targetWindow);
                            while (parent != IntPtr.Zero)
                            {
                                if (parent == _monitorWindowHandle)
                                {
                                    isOurWindow = true;
                                    break;
                                }
                                parent = GetParent(parent);
                            }
                        }
                        
                        if (isOurWindow)
                        {
                            System.Windows.Point screenPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);
                            
                            // Use Dispatcher to check UI elements on UI thread
                            Dispatcher.BeginInvoke(() =>
                            {
                                checkMousePositionAndUpdateHitTesting(screenPoint);
                            }, DispatcherPriority.Normal);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in mouse move hook: {ex.Message}");
                    }
                }
                else if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
                {
                    try
                    {
                        // Check if Ctrl key is pressed
                        short ctrlState = GetKeyState(VK_CONTROL);
                        bool ctrlPressed = (ctrlState & 0x8000) != 0;

                        if (ctrlPressed)
                        {
                            // Get the mouse position from the hook structure
                            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                            System.Windows.Point screenPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);

                            // Check if the target window is our MonitorWindow or a child of it
                            IntPtr targetWindow = WindowFromPoint(hookStruct.pt);
                            bool isOurWindow = false;

                            // Check if it's our window or a child of our window
                            if (targetWindow == _monitorWindowHandle)
                            {
                                isOurWindow = true;
                            }
                            else if (targetWindow != IntPtr.Zero)
                            {
                                // Check if it's a child window of our window
                                IntPtr parent = GetParent(targetWindow);
                                while (parent != IntPtr.Zero)
                                {
                                    if (parent == _monitorWindowHandle)
                                    {
                                        isOurWindow = true;
                                        break;
                                    }
                                    parent = GetParent(parent);
                                }
                            }

                            if (isOurWindow)
                            {
                                // Capture delta before async call (lParam may become invalid)
                                int delta = unchecked((short)(hookStruct.mouseData >> 16));

                                // Check if mouse is over the ScrollViewer area
                                // Use Dispatcher to ensure we're on UI thread for UI element access
                                Dispatcher.BeginInvoke(() =>
                                {
                                    try
                                    {
                                        System.Windows.Point scrollViewerPoint = imageScrollViewer.PointFromScreen(screenPoint);

                                        if (scrollViewerPoint.X >= 0 && scrollViewerPoint.Y >= 0 &&
                                            scrollViewerPoint.X <= imageScrollViewer.ActualWidth &&
                                            scrollViewerPoint.Y <= imageScrollViewer.ActualHeight)
                                        {
                                            // Perform zoom towards mouse cursor
                                            if (delta > 0)
                                            {
                                                currentZoom += zoomIncrement;
                                                ApplyZoom(screenPoint);
                                            }
                                            else
                                            {
                                                currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                                                ApplyZoom(screenPoint);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error in low-level hook zoom: {ex.Message}");
                                    }
                                }, DispatcherPriority.Normal);

                                // Block the message from reaching WebView2
                                return new IntPtr(1);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in LowLevelMouseHookProc: {ex.Message}");
                    }
                }
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }
        
        // Windows message hook to intercept WM_MOUSEWHEEL before WebView2 handles it
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
            {
                try
                {
                    // Check if Ctrl key is pressed using GetKeyState
                    short ctrlState = GetKeyState(VK_CONTROL);
                    bool ctrlPressed = (ctrlState & 0x8000) != 0;
                    
                    // Also check WPF keyboard state as backup
                    if (ctrlPressed || Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        // Get mouse position (screen coordinates) - extract safely
                        int lParamValue = lParam.ToInt32();
                        int x = unchecked((short)(lParamValue & 0xFFFF));
                        int y = unchecked((short)((lParamValue >> 16) & 0xFFFF));
                        
                        // Convert screen coordinates to window coordinates
                        System.Windows.Point screenPoint = new System.Windows.Point(x, y);
                        
                        // Check if mouse is over the ScrollViewer area (not the control panel)
                        // Message hooks run on the UI thread, so we can access UI elements directly
                        System.Windows.Point scrollViewerPoint = imageScrollViewer.PointFromScreen(screenPoint);
                        
                        if (scrollViewerPoint.X >= 0 && scrollViewerPoint.Y >= 0 && 
                            scrollViewerPoint.X <= imageScrollViewer.ActualWidth && 
                            scrollViewerPoint.Y <= imageScrollViewer.ActualHeight)
                        {
                            // Get wheel delta (positive = scroll up, negative = scroll down)
                            // Extract signed 16-bit value from high word of wParam safely
                            int wParamValue = wParam.ToInt32();
                            // Shift right 16 bits and mask to get high word, then cast to short
                            int highWord = (wParamValue >> 16) & 0xFFFF;
                            int delta = unchecked((short)highWord);
                            
                            // Always prevent the message from reaching WebView2 (to prevent font scaling)
                            // and always perform zoom, regardless of what's under the mouse
                            handled = true;
                            
                            // Zoom in or out based on wheel direction, towards mouse cursor
                            if (delta > 0)
                            {
                                // Zoom in
                                currentZoom += zoomIncrement;
                                ApplyZoom(screenPoint);
                            }
                            else
                            {
                                // Zoom out
                                currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                                ApplyZoom(screenPoint);
                            }
                            
                            return IntPtr.Zero;
                        }
                    }
                    else
                    {
                        // No Ctrl: forward scroll to the ScrollViewer (WebView2 is outside it now)
                        int wParamVal = wParam.ToInt32();
                        int highW = (wParamVal >> 16) & 0xFFFF;
                        int scrollDelta = unchecked((short)highW);
                        
                        if (msg == WM_MOUSEHWHEEL)
                        {
                            imageScrollViewer.ScrollToHorizontalOffset(
                                imageScrollViewer.HorizontalOffset - scrollDelta);
                        }
                        else
                        {
                            imageScrollViewer.ScrollToVerticalOffset(
                                imageScrollViewer.VerticalOffset - scrollDelta);
                        }
                        handled = true;
                        return IntPtr.Zero;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in WndProc: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            
            return IntPtr.Zero;
        }

        // Check if the mouse is over a WebView2 control
        private bool IsMouseOverWebView2(System.Windows.Point screenPoint)
        {
            try
            {
                // Convert screen point to window-relative point
                System.Windows.Point windowPoint = this.PointFromScreen(screenPoint);
                
                // Use HitTest to find what element is at this point
                HitTestResult hitTestResult = VisualTreeHelper.HitTest(this, windowPoint);
                if (hitTestResult != null && hitTestResult.VisualHit != null)
                {
                    // Walk up the visual tree to find if we're over a WebView2
                    DependencyObject current = hitTestResult.VisualHit;
                    while (current != null)
                    {
                        if (current is WebView2)
                        {
                            return true;
                        }
                        // Also check if it's a Border containing a WebView2
                        if (current is Border border && border.Child is WebView2)
                        {
                            return true;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking WebView2 hit test: {ex.Message}");
            }
            
            return false;
        }

        // Handle mouse wheel zoom when Ctrl is held - at Window level to capture before WebView2
        private void Window_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Check if Ctrl key is pressed
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Get the mouse position relative to the window
                System.Windows.Point mousePos = e.GetPosition(this);
                
                // Check if mouse is over the ScrollViewer area (not the control panel)
                // The control panel is in Grid.Row="0", ScrollViewer is in Grid.Row="1"
                System.Windows.Point scrollViewerPos = e.GetPosition(imageScrollViewer);
                
                // Check if the mouse is actually within the ScrollViewer bounds
                if (scrollViewerPos.X >= 0 && scrollViewerPos.Y >= 0 && 
                    scrollViewerPos.X <= imageScrollViewer.ActualWidth && 
                    scrollViewerPos.Y <= imageScrollViewer.ActualHeight)
                {
                    // Prevent default scrolling/zooming behavior (including WebView2 font scaling)
                    e.Handled = true;
                    
                    // Get mouse position in screen coordinates for zoom centering
                    System.Windows.Point screenPoint = this.PointToScreen(e.GetPosition(this));
                    
                    // Zoom in or out based on wheel direction, towards mouse cursor
                    if (e.Delta > 0)
                    {
                        // Zoom in
                        currentZoom += zoomIncrement;
                        ApplyZoom(screenPoint);
                    }
                    else
                    {
                        // Zoom out
                        currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                        ApplyZoom(screenPoint);
                    }
                }
            }
        }
        
        // Handle Ctrl+MouseWheel for zooming
        private void MonitorWindow_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Only handle wheel events when Ctrl is pressed
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Determine zoom direction based on wheel delta
                if (e.Delta > 0)
                {
                    // Scroll up = Zoom in
                    currentZoom += zoomIncrement;
                }
                else if (e.Delta < 0)
                {
                    // Scroll down = Zoom out
                    currentZoom = Math.Max(0.1, currentZoom - zoomIncrement);
                }
                
                // Apply the new zoom level
                ApplyZoom();
                
                // Mark the event as handled to prevent scrolling
                e.Handled = true;
            }
        }
    }
}
