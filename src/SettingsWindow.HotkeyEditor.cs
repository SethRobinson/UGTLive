using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Diagnostics;
using System.Collections.ObjectModel;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using MessageBox = System.Windows.MessageBox;
using NAudio.Wave;
using System.Collections.Generic;
using System.Windows.Forms;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace UGTLive
{
    public partial class SettingsWindow
    {
        #region Hotkey Editor
        
        // Display item for action list
        public class ActionDisplayItem
        {
            public string ActionId { get; set; } = "";
            public string ActionName { get; set; } = "";
            
            public override string ToString()
            {
                return ActionName;
            }
        }
        
        // Display item for bindings list
        public class BindingDisplayItem : System.ComponentModel.INotifyPropertyChanged
        {
            public HotkeyEntry Binding { get; set; }
            public string BindingType { get; set; } = "";
            public string BindingString { get; set; } = "";
            
            public bool IsGlobal
            {
                get => Binding.IsGlobal;
                set
                {
                    Binding.IsGlobal = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsGlobal)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ScopeLabel)));
                    BindingString = Binding.HasKeyboardHotkey() ? Binding.GetKeyboardComboString() : Binding.GetGamepadHotkeyString();
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BindingString)));
                }
            }
            
            public string ScopeLabel => IsGlobal ? "Global" : "Local";
            
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            
            public BindingDisplayItem(HotkeyEntry entry)
            {
                Binding = entry;
                
                if (entry.HasKeyboardHotkey())
                {
                    BindingType = "Keyboard";
                    BindingString = entry.GetKeyboardComboString();
                }
                else if (entry.HasGamepadHotkey())
                {
                    BindingType = "Gamepad";
                    BindingString = entry.GetGamepadHotkeyString();
                }
            }
        }
        
        private string? _selectedActionId;
        
        // Load actions into the list
        private void loadActions()
        {
            // Define all available actions
            var actions = new List<ActionDisplayItem>
            {
                new ActionDisplayItem { ActionId = "start_stop", ActionName = "Start/Stop Auto" },
                new ActionDisplayItem { ActionId = "snapshot", ActionName = "Snapshot OCR" },
                new ActionDisplayItem { ActionId = "toggle_monitor", ActionName = "Toggle Monitor Window" },
                new ActionDisplayItem { ActionId = "toggle_chatbox", ActionName = "Toggle Transcript" },
                new ActionDisplayItem { ActionId = "toggle_settings", ActionName = "Toggle Settings" },
                new ActionDisplayItem { ActionId = "toggle_log", ActionName = "Toggle Log" },
                new ActionDisplayItem { ActionId = "toggle_listen", ActionName = "Toggle Listen" },
                new ActionDisplayItem { ActionId = "view_in_browser", ActionName = "View in Browser" },
                new ActionDisplayItem { ActionId = "toggle_main_window", ActionName = "Toggle Main Window" },
                new ActionDisplayItem { ActionId = "clear_overlays", ActionName = "Clear Overlays" },
                new ActionDisplayItem { ActionId = "toggle_passthrough", ActionName = "Toggle Passthrough" },
                new ActionDisplayItem { ActionId = "toggle_overlay_mode", ActionName = "Next Overlay Mode" },
                new ActionDisplayItem { ActionId = "prev_overlay_mode", ActionName = "Previous Overlay Mode" },
                new ActionDisplayItem { ActionId = "play_all_audio", ActionName = "Play All Audio Toggle" },
                new ActionDisplayItem { ActionId = "toggle_edit_mode", ActionName = "Toggle Edit Mode" },
                new ActionDisplayItem { ActionId = "font_size_increase", ActionName = "Font Size Increase" },
                new ActionDisplayItem { ActionId = "font_size_decrease", ActionName = "Font Size Decrease" },
                new ActionDisplayItem { ActionId = "save_screenshot", ActionName = "Save Screenshot" }
            };
            
            actionsListBox.ItemsSource = actions;
            
            // Load global hotkeys enabled state
            globalHotkeysEnabledCheckBox.IsChecked = HotkeyManager.Instance.GetGlobalHotkeysEnabled();
            
            Console.WriteLine($"Loaded {actions.Count} actions into settings");
        }
        
        // Load bindings for the selected action
        private void loadBindingsForSelectedAction()
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                bindingsListView.ItemsSource = null;
                return;
            }
            
            var bindings = HotkeyManager.Instance.GetBindings(_selectedActionId);
            var displayItems = bindings.Select(b => new BindingDisplayItem(b)).ToList();
            
            bindingsListView.ItemsSource = displayItems;
            
            Console.WriteLine($"Loaded {displayItems.Count} bindings for action {_selectedActionId}");
        }
        
        // Actions list selection changed
        private void ActionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (actionsListBox.SelectedItem is ActionDisplayItem actionItem)
            {
                _selectedActionId = actionItem.ActionId;
                loadBindingsForSelectedAction();
            }
            else
            {
                _selectedActionId = null;
                bindingsListView.ItemsSource = null;
            }
        }
        
        // Bindings list selection changed
        private void BindingsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Just track selection, no action needed
        }
        
        // Add keyboard binding
        private void AddKeyboardBindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                MessageBox.Show("Please select an action first.", "No Action Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Show dialog to capture keyboard input
            var dialog = new Window
            {
                Title = "Press Key Combination",
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            
            var stackPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(20)
            };
            
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = "Press a key combination...",
                IsReadOnly = true,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                FontSize = 14
            };
            
            var globalCheckBox = new System.Windows.Controls.CheckBox
            {
                Content = "Global (works system-wide, even when app is not focused)",
                IsChecked = true,
                Margin = new Thickness(0, 10, 0, 0)
            };
            
            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(globalCheckBox);
            
            HotkeyEntry? capturedBinding = null;
            
            textBox.PreviewKeyDown += (s, args) =>
            {
                args.Handled = true;
                
                var key = args.Key;
                if (key == System.Windows.Input.Key.System)
                {
                    key = args.SystemKey;
                }
                
                // Skip modifier keys themselves
                if (key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                    key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                    key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                    key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
                {
                    return;
                }
                
                // Escape cancels
                if (key == System.Windows.Input.Key.Escape)
                {
                    dialog.Close();
                    return;
                }
                
                // Get modifiers
                bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
                bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
                bool alt = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt;
                
                // Get action name
                var actionItem = actionsListBox.SelectedItem as ActionDisplayItem;
                string actionName = actionItem?.ActionName ?? "";
                
                // Create binding
                capturedBinding = new HotkeyEntry(_selectedActionId, actionName);
                capturedBinding.KeyboardKey = key;
                capturedBinding.UseShift = shift;
                capturedBinding.UseCtrl = ctrl;
                capturedBinding.UseAlt = alt;
                capturedBinding.IsGlobal = globalCheckBox.IsChecked ?? true;
                
                textBox.Text = capturedBinding.GetKeyboardHotkeyString();
                
                // Auto-close after brief delay
                var closeTimer = new System.Windows.Threading.DispatcherTimer();
                closeTimer.Interval = TimeSpan.FromMilliseconds(500);
                closeTimer.Tick += (ts, te) =>
                {
                    closeTimer.Stop();
                    dialog.Close();
                };
                closeTimer.Start();
            };
            
            dialog.Content = stackPanel;
            textBox.Focus();
            
            dialog.ShowDialog();
            
            if (capturedBinding != null)
            {
                // Add binding and auto-save
                HotkeyManager.Instance.AddBinding(capturedBinding);
                loadBindingsForSelectedAction();
                
                // Update tooltips in MainWindow
                MainWindow.Instance.UpdateTooltips();
            }
        }
        
        // Add gamepad binding
        private void AddGamepadBindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                MessageBox.Show("Please select an action first.", "No Action Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Show dialog to capture gamepad input
            var dialog = new Window
            {
                Title = "Press Gamepad Button(s)",
                Width = 500,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "Press and hold gamepad button(s), then release...",
                Margin = new Thickness(20),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 14,
                TextWrapping = System.Windows.TextWrapping.Wrap
            };
            
            dialog.Content = textBlock;
            
            HotkeyEntry? capturedBinding = null;
            List<string> maxButtons = new List<string>();
            int stableCount = 0;
            const int STABLE_FRAMES = 5;
            
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (s, args) =>
            {
                var pressedButtons = GamepadManager.Instance.GetCurrentlyPressedButtons();
                
                if (pressedButtons.Count > maxButtons.Count)
                {
                    maxButtons = new List<string>(pressedButtons);
                    stableCount = 0;
                    textBlock.Text = $"Holding: {string.Join("+", maxButtons)}... (release when ready)";
                }
                else if (pressedButtons.Count == maxButtons.Count && pressedButtons.Count > 0)
                {
                    bool same = pressedButtons.All(b => maxButtons.Contains(b));
                    if (same)
                    {
                        stableCount++;
                    }
                    else
                    {
                        maxButtons = new List<string>(pressedButtons);
                        stableCount = 0;
                    }
                }
                else if (pressedButtons.Count == 0 && maxButtons.Count > 0 && stableCount >= STABLE_FRAMES)
                {
                    // Get action name
                    var actionItem = actionsListBox.SelectedItem as ActionDisplayItem;
                    string actionName = actionItem?.ActionName ?? "";
                    
                    // Create binding
                    capturedBinding = new HotkeyEntry(_selectedActionId, actionName);
                    capturedBinding.GamepadButtons = maxButtons;
                    
                    timer.Stop();
                    dialog.Close();
                }
            };
            
            dialog.Closed += (s, args) => timer.Stop();
            timer.Start();
            dialog.ShowDialog();
            
            if (capturedBinding != null)
            {
                // Add binding and auto-save
                HotkeyManager.Instance.AddBinding(capturedBinding);
                loadBindingsForSelectedAction();
                
                // Update tooltips in MainWindow
                MainWindow.Instance.UpdateTooltips();
            }
        }
        
        // Remove selected binding
        private void RemoveBindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedActionId))
            {
                MessageBox.Show("Please select an action first.", "No Action Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (bindingsListView.SelectedItem is BindingDisplayItem bindingItem)
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Remove binding \"{bindingItem.BindingString}\" from this action?",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Remove binding and auto-save
                    HotkeyManager.Instance.RemoveBinding(_selectedActionId, bindingItem.Binding);
                    loadBindingsForSelectedAction();
                    
                    // Update tooltips in MainWindow
                    MainWindow.Instance.UpdateTooltips();
                }
            }
            else
            {
                MessageBox.Show("Please select a binding to remove.", "No Binding Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        // Reset all hotkeys to defaults
        private void ResetHotkeysButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all hotkeys to defaults?", 
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Reset to defaults
                HotkeyManager.Instance.ResetToDefaults();
                
                // Refresh the UI
                loadActions();
                loadBindingsForSelectedAction();
                
                // Update tooltips in MainWindow
                MainWindow.Instance.UpdateTooltips();
                
                MessageBox.Show("Hotkeys reset to defaults!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        // Global hotkeys enabled checkbox changed
        private void GlobalHotkeysEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
                
            bool enabled = globalHotkeysEnabledCheckBox.IsChecked ?? true;
            HotkeyManager.Instance.SetGlobalHotkeysEnabled(enabled);
            HotkeyManager.Instance.SaveHotkeys();
            
            Console.WriteLine($"Global hotkeys {(enabled ? "enabled" : "disabled")}");
        }
        
        // Per-binding global/local checkbox changed
        private void BindingGlobalCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
                return;
            
            HotkeyManager.Instance.SaveHotkeys();
            MainWindow.Instance.UpdateTooltips();
        }
        
        #endregion
    }
}
