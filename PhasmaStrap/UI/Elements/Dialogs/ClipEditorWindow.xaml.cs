using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class ClipEditorWindow : WpfUiWindow
    {
        private const string LOG_IDENT = "ClipEditorWindow";
        private const double TimelinePad = 8;
        private static readonly TimeSpan MinSelection = TimeSpan.FromSeconds(0.2);

        private enum DragTarget { None, Start, End, Playhead }

        private readonly string _path;
        private readonly DispatcherTimer _timer;

        private ClipInfo? _info;
        private TimeSpan _duration;
        private TimeSpan _start;
        private TimeSpan _end;
        private TimeSpan _stillPosition;
        private double _speed = 1.0;
        private bool _playing;
        private bool _stillMode;
        private bool _busy;
        private bool _opened;

        private DragTarget _drag;
        private bool _cropMode;
        private bool _cropDragging;
        private Point _cropStart;
        private Rect? _crop;

        private CancellationTokenSource? _thumbCancel;
        private Task _thumbTask = Task.CompletedTask;
        private CancellationTokenSource? _stillCancel;

        public bool Saved { get; private set; }

        public ClipEditorWindow(string path)
        {
            _path = path;
            ClipProcessor.Log ??= message => App.Logger.WriteLine("ClipProcessor", message);

            InitializeComponent();

            RootTitleBar.Title = $"Edit clip - {System.IO.Path.GetFileName(path)}";

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();

            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            UpdateStatus("Loading clip...");

            try
            {
                _info = await Task.Run(() => ClipProcessor.Probe(_path));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Probe failed: {ex.Message}");
                UpdateStatus($"This clip can't be read: {ex.Message}");
                Timeline.IsEnabled = false;
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Opened {_path} ({_info.Width}x{_info.Height}, {_info.Fps:0.##}fps, {_info.Duration.TotalSeconds:0.00}s)");

            Surface.Width = _info.Width;
            Surface.Height = _info.Height;
            CropCanvas.Width = _info.Width;
            CropCanvas.Height = _info.Height;

            _duration = _info.Duration;
            _start = TimeSpan.Zero;
            _end = _duration;
            DurationText.Text = Format(_duration);

            OpenPlayer();
            LayoutTimeline();
            UpdateStatus();
            StartThumbnails();
        }

        private void OpenPlayer()
        {
            _opened = false;

            if (_stillMode)
            {
                ShowStill(_start);
                return;
            }

            Player.Source = new Uri(_path, UriKind.Absolute);

            Player.Play();
            Player.Pause();
            _timer.Start();
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            _opened = true;
            Player.Position = _start;
            Player.SpeedRatio = _speed;

            if (!_playing)
                Player.Pause();
        }

        private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            App.Logger.WriteLine(LOG_IDENT, $"MediaElement can't play this clip ({e.ErrorException?.Message}) - falling back to still frames");

            _stillMode = true;
            _playing = false;
            Player.Visibility = Visibility.Collapsed;
            StillImage.Visibility = Visibility.Visible;
            PlayButton.IsEnabled = false;
            PlayButton.ToolTip = "Windows can't play MP4 video here (the Media Feature Pack is missing), so there is no live preview. Trimming, cropping and saving still work.";
            VideoMessage.Text = "No live playback on this system - showing still frames";
            VideoMessage.Visibility = Visibility.Visible;

            ShowStill(_start);
        }

        private void StartThumbnails()
        {
            _thumbCancel?.Cancel();
            _thumbCancel = new CancellationTokenSource();
            CancellationToken token = _thumbCancel.Token;

            int count = Math.Clamp((int)(Timeline.ActualWidth / 90), 6, 16);
            ThumbStrip.Children.Clear();

            _thumbTask = Task.Run(() =>
            {
                try
                {
                    var thumbs = ClipProcessor.GrabThumbnails(_path, count, 200, token);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        ThumbStrip.Children.Clear();
                        foreach (var thumb in thumbs)
                        {
                            var source = BitmapSource.Create(thumb.Width, thumb.Height, 96, 96, PixelFormats.Bgr32, null, thumb.Bgra, thumb.Width * 4);
                            source.Freeze();
                            ThumbStrip.Children.Add(new Image { Source = source, Stretch = Stretch.UniformToFill });
                        }
                    }));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Thumbnails failed: {ex.Message}");
                }
            });
        }

        private TimeSpan Position => _stillMode || !_opened ? _stillPosition : Player.Position;

        private TimeSpan FrameStep => TimeSpan.FromSeconds(1.0 / Math.Max(1, _info?.Fps ?? 12));

        private void Seek(TimeSpan position)
        {
            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, _duration.Ticks));
            _stillPosition = position;

            if (_stillMode)
                ShowStill(position);
            else if (_opened)
                Player.Position = position;

            UpdatePlayhead();
        }

        private void ShowStill(TimeSpan position)
        {
            _stillCancel?.Cancel();
            _stillCancel = new CancellationTokenSource();
            CancellationToken token = _stillCancel.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(60, token);
                    byte[]? frame = ClipProcessor.GrabFrame(_path, position, out int w, out int h);
                    if (frame is null || token.IsCancellationRequested)
                        return;

                    var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, frame, w * 4);
                    source.Freeze();
                    _ = Dispatcher.BeginInvoke(new Action(() => { if (!token.IsCancellationRequested) StillImage.Source = source; }));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Still frame failed: {ex.Message}");
                }
            });
        }

        private void Tick()
        {
            if (_stillMode || !_opened)
                return;

            if (_playing && Player.Position >= _end)
                Player.Position = _start;

            if (_drag == DragTarget.None)
                UpdatePlayhead();
        }

        private void SetPlaying(bool playing)
        {
            if (_stillMode || !_opened || _busy)
                playing = false;

            _playing = playing;

            if (!_stillMode && _opened)
            {
                if (playing)
                {
                    if (Player.Position < _start || Player.Position >= _end - FrameStep)
                        Player.Position = _start;

                    Player.SpeedRatio = _speed;
                    Player.Play();
                }
                else
                {
                    Player.Pause();
                }
            }

            PlayButton.Icon = playing ? Wpf.Ui.Common.SymbolRegular.Pause24 : Wpf.Ui.Common.SymbolRegular.Play24;
            PlayButton.Content = playing ? "Pause" : "Play";
        }

        private void Play_Click(object sender, RoutedEventArgs e) => SetPlaying(!_playing);

        private void StepBack_Click(object sender, RoutedEventArgs e) { SetPlaying(false); Seek(Position - FrameStep); }

        private void StepForward_Click(object sender, RoutedEventArgs e) { SetPlaying(false); Seek(Position + FrameStep); }

        private void SpeedBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedBox.SelectedItem is ComboBoxItem { Tag: string tag } && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
            {
                _speed = speed;

                if (!_stillMode && _opened)
                {
                    Player.SpeedRatio = speed;

                    if (!_playing)
                        Player.Pause();
                }

                if (IsLoaded)
                    UpdateStatus();
            }
        }

        private void SetStart(TimeSpan value)
        {
            _start = TimeSpan.FromTicks(Math.Clamp(value.Ticks, 0, Math.Max(0, (_end - MinSelection).Ticks)));
            LayoutTimeline();
            UpdateStatus();
        }

        private void SetEnd(TimeSpan value)
        {
            _end = TimeSpan.FromTicks(Math.Clamp(value.Ticks, Math.Min(_duration.Ticks, (_start + MinSelection).Ticks), _duration.Ticks));
            LayoutTimeline();
            UpdateStatus();
        }

        private void SetStart_Click(object sender, RoutedEventArgs e) => SetStart(Position);

        private void SetEnd_Click(object sender, RoutedEventArgs e) => SetEnd(Position);

        private void ResetTrim_Click(object sender, RoutedEventArgs e)
        {
            _start = TimeSpan.Zero;
            _end = _duration;
            LayoutTimeline();
            UpdateStatus();
        }

        private double TrackWidth => Math.Max(1, Timeline.ActualWidth - TimelinePad * 2);

        private double TimeToX(TimeSpan time) => _duration.Ticks <= 0 ? TimelinePad : TimelinePad + (double)time.Ticks / _duration.Ticks * TrackWidth;

        private TimeSpan XToTime(double x)
        {
            double fraction = Math.Clamp((x - TimelinePad) / TrackWidth, 0, 1);
            return TimeSpan.FromTicks((long)(fraction * _duration.Ticks));
        }

        private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutTimeline();

        private void LayoutTimeline()
        {
            double height = Timeline.ActualHeight;
            if (height <= 0)
                return;

            const double top = 6;
            double trackHeight = height - top * 2;
            double startX = TimeToX(_start), endX = TimeToX(_end);

            Place(DimLeft, TimelinePad, top, Math.Max(0, startX - TimelinePad), trackHeight);
            Place(DimRight, endX, top, Math.Max(0, TimelinePad + TrackWidth - endX), trackHeight);
            Place(RangeOutline, startX, top - 1, Math.Max(0, endX - startX), trackHeight + 2);
            Place(StartHandle, startX - StartHandle.Width, top - 3, StartHandle.Width, trackHeight + 6);
            Place(EndHandle, endX, top - 3, EndHandle.Width, trackHeight + 6);

            Playhead.Height = height;
            UpdatePlayhead();
        }

        private static void Place(FrameworkElement element, double x, double y, double width, double height)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            element.Width = width;
            element.Height = height;
        }

        private void UpdatePlayhead()
        {
            TimeSpan position = Position;
            Canvas.SetLeft(Playhead, TimeToX(position) - 1);
            PositionText.Text = Format(position);
        }

        private void Timeline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_busy || _duration <= TimeSpan.Zero)
                return;

            double x = e.GetPosition(Timeline).X;
            double startX = TimeToX(_start), endX = TimeToX(_end);

            if (x >= startX - 14 && x <= startX + 4)
                _drag = DragTarget.Start;
            else if (x >= endX - 4 && x <= endX + 14)
                _drag = DragTarget.End;
            else
                _drag = DragTarget.Playhead;

            SetPlaying(false);
            Timeline.CaptureMouse();
            ApplyDrag(x);
        }

        private void Timeline_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag != DragTarget.None)
                ApplyDrag(e.GetPosition(Timeline).X);
        }

        private void Timeline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag == DragTarget.None)
                return;

            _drag = DragTarget.None;
            Timeline.ReleaseMouseCapture();
        }

        private void ApplyDrag(double x)
        {
            TimeSpan time = XToTime(x);

            switch (_drag)
            {
                case DragTarget.Start:
                    SetStart(time);
                    Seek(_start);
                    break;
                case DragTarget.End:
                    SetEnd(time);
                    Seek(_end);
                    break;
                case DragTarget.Playhead:
                    Seek(time);
                    break;
            }
        }

        private void Crop_Click(object sender, RoutedEventArgs e) => SetCropMode(!_cropMode);

        private void SetCropMode(bool enabled)
        {
            _cropMode = enabled;
            CropButton.Appearance = enabled ? Wpf.Ui.Common.ControlAppearance.Primary : Wpf.Ui.Common.ControlAppearance.Secondary;
            CropCanvas.Cursor = enabled ? Cursors.Cross : Cursors.Hand;

            if (enabled)
                SetPlaying(false);

            UpdateStatus(enabled ? "drag a rectangle over the video" : null);
        }

        private void ClearCrop_Click(object sender, RoutedEventArgs e)
        {
            _crop = null;
            DrawCrop();
            UpdateStatus();
        }

        private Point ClampToVideo(Point p) => new(Math.Clamp(p.X, 0, CropCanvas.Width), Math.Clamp(p.Y, 0, CropCanvas.Height));

        private void CropCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_busy)
                return;

            if (!_cropMode)
            {
                SetPlaying(!_playing);
                return;
            }

            _cropStart = ClampToVideo(e.GetPosition(CropCanvas));
            _cropDragging = true;
            CropCanvas.CaptureMouse();
        }

        private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_cropDragging)
                return;

            _crop = new Rect(_cropStart, ClampToVideo(e.GetPosition(CropCanvas)));
            DrawCrop();
        }

        private void CropCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_cropDragging)
                return;

            _cropDragging = false;
            CropCanvas.ReleaseMouseCapture();

            if (_crop is Rect rect && (rect.Width < 64 || rect.Height < 64))
            {
                _crop = null;
                DrawCrop();
                SetCropMode(true);
                UpdateStatus("that area is too small - a crop has to be at least 64 x 64 pixels");
                return;
            }

            DrawCrop();
            SetCropMode(false);
        }

        private void DrawCrop()
        {
            CropCanvas.Children.Clear();
            ClearCropButton.Visibility = _crop is null ? Visibility.Collapsed : Visibility.Visible;

            if (_crop is not Rect rect)
                return;

            CropCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, CropCanvas.Width, CropCanvas.Height)),
                    new RectangleGeometry(rect)),
                IsHitTestVisible = false,
            });

            double stroke = Math.Max(2, CropCanvas.Width / 400);
            var outline = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Stroke = Brushes.White,
                StrokeThickness = stroke,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(outline, rect.X);
            Canvas.SetTop(outline, rect.Y);
            CropCanvas.Children.Add(outline);
        }

        private static string Format(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";

        private bool HasChanges => _start > TimeSpan.Zero || _end < _duration || _crop is not null || Math.Abs(_speed - 1.0) > 0.001;

        private void UpdateStatus(string? extra = null)
        {
            if (_info is null)
            {
                StatusText.Text = extra ?? "";
                return;
            }

            TimeSpan selection = _end - _start;
            string size = _crop is Rect rect ? $"{_info.Width} × {_info.Height} → {(int)Math.Round(rect.Width) & ~1} × {(int)Math.Round(rect.Height) & ~1}" : $"{_info.Width} × {_info.Height}";

            string speed = Math.Abs(_speed - 1.0) > 0.001 ? $"  ·  {_speed:0.##}× → {selection.TotalSeconds / _speed:0.0}s saved, without sound" : "";

            StatusText.Text = $"Selected {selection.TotalSeconds:0.0}s of {_duration.TotalSeconds:0.0}s  ·  {size}{speed}" + (extra is null ? "" : $"  ·  {extra}");
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            FooterButtons.IsEnabled = !busy;
            GifTools.IsEnabled = !busy;
            Timeline.IsEnabled = !busy;
            ExportProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ExportProgress.Value = 0;

            if (busy)
                SetPlaying(false);
        }

        private ClipEditOptions BuildOptions()
        {
            var options = new ClipEditOptions
            {
                Start = _start,

                End = _end >= _duration ? TimeSpan.MaxValue : _end,
                Speed = _speed,
            };

            if (_crop is Rect rect)
            {
                options.CropX = (int)Math.Round(rect.X);
                options.CropY = (int)Math.Round(rect.Y);
                options.CropWidth = (int)Math.Round(rect.Width);
                options.CropHeight = (int)Math.Round(rect.Height);
            }

            return options;
        }

        private string UniqueCopyPath()
        {
            string directory = System.IO.Path.GetDirectoryName(_path)!;
            string stem = System.IO.Path.GetFileNameWithoutExtension(_path);

            for (int n = 1; ; n++)
            {
                string candidate = System.IO.Path.Combine(directory, $"{stem}_edited{(n == 1 ? "" : n.ToString())}.mp4");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        private async void SaveCopy_Click(object sender, RoutedEventArgs e) => await ExportAsync(overwrite: false);

        private async void Save_Click(object sender, RoutedEventArgs e) => await ExportAsync(overwrite: true);

        private async Task ExportAsync(bool overwrite)
        {
            if (_busy || _info is null)
                return;

            if (!HasChanges)
            {
                UpdateStatus("nothing to save yet - trim, crop or change the speed first");
                return;
            }

            ClipEditOptions options = BuildOptions();
            string destination = overwrite ? _path + ".editing.mp4" : UniqueCopyPath();

            SetBusy(true);
            UpdateStatus("saving...");

            try
            {
                if (overwrite)
                {
                    _thumbCancel?.Cancel();
                    _stillCancel?.Cancel();
                    await _thumbTask;

                    _timer.Stop();
                    _opened = false;
                    Player.Stop();
                    Player.Close();
                    Player.Source = null;
                }

                await Task.Run(() => ClipProcessor.Export(_path, destination, options,
                    progress => Dispatcher.BeginInvoke(new Action(() => ExportProgress.Value = progress))));

                if (overwrite)
                {
                    await ReplaceOriginalAsync(destination);
                    Saved = true;
                    App.Logger.WriteLine(LOG_IDENT, $"Replaced {_path}");

                    _crop = null;
                    DrawCrop();
                    SpeedBox.SelectedIndex = 2;
                    SetBusy(false);
                    await LoadAsync();
                    UpdateStatus("saved");
                    return;
                }

                Saved = true;
                App.Logger.WriteLine(LOG_IDENT, $"Saved copy {destination}");
                SetBusy(false);
                UpdateStatus($"saved {System.IO.Path.GetFileName(destination)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Export failed: {ex}");

                try { if (overwrite && File.Exists(destination)) File.Delete(destination); } catch { }

                SetBusy(false);

                if (overwrite && !_opened)
                    OpenPlayer();

                UpdateStatus($"saving failed: {ex.Message}");
            }
        }

        private async Task ReplaceOriginalAsync(string edited)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(edited, _path, true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    await Task.Delay(150);
                }
            }
        }

        private static int TagOf(ComboBox box, int fallback) =>
            box.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int value) ? value : fallback;

        private async void ExportGif_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _info is null)
                return;

            ClipEditOptions options = BuildOptions();
            var gif = new GifExportOptions
            {
                MaxWidth = TagOf(GifWidthBox, 640),
                Fps = TagOf(GifFpsBox, 15),
                Dither = GifDitherBox.IsChecked == true,
            };

            string directory = System.IO.Path.GetDirectoryName(_path)!;
            string stem = System.IO.Path.GetFileNameWithoutExtension(_path);
            string destination = System.IO.Path.Combine(directory, stem + ".gif");
            for (int n = 2; File.Exists(destination); n++)
                destination = System.IO.Path.Combine(directory, $"{stem}_{n}.gif");

            SetBusy(true);
            UpdateStatus("making the GIF...");

            try
            {
                await Task.Run(() => ClipProcessor.ExportGif(_path, destination, options, gif,
                    progress => Dispatcher.BeginInvoke(new Action(() => ExportProgress.Value = progress))));

                double megabytes = new FileInfo(destination).Length / 1048576.0;

                ClipboardShare.Log ??= message => App.Logger.WriteLine("ClipboardShare", message);
                bool copied = ClipboardShare.CopyFile(destination);

                Saved = true;
                App.Logger.WriteLine(LOG_IDENT, $"Exported {destination} ({megabytes:0.0} MB)");
                SetBusy(false);

                string note = megabytes > 10 ? " - over Discord's 10 MB limit; try a smaller width, fewer fps or a shorter trim" : "";
                UpdateStatus($"saved {System.IO.Path.GetFileName(destination)} ({megabytes:0.0} MB){(copied ? ", copied - paste it with Ctrl+V" : "")}{note}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GIF export failed: {ex}");
                SetBusy(false);
                UpdateStatus($"couldn't make the GIF: {ex.Message}");
            }
        }

        private async void SaveFrame_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _info is null)
                return;

            SetPlaying(false);
            TimeSpan position = Position;

            try
            {
                string path = await Task.Run(() =>
                {
                    byte[]? frame = ClipProcessor.GrabFrame(_path, position, out int w, out int h)
                        ?? throw new InvalidOperationException("there is no frame at this position");

                    var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, frame, w * 4);
                    source.Freeze();

                    Directory.CreateDirectory(ScreenshotCapture.ScreenshotsDir);
                    string target = System.IO.Path.Combine(ScreenshotCapture.ScreenshotsDir, $"ReplayFrame_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    using FileStream stream = File.Create(target);
                    encoder.Save(stream);
                    return target;
                });

                Saved = true;
                UpdateStatus($"frame saved to Screenshots as {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save frame failed: {ex.Message}");
                UpdateStatus($"couldn't save the frame: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }

            _timer.Stop();
            _thumbCancel?.Cancel();
            _stillCancel?.Cancel();

            try
            {
                Player.Stop();
                Player.Close();
            }
            catch
            {
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_busy || _info is null)
                return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl && e.Key == Key.S)
            {
                SaveCopy_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            bool handled = true;

            switch (e.Key)
            {
                case Key.Space: SetPlaying(!_playing); break;
                case Key.Left: SetPlaying(false); Seek(Position - FrameStep); break;
                case Key.Right: SetPlaying(false); Seek(Position + FrameStep); break;
                case Key.Home: SetPlaying(false); Seek(_start); break;
                case Key.End: SetPlaying(false); Seek(_end); break;
                case Key.I:
                case Key.OemOpenBrackets: SetStart(Position); break;
                case Key.O:
                case Key.OemCloseBrackets: SetEnd(Position); break;
                case Key.C: SetCropMode(!_cropMode); break;
                case Key.Escape:
                    if (_cropMode)
                        SetCropMode(false);
                    else
                        handled = false;
                    break;
                default: handled = false; break;
            }

            e.Handled = handled;
        }
    }
}
