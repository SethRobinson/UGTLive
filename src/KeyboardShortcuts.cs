using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Forms; // For Control.ModifierKeys

namespace UGTLive
{
    public static class KeyboardShortcuts
    {
        #region Events
        
        public static event EventHandler? StartStopRequested;
        public static event EventHandler? MonitorToggleRequested;
        public static event EventHandler? ChatBoxToggleRequested;
        public static event EventHandler? SettingsToggleRequested;
        public static event EventHandler? LogToggleRequested;
        public static event EventHandler? MainWindowVisibilityToggleRequested;
        
        #endregion
        
        #region Global Keyboard Hook
        
        // For global keyboard hook
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hInstance, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hInstance);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, int wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        // For window focus checking
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        
        // Reference to our console window handle (set by MainWindow)
        private static IntPtr _consoleWindowHandle = IntPtr.Zero;
        
        // NEW: Allow temporarily disabling all shortcut handling (for example while the Settings window is active)
        private static bool _shortcutsEnabled = true;
        // When true, ignore the next KeyDown raised via WPF (because we already handled it in low-level hook)
        private static bool _skipNextKeyDown = false;
        public static void SetShortcutsEnabled(bool enabled)
        {
            _shortcutsEnabled = enabled;
            Console.WriteLine($"Keyboard shortcuts {(_shortcutsEnabled ? "enabled" : "disabled")}");
        }
        
        // Set up global keyboard hook
        public static void InitializeGlobalHook()
        {
            if (_hookID == IntPtr.Zero) // Only set if not already set
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule!)
                {
                    if (curModule != null)
                    {
                        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                        Console.WriteLine("Global keyboard hook initialized");
                    }
                }
            }
        }
        
        // Remove the hook
        public static void CleanupGlobalHook()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                Console.WriteLine("Global keyboard hook removed");
            }
        }
        
        // Set the console window handle for proper hook handling
        public static void SetConsoleWindowHandle(IntPtr consoleHandle)
        {
            _consoleWindowHandle = consoleHandle;
        }
        
        // Keyboard hook callback
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // If shortcuts are disabled, simply pass the message to next hook
                if (!_shortcutsEnabled)
                {
                    return CallNextHookEx(_hookID, nCode, (int)wParam, lParam);
                }

                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    // Check if foreground window belongs to our application
                    if (IsOurApplicationActive())
                    {
                        int vkCode = Marshal.ReadInt32(lParam);
                        
                        // Determine which modifiers are currently pressed
                        Keys modifiers = Control.ModifierKeys;
                        bool isShiftOnly = modifiers == Keys.Shift;

                        // Also ignore if either Windows key is held down (not reported in Control.ModifierKeys)
                        bool isWinPressed = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

                        if (isShiftOnly && !isWinPressed)
                        {
                            // Convert the virtual key code to a Key
                            Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                            // Check if it's one of our shortcuts (S, M, C, P, L, H with Shift)
                            if (IsShortcutKey(key, ModifierKeys.Shift))
                            {
                                // Only process if one of our app windows has focus (or console window)
                                // Handle the shortcut
                                HandleRawKeyDown(key, ModifierKeys.Shift);
                                // Do NOT block the key from other global hooks or applications
                            }
                        }
                    }
                }
                
                // Call the next hook in the chain
                return CallNextHookEx(_hookID, nCode, (int)wParam, lParam);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in keyboard hook: {ex.Message}");
                return CallNextHookEx(_hookID, nCode, (int)wParam, lParam);
            }
        }
        
        // Check if the foreground window belongs to our application process
        private static bool IsOurApplicationActive()
        {
            try
            {
                // Get the foreground window handle
                IntPtr foregroundWindow = GetForegroundWindow();
                
                // Get the process ID for the foreground window
                uint foregroundProcessId;
                GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
                
                // Check if it's our process ID
                bool isOurProcessActive = (foregroundProcessId == (uint)Process.GetCurrentProcess().Id);
                
                // Also check if it's our console window
                if (!isOurProcessActive && _consoleWindowHandle != IntPtr.Zero)
                {
                    isOurProcessActive = (foregroundWindow == _consoleWindowHandle);
                }
                
                return isOurProcessActive;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking application focus: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Standard Shortcut Handling
        
        // Handle shortcut keys - for regular window event handling
        public static bool HandleKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // If a low-level hook already handled this physical key press, skip it once
                if (_skipNextKeyDown)
                {
                    _skipNextKeyDown = false; // reset
                    e.Handled = true; // consume the event so the key isn't typed
                    return true;
                }
                if (!_shortcutsEnabled)
                {
                    return false;
                }
                // Shift+S: Start/Stop OCR
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+M: Toggle Monitor Window
                else if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+C: Toggle ChatBox
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+P: Toggle Settings
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+L: Toggle Log
                else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
                // Shift+H: Toggle Main Window Visibility
                else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                    e.Handled = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling keyboard shortcut: {ex.Message}");
            }
            
            return false;
        }
        
        // Method to check if a key combination matches our shortcuts
        public static bool IsShortcutKey(Key key, ModifierKeys modifiers)
        {
            if (modifiers != ModifierKeys.Shift)
                return false;
                
            // Check if it's one of our shortcut keys
            return key == Key.S || key == Key.M || key == Key.C || 
                   key == Key.P || key == Key.L || key == Key.H;
        }
        
        // Handle raw key input for global hook
        public static bool HandleRawKeyDown(Key key, ModifierKeys modifiers)
        {
            try
            {
                if (!_shortcutsEnabled)
                    return false;
                if (modifiers != ModifierKeys.Shift)
                    return false;
                    
                // Shift+S: Start/Stop OCR
                if (key == Key.S)
                {
                    _skipNextKeyDown = true;
                    StartStopRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+M: Toggle Monitor Window
                else if (key == Key.M)
                {
                    _skipNextKeyDown = true;
                    MonitorToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+C: Toggle ChatBox
                else if (key == Key.C)
                {
                    _skipNextKeyDown = true;
                    ChatBoxToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+P: Toggle Settings
                else if (key == Key.P)
                {
                    _skipNextKeyDown = true;
                    SettingsToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+L: Toggle Log
                else if (key == Key.L)
                {
                    _skipNextKeyDown = true;
                    LogToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
                // Shift+H: Toggle Main Window Visibility
                else if (key == Key.H)
                {
                    _skipNextKeyDown = true;
                    MainWindowVisibilityToggleRequested?.Invoke(null, EventArgs.Empty);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling raw keyboard shortcut: {ex.Message}");
            }
            
            return false;
        }
        
        #endregion
    }
}