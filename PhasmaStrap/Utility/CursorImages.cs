using System.Windows.Media.Imaging;

namespace PhasmaStrap.Utility
{
    public static class CursorImages
    {
        private const string LOG_IDENT = "CursorImages";

        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" };

        public const string ReadableList = "PNG, JPG, BMP, GIF, TIFF or ICO";

        public static string PickerFilter => "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico";

        public static bool IsSupported(string path) =>
            Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static string? FindSource(string folder, string canonicalName)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return null;

            string exact = Path.Combine(folder, canonicalName);

            if (File.Exists(exact))
                return exact;

            string stem = Path.GetFileNameWithoutExtension(canonicalName);

            foreach (string extension in Extensions)
            {
                string candidate = Path.Combine(folder, stem + extension);

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static bool AnyIn(string folder, IEnumerable<string> canonicalNames) =>
            canonicalNames.Any(name => FindSource(folder, name) is not null);

        private static readonly Dictionary<string, int> CursorSizes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MouseLockedCursor.png", 32 },
            { "ArrowCursor.png", 64 },
            { "ArrowFarCursor.png", 64 },
            { "IBeamCursor.png", 64 }
        };

        public static int SizeFor(string fileName) =>
            CursorSizes.TryGetValue(Path.GetFileName(fileName), out int size) ? size : 64;

        private static BitmapSource Read(string sourcePath)
        {
            using FileStream stream = File.OpenRead(sourcePath);

            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            return decoder.Frames
                .OrderByDescending(candidate => (long)candidate.PixelWidth * candidate.PixelHeight)
                .First();
        }

        private static BitmapSource FitTo(BitmapSource source, int box)
        {
            if (source.PixelWidth <= box && source.PixelHeight <= box)
                return source;

            double scale = Math.Min((double)box / source.PixelWidth, (double)box / source.PixelHeight);

            var scaled = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
            scaled.Freeze();

            return scaled;
        }

        private static byte[] Encode(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var output = new MemoryStream();
            encoder.Save(output);

            return output.ToArray();
        }

        public static byte[] ToPng(string sourcePath) => Encode(Read(sourcePath));

        public static void WritePng(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            Filesystem.AssertReadOnly(destinationPath);

            BitmapSource source = Read(sourcePath);
            int box = SizeFor(destinationPath);
            BitmapSource fitted = FitTo(source, box);

            if (ReferenceEquals(fitted, source) && Path.GetExtension(sourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destinationPath, true);
                return;
            }

            File.WriteAllBytes(destinationPath, Encode(fitted));

            if (!ReferenceEquals(fitted, source))
                App.Logger.WriteLine(LOG_IDENT, $"Scaled {Path.GetFileName(sourcePath)} from {source.PixelWidth}x{source.PixelHeight} to {fitted.PixelWidth}x{fitted.PixelHeight} for {Path.GetFileName(destinationPath)}");
            else
                App.Logger.WriteLine(LOG_IDENT, $"Converted {Path.GetFileName(sourcePath)} to PNG for {Path.GetFileName(destinationPath)}");
        }

        public const string ChosenFolderName = "Chosen";

        private const string ImageNote = "from-image.txt";
        private const string PackNote = "from-folder.txt";

        public static string ChosenFolder => Path.Combine(Paths.CursorSets, ChosenFolderName);

        private static string Reset()
        {
            string folder = ChosenFolder;

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            Directory.CreateDirectory(folder);

            return folder;
        }

        public static string BuildFromOneImage(string imagePath)
        {
            string folder = Reset();

            WritePng(imagePath, Path.Combine(folder, "ArrowCursor.png"));
            WritePng(imagePath, Path.Combine(folder, "ArrowFarCursor.png"));

            File.WriteAllText(Path.Combine(folder, ImageNote), imagePath);

            App.Logger.WriteLine(LOG_IDENT, $"Built an arrow cursor from {Path.GetFileName(imagePath)}");

            return folder;
        }

        public static string BuildFromFolder(string sourceFolder, IEnumerable<string> canonicalNames)
        {
            string folder = Reset();
            int taken = 0;

            foreach (string name in canonicalNames)
            {
                string? source = FindSource(sourceFolder, name);

                if (source is null)
                    continue;

                WritePng(source, Path.Combine(folder, name));
                taken++;
            }

            File.WriteAllText(Path.Combine(folder, PackNote), sourceFolder);

            App.Logger.WriteLine(LOG_IDENT, $"Took {taken} cursor image(s) from {sourceFolder}");

            return folder;
        }

        public static void Forget()
        {
            try
            {
                if (Directory.Exists(ChosenFolder))
                    Directory.Delete(ChosenFolder, true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not clear the chosen cursor: {ex.Message}");
            }
        }

        private static string? NoteIn(string folder, string name)
        {
            try
            {
                string note = Path.Combine(folder, name);
                string? read = File.Exists(note) ? File.ReadAllText(note).Trim() : null;
                return string.IsNullOrEmpty(read) ? null : read;
            }
            catch
            {
                return null;
            }
        }

        public static string? ImageBehind(string folder) => NoteIn(folder, ImageNote);

        public static string? PackBehind(string folder) => NoteIn(folder, PackNote);
    }
}
