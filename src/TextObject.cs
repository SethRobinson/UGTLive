using System;
using System.Windows.Media;

namespace UGTLive
{
    public class TextObject : IDisposable
    {
        // Properties
        public string Text { get; set; }
        public double Confidence { get; set; } = 1.0;
        public string ID { get; set; } = Guid.NewGuid().ToString();
        public string TextTranslated { get; set; } = string.Empty;
        public string TextOrientation { get; set; } = "horizontal";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public SolidColorBrush TextColor { get; set; }
        public SolidColorBrush BackgroundColor { get; set; }
        
        // Store the original capture position
        public double CaptureX { get; set; }
        public double CaptureY { get; set; }
        
        // Audio properties for TTS preloading
        public string? SourceAudioFilePath { get; set; }
        public string? TargetAudioFilePath { get; set; }
        public bool SourceAudioReady { get; set; } = false;
        public bool TargetAudioReady { get; set; } = false;

        private bool _isDisposed;

        // Constructor with default parameters
        public TextObject(
            string text = "New text added!",
            double x = 0,
            double y = 0,
            double width = 0,
            double height = 0,
            SolidColorBrush? textColor = null,
            SolidColorBrush? backgroundColor = null,
            double captureX = 0,
            double captureY = 0,
            string textOrientation = "horizontal")
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            TextColor = textColor ?? new SolidColorBrush(Colors.White);
            BackgroundColor = backgroundColor ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 0, 0));
            CaptureX = captureX;
            CaptureY = captureY;
            TextOrientation = textOrientation;
        }

        // Update method stub - just triggers MonitorWindow refresh
        public void UpdateUIElement(OverlayMode? mode = null)
        {
            if (MonitorWindow.Instance != null)
            {
                MonitorWindow.Instance.RefreshOverlays();
            }
        }

        // Cleanup
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

                _isDisposed = true;
        }

        // Set font size (stub for compatibility)
        public void SetFontSize(double fontSize)
        {
            // Font size is now handled by MonitorWindow HTML generation
            // This method is kept for backwards compatibility
        }
    }
}
