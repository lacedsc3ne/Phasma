using System.Security.Cryptography;

namespace PhasmaStrap.UI
{
    internal static class BackgroundLibrary
    {
        private const string LOG_IDENT = "BackgroundLibrary";

        public static readonly string[] ImageExtensions = { ".gif", ".png", ".jpg", ".jpeg", ".bmp" };

        public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".webm", ".mkv" };

        public static readonly string[] Extensions = ImageExtensions.Concat(VideoExtensions).ToArray();

        public const long LargeVideoBytes = 300L * 1024 * 1024;

        public static bool IsVideo(string? path) =>
            !string.IsNullOrEmpty(path) && VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static System.Windows.Media.Imaging.BitmapSource? LoadThumbnail(string path, int width)
        {
            if (!IsVideo(path))
                return GifImageBehavior.LoadThumbnail(path, width);

            try
            {
                PhasmaStrap.Utility.ClipProcessor.Log ??= message => App.Logger.WriteLine("ClipProcessor", message);
                PhasmaStrap.Utility.ClipInfo info = PhasmaStrap.Utility.ClipProcessor.Probe(path);
                TimeSpan at = TimeSpan.FromSeconds(Math.Min(1.0, info.Duration.TotalSeconds / 2));

                byte[]? pixels = PhasmaStrap.Utility.ClipProcessor.GrabFrame(path, at, out int w, out int h);
                if (pixels is null || w <= 0 || h <= 0)
                    return null;

                var frame = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels, w * 4);

                System.Windows.Media.Imaging.BitmapSource result = frame;
                if (w > width)
                {
                    double scale = (double)width / w;
                    result = new System.Windows.Media.Imaging.TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
                }

                var copy = new System.Windows.Media.Imaging.WriteableBitmap(result);
                copy.Freeze();
                return copy;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Video thumbnail failed for '{Path.GetFileName(path)}': {ex.Message}");
                return null;
            }
        }

        public static string Directory => Path.Combine(Paths.Base, "Backgrounds");

        public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static bool Contains(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Directory), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static List<string> List()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    return new List<string>();

                return new DirectoryInfo(Directory).GetFiles()
                    .Where(f => IsSupported(f.Name))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Select(f => f.FullName)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list backgrounds: {ex.Message}");
                return new List<string>();
            }
        }

        public static string Import(string source)
        {
            if (IsVideo(source))
            {
                PhasmaStrap.Utility.ClipInfo info;
                try
                {
                    info = PhasmaStrap.Utility.ClipProcessor.Probe(source);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Windows cannot play this video ({ex.Message.Trim()}). MP4 with H.264 always works; WebM and MKV need the matching codec from the Microsoft Store.", ex);
                }

                if (info.Width <= 0 || info.Height <= 0)
                    throw new InvalidDataException("This file has no video picture Windows can read.");
            }

            System.IO.Directory.CreateDirectory(Directory);

            string hash;
            using (FileStream stream = File.OpenRead(source))
            using (var sha = SHA256.Create())
                hash = Convert.ToHexString(sha.ComputeHash(stream))[..10].ToLowerInvariant();

            string stem = new string(Path.GetFileNameWithoutExtension(source).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
            if (stem.Length == 0)
                stem = "background";
            if (stem.Length > 40)
                stem = stem[..40];

            string target = Path.Combine(Directory, $"{stem}-{hash}{Path.GetExtension(source).ToLowerInvariant()}");

            if (!File.Exists(target))
            {
                File.Copy(source, target);
                File.SetCreationTimeUtc(target, DateTime.UtcNow);
                App.Logger.WriteLine(LOG_IDENT, $"Imported '{source}' as '{Path.GetFileName(target)}'");
            }

            return target;
        }

        public static void Remove(string path)
        {
            if (!Contains(path))
                return;

            try
            {
                File.Delete(path);
                App.Logger.WriteLine(LOG_IDENT, $"Removed '{Path.GetFileName(path)}'");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not remove '{path}': {ex.Message}");
            }
        }

        public static string DisplayName(string path)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            int dash = stem.LastIndexOf('-');

            if (Contains(path) && dash > 0 && stem.Length - dash - 1 == 10)
                stem = stem[..dash];

            return stem;
        }
    }
}
