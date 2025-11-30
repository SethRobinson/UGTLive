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
        
        private Dictionary<string, List<HotkeyEntry>> _actionBindings = new Dictionary<string, List<HotkeyEntry>>();
        private bool _globalHotkeysEnabled = true;
        private bool _isEnabled = true;
        
        // Debouncing for actions to prevent double-triggers
        private Dictionary<string, DateTime> _lastActionTime = new Dictionary<string, DateTime>();
        private const int DEBOUNCE_MS = 50; // 50ms debounce window (prevents double-triggers while staying responsive)
        
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
        public event EventHandler? ListenToggleRequested;
        public event EventHandler? ViewInBrowserRequested;
        public event EventHandler? PlayAllAudioRequested;
        public event EventHandler? SnapshotRequested;
        
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
            if (ConfigManager.Instance.GetLogExtraDebugStuff())
            {
                Console.WriteLine($"Hotkey system {(enabled ? "enabled" : "disabled")}");
            }
        }
        
        // Get all action IDs
        public List<string> GetActionIds()
        {
            return new List<string>(_actionBindings.Keys);
        }
        
        // Get all bindings for a specific action
        public List<HotkeyEntry> GetBindings(string actionId)
        {
            if (_actionBindings.TryGetValue(actionId, out var bindings))
            {
                return new List<HotkeyEntry>(bindings);
            }
            return new List<HotkeyEntry>();
        }
        
        // Get all actions with their bindings (for UI display)
        public Dictionary<string, List<HotkeyEntry>> GetAllBindings()
        {
            var result = new Dictionary<string, List<HotkeyEntry>>();
            foreach (var kvp in _actionBindings)
            {
                result[kvp.Key] = new List<HotkeyEntry>(kvp.Value);
            }
            return result;
        }
        
        // Add a new binding to an action (auto-saves)
        public void AddBinding(HotkeyEntry entry)
        {
            if (!_actionBindings.ContainsKey(entry.ActionId))
            {
                _actionBindings[entry.ActionId] = new List<HotkeyEntry>();
            }
            
            _actionBindings[entry.ActionId].Add(entry);
            SaveHotkeys();
        }
        
        // Remove a specific binding from an action (auto-saves)
        public void RemoveBinding(string actionId, HotkeyEntry entry)
        {
            if (_actionBindings.TryGetValue(actionId, out var bindings))
            {
                bindings.Remove(entry);
                if (bindings.Count == 0)
                {
                    _actionBindings.Remove(actionId);
                }
                SaveHotkeys();
            }
        }
        
        // Remove all bindings for an action (auto-saves)
        public void RemoveAllBindings(string actionId)
        {
            if (_actionBindings.ContainsKey(actionId))
            {
                _actionBindings.Remove(actionId);
                SaveHotkeys();
            }
        }
        
        // Legacy method for backward compatibility - adds/replaces first binding
        public void SetHotkey(HotkeyEntry entry)
        {
            if (!_actionBindings.ContainsKey(entry.ActionId))
            {
                _actionBindings[entry.ActionId] = new List<HotkeyEntry>();
            }
            
            // Replace first binding or add new one
            if (_actionBindings[entry.ActionId].Count > 0)
            {
                _actionBindings[entry.ActionId][0] = entry;
            }
            else
            {
                _actionBindings[entry.ActionId].Add(entry);
            }
            
            SaveHotkeys();
        }
        
        // Handle keyboard input
        public bool HandleKeyDown(Key key, ModifierKeys modifiers)
        {
            if (!_isEnabled)
                return false;
                
            // Check all bindings for all actions
            foreach (var kvp in _actionBindings)
            {
                foreach (var binding in kvp.Value)
                {
                    if (binding.KeyboardKey == key && binding.MatchesKeyboardModifiers(modifiers))
                    {
                        TriggerAction(kvp.Key);
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        // Handle gamepad input
        private void GamepadManager_ButtonsPressed(object? sender, List<string> pressedButtons)
        {
            if (!_isEnabled)
                return;
                
            // When global hotkeys are disabled, check if app has focus
            // (Gamepad events are always "global" so we need this check)
            if (!_globalHotkeysEnabled && !KeyboardShortcuts.IsOurApplicationActive())
            {
                return;
            }
                
            // Check all bindings for all actions
            foreach (var kvp in _actionBindings)
            {
                foreach (var binding in kvp.Value)
                {
                    if (binding.MatchesGamepadButtons(pressedButtons))
                    {
                        // Invoke on UI thread
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            TriggerAction(kvp.Key);
                        });
                        return;
                    }
                }
            }
        }
        
        // Trigger an action by ID
        private void TriggerAction(string actionId)
        {
            // Check debounce - prevent triggering the same action too quickly
            if (_lastActionTime.TryGetValue(actionId, out DateTime lastTime))
            {
                double msSinceLastTrigger = (DateTime.Now - lastTime).TotalMilliseconds;
                if (msSinceLastTrigger < DEBOUNCE_MS)
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Hotkey {actionId} debounced ({msSinceLastTrigger:F0}ms since last trigger)");
                    }
                    return;
                }
            }
            
            _lastActionTime[actionId] = DateTime.Now;
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
                case "toggle_listen":
                    ListenToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "view_in_browser":
                    ViewInBrowserRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "play_all_audio":
                    PlayAllAudioRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "snapshot":
                    SnapshotRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        
        // Reset to default hotkeys
        public void ResetToDefaults()
        {
            CreateDefaultHotkeys();
            Console.WriteLine("Hotkeys reset to defaults");
        }
        
        // Load hotkeys from file
        private void LoadHotkeys()
        {
            _actionBindings.Clear();
            
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
                    int bindingCount = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i]))
                            continue;
                            
                        var entry = HotkeyEntry.Deserialize(lines[i]);
                        if (entry != null)
                        {
                            if (!_actionBindings.ContainsKey(entry.ActionId))
                            {
                                _actionBindings[entry.ActionId] = new List<HotkeyEntry>();
                            }
                            _actionBindings[entry.ActionId].Add(entry);
                            bindingCount++;
                        }
                    }
                    
                    Console.WriteLine($"Loaded {bindingCount} hotkey bindings from {HOTKEYS_FILE}");
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
                int bindingCount = 0;
                foreach (var kvp in _actionBindings)
                {
                    foreach (var binding in kvp.Value)
                    {
                        lines.Add(binding.Serialize());
                        bindingCount++;
                    }
                }
                
                File.WriteAllLines(HOTKEYS_FILE, lines);
                Console.WriteLine($"Saved {bindingCount} hotkey bindings to {HOTKEYS_FILE}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving hotkeys: {ex.Message}");
            }
        }
        
        // Create default hotkeys
        private void CreateDefaultHotkeys()
        {
            _actionBindings.Clear();
            _globalHotkeysEnabled = true;
            
            // Start/Stop - Shift+S
            var startStop = new HotkeyEntry("start_stop", "Start/Stop Live OCR");
            startStop.KeyboardKey = Key.S;
            startStop.UseShift = true;
            _actionBindings["start_stop"] = new List<HotkeyEntry> { startStop };
            
            // Toggle Monitor - Shift+M
            var toggleMonitor = new HotkeyEntry("toggle_monitor", "Toggle Monitor Window");
            toggleMonitor.KeyboardKey = Key.M;
            toggleMonitor.UseShift = true;
            _actionBindings["toggle_monitor"] = new List<HotkeyEntry> { toggleMonitor };
            
            // Toggle ChatBox - Shift+C
            var toggleChatBox = new HotkeyEntry("toggle_chatbox", "Toggle ChatBox");
            toggleChatBox.KeyboardKey = Key.C;
            toggleChatBox.UseShift = true;
            _actionBindings["toggle_chatbox"] = new List<HotkeyEntry> { toggleChatBox };
            
            // Toggle Settings - Shift+E (changed from P since P is now Passthrough)
            var toggleSettings = new HotkeyEntry("toggle_settings", "Toggle Settings");
            toggleSettings.KeyboardKey = Key.E;
            toggleSettings.UseShift = true;
            _actionBindings["toggle_settings"] = new List<HotkeyEntry> { toggleSettings };
            
            // Toggle Log - Shift+L
            var toggleLog = new HotkeyEntry("toggle_log", "Toggle Log");
            toggleLog.KeyboardKey = Key.L;
            toggleLog.UseShift = true;
            _actionBindings["toggle_log"] = new List<HotkeyEntry> { toggleLog };
            
            // Toggle Main Window - Shift+H
            var toggleMainWindow = new HotkeyEntry("toggle_main_window", "Toggle Main Window");
            toggleMainWindow.KeyboardKey = Key.H;
            toggleMainWindow.UseShift = true;
            _actionBindings["toggle_main_window"] = new List<HotkeyEntry> { toggleMainWindow };
            
            // Clear Overlays - Shift+X
            var clearOverlays = new HotkeyEntry("clear_overlays", "Clear Overlays");
            clearOverlays.KeyboardKey = Key.X;
            clearOverlays.UseShift = true;
            _actionBindings["clear_overlays"] = new List<HotkeyEntry> { clearOverlays };
            
            // Toggle Passthrough - Shift+P
            var togglePassthrough = new HotkeyEntry("toggle_passthrough", "Toggle Passthrough");
            togglePassthrough.KeyboardKey = Key.P;
            togglePassthrough.UseShift = true;
            _actionBindings["toggle_passthrough"] = new List<HotkeyEntry> { togglePassthrough };
            
            // Toggle Overlay Mode - Tab
            var toggleOverlayMode = new HotkeyEntry("toggle_overlay_mode", "Toggle Overlay Mode");
            toggleOverlayMode.KeyboardKey = Key.Tab;
            toggleOverlayMode.UseShift = false;
            _actionBindings["toggle_overlay_mode"] = new List<HotkeyEntry> { toggleOverlayMode };
            
            // Toggle Listen - No default key
            var toggleListen = new HotkeyEntry("toggle_listen", "Toggle Listen");
            _actionBindings["toggle_listen"] = new List<HotkeyEntry> { toggleListen };
            
            // View in Browser - Shift+B
            var viewInBrowser = new HotkeyEntry("view_in_browser", "View in Browser");
            viewInBrowser.KeyboardKey = Key.B;
            viewInBrowser.UseShift = true;
            _actionBindings["view_in_browser"] = new List<HotkeyEntry> { viewInBrowser };
            
            // Snapshot - Shift+Z
            var snapshot = new HotkeyEntry("snapshot", "Snapshot OCR");
            snapshot.KeyboardKey = Key.Z;
            snapshot.UseShift = true;
            _actionBindings["snapshot"] = new List<HotkeyEntry> { snapshot };
            
            SaveHotkeys();
            Console.WriteLine("Created default hotkeys");
        }
    }
}

