using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class GifMakerWindow
    {
        private const string LOG_IDENT = "GifMaker";
        private const int MaxFrames = 900;

        public sealed class Picture
        {
            public string Name { get; init; } = "";
            public BitmapSource Source { get; init; } = null!;
            public BitmapSource Thumbnail => Source;
        }

        private readonly ObservableCollection<Picture> _pictures = new();
        private readonly DispatcherTimer _timer = new();
        private int _frame;
        private bool _ready;
        private bool _saving;
        private string? _savedPath;

        private int _width = 640, _height = 360, _fps = 15, _motion = 1;
        private double _seconds = 2;
        private bool _crossfade = true, _dither;

        public GifMakerWindow(BitmapSource first, string name)
        {
            InitializeComponent();

            _pictures.Add(new Picture { Name = name, Source = first });
            PictureList.ItemsSource = _pictures;
            PictureList.SelectedIndex = 0;

            MotionBox.SelectedIndex = 1;
            TransitionBox.SelectedIndex = 0;
            WidthBox.SelectedIndex = 2;
            FpsBox.SelectedIndex = 1;
            SecondsSlider.Value = 3;

            _timer.Tick += (_, _) => ShowNextFrame();
            _ready = true;
            Restart();

            Closing += OnClosing;
            Closed += (_, _) => _timer.Stop();
        }

        private void ReadSettings()
        {
            _motion = Math.Max(0, MotionBox.SelectedIndex);
            _crossfade = TransitionBox.SelectedIndex != 1;
            _seconds = SecondsSlider.Value;
            _fps = int.TryParse((FpsBox.SelectedItem as ComboBoxItem)?.Tag as string, out int fps) ? fps : 15;
            _width = int.TryParse((WidthBox.SelectedItem as ComboBoxItem)?.Tag as string, out int width) ? width : 640;
            _dither = DitherBox.IsChecked == true;

            BitmapSource shape = _pictures[0].Source;
            _height = Math.Clamp((int)Math.Round(_width * (double)shape.PixelHeight / shape.PixelWidth), 16, 1080);

            TransitionBox.IsEnabled = _pictures.Count > 1;
            SecondsText.Text = $"{_seconds:0.0} s";
        }

        private int PerPicture => Math.Max(1, (int)Math.Round(_seconds * _fps));
        private int FadeFrames => _pictures.Count > 1 && _crossfade ? Math.Max(1, (int)Math.Round(0.4 * _fps)) : 0;

        private int TotalFrames => _pictures.Count == 1 && _motion == 0
            ? 1
            : Math.Min(MaxFrames, _pictures.Count * PerPicture + (_pictures.Count - 1) * FadeFrames);

        private void Setting_Changed(object sender, RoutedEventArgs e) => Restart();

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Restart();

        private void Restart()
        {
            if (!_ready || _pictures.Count == 0)
                return;

            ReadSettings();
            _frame = 0;

            int total = TotalFrames;
            double length = total / (double)_fps;
            bool capped = _pictures.Count * PerPicture + (_pictures.Count - 1) * FadeFrames > MaxFrames;
            InfoText.Text = total == 1
                ? $"{_width} × {_height}  ·  a still picture"
                : $"{_width} × {_height}  ·  {total} frames  ·  {length:0.0} s, loops{(capped ? $"  ·  cut to {MaxFrames} frames" : "")}";

            UpdateButtons();

            _timer.Stop();
            ShowNextFrame();
            if (total > 1)
            {
                _timer.Interval = TimeSpan.FromSeconds(1.0 / _fps);
                _timer.Start();
            }
        }

        private void ShowNextFrame()
        {
            int total = TotalFrames;
            PreviewImage.Source = RenderFrame(_frame % total);
            _frame = (_frame + 1) % total;
        }

        private readonly record struct Layer(int Picture, double Progress, double Opacity);

        private List<Layer> LayersAt(int frame)
        {
            int per = PerPicture, fade = FadeFrames, n = _pictures.Count;
            int segment = per + fade;
            int index = Math.Min(n - 1, frame / segment);
            int into = frame - index * segment;

            if (into < per)
                return new() { new Layer(index, per == 1 ? 0 : into / (per - 1.0), 1) };

            double t = (into - per + 1) / (fade + 1.0);
            return new() { new Layer(index, 1, 1), new Layer(Math.Min(n - 1, index + 1), 0, t) };
        }

        private BitmapSource RenderFrame(int frame)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, _width, _height));
                foreach (Layer layer in LayersAt(frame))
                    DrawPicture(dc, _pictures[layer.Picture].Source, layer.Progress, layer.Opacity);
            }

            var target = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }

        private void DrawPicture(DrawingContext dc, BitmapSource source, double progress, double opacity)
        {
            double fit = Math.Min(_width / (double)source.PixelWidth, _height / (double)source.PixelHeight);
            double baseW = source.PixelWidth * fit, baseH = source.PixelHeight * fit;

            (double scale, double pan) = _motion switch
            {
                1 => (1 + 0.25 * progress, 0.0),
                2 => (1.25 - 0.25 * progress, 0.0),
                3 => (1.2, -1 + 2 * progress),
                4 => (1.2, 1 - 2 * progress),
                _ => (1.0, 0.0),
            };

            double w = baseW * scale, h = baseH * scale;
            double slack = Math.Max(0, (w - _width) / 2);
            double x = (_width - w) / 2 - pan * slack;
            double y = (_height - h) / 2;

            dc.PushOpacity(opacity);
            dc.DrawImage(source, new Rect(x, y, w, h));
            dc.Pop();
        }

        private void PictureList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            int i = PictureList.SelectedIndex;
            UpButton.IsEnabled = i > 0;
            DownButton.IsEnabled = i >= 0 && i < _pictures.Count - 1;
            RemoveButton.IsEnabled = i >= 0 && _pictures.Count > 1;
            SaveButton.IsEnabled = !_saving;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add screenshots",
                Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
                Multiselect = true,
            };
            if (Directory.Exists(ScreenshotCapture.ScreenshotsDir))
                dialog.InitialDirectory = ScreenshotCapture.ScreenshotsDir;

            if (dialog.ShowDialog(this) != true)
                return;

            int added = 0;
            foreach (string file in dialog.FileNames)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(file, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    _pictures.Add(new Picture { Name = System.IO.Path.GetFileNameWithoutExtension(file), Source = image });
                    added++;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not open {file}: {ex.Message}");
                }
            }

            StatusText.Text = added == dialog.FileNames.Length ? "" : $"{dialog.FileNames.Length - added} picture(s) couldn't be opened.";
            Restart();
        }

        private void Move(int by)
        {
            int i = PictureList.SelectedIndex;
            int j = i + by;
            if (i < 0 || j < 0 || j >= _pictures.Count)
                return;

            _pictures.Move(i, j);
            PictureList.SelectedIndex = j;
            Restart();
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

        private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            int i = PictureList.SelectedIndex;
            if (i < 0 || _pictures.Count <= 1)
                return;

            _pictures.RemoveAt(i);
            PictureList.SelectedIndex = Math.Min(i, _pictures.Count - 1);
            Restart();
        }

        private string TargetPath()
        {
            Directory.CreateDirectory(InstantReplayRecorder.ClipsDir);
            string stem = string.Concat(_pictures[0].Name.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            if (stem.Length == 0)
                stem = "Screenshot";

            string path = System.IO.Path.Combine(InstantReplayRecorder.ClipsDir, stem + ".gif");
            for (int n = 2; File.Exists(path); n++)
                path = System.IO.Path.Combine(InstantReplayRecorder.ClipsDir, $"{stem}_{n}.gif");
            return path;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_saving || _pictures.Count == 0)
                return;

            _saving = true;
            _timer.Stop();
            UpdateButtons();
            RevealButton.Visibility = Visibility.Collapsed;
            SaveProgress.Visibility = Visibility.Visible;
            SaveProgress.Value = 0;
            StatusText.Text = "Making the GIF...";

            ReadSettings();
            int width = _width, height = _height, total = TotalFrames;
            int delay = Math.Max(2, (int)Math.Round(100.0 / _fps));
            bool dither = _dither;
            string path = TargetPath();
            string temp = path + ".part";

            var frames = new BlockingCollection<byte[]>(8);
            Task encode = Task.Run(() =>
            {
                using FileStream stream = File.Create(temp);
                using var writer = new GifWriter(stream, width, height, dither);
                foreach (byte[] frame in frames.GetConsumingEnumerable())
                    writer.AddFrame(frame, delay);
            });

            try
            {
                for (int f = 0; f < total; f++)
                {
                    BitmapSource image = RenderFrame(f);
                    byte[] pixels = new byte[width * height * 4];
                    image.CopyPixels(pixels, width * 4, 0);

                    while (!frames.TryAdd(pixels))
                    {
                        if (encode.IsCompleted)
                            break;
                        await Task.Delay(5);
                    }

                    if (encode.IsCompleted)
                        break;

                    SaveProgress.Value = (f + 1.0) / total;
                    if (f % 4 == 0)
                        await Task.Yield();
                }

                frames.CompleteAdding();
                await encode;

                File.Move(temp, path, true);
                _savedPath = path;

                long bytes = new FileInfo(path).Length;
                StatusText.Text = $"Saved {System.IO.Path.GetFileName(path)} ({bytes / 1048576.0:0.0} MB)";
                RevealButton.Visibility = Visibility.Visible;
                App.Logger.WriteLine(LOG_IDENT, $"Saved {path}: {width}x{height}, {total} frames, {bytes} bytes");
            }
            catch (Exception ex)
            {
                frames.CompleteAdding();
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusText.Text = $"The GIF couldn't be saved: {ex.Message}";
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
            finally
            {
                _saving = false;
                SaveProgress.Visibility = Visibility.Collapsed;
                UpdateButtons();
                Restart();
            }
        }

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            if (_savedPath is not null)
                NotificationCenter.RevealFile(_savedPath)();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_saving)
                return;

            e.Cancel = true;
            StatusText.Text = "Still saving - close this once the GIF is done.";
        }
    }
}
