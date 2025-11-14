using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace UGTLive
{
    // Represents a single hotkey entry
    public class HotkeyEntry
    {
        public string ActionName { get; set; } = "";
        public string ActionId { get; set; } = "";
        
        // Keyboard hotkey
        public Key KeyboardKey { get; set; } = Key.None;
        public bool UseShift { get; set; } = false;
        public bool UseCtrl { get; set; } = false;
        public bool UseAlt { get; set; } = false;
        
        // Gamepad hotkey
        public List<string> GamepadButtons { get; set; } = new List<string>();
        
        public HotkeyEntry()
        {
        }
        
        public HotkeyEntry(string actionId, string actionName)
        {
            ActionId = actionId;
            ActionName = actionName;
        }
        
        // Check if this entry has a keyboard hotkey
        public bool HasKeyboardHotkey()
        {
            return KeyboardKey != Key.None;
        }
        
        // Check if this entry has a gamepad hotkey
        public bool HasGamepadHotkey()
        {
            return GamepadButtons.Count > 0;
        }
        
        // Get keyboard hotkey as string
        public string GetKeyboardHotkeyString()
        {
            if (KeyboardKey == Key.None)
                return "None";
                
            List<string> parts = new List<string>();
            
            if (UseCtrl) parts.Add("Ctrl");
            if (UseAlt) parts.Add("Alt");
            if (UseShift) parts.Add("Shift");
            parts.Add(KeyboardKey.ToString());
            
            return string.Join("+", parts);
        }
        
        // Get gamepad hotkey as string
        public string GetGamepadHotkeyString()
        {
            if (GamepadButtons.Count == 0)
                return "None";
                
            return string.Join("+", GamepadButtons);
        }
        
        // Check if keyboard modifiers match
        public bool MatchesKeyboardModifiers(ModifierKeys currentModifiers)
        {
            bool hasShift = (currentModifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool hasCtrl = (currentModifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool hasAlt = (currentModifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            
            return hasShift == UseShift && hasCtrl == UseCtrl && hasAlt == UseAlt;
        }
        
        // Check if gamepad buttons match
        public bool MatchesGamepadButtons(List<string> pressedButtons)
        {
            if (GamepadButtons.Count == 0 || pressedButtons.Count == 0)
                return false;
                
            // All buttons in the hotkey must be pressed
            return GamepadButtons.All(button => pressedButtons.Contains(button));
        }
        
        // Serialize to string for saving to file
        public string Serialize()
        {
            List<string> parts = new List<string>();
            parts.Add(ActionId);
            parts.Add(ActionName);
            
            // Keyboard hotkey
            parts.Add(KeyboardKey.ToString());
            parts.Add(UseShift ? "1" : "0");
            parts.Add(UseCtrl ? "1" : "0");
            parts.Add(UseAlt ? "1" : "0");
            
            // Gamepad hotkey
            parts.Add(GamepadButtons.Count > 0 ? string.Join(",", GamepadButtons) : "");
            
            return string.Join("|", parts);
        }
        
        // Deserialize from string loaded from file
        public static HotkeyEntry? Deserialize(string data)
        {
            try
            {
                string[] parts = data.Split('|');
                if (parts.Length < 7)
                    return null;
                    
                var entry = new HotkeyEntry
                {
                    ActionId = parts[0],
                    ActionName = parts[1]
                };
                
                // Parse keyboard key
                if (Enum.TryParse<Key>(parts[2], out Key key))
                {
                    entry.KeyboardKey = key;
                }
                
                entry.UseShift = parts[3] == "1";
                entry.UseCtrl = parts[4] == "1";
                entry.UseAlt = parts[5] == "1";
                
                // Parse gamepad buttons
                if (!string.IsNullOrWhiteSpace(parts[6]))
                {
                    entry.GamepadButtons = parts[6].Split(',').ToList();
                }
                
                return entry;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deserializing hotkey entry: {ex.Message}");
                return null;
            }
        }
    }
}

