using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UGTLive
{
    public partial class ConfigManager
    {
        // OCR Display methods
        
        // Check if translation should stay onscreen until replaced
        public bool IsLeaveTranslationOnscreenEnabled()
        {
            string value = GetValue(LEAVE_TRANSLATION_ONSCREEN, "false");
            return value.ToLower() == "true";
        }
        
        // Set whether translation should stay onscreen until replaced
        public void SetLeaveTranslationOnscreenEnabled(bool enabled)
        {
            _configValues[LEAVE_TRANSLATION_ONSCREEN] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Leave translation onscreen enabled: {enabled}");
        }
        
        // Check if translated text should be kept until replaced
        public bool IsKeepTranslatedTextUntilReplacedEnabled()
        {
            string value = GetValue(KEEP_TRANSLATED_TEXT_UNTIL_REPLACED, "true");
            return value.ToLower() == "true";
        }
        
        // Set whether translated text should be kept until replaced
        public void SetKeepTranslatedTextUntilReplacedEnabled(bool enabled)
        {
            _configValues[KEEP_TRANSLATED_TEXT_UNTIL_REPLACED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Keep translated text until replaced enabled: {enabled}");
        }
        
        // Block Detection methods
        
        // Get Block Detection Scale
        public double GetBlockDetectionScale()
        {
            string value = GetValue(BLOCK_DETECTION_SCALE, "5.0");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && scale > 0)
            {
                return scale;
            }
            return 5.0; // Default
        }
        
        // Set Block Detection Scale
        public void SetBlockDetectionScale(double scale)
        {
            if (scale > 0)
            {
                _configValues[BLOCK_DETECTION_SCALE] = scale.ToString("F2", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Block detection scale set to: {scale:F2}");
            }
        }
        
        // Get Block Detection Settle Time
        public double GetBlockDetectionSettleTime()
        {
            string value = GetValue(BLOCK_DETECTION_SETTLE_TIME, "0.15");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double time) && time >= 0)
            {
                return time;
            }
            return 0.15; // Default
        }
        
        // Set Block Detection Settle Time
        public void SetBlockDetectionSettleTime(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[BLOCK_DETECTION_SETTLE_TIME] = seconds.ToString("F2", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Block detection settle time set to: {seconds:F2} seconds");
            }
        }

        // Get Block Detection Max Settle Time
        public double GetBlockDetectionMaxSettleTime()
        {
            string value = GetValue(BLOCK_DETECTION_MAX_SETTLE_TIME, "1.00");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double time) && time >= 0)
            {
                return time;
            }
            return 1.0; // Default
        }

        // Set Block Detection Max Settle Time
        public void SetBlockDetectionMaxSettleTime(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[BLOCK_DETECTION_MAX_SETTLE_TIME] = seconds.ToString("F2", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Block detection max settle time set to: {seconds:F2} seconds");
            }
        }
        
        // Get Cooldown Hash Compare Length - how many characters to compare when checking if OCR content is similar during cooldown
        public int GetCooldownHashCompareLength()
        {
            string value = GetValue(COOLDOWN_HASH_COMPARE_LENGTH, "15");
            if (int.TryParse(value, out int length) && length >= 0)
            {
                return length;
            }
            return 15; // Default
        }
        
        // Set Cooldown Hash Compare Length
        public void SetCooldownHashCompareLength(int length)
        {
            if (length >= 0)
            {
                _configValues[COOLDOWN_HASH_COMPARE_LENGTH] = length.ToString();
                SaveConfig();
                Console.WriteLine($"Cooldown hash compare length set to: {length} characters");
            }
        }
        
        // Get Overlay Clear Delay
        public double GetOverlayClearDelaySeconds()
        {
            string value = GetValue(OVERLAY_CLEAR_DELAY_SECONDS, "0.3");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double delay) && delay >= 0)
            {
                return delay;
            }
            return 0.3; // Default
        }
        
        // Set Overlay Clear Delay
        public void SetOverlayClearDelaySeconds(double seconds)
        {
            if (seconds >= 0)
            {
                _configValues[OVERLAY_CLEAR_DELAY_SECONDS] = seconds.ToString("F2", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Overlay clear delay set to: {seconds:F2} seconds");
            }
        }
        
        // Get Snapshot Toggle Mode (if true, pressing snapshot while overlay is displayed clears it)
        public bool GetSnapshotToggleMode()
        {
            string value = GetValue(SNAPSHOT_TOGGLE_MODE, "true");
            return value.ToLower() == "true";
        }
        
        // Set Snapshot Toggle Mode
        public void SetSnapshotToggleMode(bool enabled)
        {
            _configValues[SNAPSHOT_TOGGLE_MODE] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Snapshot toggle mode: {enabled}");
        }

        // Monitor Window Override Color methods
        
        // Get/Set Monitor Override BG Color Enabled
        public bool IsMonitorOverrideBgColorEnabled()
        {
            return GetBoolValue(MONITOR_OVERRIDE_BG_COLOR_ENABLED, false);
        }

        public void SetMonitorOverrideBgColorEnabled(bool enabled)
        {
            _configValues[MONITOR_OVERRIDE_BG_COLOR_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Monitor override BG color enabled: {enabled}");
        }

        // Get/Set Monitor Override BG Color
        public System.Windows.Media.Color GetMonitorOverrideBgColor()
        {
            string value = GetValue(MONITOR_OVERRIDE_BG_COLOR, "#FF000000"); // Default: Black
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Monitor override BG color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.Black;
        }

        public void SetMonitorOverrideBgColor(System.Windows.Media.Color color)
        {
            string hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _configValues[MONITOR_OVERRIDE_BG_COLOR] = hexColor;
            SaveConfig();
            Console.WriteLine($"Monitor override BG color set to: {hexColor}");
        }

        // Get/Set Monitor Background Opacity
        public double GetMonitorBgOpacity()
        {
            string value = GetValue(MONITOR_BG_OPACITY, "1.0");
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) && opacity >= 0.0 && opacity <= 1.0)
            {
                return opacity;
            }
            return 1.0; // Default: 100% opacity (fully opaque)
        }

        public void SetMonitorBgOpacity(double opacity)
        {
            if (opacity >= 0.0 && opacity <= 1.0)
            {
                _configValues[MONITOR_BG_OPACITY] = opacity.ToString("F2", CultureInfo.InvariantCulture);
                SaveConfig();
                Console.WriteLine($"Monitor background opacity set to: {opacity:F2}");
            }
            else
            {
                Console.WriteLine($"Invalid opacity value: {opacity}. Must be between 0.0 and 1.0.");
            }
        }

        // Get/Set Monitor Override Font Color Enabled
        public bool IsMonitorOverrideFontColorEnabled()
        {
            return GetBoolValue(MONITOR_OVERRIDE_FONT_COLOR_ENABLED, false);
        }

        public void SetMonitorOverrideFontColorEnabled(bool enabled)
        {
            _configValues[MONITOR_OVERRIDE_FONT_COLOR_ENABLED] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Monitor override font color enabled: {enabled}");
        }

        // Get/Set Monitor Override Font Color
        public System.Windows.Media.Color GetMonitorOverrideFontColor()
        {
            string value = GetValue(MONITOR_OVERRIDE_FONT_COLOR, "#FFFFFFFF"); // Default: White
            try
            {
                if (value.StartsWith("#") && value.Length >= 7)
                {
                    byte a = 255; // Default alpha is fully opaque
                    
                    // Parse alpha if provided (#AARRGGBB format)
                    if (value.Length >= 9)
                    {
                        a = byte.Parse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    }
                    
                    // Parse RGB values
                    int offset = value.Length >= 9 ? 3 : 1;
                    byte r = byte.Parse(value.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(value.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(value.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
                    
                    return System.Windows.Media.Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing Monitor override font color: {ex.Message}");
            }
            
            // Return default color if parsing fails
            return System.Windows.Media.Colors.White;
        }

        public void SetMonitorOverrideFontColor(System.Windows.Media.Color color)
        {
            string hexColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _configValues[MONITOR_OVERRIDE_FONT_COLOR] = hexColor;
            SaveConfig();
            Console.WriteLine($"Monitor override font color set to: {hexColor}");
        }

        // Get/Set Monitor Text Area Expansion Width
        public int GetMonitorTextAreaExpansionWidth()
        {
            string value = GetValue(MONITOR_TEXT_AREA_EXPANSION_WIDTH, "6");
            if (int.TryParse(value, out int width) && width >= 0)
            {
                return width;
            }
            return 6; // Default: 6 pixels
        }

        public void SetMonitorTextAreaExpansionWidth(int width)
        {
            _configValues[MONITOR_TEXT_AREA_EXPANSION_WIDTH] = width.ToString();
            SaveConfig();
            Console.WriteLine($"Monitor text area expansion width set to: {width}");
        }

        // Get/Set Monitor Text Area Expansion Height
        public int GetMonitorTextAreaExpansionHeight()
        {
            string value = GetValue(MONITOR_TEXT_AREA_EXPANSION_HEIGHT, "2");
            if (int.TryParse(value, out int height) && height >= 0)
            {
                return height;
            }
            return 2; // Default: 2 pixels
        }

        public void SetMonitorTextAreaExpansionHeight(int height)
        {
            _configValues[MONITOR_TEXT_AREA_EXPANSION_HEIGHT] = height.ToString();
            SaveConfig();
            Console.WriteLine($"Monitor text area expansion height set to: {height}");
        }

        // Get/Set Monitor Text Overlay Border Radius
        public int GetMonitorTextOverlayBorderRadius()
        {
            string value = GetValue(MONITOR_TEXT_OVERLAY_BORDER_RADIUS, "8");
            if (int.TryParse(value, out int radius) && radius >= 0)
            {
                return radius;
            }
            return 8; // Default: 8 pixels
        }

        public void SetMonitorTextOverlayBorderRadius(int radius)
        {
            _configValues[MONITOR_TEXT_OVERLAY_BORDER_RADIUS] = radius.ToString();
            SaveConfig();
            Console.WriteLine($"Monitor text overlay border radius set to: {radius}");
        }

        // Get/Set Monitor Overlay Mode
        public string GetMonitorOverlayMode()
        {
            return GetValue(MONITOR_OVERLAY_MODE, "Translated"); // Default to Translated
        }

        public void SetMonitorOverlayMode(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _configValues[MONITOR_OVERLAY_MODE] = mode;
                SaveConfig();
                Console.WriteLine($"Monitor overlay mode set to: {mode}");
            }
        }
        
        public string GetMainWindowOverlayMode()
        {
            return GetValue(MAIN_WINDOW_OVERLAY_MODE, "Translated"); // Default to Translated
        }

        public void SetMainWindowOverlayMode(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _configValues[MAIN_WINDOW_OVERLAY_MODE] = mode;
                SaveConfig();
                Console.WriteLine($"Main window overlay mode set to: {mode}");
            }
        }
        
        public bool GetMainWindowMousePassthrough()
        {
            string value = GetValue(MAIN_WINDOW_MOUSE_PASSTHROUGH, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetMainWindowMousePassthrough(bool enabled)
        {
            _configValues[MAIN_WINDOW_MOUSE_PASSTHROUGH] = enabled.ToString().ToLower();
            SaveConfig();
            if (GetLogExtraDebugStuff())
            {
                Console.WriteLine($"Main window mouse passthrough set to: {enabled}");
            }
        }
        
        public bool GetWindowsVisibleInScreenshots()
        {
            string value = GetValue(WINDOWS_VISIBLE_IN_SCREENSHOTS, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetWindowsVisibleInScreenshots(bool visible)
        {
            _configValues[WINDOWS_VISIBLE_IN_SCREENSHOTS] = visible.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Windows visible in screenshots set to: {visible}");
        }

        public string GetTextOverlayHorizontalAlignment()
        {
            return GetValue(TEXT_OVERLAY_HORIZONTAL_ALIGNMENT, "center");
        }

        public void SetTextOverlayHorizontalAlignment(string alignment)
        {
            _configValues[TEXT_OVERLAY_HORIZONTAL_ALIGNMENT] = alignment.ToLower();
            SaveConfig();
        }

        public string GetTextOverlayVerticalAlignment()
        {
            return GetValue(TEXT_OVERLAY_VERTICAL_ALIGNMENT, "center");
        }

        public void SetTextOverlayVerticalAlignment(string alignment)
        {
            _configValues[TEXT_OVERLAY_VERTICAL_ALIGNMENT] = alignment.ToLower();
            SaveConfig();
        }

        public bool GetEditModeEnabled()
        {
            string value = GetValue(EDIT_MODE_ENABLED, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void SetEditModeEnabled(bool enabled)
        {
            _configValues[EDIT_MODE_ENABLED] = enabled.ToString().ToLower();
        }

        // Check if persist window size is enabled
        public bool IsPersistWindowSizeEnabled()
        {
            return GetBoolValue(PERSIST_WINDOW_SIZE, true);
        }

        // Set persist window size enabled
        public void SetPersistWindowSizeEnabled(bool enabled)
        {
            _configValues[PERSIST_WINDOW_SIZE] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Persist window size enabled: {enabled}");
        }

        public bool IsCompletionSoundEnabled()
        {
            return GetBoolValue(PLAY_COMPLETION_SOUND, false);
        }

        public void SetCompletionSoundEnabled(bool enabled)
        {
            _configValues[PLAY_COMPLETION_SOUND] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Completion sound enabled: {enabled}");
        }

        // Debug logging settings
        public bool GetLogExtraDebugStuff()
        {
            string value = GetValue(LOG_EXTRA_DEBUG_STUFF, "false");
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        
        public void SetLogExtraDebugStuff(bool enabled)
        {
            _configValues[LOG_EXTRA_DEBUG_STUFF] = enabled.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Log extra debug stuff set to: {enabled}");
        }

        // Font Settings methods
        
        // Get/Set Source Language Font Family
        public string GetSourceLanguageFontFamily()
        {
            return GetValue(SOURCE_LANGUAGE_FONT_FAMILY, "MS Gothic");
        }

        public void SetSourceLanguageFontFamily(string fontFamily)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                _configValues[SOURCE_LANGUAGE_FONT_FAMILY] = fontFamily;
                SaveConfig();
                Console.WriteLine($"Source language font family set to: {fontFamily}");
            }
        }

        // Get/Set Source Language Font Bold
        public bool GetSourceLanguageFontBold()
        {
            return GetBoolValue(SOURCE_LANGUAGE_FONT_BOLD, true);
        }

        public void SetSourceLanguageFontBold(bool bold)
        {
            _configValues[SOURCE_LANGUAGE_FONT_BOLD] = bold.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Source language font bold set to: {bold}");
        }

        // Get/Set Target Language Font Family
        public string GetTargetLanguageFontFamily()
        {
            return GetValue(TARGET_LANGUAGE_FONT_FAMILY, "Comic Sans MS");
        }

        public void SetTargetLanguageFontFamily(string fontFamily)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                _configValues[TARGET_LANGUAGE_FONT_FAMILY] = fontFamily;
                SaveConfig();
                Console.WriteLine($"Target language font family set to: {fontFamily}");
            }
        }

        // Get/Set Target Language Font Bold
        public bool GetTargetLanguageFontBold()
        {
            return GetBoolValue(TARGET_LANGUAGE_FONT_BOLD, true);
        }

        public void SetTargetLanguageFontBold(bool bold)
        {
            _configValues[TARGET_LANGUAGE_FONT_BOLD] = bold.ToString().ToLower();
            SaveConfig();
            Console.WriteLine($"Target language font bold set to: {bold}");
        }
    }
}
