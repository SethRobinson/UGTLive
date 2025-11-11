using System;

namespace UGTLive
{
    /// <summary>
    /// Manages error popups to prevent multiple popups from appearing at once
    /// </summary>
    public static class ErrorPopupManager
    {
        private static bool _isPopupShowing = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Shows an error popup if one is not already showing
        /// </summary>
        /// <param name="message">The error message to display</param>
        /// <param name="title">The title of the error dialog</param>
        public static void ShowError(string message, string title = "Translation Error")
        {
            lock (_lock)
            {
                if (_isPopupShowing)
                {
                    // A popup is already showing, just log to console instead
                    Console.WriteLine($"Suppressed duplicate error popup: {title} - {message}");
                    return;
                }

                _isPopupShowing = true;
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    System.Windows.MessageBox.Show(
                        message,
                        title,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
                finally
                {
                    // Reset the flag immediately after the popup is dismissed
                    lock (_lock)
                    {
                        _isPopupShowing = false;
                    }
                }
            });
        }
    }
}

