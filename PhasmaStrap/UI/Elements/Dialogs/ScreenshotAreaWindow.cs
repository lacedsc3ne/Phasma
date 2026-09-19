using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;
using ShapeRect = System.Windows.Shapes.Rectangle;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // "Pick an area" screenshots: the game's picture is frozen over the Roblox window, you drag over
    // the part you want and it's saved. Enter or a double-click takes the whole window, Esc (or
    // switching away) cancels. Built in code - it's one picture and a rectangle.
    public sealed class ScreenshotAreaWindow : Window
    {
        private readonly System.Drawing.Bitmap _shot;
        private readonly System.Drawing.Rectangle _screenRect;
        private readonly Action<System.Drawing.Bitmap?> _done;

        private readonly Canvas _canvas = new();
        private readonly ShapePath _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), IsHitTestVisible = false };
        private readonly ShapeRect _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Border _sizeTag = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x12)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock _sizeText = new() { Foreground = Brushes.White, FontSize = 12, FontFamily = new System.Windows.Media.FontFamily("Consolas") };

        private Point? _start;
        private Rect _selection = Rect.Empty;
        private bool _finished;

        // shot: the Roblox window's picture; screenRect: where that window is, in screen pixels
        public static void Pick(System.Drawing.Bitmap shot, System.Drawing.Rectangle screenRect, Action<System.Drawing.Bitmap?> done)
        {
            var window = new ScreenshotAreaWindow(shot, screenRect, done);
            window.Show();
            window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            window.Focus();
        }

        private ScreenshotAreaWindow(System.Drawing.Bitmap shot, System.Drawing.Rectangle screenRect, Action<System.Drawing.Bitmap?> done)
        {
            _shot = shot;
            _screenRect = screenRect;
            _done = done;

            Title = "PhasmaStrap - pick an area";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;
            Focusable = true;

            var grid = new Grid();
            grid.Children.Add(new Image { Source = ToImage(shot), Stretch = Stretch.Fill });
            grid.Children.Add(_canvas);
            _canvas.Background = Brushes.Transparent;
            _canvas.Children.Add(_shade);
            _canvas.Children.Add(_frame);
            _sizeTag.Child = _sizeText;
            _canvas.Children.Add(_sizeTag);

            var hint = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 18, 0, 0),
                Padding = new Thickness(14, 7, 14, 7),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x16, 0x17, 0x1B)),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 13,
                    Text = "Drag over the part you want  ·  Enter or double-click: whole window  ·  Esc: cancel",
                },
            };
            grid.Children.Add(hint);
            Content = grid;

            SourceInitialized += (_, _) =>
            {
                // exactly over the game, in real pixels whatever the display scaling
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, _screenRect.X, _screenRect.Y, _screenRect.Width, _screenRect.Height, 0x0040 /* SHOWWINDOW */);
            };

            Loaded += (_, _) => UpdateShade();
            SizeChanged += (_, _) => UpdateShade();

            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += (_, _) =>
            {
                // right-click drops the selection; with none, it cancels
                if (_selection.IsEmpty)
                    Finish(null);
                else
                    SetSelection(Rect.Empty);
            };
            KeyDown += OnKey;
            Deactivated += (_, _) => Finish(null);
        }

        private static BitmapSource ToImage(System.Drawing.Bitmap bitmap)
        {
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * bitmap.Height, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                FinishWhole();
                return;
            }

            _start = e.GetPosition(_canvas);
            SetSelection(new Rect(_start.Value, _start.Value));
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_start is Point start && e.LeftButton == MouseButtonState.Pressed)
                SetSelection(new Rect(start, e.GetPosition(_canvas)));
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_start is null)
                return;

            _start = null;
            ReleaseMouseCapture();

            // a click without a drag isn't a choice yet
            System.Drawing.Rectangle pixels = ToPixels(_selection);
            if (pixels.Width < 4 || pixels.Height < 4)
            {
                SetSelection(Rect.Empty);
                return;
            }

            Finish(_shot.Clone(pixels, _shot.PixelFormat));
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Finish(null);
            else if (e.Key == Key.Enter)
                FinishWhole();
        }

        private void FinishWhole() => Finish(_shot.Clone(new System.Drawing.Rectangle(0, 0, _shot.Width, _shot.Height), _shot.PixelFormat));

        private void Finish(System.Drawing.Bitmap? result)
        {
            if (_finished)
                return;
            _finished = true;

            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
                // already closing (Deactivated fires while the window closes)
            }

            try
            {
                _done(result);
            }
            finally
            {
                _shot.Dispose();
            }
        }

        private System.Drawing.Rectangle ToPixels(Rect rect)
        {
            if (rect.IsEmpty || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
                return System.Drawing.Rectangle.Empty;

            double sx = _shot.Width / _canvas.ActualWidth, sy = _shot.Height / _canvas.ActualHeight;
            int left = Math.Clamp((int)Math.Round(rect.Left * sx), 0, _shot.Width);
            int top = Math.Clamp((int)Math.Round(rect.Top * sy), 0, _shot.Height);
            int right = Math.Clamp((int)Math.Round(rect.Right * sx), 0, _shot.Width);
            int bottom = Math.Clamp((int)Math.Round(rect.Bottom * sy), 0, _shot.Height);
            return new System.Drawing.Rectangle(left, top, right - left, bottom - top);
        }

        private void SetSelection(Rect rect)
        {
            _selection = rect;
            UpdateShade();

            if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1)
            {
                _frame.Visibility = Visibility.Collapsed;
                _sizeTag.Visibility = Visibility.Collapsed;
                return;
            }

            _frame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_frame, rect.Left);
            Canvas.SetTop(_frame, rect.Top);
            _frame.Width = rect.Width;
            _frame.Height = rect.Height;

            System.Drawing.Rectangle pixels = ToPixels(rect);
            _sizeText.Text = $"{pixels.Width} × {pixels.Height}";
            _sizeTag.Visibility = Visibility.Visible;
            Canvas.SetLeft(_sizeTag, rect.Left);
            Canvas.SetTop(_sizeTag, rect.Bottom + 6 + 22 > _canvas.ActualHeight ? Math.Max(0, rect.Top - 28) : rect.Bottom + 6);
        }

        // everything but the selection is dimmed
        private void UpdateShade()
        {
            var all = new RectangleGeometry(new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight));
            _shade.Data = _selection.IsEmpty || _selection.Width < 1 || _selection.Height < 1
                ? all
                : new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(_selection));
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
