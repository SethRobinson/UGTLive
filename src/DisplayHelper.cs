namespace UGTLive
{
    internal static class DisplayHelper
    {
        public static double GetWindowsTextScaleFactor()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
                if (key != null)
                {
                    var value = key.GetValue("TextScaleFactor");
                    if (value is int intValue)
                        return intValue / 100.0;
                }
            }
            catch { }
            return 1.0;
        }
    }
}
