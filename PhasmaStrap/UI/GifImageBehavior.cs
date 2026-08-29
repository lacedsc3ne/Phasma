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
        private const long MaxEncodedBytes = 16L * 1024L * 1024L;

        private const long MaxDecodedBytes = 48L * 1024L * 1024L;

        private const int MaxAnimationFrames = 120;

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
                catch
                {
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

            long decodedBytes = 0;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                long frameBytes = (long)frame.PixelWidth * frame.PixelHeight * 4;
                if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || frameBytes > MaxDecodedBytes || decodedBytes > MaxDecodedBytes - frameBytes)
                {
                    var first = decoder.Frames[0];
                    first.Freeze();
                    image.Source = first;
                    return;
                }
                decodedBytes += frameBytes;
            }

            if (decoder.Frames.Count > MaxAnimationFrames)
            {
                var first = decoder.Frames[0];
                first.Freeze();
                image.Source = first;
                return;
            }

            var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            var time = TimeSpan.Zero;

            foreach (var frame in decoder.Frames)
            {
                frame.Freeze();
                animation.KeyFrames.Add(new DiscreteObjectKeyFrame(frame, KeyTime.FromTimeSpan(time)));

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
            image.Source = decoder.Frames[0];
            image.BeginAnimation(Image.SourceProperty, animation);
        }
    }
}
