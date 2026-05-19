using System;

namespace UGTLive
{
    /// <summary>
    /// Manages error popups to prevent multiple popups from appearing at once
    /// </summary>
    public static class ErrorPopupManager
    {
        private static bool _isPopupShowing = false;
        private static bool _isServiceWarningShowing = false;
        private static readonly object _lock = new object();

        // De-dupe / rate-limit state so a retry storm can't stack 50 modal dialogs.
        private static string _lastSignature = "";
        private static DateTime _lastShownUtc = DateTime.MinValue;
        private static int _suppressedCount = 0;
        private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(15);

        /// <summary>
        /// When true (headless/test mode), errors are written to the console
        /// instead of showing a blocking MessageBox.
        /// </summary>
        public static bool SuppressPopups = false;

        /// <summary>
        /// Shows an error popup if one is not already showing
        /// </summary>
        /// <param name="message">The error message to display</param>
        /// <param name="title">The title of the error dialog</param>
        public static void ShowError(string message, string title = "Translation Error")
        {
            if (SuppressPopups)
            {
                Console.WriteLine($"[ERROR] {title}: {message}");
                return;
            }

            string signature = title + "\n" + message;
            lock (_lock)
            {
                // Never stack: if a dialog is already up, just count + log.
                if (_isPopupShowing)
                {
                    _suppressedCount++;
                    Console.WriteLine($"Suppressed error popup (one already open): {title} - {message}");
                    return;
                }

                // De-dupe: identical error within the cooldown window is logged, not shown.
                if (signature == _lastSignature &&
                    (DateTime.UtcNow - _lastShownUtc) < _cooldown)
                {
                    _suppressedCount++;
                    Console.WriteLine($"Suppressed repeat error popup (x{_suppressedCount}): {title} - {message}");
                    return;
                }

                _isPopupShowing = true;
                _lastSignature = signature;
            }

            int suppressed;
            lock (_lock) { suppressed = _suppressedCount; _suppressedCount = 0; }
            string body = message;
            if (suppressed > 0)
                body += $"\n\n(plus {suppressed} more similar error(s) suppressed)";

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    System.Windows.MessageBox.Show(
                        body,
                        title,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
                finally
                {
                    // Start the cooldown from dismissal so repeats don't re-pop instantly.
                    lock (_lock)
                    {
                        _isPopupShowing = false;
                        _lastShownUtc = DateTime.UtcNow;
                    }
                }
            });
        }

        /// <summary>
        /// Shows a service warning popup if one is not already showing
        /// Returns true if user clicked Yes, false otherwise
        /// </summary>
        /// <param name="message">The warning message to display</param>
        /// <param name="title">The title of the warning dialog</param>
        public static bool ShowServiceWarning(string message, string title = "Service Not Available")
        {
            // Suppress service warnings when ServerSetupDialog is open
            if (ServerSetupDialog.IsDialogOpen)
            {
                Console.WriteLine($"Suppressed service warning while ServerSetupDialog is open: {title} - {message}");
                return false;
            }
            
            lock (_lock)
            {
                if (_isServiceWarningShowing)
                {
                    // A service warning is already showing, just log to console instead
                    Console.WriteLine($"Suppressed duplicate service warning popup: {title} - {message}");
                    return false;
                }

                _isServiceWarningShowing = true;
            }

            bool result = false;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var messageBoxResult = System.Windows.MessageBox.Show(
                        message,
                        title,
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    
                    result = messageBoxResult == System.Windows.MessageBoxResult.Yes;
                }
                finally
                {
                    // Reset the flag immediately after the popup is dismissed
                    lock (_lock)
                    {
                        _isServiceWarningShowing = false;
                    }
                }
            });

            return result;
        }
    }
}

