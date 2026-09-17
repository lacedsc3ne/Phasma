using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

// Ported from Voidstrap (UI/GifImageBehavior.cs): lets a plain <Image> play an animated GIF by
// decoding its frames into a discrete-keyframe ObjectAnimationUsingKeyFrames, rather than relying
// on a NuGet animated-GIF package. Used by GlobalBackground for animated background images.
//
// Voidstrap's version loaded static (non-GIF) images via its own Voidstrap.Utility.SafeImaging
// helper, which doesn't exist in PhasmaStrap - replaced here with a plain try/catch BitmapImage
// load (LoadStatic).
namespace PhasmaStrap.UI
{
    public static class GifImageBehavior
    {
        // GIF wallpapers are routinely 20-60 MB with a few hundred frames - the original limits
        // (16 MB / 120 frames / 48 MB decoded) silently fell back to a static first frame for
        // most of them, which looked exactly like "GIFs don't animate". Frames are downscaled to
        // the window's size before counting against the decoded budget.
        private const long MaxEncodedBytes = 96L * 1024L * 1024L;

        private const long MaxDecodedBytes = 640L * 1024L * 1024L;

        private const int MaxAnimationFrames = 600;

        private const int MaxFrameWidth = 1920;

        private const string LOG_IDENT = "GifImageBehavior";

        public static readonly DependencyProperty SourcePathProperty =
            DependencyProperty.RegisterAttached(
                "SourcePath",
                typeof(string),
                typeof(GifImageBehavior),
                new PropertyMetadata(null, OnSourcePathChanged));

        public static string GetSourcePath(DependencyObject obj) => (string)obj.GetValue(SourcePathProperty);

        public static void SetSourcePath(DependencyObject obj, string value) => obj.SetValue(SourcePathProperty, value);

        private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Image image)
                return;

            image.BeginAnimation(Image.SourceProperty, null);

            string path = e.NewValue as string ?? "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                image.Source = null;
                return;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".gif")
            {
                try
                {
                    AnimateGif(image, path);
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"GIF animation failed for '{path}', showing it as a still image: {ex.Message}");
                }
            }

            image.Source = LoadStatic(path);
        }

        private static ImageSource? LoadStatic(string path)
        {
            try
            {
                BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                    bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static void AnimateGif(Image image, string path)
        {
            if (new FileInfo(path).Length > MaxEncodedBytes)
            {
                App.Logger.WriteLine(LOG_IDENT, $"'{path}' is over {MaxEncodedBytes / 1024 / 1024} MB - showing it as a still image");
                image.Source = LoadStatic(path);
                return;
            }

            var decoder = new GifBitmapDecoder(
                new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                image.Source = null;
                return;
            }

            if (decoder.Frames.Count == 1)
            {
                var single = decoder.Frames[0];
                single.Freeze();
                image.Source = single;
                return;
            }

            if (decoder.Frames.Count > MaxAnimationFrames)
            {
                App.Logger.WriteLine(LOG_IDENT, $"'{path}' has {decoder.Frames.Count} frames (limit {MaxAnimationFrames}) - showing it as a still image");
                var first = decoder.Frames[0];
                first.Freeze();
                image.Source = first;
                return;
            }

            // downscale oversized frames so a 4K GIF doesn't need gigabytes of decoded bitmaps
            double scale = decoder.Frames[0].PixelWidth > MaxFrameWidth ? (double)MaxFrameWidth / decoder.Frames[0].PixelWidth : 1.0;

            long decodedBytes = 0;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                long frameBytes = (long)(frame.PixelWidth * scale) * (long)(frame.PixelHeight * scale) * 4;
                if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || frameBytes > MaxDecodedBytes || decodedBytes > MaxDecodedBytes - frameBytes)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"'{path}' would need more than {MaxDecodedBytes / 1024 / 1024} MB decoded - showing it as a still image");
                    var first = decoder.Frames[0];
                    first.Freeze();
                    image.Source = first;
                    return;
                }
                decodedBytes += frameBytes;
            }

            var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            var time = TimeSpan.Zero;

            foreach (var frame in decoder.Frames)
            {
                BitmapSource keyframe = frame;
                if (scale < 1.0)
                {
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    scaled.Freeze();
                    keyframe = scaled;
                }
                else
                {
                    frame.Freeze();
                }

                animation.KeyFrames.Add(new DiscreteObjectKeyFrame(keyframe, KeyTime.FromTimeSpan(time)));

                int delayCentiseconds = 10;
                try
                {
                    if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("/grctlext/Delay"))
                    {
                        var raw = md.GetQuery("/grctlext/Delay");
                        if (raw is ushort cs && cs > 0)
                            delayCentiseconds = cs;
                    }
                }
                catch
                {
                }

                time += TimeSpan.FromMilliseconds(delayCentiseconds * 10);
            }

            animation.Duration = new Duration(time);
            animation.Freeze();
            image.Source = (ImageSource)animation.KeyFrames[0].Value;

            // the background Image is created before it's in the visual tree - start the clock
            // once it's actually loaded so it can't be dropped before the element is rendered
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

            App.Logger.WriteLine(LOG_IDENT, $"Animating '{Path.GetFileName(path)}': {decoder.Frames.Count} frames, {time.TotalSeconds:0.0}s loop{(scale < 1.0 ? $", scaled x{scale:0.00}" : "")}");
        }
    }
}
