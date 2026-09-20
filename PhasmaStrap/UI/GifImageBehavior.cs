using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using Image = System.Windows.Controls.Image;

namespace PhasmaStrap.UI
{
    public static class GifImageBehavior
    {
        private const string LOG_IDENT = "GifImageBehavior";

        private const long MaxEncodedBytes = 128L * 1024 * 1024;

        private const long MaxDecodedBytes = 384L * 1024 * 1024;

        private const int MaxFrames = 900;
        private const int MaxFrameWidth = 1920;
        private const int MaxStillWidth = 2560;

        public sealed class Media
        {
            public BitmapSource[] Frames = Array.Empty<BitmapSource>();
            public TimeSpan[] Delays = Array.Empty<TimeSpan>();
            public TimeSpan Duration;
            public bool IsAnimated => Frames.Length > 1;
        }

        public static readonly DependencyProperty SourcePathProperty =
            DependencyProperty.RegisterAttached("SourcePath", typeof(string), typeof(GifImageBehavior), new PropertyMetadata(null, OnSourcePathChanged));

        public static string GetSourcePath(DependencyObject obj) => (string)obj.GetValue(SourcePathProperty);

        public static void SetSourcePath(DependencyObject obj, string value) => obj.SetValue(SourcePathProperty, value);

        private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Image image)
                return;

            image.BeginAnimation(Image.SourceProperty, null);
            image.Source = null;

            string path = e.NewValue as string ?? "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            image.BeginAnimation(UIElement.OpacityProperty, null);
            image.Opacity = 0;

            Task.Run(() => Load(path)).ContinueWith(task =>
            {
                image.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!string.Equals(GetSourcePath(image), path, StringComparison.OrdinalIgnoreCase))
                        return;

                    Media? media = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                    if (media is null || media.Frames.Length == 0)
                    {
                        if (task.Exception is not null)
                            App.Logger.WriteLine(LOG_IDENT, $"Could not load '{path}': {task.Exception.GetBaseException().Message}");
                        return;
                    }

                    Show(image, media);
                }));
            });
        }

        private static void Show(Image image, Media media)
        {
            image.Source = media.Frames[0];

            if (media.IsAnimated)
            {
                var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = new Duration(media.Duration) };
                TimeSpan time = TimeSpan.Zero;

                for (int i = 0; i < media.Frames.Length; i++)
                {
                    animation.KeyFrames.Add(new DiscreteObjectKeyFrame(media.Frames[i], KeyTime.FromTimeSpan(time)));
                    time += media.Delays[i];
                }

                double shortest = media.Delays.Where(d => d > TimeSpan.Zero).DefaultIfEmpty(TimeSpan.FromMilliseconds(100)).Min().TotalMilliseconds;
                Timeline.SetDesiredFrameRate(animation, Math.Clamp((int)Math.Ceiling(1000.0 / shortest), 1, 60));

                animation.Freeze();

                if (image.IsLoaded)
                {
                    image.BeginAnimation(Image.SourceProperty, animation);
                }
                else
                {
                    RoutedEventHandler? onLoaded = null;
                    onLoaded = (_, _) =>
                    {
                        image.Loaded -= onLoaded;
                        image.BeginAnimation(Image.SourceProperty, animation);
                    };
                    image.Loaded += onLoaded;
                }
            }

            image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private static readonly object _cacheLock = new();
        private static string _cacheKey = "";
        private static Media? _cacheMedia;

        public static Media Load(string path)
        {
            var info = new FileInfo(path);
            string key = $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

            lock (_cacheLock)
            {
                if (_cacheKey == key && _cacheMedia is not null)
                    return _cacheMedia;
            }

            var timer = Stopwatch.StartNew();
            Media media = string.Equals(info.Extension, ".gif", StringComparison.OrdinalIgnoreCase) && info.Length <= MaxEncodedBytes
                ? LoadGif(path)
                : LoadStill(path);

            App.Logger.WriteLine(LOG_IDENT, media.IsAnimated
                ? $"Loaded '{info.Name}': {media.Frames.Length} frames, {media.Frames[0].PixelWidth}x{media.Frames[0].PixelHeight}, {media.Duration.TotalSeconds:0.0}s loop, {timer.ElapsedMilliseconds}ms"
                : $"Loaded '{info.Name}' as a still image, {timer.ElapsedMilliseconds}ms");

            lock (_cacheLock)
            {
                _cacheKey = key;
                _cacheMedia = media;
            }

            return media;
        }

        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cacheKey = "";
                _cacheMedia = null;
            }
        }

        public static BitmapSource? LoadThumbnail(string path, int width)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = width;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Thumbnail failed for '{Path.GetFileName(path)}': {ex.Message}");
                return null;
            }
        }

        private static Media LoadStill(string path)
        {
            using FileStream stream = File.OpenRead(path);

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];

            if (frame.PixelWidth > MaxStillWidth)
            {
                double scale = (double)MaxStillWidth / frame.PixelWidth;
                frame = Materialize(new TransformedBitmap(frame, new ScaleTransform(scale, scale)));
            }

            if (frame.CanFreeze)
                frame.Freeze();

            return new Media { Frames = new[] { frame }, Delays = new[] { TimeSpan.Zero } };
        }

        private static Media LoadGif(string path)
        {
            using FileStream stream = File.OpenRead(path);

            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            int count = decoder.Frames.Count;

            if (count == 0)
                return new Media();

            if (count == 1 || count > MaxFrames)
            {
                if (count > MaxFrames)
                    App.Logger.WriteLine(LOG_IDENT, $"'{Path.GetFileName(path)}' has {count} frames (limit {MaxFrames}) - showing it as a still image");

                BitmapSource only = Materialize(new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0));
                return new Media { Frames = new[] { only }, Delays = new[] { TimeSpan.Zero } };
            }

            int canvasWidth = ReadInt(decoder.Metadata, "/logscrdesc/Width", decoder.Frames[0].PixelWidth);
            int canvasHeight = ReadInt(decoder.Metadata, "/logscrdesc/Height", decoder.Frames[0].PixelHeight);
            canvasWidth = Math.Max(1, canvasWidth);
            canvasHeight = Math.Max(1, canvasHeight);

            double scale = Math.Min(1.0, (double)MaxFrameWidth / canvasWidth);
            long fullBytes = (long)(canvasWidth * scale) * (long)(canvasHeight * scale) * 4 * count;
            if (fullBytes > MaxDecodedBytes)
                scale *= Math.Sqrt((double)MaxDecodedBytes / fullBytes);

            int stride = canvasWidth * 4;
            byte[] canvas = new byte[stride * canvasHeight];
            byte[]? restore = null;

            var frames = new BitmapSource[count];
            var delays = new TimeSpan[count];
            TimeSpan total = TimeSpan.Zero;

            for (int i = 0; i < count; i++)
            {
                BitmapFrame raw = decoder.Frames[i];
                var metadata = raw.Metadata as BitmapMetadata;

                int left = ReadInt(metadata, "/imgdesc/Left", 0);
                int top = ReadInt(metadata, "/imgdesc/Top", 0);
                int disposal = ReadInt(metadata, "/grctlext/Disposal", 0);
                int delay = ReadInt(metadata, "/grctlext/Delay", 10);

                if (delay < 2)
                    delay = 10;

                int width = Math.Min(raw.PixelWidth, canvasWidth - left);
                int height = Math.Min(raw.PixelHeight, canvasHeight - top);

                if (disposal == 3)
                    restore = (byte[])canvas.Clone();

                if (width > 0 && height > 0 && left >= 0 && top >= 0)
                {
                    var converted = new FormatConvertedBitmap(raw, PixelFormats.Bgra32, null, 0);
                    int rawStride = raw.PixelWidth * 4;
                    byte[] pixels = new byte[rawStride * raw.PixelHeight];
                    converted.CopyPixels(pixels, rawStride, 0);

                    for (int y = 0; y < height; y++)
                    {
                        int source = y * rawStride;
                        int target = (top + y) * stride + left * 4;

                        for (int x = 0; x < width; x++, source += 4, target += 4)
                        {
                            if (pixels[source + 3] == 0)
                                continue;

                            canvas[target] = pixels[source];
                            canvas[target + 1] = pixels[source + 1];
                            canvas[target + 2] = pixels[source + 2];
                            canvas[target + 3] = 255;
                        }
                    }
                }

                BitmapSource composed = BitmapSource.Create(canvasWidth, canvasHeight, 96, 96, PixelFormats.Bgra32, null, (byte[])canvas.Clone(), stride);
                if (scale < 0.999)
                    composed = Materialize(new TransformedBitmap(composed, new ScaleTransform(scale, scale)));

                composed.Freeze();
                frames[i] = composed;
                delays[i] = TimeSpan.FromMilliseconds(delay * 10);
                total += delays[i];

                if (disposal == 2 && width > 0 && height > 0 && left >= 0 && top >= 0)
                {
                    for (int y = 0; y < height; y++)
                        Array.Clear(canvas, (top + y) * stride + left * 4, width * 4);
                }
                else if (disposal == 3 && restore is not null)
                {
                    canvas = restore;
                    restore = null;
                }
            }

            return new Media { Frames = frames, Delays = delays, Duration = total };
        }

        private static BitmapSource Materialize(BitmapSource source)
        {
            BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int stride = bgra.PixelWidth * 4;
            byte[] pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);

            BitmapSource result = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96, bgra.Format, null, pixels, stride);
            result.Freeze();
            return result;
        }

        private static int ReadInt(ImageMetadata? metadata, string query, int fallback)
        {
            try
            {
                if (metadata is BitmapMetadata bitmapMetadata && bitmapMetadata.ContainsQuery(query))
                {
                    object? value = bitmapMetadata.GetQuery(query);
                    if (value is not null)
                        return Convert.ToInt32(value);
                }
            }
            catch
            {
            }

            return fallback;
        }
    }
}
