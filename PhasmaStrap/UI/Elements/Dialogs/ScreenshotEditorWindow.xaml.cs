using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// A small image editor for the Capture page's screenshots: crop, pen, arrow, box, ellipse,
    /// text and pixelate (to hide names/chat), with undo/redo, copy to clipboard, and save over
    /// the original or as a new file. Annotations are kept as vector shapes on a canvas above the
    /// bitmap until export, so everything stays editable through undo until you save.
    /// </summary>
    public partial class ScreenshotEditorWindow : WpfUiWindow
    {
        private const string LOG_IDENT = "ScreenshotEditorWindow";
        private const int MaxUndo = 40;

        private readonly string _path;
        private BitmapSource _bitmap = null!;
        private readonly List<Annotation> _annotations = new();
        private Stack<EditorState> _undo = new();
        private readonly Stack<EditorState> _redo = new();

        private string _tool = "Pen";
        private Color _color = (Color)ColorConverter.ConvertFromString("#FF3B30");
        private Point _dragStart;
        private bool _dragging;
        private Rect? _cropRect;
        private Point _textPosition;
        private UIElement? _previewShape;
        private readonly List<Point> _penPoints = new();
        private bool _suppressZoomEvent;

        public bool Saved { get; private set; }

        public ScreenshotEditorWindow(string path)
        {
            _path = path;

            InitializeComponent();

            RootTitleBar.Title = $"Edit screenshot - {System.IO.Path.GetFileName(path)}";
            LoadBitmap(path);
            SizeSlider.Value = Math.Clamp(Math.Round(_bitmap.PixelWidth / 320.0), 3, 12);
            SelectTool("Pen");
            HighlightSwatch();
            UpdateUndoButtons();

            Loaded += (_, _) => ZoomToFit();
            App.Logger.WriteLine(LOG_IDENT, $"Opened {path} ({_bitmap.PixelWidth}x{_bitmap.PixelHeight})");
        }

        // ---------------------------------------------------------------- state / rendering

        private sealed class EditorState
        {
            public BitmapSource Bitmap = null!;
            public List<Annotation> Annotations = new();
        }

        private abstract class Annotation
        {
            public Color Color;
            public double Thickness;

            public abstract UIElement Render(BitmapSource baseImage);

            public abstract Annotation Clone();

            public abstract void Offset(double dx, double dy);

            protected Brush Stroke => new SolidColorBrush(Color);
        }

        private sealed class PenAnnotation : Annotation
        {
            public List<Point> Points = new();

            public override UIElement Render(BitmapSource baseImage) => new Polyline
            {
                Points = new PointCollection(Points),
                Stroke = Stroke,
                StrokeThickness = Thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };

            public override Annotation Clone() => new PenAnnotation { Color = Color, Thickness = Thickness, Points = new List<Point>(Points) };

            public override void Offset(double dx, double dy)
            {
                for (int i = 0; i < Points.Count; i++)
                    Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy);
            }
        }

        private sealed class ArrowAnnotation : Annotation
        {
            public Point From, To;

            public override UIElement Render(BitmapSource baseImage)
            {
                var canvas = new Canvas();
                canvas.Children.Add(new Line { X1 = From.X, Y1 = From.Y, X2 = To.X, Y2 = To.Y, Stroke = Stroke, StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round });

                Vector dir = To - From;
                if (dir.Length > 0.01)
                {
                    dir.Normalize();
                    double head = Math.Max(18, Thickness * 5);
                    Vector perp = new Vector(-dir.Y, dir.X);
                    Point tip = To;
                    Point left = To - dir * head + perp * head * 0.5;
                    Point right = To - dir * head - perp * head * 0.5;
                    canvas.Children.Add(new Polygon { Points = new PointCollection { tip, left, right }, Fill = Stroke });
                }

                return canvas;
            }

            public override Annotation Clone() => new ArrowAnnotation { Color = Color, Thickness = Thickness, From = From, To = To };

            public override void Offset(double dx, double dy)
            {
                From = new Point(From.X + dx, From.Y + dy);
                To = new Point(To.X + dx, To.Y + dy);
            }
        }

        private sealed class ShapeAnnotation : Annotation
        {
            public Rect Bounds;
            public bool Ellipse;

            public override UIElement Render(BitmapSource baseImage)
            {
                Shape shape = Ellipse ? new Ellipse() : new Rectangle { RadiusX = 2, RadiusY = 2 };
                shape.Stroke = Stroke;
                shape.StrokeThickness = Thickness;
                shape.Width = Bounds.Width;
                shape.Height = Bounds.Height;
                Canvas.SetLeft(shape, Bounds.X);
                Canvas.SetTop(shape, Bounds.Y);
                return shape;
            }

            public override Annotation Clone() => new ShapeAnnotation { Color = Color, Thickness = Thickness, Bounds = Bounds, Ellipse = Ellipse };

            public override void Offset(double dx, double dy) => Bounds = new Rect(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height);
        }

        private sealed class TextAnnotation : Annotation
        {
            public Point Position;
            public string Text = "";
            public double FontSize;

            public override UIElement Render(BitmapSource baseImage)
            {
                var block = new TextBlock
                {
                    Text = Text,
                    FontSize = FontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Stroke,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9, Color = Colors.Black },
                };
                Canvas.SetLeft(block, Position.X);
                Canvas.SetTop(block, Position.Y);
                return block;
            }

            public override Annotation Clone() => new TextAnnotation { Color = Color, Thickness = Thickness, Position = Position, Text = Text, FontSize = FontSize };

            public override void Offset(double dx, double dy) => Position = new Point(Position.X + dx, Position.Y + dy);
        }

        private sealed class PixelateAnnotation : Annotation
        {
            public Rect Bounds;

            public override UIElement Render(BitmapSource baseImage)
            {
                var bounds = Rect.Intersect(Bounds, new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
                if (bounds.Width < 2 || bounds.Height < 2)
                    return new Canvas();

                var crop = new CroppedBitmap(baseImage, new Int32Rect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
                int block = Math.Max(6, (int)Math.Max(bounds.Width, bounds.Height) / 18);
                double scale = 1.0 / block;
                var small = new TransformedBitmap(crop, new ScaleTransform(scale, scale));
                small.Freeze();

                var image = new Image
                {
                    Source = small,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Stretch = Stretch.Fill,
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, bounds.X);
                Canvas.SetTop(image, bounds.Y);
                return image;
            }

            public override Annotation Clone() => new PixelateAnnotation { Color = Color, Thickness = Thickness, Bounds = Bounds };

            public override void Offset(double dx, double dy) => Bounds = new Rect(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height);
        }

        private void LoadBitmap(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            // normalise to a plain 96-dpi Pbgra32 bitmap so pixel coordinates == layout coordinates
            _bitmap = NormalizeDpi(image);
            ApplyBitmap();
        }

        private static BitmapSource NormalizeDpi(BitmapSource source)
        {
            if (Math.Abs(source.DpiX - 96) < 0.01 && Math.Abs(source.DpiY - 96) < 0.01 && source.Format == PixelFormats.Pbgra32)
                return source;

            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        private void ApplyBitmap()
        {
            BaseImage.Source = _bitmap;
            Surface.Width = _bitmap.PixelWidth;
            Surface.Height = _bitmap.PixelHeight;
            Overlay.Width = Preview.Width = TextLayer.Width = _bitmap.PixelWidth;
            Overlay.Height = Preview.Height = TextLayer.Height = _bitmap.PixelHeight;
            RebuildOverlay();
            UpdateStatus();
        }

        private void RebuildOverlay()
        {
            Overlay.Children.Clear();
            foreach (Annotation annotation in _annotations)
                Overlay.Children.Add(annotation.Render(_bitmap));
        }

        private void UpdateStatus(string? extra = null)
        {
            StatusText.Text = $"{_bitmap.PixelWidth} × {_bitmap.PixelHeight}  ·  {_annotations.Count} edit(s)" + (extra is null ? "" : $"  ·  {extra}");
        }

        private EditorState Snapshot() => new EditorState { Bitmap = _bitmap, Annotations = _annotations.Select(a => a.Clone()).ToList() };

        private void PushUndo()
        {
            _undo.Push(Snapshot());
            if (_undo.Count > MaxUndo)
                _undo = TrimStack(_undo);
            _redo.Clear();
            UpdateUndoButtons();
        }

        private static Stack<EditorState> TrimStack(Stack<EditorState> stack)
        {
            var items = stack.ToArray(); // top first
            var trimmed = new Stack<EditorState>();
            for (int i = Math.Min(items.Length, MaxUndo) - 1; i >= 0; i--)
                trimmed.Push(items[i]);
            return trimmed;
        }

        private void Restore(EditorState state)
        {
            _bitmap = state.Bitmap;
            _annotations.Clear();
            _annotations.AddRange(state.Annotations.Select(a => a.Clone()));
            ApplyBitmap();
        }

        private void UpdateUndoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        // ---------------------------------------------------------------- tools

        private void SelectTool(string tool)
        {
            CommitText();
            _tool = tool;
            App.Logger.WriteLine(LOG_IDENT, $"Tool -> {tool}");

            foreach (var button in new[] { ToolCrop, ToolPen, ToolArrow, ToolRect, ToolEllipse, ToolText, ToolPixelate })
                button.Appearance = (string)button.Tag == tool ? Wpf.Ui.Common.ControlAppearance.Primary : Wpf.Ui.Common.ControlAppearance.Secondary;

            if (tool != "Crop")
                ClearCrop();

            Overlay.Cursor = tool == "Text" ? Cursors.IBeam : Cursors.Cross;
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tool)
                SelectTool(tool);
        }

        private void Swatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string hex)
            {
                _color = (Color)ColorConverter.ConvertFromString(hex);
                HighlightSwatch();
            }
        }

        private void HighlightSwatch()
        {
            foreach (Border swatch in SwatchPanel.Children.OfType<Border>())
            {
                bool active = swatch.Tag is string hex && (Color)ColorConverter.ConvertFromString(hex) == _color;
                swatch.BorderBrush = active ? (Brush)(TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White) : Brushes.Transparent;
            }
        }

        private double Thickness => Math.Max(1, SizeSlider.Value);

        private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _bitmap.PixelWidth), Math.Clamp(p.Y, 0, _bitmap.PixelHeight));

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CommitText();

            Point p = Clamp(e.GetPosition(Overlay));
            _dragStart = p;

            if (_tool == "Text")
            {
                _textPosition = p;
                BeginText(p);
                return;
            }

            _dragging = true;
            Overlay.CaptureMouse();
            _penPoints.Clear();
            _penPoints.Add(p);
            Preview.Children.Clear();
            _previewShape = null;

            if (_tool == "Crop")
                ClearCrop(keepPreview: false);
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;

            Point p = Clamp(e.GetPosition(Overlay));

            switch (_tool)
            {
                case "Pen":
                    _penPoints.Add(p);
                    ShowPreview(new PenAnnotation { Color = _color, Thickness = Thickness, Points = new List<Point>(_penPoints) });
                    break;
                case "Arrow":
                    ShowPreview(new ArrowAnnotation { Color = _color, Thickness = Thickness, From = _dragStart, To = p });
                    break;
                case "Rect":
                case "Ellipse":
                    ShowPreview(new ShapeAnnotation { Color = _color, Thickness = Thickness, Bounds = RectFrom(_dragStart, p), Ellipse = _tool == "Ellipse" });
                    break;
                case "Pixelate":
                    ShowPreview(new PixelateAnnotation { Bounds = RectFrom(_dragStart, p) });
                    break;
                case "Crop":
                    ShowCropPreview(RectFrom(_dragStart, p));
                    break;
            }
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;

            _dragging = false;
            Overlay.ReleaseMouseCapture();
            Point p = Clamp(e.GetPosition(Overlay));

            Annotation? result = _tool switch
            {
                "Pen" when _penPoints.Count > 1 => new PenAnnotation { Color = _color, Thickness = Thickness, Points = new List<Point>(_penPoints) },
                "Arrow" when (p - _dragStart).Length > 3 => new ArrowAnnotation { Color = _color, Thickness = Thickness, From = _dragStart, To = p },
                "Rect" or "Ellipse" when RectFrom(_dragStart, p) is { Width: > 2, Height: > 2 } r => new ShapeAnnotation { Color = _color, Thickness = Thickness, Bounds = r, Ellipse = _tool == "Ellipse" },
                "Pixelate" when RectFrom(_dragStart, p) is { Width: > 2, Height: > 2 } r => new PixelateAnnotation { Bounds = r },
                _ => null,
            };

            if (_tool == "Crop")
            {
                Rect r = RectFrom(_dragStart, p);
                if (r.Width > 4 && r.Height > 4)
                {
                    _cropRect = r;
                    ShowCropPreview(r);
                    ApplyCropButton.Visibility = Visibility.Visible;
                    UpdateStatus($"crop {(int)r.Width} × {(int)r.Height} - press Apply crop or Enter");
                }
                else
                {
                    ClearCrop();
                }
                return;
            }

            Preview.Children.Clear();
            _previewShape = null;

            if (result is null)
                return;

            PushUndo();
            _annotations.Add(result);
            Overlay.Children.Add(result.Render(_bitmap));
            App.Logger.WriteLine(LOG_IDENT, $"Added {result.GetType().Name} start=({_dragStart.X:0},{_dragStart.Y:0}) end=({p.X:0},{p.Y:0}) total={_annotations.Count}");
            UpdateStatus();
        }

        private static Rect RectFrom(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private void ShowPreview(Annotation annotation)
        {
            Preview.Children.Clear();
            _previewShape = annotation.Render(_bitmap);
            Preview.Children.Add(_previewShape);
        }

        // ---------------------------------------------------------------- crop

        private void ShowCropPreview(Rect rect)
        {
            Preview.Children.Clear();

            var dim = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight)),
                    new RectangleGeometry(rect)),
            };
            Preview.Children.Add(dim);

            var outline = new Rectangle { Width = rect.Width, Height = rect.Height, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } };
            Canvas.SetLeft(outline, rect.X);
            Canvas.SetTop(outline, rect.Y);
            Preview.Children.Add(outline);
        }

        private void ClearCrop(bool keepPreview = false)
        {
            _cropRect = null;
            ApplyCropButton.Visibility = Visibility.Collapsed;
            if (!keepPreview)
                Preview.Children.Clear();
        }

        private void ApplyCrop_Click(object sender, RoutedEventArgs e) => ApplyCrop();

        private void ApplyCrop()
        {
            if (_cropRect is not Rect rect)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"ApplyCrop {rect}");

            var pixelRect = new Int32Rect((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
            pixelRect.Width = Math.Clamp(pixelRect.Width, 1, _bitmap.PixelWidth - pixelRect.X);
            pixelRect.Height = Math.Clamp(pixelRect.Height, 1, _bitmap.PixelHeight - pixelRect.Y);

            PushUndo();

            var cropped = new CroppedBitmap(_bitmap, pixelRect);
            cropped.Freeze();
            _bitmap = NormalizeDpi(cropped);

            foreach (Annotation annotation in _annotations)
                annotation.Offset(-pixelRect.X, -pixelRect.Y);

            ClearCrop();
            ApplyBitmap();
            ZoomToFit();
        }

        // ---------------------------------------------------------------- text

        private void BeginText(Point p)
        {
            TextInput.Text = "";
            TextInput.FontSize = TextFontSize;
            TextInput.Foreground = new SolidColorBrush(_color);
            Canvas.SetLeft(TextInput, p.X);
            Canvas.SetTop(TextInput, p.Y);
            TextLayer.IsHitTestVisible = true;
            TextInput.Visibility = Visibility.Visible;

            // the box has only just become visible; focus it once layout has run so the caret actually lands in it
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TextInput.Visibility == Visibility.Visible)
                    Keyboard.Focus(TextInput);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // scale with the image so text is legible on a 1080p/1440p capture, not just a small crop
        private double TextFontSize => 10 + Thickness * 3 + _bitmap.PixelWidth / 160.0;

        private void CommitText()
        {
            if (TextInput.Visibility != Visibility.Visible)
                return;

            string text = TextInput.Text.Trim();
            TextInput.Visibility = Visibility.Collapsed;
            TextLayer.IsHitTestVisible = false;
            App.Logger.WriteLine(LOG_IDENT, $"CommitText \"{text}\"");

            if (text.Length == 0)
                return;

            PushUndo();
            var annotation = new TextAnnotation { Color = _color, Thickness = Thickness, Position = _textPosition, Text = text, FontSize = TextFontSize };
            _annotations.Add(annotation);
            Overlay.Children.Add(annotation.Render(_bitmap));
            UpdateStatus();
        }

        private void TextInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                TextInput.Text = "";
                CommitText();
                e.Handled = true;
            }
        }

        private void TextInput_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitText();

        // ---------------------------------------------------------------- undo / redo

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            CommitText();
            App.Logger.WriteLine(LOG_IDENT, $"Undo (stack={_undo.Count})");
            if (_undo.Count == 0)
                return;

            _redo.Push(Snapshot());
            Restore(_undo.Pop());
            ClearCrop();
            UpdateUndoButtons();
        }

        private void Redo()
        {
            App.Logger.WriteLine(LOG_IDENT, $"Redo (stack={_redo.Count})");
            if (_redo.Count == 0)
                return;

            _undo.Push(Snapshot());
            Restore(_redo.Pop());
            ClearCrop();
            UpdateUndoButtons();
        }

        // ---------------------------------------------------------------- zoom

        private void ZoomToFit()
        {
            double availableW = Math.Max(100, Scroller.ViewportWidth - 40);
            double availableH = Math.Max(100, Scroller.ViewportHeight - 40);
            double fit = Math.Min(availableW / _bitmap.PixelWidth, availableH / _bitmap.PixelHeight);
            SetZoom(Math.Clamp(fit, ZoomSlider.Minimum, ZoomSlider.Maximum));
        }

        private void SetZoom(double zoom)
        {
            _suppressZoomEvent = true;
            ZoomSlider.Value = zoom;
            _suppressZoomEvent = false;
            Surface.LayoutTransform = new ScaleTransform(zoom, zoom);
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressZoomEvent || Surface is null)
                return;

            Surface.LayoutTransform = new ScaleTransform(e.NewValue, e.NewValue);
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomToFit();

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            SetZoom(Math.Clamp(ZoomSlider.Value * factor, ZoomSlider.Minimum, ZoomSlider.Maximum));
            e.Handled = true;
        }

        // ---------------------------------------------------------------- export

        private BitmapSource RenderResult()
        {
            CommitText();
            Preview.Children.Clear();
            TextInput.Visibility = Visibility.Collapsed;

            // render the surface at its natural pixel size, ignoring the on-screen zoom
            Transform? zoom = Surface.LayoutTransform;
            Surface.LayoutTransform = Transform.Identity;
            Surface.Measure(new Size(_bitmap.PixelWidth, _bitmap.PixelHeight));
            Surface.Arrange(new Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight));
            Surface.UpdateLayout();

            // through a VisualBrush: rendering Surface itself would include where it sits in the
            // window (it's centred when the picture is smaller than the view), shifting the result
            // off the bitmap - a small or cropped screenshot came out all black
            var sheet = new DrawingVisual();
            using (DrawingContext dc = sheet.RenderOpen())
                dc.DrawRectangle(new VisualBrush(Surface) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight));

            var target = new RenderTargetBitmap(_bitmap.PixelWidth, _bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(sheet);
            target.Freeze();

            Surface.LayoutTransform = zoom;
            Surface.InvalidateMeasure();
            Surface.UpdateLayout();

            return target;
        }

        private static void WritePng(BitmapSource image, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private void MakeGif_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var maker = new GifMakerWindow(RenderResult(), System.IO.Path.GetFileNameWithoutExtension(_path)) { Owner = this };
                maker.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                UpdateStatus($"couldn't open the GIF maker: {ex.Message}");
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetImage(RenderResult());
                UpdateStatus("copied to clipboard");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Copy failed: {ex.Message}");
                UpdateStatus($"copy failed: {ex.Message}");
            }
        }

        private void SaveCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(_path)!;
                string stem = System.IO.Path.GetFileNameWithoutExtension(_path);
                string target;
                int n = 1;
                do
                {
                    target = System.IO.Path.Combine(directory, $"{stem}_edited{(n == 1 ? "" : n.ToString())}.png");
                    n++;
                }
                while (File.Exists(target));

                WritePng(RenderResult(), target);
                Saved = true;
                UpdateStatus($"saved {System.IO.Path.GetFileName(target)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save copy failed: {ex.Message}");
                UpdateStatus($"save failed: {ex.Message}");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BitmapSource result = RenderResult();
                string temp = _path + ".tmp";
                WritePng(result, temp);
                File.Move(temp, _path, true);
                Saved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save failed: {ex.Message}");
                UpdateStatus($"save failed: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Saved;
            Close();
        }

        // ---------------------------------------------------------------- keyboard

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TextInput.Visibility == Visibility.Visible && TextInput.IsKeyboardFocusWithin)
                return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.C) { Copy_Click(sender, e); e.Handled = true; return; }
            if (ctrl && e.Key == Key.S) { Save_Click(sender, e); e.Handled = true; return; }

            if (e.Key == Key.Enter && _cropRect is not null) { ApplyCrop(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { ClearCrop(); Preview.Children.Clear(); e.Handled = true; return; }

            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            string? tool = e.Key switch
            {
                Key.C => "Crop",
                Key.P => "Pen",
                Key.A => "Arrow",
                Key.R => "Rect",
                Key.E => "Ellipse",
                Key.T => "Text",
                Key.X => "Pixelate",
                _ => null,
            };

            if (tool is not null)
            {
                SelectTool(tool);
                e.Handled = true;
            }
        }
    }
}
