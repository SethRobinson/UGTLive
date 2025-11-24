using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace UGTLive
{
    public static class IconHelper
    {
        /// <summary>
        /// Loads the highest resolution icon from the application's icon file
        /// </summary>
        public static BitmapSource? LoadHighResIcon()
        {
            try
            {
                Uri iconUri = new Uri("pack://application:,,,/media/Icon1.ico", UriKind.RelativeOrAbsolute);
                IconBitmapDecoder iconDecoder = new IconBitmapDecoder(
                    iconUri,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                
                // Get the highest resolution frame from the ICO file
                BitmapSource highResIcon = iconDecoder.Frames
                    .OrderByDescending(f => f.PixelWidth)
                    .First();
                
                return highResIcon;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading high-res icon: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Sets the highest resolution icon on a window
        /// </summary>
        public static void SetWindowIcon(Window window)
        {
            var icon = LoadHighResIcon();
            if (icon != null)
            {
                window.Icon = icon;
            }
        }
    }
}

