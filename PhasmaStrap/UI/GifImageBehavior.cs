using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using Image = System.Windows.Controls.Image;

// Lets a plain <Image> show a still image or play an animated GIF, used by GlobalBackground for
// the settings window's background.
//
// Second version. What the first one (a port of Voidstrap's) got wrong:
//   - it showed the GIF decoder's raw frames. Those are not pictures of the whole animation:
//     an optimised GIF stores only the rectangle that changed, positioned by an offset, with a
//     disposal rule saying what to do with it afterwards. Shown raw, most real-world GIFs jitter,
//     smear or flash. Frames are now composited onto a canvas the way a browser does it.
//   - it decoded on the UI thread (a 100-frame GIF froze the window for a moment) and decoded
//     again for every refresh - toggling the background or nudging the dim slider re-read the
//     whole file each time. Decoding now happens once, on a worker thread, and the result is
//     cached by path + size + timestamp and shared.
//   - it opened files by URI, which goes through WPF's image cache: replace a file under the same
//     name and the old picture kept coming back until restart. Files are now read from a stream
//     with the cache bypassed.
namespace PhasmaStrap.UI
{
    public static class GifImageBehavior
    {
        private const string LOG_IDENT = "GifImageBehavior";

        private const long MaxEncodedBytes = 128L * 1024 * 1024;

        // composited frames are 4 bytes a pixel; frames are scaled down to stay inside this
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

        // ------------------------------------------------------------------ attached property

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

            // start transparent and fade in once - not per frame
            image.BeginAnimation(UIElement.OpacityProperty, null);
            image.Opacity = 0;

            Task.Run(() => Load(path)).ContinueWith(task =>
            {
                image.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // the path was changed again while this one was decoding
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

                animation.Freeze();

                // an animation started on an element that isn't in the tree yet can be dropped
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

        // ------------------------------------------------------------------ loading + cache

        private static readonly object _cacheLock = new();
        private static string _cacheKey = "";
        private static Media? _cacheMedia;

        // Decodes (or returns the cached copy of) an image or GIF. Safe to call from any thread;
        // every bitmap it returns is frozen.
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

            // one entry: only one background is ever on screen, and a decoded GIF is large
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

        // First frame only, small - for the picker's thumbnails.
        public static BitmapSource? LoadThumbnail(string path, int width)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = width;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
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

            // the logical screen: the canvas every frame is drawn onto
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

                // browsers treat 0-1 centiseconds as "unspecified" and play those at 10fps
                if (delay < 2)
                    delay = 10;

                int width = Math.Min(raw.PixelWidth, canvasWidth - left);
                int height = Math.Min(raw.PixelHeight, canvasHeight - top);

                // disposal 3 = put back what was there before this frame
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
                            // GIF transparency is all-or-nothing: a transparent pixel leaves the canvas as it was
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

                // apply this frame's disposal before the next one is drawn
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

        // turns a lazy bitmap chain (transform / format conversion) into plain pixels, so the source
        // frames behind it can be collected
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
