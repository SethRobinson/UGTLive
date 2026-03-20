using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Forms;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;

namespace UGTLive
{
    public partial class CaptureSelectorWindow : Window
    {
        private Point startPoint;
        private bool isDrawing = false;

        public event EventHandler<Rect>? SelectionComplete;

        private static CaptureSelectorWindow? _currentInstance;

        public CaptureSelectorWindow()
        {
            InitializeComponent();

            IconHelper.SetWindowIcon(this);

            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
            this.MouseMove += OnMouseMove;
            this.MouseLeftButtonUp += OnMouseLeftButtonUp;
            this.KeyDown += OnKeyDown;

            SetWindowToAllScreens();
        }

        public bool WasCancelled { get; set; } = false;

        public static CaptureSelectorWindow GetInstance()
        {
            if (_currentInstance != null)
            {
                _currentInstance.WasCancelled = true;
                _currentInstance.Close();
                _currentInstance = null;
            }

            _currentInstance = new CaptureSelectorWindow();
            return _currentInstance;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.WasCancelled = true;
                this.Close();
            }
        }

        private void SetWindowToAllScreens()
        {
            this.WindowState = WindowState.Normal;

            var allScreens = Forms.Screen.AllScreens;

            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;

            foreach (var screen in allScreens)
            {
                left = Math.Min(left, screen.Bounds.Left);
                top = Math.Min(top, screen.Bounds.Top);
                right = Math.Max(right, screen.Bounds.Right);
                bottom = Math.Max(bottom, screen.Bounds.Bottom);
            }

            this.Left = left;
            this.Top = top;
            this.Width = right - left;
            this.Height = bottom - top;

            Loaded += (s, e) => PositionInstructionText();
        }

        private void PositionInstructionText()
        {
            try
            {
                Window? mainWindow = null;
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is MainWindow)
                    {
                        mainWindow = window;
                        break;
                    }
                }

                if (mainWindow != null)
                {
                    System.Drawing.Point mainWindowPoint = new System.Drawing.Point(
                        (int)(mainWindow.Left + (mainWindow.Width / 2)),
                        (int)(mainWindow.Top + (mainWindow.Height / 2))
                    );
                    Forms.Screen mainWindowScreen = Forms.Screen.FromPoint(mainWindowPoint);

                    double centerX = mainWindow.Left + (mainWindow.Width / 2);

                    double textWidth = instructionText.ActualWidth > 0 ?
                        instructionText.ActualWidth : 450;

                    double leftPosition = centerX - (textWidth / 2);
                    double topPosition = mainWindow.Top - 80;

                    leftPosition = Math.Max(mainWindowScreen.Bounds.Left + 10, leftPosition);
                    leftPosition = Math.Min(mainWindowScreen.Bounds.Right - textWidth - 10, leftPosition);
                    topPosition = Math.Max(mainWindowScreen.Bounds.Top + 10, topPosition);

                    Point screenPoint = new Point(leftPosition, topPosition);
                    Point windowPoint = this.PointFromScreen(screenPoint);

                    instructionText.Margin = new Thickness(
                        windowPoint.X,
                        windowPoint.Y,
                        0,
                        0);
                    instructionText.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    instructionText.VerticalAlignment = System.Windows.VerticalAlignment.Top;

                    instructionText.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0));
                    instructionText.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
                    instructionText.FontWeight = FontWeights.Bold;
                    instructionText.Padding = new Thickness(20, 10, 20, 10);

                    PositionCancelButton(mainWindow, mainWindowScreen);

                    Console.WriteLine($"Capture selector: positioned instruction text at {leftPosition}, {topPosition}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error positioning capture selector instruction text: {ex.Message}");
            }
        }

        private void PositionCancelButton(Window mainWindow, Forms.Screen mainWindowScreen)
        {
            try
            {
                double textMarginLeft = instructionText.Margin.Left;
                double textMarginTop = instructionText.Margin.Top;

                double buttonLeft = Math.Max(10, textMarginLeft - cancelButton.Width - 10);
                double buttonTop = textMarginTop;

                buttonLeft = Math.Max(mainWindowScreen.Bounds.Left + 10, buttonLeft);
                buttonTop = Math.Max(mainWindowScreen.Bounds.Top + 10, buttonTop);

                cancelButton.Margin = new Thickness(buttonLeft, buttonTop, 0, 0);
                cancelButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                cancelButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;

                cancelButton.Visibility = Visibility.Visible;
                cancelButton.IsEnabled = true;
                cancelButton.Focus();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error positioning capture selector cancel button: {ex.Message}");
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            startPoint = e.GetPosition(this);
            isDrawing = true;

            selectionRectangle.Width = 0;
            selectionRectangle.Height = 0;
            selectionRectangle.Visibility = Visibility.Visible;

            Canvas.SetLeft(selectionRectangle, startPoint.X);
            Canvas.SetTop(selectionRectangle, startPoint.Y);

            this.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDrawing) return;

            Point currentPoint = e.GetPosition(this);

            double width = Math.Abs(currentPoint.X - startPoint.X);
            double height = Math.Abs(currentPoint.Y - startPoint.Y);

            double left = Math.Min(currentPoint.X, startPoint.X);
            double top = Math.Min(currentPoint.Y, startPoint.Y);

            selectionRectangle.Width = width;
            selectionRectangle.Height = height;

            selectionRectangle.Margin = new Thickness(left, top, 0, 0);
            selectionRectangle.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            selectionRectangle.VerticalAlignment = System.Windows.VerticalAlignment.Top;

            instructionText.Text = $"Selection size: {(int)width} x {(int)height}";
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDrawing) return;

            this.ReleaseMouseCapture();
            isDrawing = false;

            if (WasCancelled) return;

            Point currentPoint = e.GetPosition(this);
            double width = Math.Abs(currentPoint.X - startPoint.X);
            double height = Math.Abs(currentPoint.Y - startPoint.Y);
            double left = Math.Min(currentPoint.X, startPoint.X);
            double top = Math.Min(currentPoint.Y, startPoint.Y);

            if (width < 50 || height < 50)
            {
                MessageBox.Show("Please drag out a larger area for the capture region (at least 50x50 pixels).",
                                "Selection too small",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            Point screenPoint = this.PointToScreen(new Point(left, top));

            Rect selectionRect = new Rect(
                screenPoint.X,
                screenPoint.Y,
                width,
                height
            );

            SelectionComplete?.Invoke(this, selectionRect);

            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.WasCancelled = true;
            this.Close();
        }
    }
}
