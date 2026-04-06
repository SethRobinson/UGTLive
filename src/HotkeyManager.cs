using System;
using System.Collections.Generic;
using System.Globalization;
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
        private bool _isNewHotkeyFile = false;
        
        // Debouncing for actions to prevent double-triggers
        private Dictionary<string, DateTime> _lastActionTime = new Dictionary<string, DateTime>();
        private const int DEBOUNCE_MS = 100; // 100ms debounce window (prevents double-triggers from gamepad while staying responsive)
        
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
        public event EventHandler? OverlayModePreviousRequested;
        public event EventHandler? ListenToggleRequested;
        public event EventHandler? ViewInBrowserRequested;
        public event EventHandler? PlayAllAudioRequested;
        public event EventHandler? SnapshotRequested;
        
        private HotkeyManager()
        {
            LoadHotkeys();
            
            // Offer to reset hotkeys if defaults have improved since last upgrade
            MigrateHotkeysIfNeeded();
            
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
        
        // Handle keyboard input from global hook (only fires IsGlobal bindings)
        public bool HandleKeyDown(Key key, ModifierKeys modifiers)
        {
            if (!_isEnabled)
                return false;
                
            foreach (var kvp in _actionBindings)
            {
                foreach (var binding in kvp.Value)
                {
                    if (binding.IsGlobal && binding.KeyboardKey == key && binding.MatchesKeyboardModifiers(modifiers))
                    {
                        TriggerAction(kvp.Key);
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        // Handle keyboard input from window-level PreviewKeyDown (only fires local/non-global bindings)
        public bool HandleKeyDownLocal(Key key, ModifierKeys modifiers)
        {
            if (!_isEnabled)
                return false;
                
            foreach (var kvp in _actionBindings)
            {
                foreach (var binding in kvp.Value)
                {
                    if (!binding.IsGlobal && binding.KeyboardKey == key && binding.MatchesKeyboardModifiers(modifiers))
                    {
                        TriggerAction(kvp.Key);
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        // Handle keyboard input when master global switch is off (fires ALL bindings regardless of scope)
        public bool HandleKeyDownAll(Key key, ModifierKeys modifiers)
        {
            if (!_isEnabled)
                return false;
                
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
            
            Console.WriteLine($"Gamepad buttons pressed: {string.Join(", ", pressedButtons)}");
                
            // Check all bindings for all actions
            foreach (var kvp in _actionBindings)
            {
                foreach (var binding in kvp.Value)
                {
                    if (binding.MatchesGamepadButtons(pressedButtons))
                    {
                        Console.WriteLine($"Gamepad matched action: {kvp.Key}");
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
                case "prev_overlay_mode":
                    OverlayModePreviousRequested?.Invoke(this, EventArgs.Empty);
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
        
        // One-time migration: offer to reset hotkeys when defaults have changed significantly.
        // LAST_HOTKEY_CHANGE_VERSION: bump this only when the default hotkeys actually change.
        private const double LAST_HOTKEY_CHANGE_VERSION = 1.25;
        
        private void MigrateHotkeysIfNeeded()
        {
            string lastVersion = ConfigManager.Instance.GetValue(ConfigManager.LAST_HOTKEY_UPGRADE_VERSION, "0");
            if (!double.TryParse(lastVersion, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double lastHotkeyVersion))
            {
                lastHotkeyVersion = 0;
            }
            
            if (lastHotkeyVersion >= LAST_HOTKEY_CHANGE_VERSION)
            {
                return;
            }
            
            if (_isNewHotkeyFile || ConfigManager.Instance.IsNewConfig)
            {
                ConfigManager.Instance.SetValue(ConfigManager.LAST_HOTKEY_UPGRADE_VERSION,
                    LAST_HOTKEY_CHANGE_VERSION.ToString(CultureInfo.InvariantCulture));
                ConfigManager.Instance.SaveConfig();
                return;
            }
            
            var result = System.Windows.MessageBox.Show(
                "Default hotkeys have changed quite a bit to prevent accidental triggers " +
                "during normal typing (Shift+letter changed to Ctrl+Shift+letter, and hotkeys " +
                "now support per-binding global/local scope).\n\n" +
                "Would you like to reset your hotkeys to the new defaults?\n\n" +
                "Choose 'Yes' to use the improved defaults (recommended).\n" +
                "Choose 'No' to keep your current hotkeys.",
                "Hotkey Defaults Updated",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                ResetToDefaults();
                Console.WriteLine("User chose to reset hotkeys to new defaults");
            }
            else
            {
                Console.WriteLine("User chose to keep existing hotkeys");
            }
            
            ConfigManager.Instance.SetValue(ConfigManager.LAST_HOTKEY_UPGRADE_VERSION,
                LAST_HOTKEY_CHANGE_VERSION.ToString(CultureInfo.InvariantCulture));
            ConfigManager.Instance.SaveConfig();
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
                    if (lines.Length > 0)
                    {
                        string firstLine = lines[0].Trim();
                        
                        if (firstLine.StartsWith("global_hotkeys_enabled=", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = firstLine.Substring("global_hotkeys_enabled=".Length);
                            if (bool.TryParse(value, out bool globalEnabled))
                            {
                                _globalHotkeysEnabled = globalEnabled;
                            }
                        }
                        else if (bool.TryParse(firstLine, out bool legacyGlobalEnabled))
                        {
                            // Backward compatibility with old format (bare "True"/"False")
                            _globalHotkeysEnabled = legacyGlobalEnabled;
                        }
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
                    _isNewHotkeyFile = true;
                    CreateDefaultHotkeys();
                }
            }
            else
            {
                _isNewHotkeyFile = true;
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
                lines.Add($"global_hotkeys_enabled={_globalHotkeysEnabled}");
                
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
            
            // Start/Stop - Ctrl+Shift+S (Global)
            var startStop = new HotkeyEntry("start_stop", "Start/Stop Live OCR");
            startStop.KeyboardKey = Key.S;
            startStop.UseShift = true;
            startStop.UseCtrl = true;
            _actionBindings["start_stop"] = new List<HotkeyEntry> { startStop };
            
            // Toggle Monitor - Ctrl+Shift+M (Global)
            var toggleMonitor = new HotkeyEntry("toggle_monitor", "Toggle Monitor Window");
            toggleMonitor.KeyboardKey = Key.M;
            toggleMonitor.UseShift = true;
            toggleMonitor.UseCtrl = true;
            _actionBindings["toggle_monitor"] = new List<HotkeyEntry> { toggleMonitor };
            
            // Toggle ChatBox - Ctrl+Shift+C (Global)
            var toggleChatBox = new HotkeyEntry("toggle_chatbox", "Toggle Transcript");
            toggleChatBox.KeyboardKey = Key.C;
            toggleChatBox.UseShift = true;
            toggleChatBox.UseCtrl = true;
            _actionBindings["toggle_chatbox"] = new List<HotkeyEntry> { toggleChatBox };
            
            // Toggle Settings - Ctrl+Shift+E (Global)
            var toggleSettings = new HotkeyEntry("toggle_settings", "Toggle Settings");
            toggleSettings.KeyboardKey = Key.E;
            toggleSettings.UseShift = true;
            toggleSettings.UseCtrl = true;
            _actionBindings["toggle_settings"] = new List<HotkeyEntry> { toggleSettings };
            
            // Toggle Log - Ctrl+Shift+L (Global)
            var toggleLog = new HotkeyEntry("toggle_log", "Toggle Log");
            toggleLog.KeyboardKey = Key.L;
            toggleLog.UseShift = true;
            toggleLog.UseCtrl = true;
            _actionBindings["toggle_log"] = new List<HotkeyEntry> { toggleLog };
            
            // Toggle Main Window - Ctrl+Shift+H (Global)
            var toggleMainWindow = new HotkeyEntry("toggle_main_window", "Toggle Main Window");
            toggleMainWindow.KeyboardKey = Key.H;
            toggleMainWindow.UseShift = true;
            toggleMainWindow.UseCtrl = true;
            _actionBindings["toggle_main_window"] = new List<HotkeyEntry> { toggleMainWindow };
            
            // Clear Overlays - Ctrl+Shift+X (Global)
            var clearOverlays = new HotkeyEntry("clear_overlays", "Clear Overlays");
            clearOverlays.KeyboardKey = Key.X;
            clearOverlays.UseShift = true;
            clearOverlays.UseCtrl = true;
            _actionBindings["clear_overlays"] = new List<HotkeyEntry> { clearOverlays };
            
            // Toggle Passthrough - Ctrl+Shift+P (Global)
            var togglePassthrough = new HotkeyEntry("toggle_passthrough", "Toggle Passthrough");
            togglePassthrough.KeyboardKey = Key.P;
            togglePassthrough.UseShift = true;
            togglePassthrough.UseCtrl = true;
            _actionBindings["toggle_passthrough"] = new List<HotkeyEntry> { togglePassthrough };
            
            // Next Overlay Mode - Ctrl+Shift+Tab (Global) + Tab (Local)
            var overlayModeGlobal = new HotkeyEntry("toggle_overlay_mode", "Next Overlay Mode");
            overlayModeGlobal.KeyboardKey = Key.Tab;
            overlayModeGlobal.UseShift = true;
            overlayModeGlobal.UseCtrl = true;
            overlayModeGlobal.IsGlobal = true;
            var overlayModeLocal = new HotkeyEntry("toggle_overlay_mode", "Next Overlay Mode");
            overlayModeLocal.KeyboardKey = Key.Tab;
            overlayModeLocal.IsGlobal = false;
            _actionBindings["toggle_overlay_mode"] = new List<HotkeyEntry> { overlayModeGlobal, overlayModeLocal };
            
            // Previous Overlay Mode - Shift+Tab (Local)
            var prevOverlayMode = new HotkeyEntry("prev_overlay_mode", "Previous Overlay Mode");
            prevOverlayMode.KeyboardKey = Key.Tab;
            prevOverlayMode.UseShift = true;
            prevOverlayMode.IsGlobal = false;
            _actionBindings["prev_overlay_mode"] = new List<HotkeyEntry> { prevOverlayMode };
            
            // Toggle Listen - No default key
            var toggleListen = new HotkeyEntry("toggle_listen", "Toggle Listen");
            _actionBindings["toggle_listen"] = new List<HotkeyEntry> { toggleListen };
            
            // View in Browser - Ctrl+Shift+B (Global)
            var viewInBrowser = new HotkeyEntry("view_in_browser", "View in Browser");
            viewInBrowser.KeyboardKey = Key.B;
            viewInBrowser.UseShift = true;
            viewInBrowser.UseCtrl = true;
            _actionBindings["view_in_browser"] = new List<HotkeyEntry> { viewInBrowser };
            
            // Snapshot - Ctrl+Shift+Z (Global)
            var snapshot = new HotkeyEntry("snapshot", "Snapshot OCR");
            snapshot.KeyboardKey = Key.Z;
            snapshot.UseShift = true;
            snapshot.UseCtrl = true;
            _actionBindings["snapshot"] = new List<HotkeyEntry> { snapshot };
            
            SaveHotkeys();
            Console.WriteLine("Created default hotkeys");
        }
    }
}

