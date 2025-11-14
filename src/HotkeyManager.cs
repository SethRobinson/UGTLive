using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace UGTLive
{
    // Manages all hotkeys for the application
    public class HotkeyManager
    {
        private static HotkeyManager? _instance;
        private const string HOTKEYS_FILE = "hotkeys.txt";
        
        private List<HotkeyEntry> _hotkeys = new List<HotkeyEntry>();
        private bool _globalHotkeysEnabled = true;
        private bool _isEnabled = true;
        
        public static HotkeyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HotkeyManager();
                }
                return _instance;
            }
        }
        
        // Events for each action
        public event EventHandler? StartStopRequested;
        public event EventHandler? MonitorToggleRequested;
        public event EventHandler? ChatBoxToggleRequested;
        public event EventHandler? SettingsToggleRequested;
        public event EventHandler? LogToggleRequested;
        public event EventHandler? MainWindowVisibilityToggleRequested;
        public event EventHandler? ClearOverlaysRequested;
        public event EventHandler? PassthroughToggleRequested;
        public event EventHandler? OverlayModeToggleRequested;
        
        private HotkeyManager()
        {
            LoadHotkeys();
            
            // Subscribe to gamepad events
            GamepadManager.Instance.ButtonsPressed += GamepadManager_ButtonsPressed;
        }
        
        // Get/set global hotkeys enabled
        public bool GetGlobalHotkeysEnabled()
        {
            return _globalHotkeysEnabled;
        }
        
        public void SetGlobalHotkeysEnabled(bool enabled)
        {
            _globalHotkeysEnabled = enabled;
            Console.WriteLine($"Global hotkeys {(enabled ? "enabled" : "disabled")}");
        }
        
        // Get/set hotkey system enabled
        public bool IsEnabled()
        {
            return _isEnabled;
        }
        
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            Console.WriteLine($"Hotkey system {(enabled ? "enabled" : "disabled")}");
        }
        
        // Get all hotkeys
        public List<HotkeyEntry> GetHotkeys()
        {
            return new List<HotkeyEntry>(_hotkeys);
        }
        
        // Add or update a hotkey
        public void SetHotkey(HotkeyEntry entry)
        {
            var existing = _hotkeys.FirstOrDefault(h => h.ActionId == entry.ActionId);
            if (existing != null)
            {
                _hotkeys.Remove(existing);
            }
            
            _hotkeys.Add(entry);
            SaveHotkeys();
        }
        
        // Remove a hotkey
        public void RemoveHotkey(string actionId)
        {
            _hotkeys.RemoveAll(h => h.ActionId == actionId);
            SaveHotkeys();
        }
        
        // Get hotkey for an action
        public HotkeyEntry? GetHotkey(string actionId)
        {
            return _hotkeys.FirstOrDefault(h => h.ActionId == actionId);
        }
        
        // Handle keyboard input
        public bool HandleKeyDown(Key key, ModifierKeys modifiers)
        {
            if (!_isEnabled)
                return false;
                
            // Check if we should process the hotkey based on global setting
            // If global is disabled, only process if main window is active
            if (!_globalHotkeysEnabled)
            {
                // Get the main window
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow == null || !mainWindow.IsActive)
                {
                    return false;
                }
            }
                
            // Find matching hotkey
            var hotkey = _hotkeys.FirstOrDefault(h => 
                h.KeyboardKey == key && h.MatchesKeyboardModifiers(modifiers));
                
            if (hotkey != null)
            {
                TriggerAction(hotkey.ActionId);
                return true;
            }
            
            return false;
        }
        
        // Handle gamepad input
        private void GamepadManager_ButtonsPressed(object? sender, List<string> pressedButtons)
        {
            if (!_isEnabled)
                return;
                
            // Check if we should process the hotkey based on global setting
            // If global is disabled, only process if main window is active
            if (!_globalHotkeysEnabled)
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow == null || !mainWindow.IsActive)
                {
                    return;
                }
            }
                
            // Find matching hotkey
            var hotkey = _hotkeys.FirstOrDefault(h => h.MatchesGamepadButtons(pressedButtons));
            
            if (hotkey != null)
            {
                // Invoke on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    TriggerAction(hotkey.ActionId);
                });
            }
        }
        
        // Trigger an action by ID
        private void TriggerAction(string actionId)
        {
            Console.WriteLine($"Hotkey triggered: {actionId}");
            
            switch (actionId)
            {
                case "start_stop":
                    StartStopRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_monitor":
                    MonitorToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_chatbox":
                    ChatBoxToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_settings":
                    SettingsToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_log":
                    LogToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_main_window":
                    MainWindowVisibilityToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "clear_overlays":
                    ClearOverlaysRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_passthrough":
                    PassthroughToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "toggle_overlay_mode":
                    OverlayModeToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        
        // Load hotkeys from file
        private void LoadHotkeys()
        {
            _hotkeys.Clear();
            
            if (File.Exists(HOTKEYS_FILE))
            {
                try
                {
                    string[] lines = File.ReadAllLines(HOTKEYS_FILE);
                    
                    // First line is global hotkeys enabled flag
                    if (lines.Length > 0 && bool.TryParse(lines[0], out bool globalEnabled))
                    {
                        _globalHotkeysEnabled = globalEnabled;
                    }
                    
                    // Rest of lines are hotkey entries
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i]))
                            continue;
                            
                        var entry = HotkeyEntry.Deserialize(lines[i]);
                        if (entry != null)
                        {
                            _hotkeys.Add(entry);
                        }
                    }
                    
                    Console.WriteLine($"Loaded {_hotkeys.Count} hotkeys from {HOTKEYS_FILE}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading hotkeys: {ex.Message}");
                    CreateDefaultHotkeys();
                }
            }
            else
            {
                CreateDefaultHotkeys();
            }
        }
        
        // Save hotkeys to file
        public void SaveHotkeys()
        {
            try
            {
                List<string> lines = new List<string>();
                
                // First line is global hotkeys enabled flag
                lines.Add(_globalHotkeysEnabled.ToString());
                
                // Rest of lines are hotkey entries
                foreach (var hotkey in _hotkeys)
                {
                    lines.Add(hotkey.Serialize());
                }
                
                File.WriteAllLines(HOTKEYS_FILE, lines);
                Console.WriteLine($"Saved {_hotkeys.Count} hotkeys to {HOTKEYS_FILE}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving hotkeys: {ex.Message}");
            }
        }
        
        // Create default hotkeys
        private void CreateDefaultHotkeys()
        {
            _hotkeys.Clear();
            _globalHotkeysEnabled = true;
            
            // Start/Stop - Shift+S
            var startStop = new HotkeyEntry("start_stop", "Start/Stop OCR");
            startStop.KeyboardKey = Key.S;
            startStop.UseShift = true;
            _hotkeys.Add(startStop);
            
            // Toggle Monitor - Shift+M
            var toggleMonitor = new HotkeyEntry("toggle_monitor", "Toggle Monitor Window");
            toggleMonitor.KeyboardKey = Key.M;
            toggleMonitor.UseShift = true;
            _hotkeys.Add(toggleMonitor);
            
            // Toggle ChatBox - Shift+C
            var toggleChatBox = new HotkeyEntry("toggle_chatbox", "Toggle ChatBox");
            toggleChatBox.KeyboardKey = Key.C;
            toggleChatBox.UseShift = true;
            _hotkeys.Add(toggleChatBox);
            
            // Toggle Settings - Shift+E (changed from P since P is now Passthrough)
            var toggleSettings = new HotkeyEntry("toggle_settings", "Toggle Settings");
            toggleSettings.KeyboardKey = Key.E;
            toggleSettings.UseShift = true;
            _hotkeys.Add(toggleSettings);
            
            // Toggle Log - Shift+L
            var toggleLog = new HotkeyEntry("toggle_log", "Toggle Log");
            toggleLog.KeyboardKey = Key.L;
            toggleLog.UseShift = true;
            _hotkeys.Add(toggleLog);
            
            // Toggle Main Window - Shift+H
            var toggleMainWindow = new HotkeyEntry("toggle_main_window", "Toggle Main Window");
            toggleMainWindow.KeyboardKey = Key.H;
            toggleMainWindow.UseShift = true;
            _hotkeys.Add(toggleMainWindow);
            
            // Clear Overlays - Shift+X
            var clearOverlays = new HotkeyEntry("clear_overlays", "Clear Overlays");
            clearOverlays.KeyboardKey = Key.X;
            clearOverlays.UseShift = true;
            _hotkeys.Add(clearOverlays);
            
            // Toggle Passthrough - Shift+P
            var togglePassthrough = new HotkeyEntry("toggle_passthrough", "Toggle Passthrough");
            togglePassthrough.KeyboardKey = Key.P;
            togglePassthrough.UseShift = true;
            _hotkeys.Add(togglePassthrough);
            
            // Toggle Overlay Mode - Shift+O
            var toggleOverlayMode = new HotkeyEntry("toggle_overlay_mode", "Toggle Overlay Mode");
            toggleOverlayMode.KeyboardKey = Key.O;
            toggleOverlayMode.UseShift = true;
            _hotkeys.Add(toggleOverlayMode);
            
            SaveHotkeys();
            Console.WriteLine("Created default hotkeys");
        }
    }
}

