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

        public static byte[] ToPng(string sourcePath)
        {
            using FileStream stream = File.OpenRead(sourcePath);

            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            BitmapFrame frame = decoder.Frames
                .OrderByDescending(candidate => (long)candidate.PixelWidth * candidate.PixelHeight)
                .First();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));

            using var output = new MemoryStream();
            encoder.Save(output);

            return output.ToArray();
        }

        public static void WritePng(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            Filesystem.AssertReadOnly(destinationPath);

            if (Path.GetExtension(sourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destinationPath, true);
                return;
            }

            byte[] png = ToPng(sourcePath);
            File.WriteAllBytes(destinationPath, png);

            App.Logger.WriteLine(LOG_IDENT, $"Converted {Path.GetFileName(sourcePath)} to PNG for {Path.GetFileName(destinationPath)}");
        }

        public const string QuickPickFolderName = "QuickPick";

        public static string QuickPickFolder => Path.Combine(Paths.CursorSets, QuickPickFolderName);

        public static string BuildFromOneImage(string imagePath)
        {
            string folder = QuickPickFolder;

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            Directory.CreateDirectory(folder);

            WritePng(imagePath, Path.Combine(folder, "ArrowCursor.png"));
            WritePng(imagePath, Path.Combine(folder, "ArrowFarCursor.png"));

            File.WriteAllText(Path.Combine(folder, "source.txt"), imagePath);

            App.Logger.WriteLine(LOG_IDENT, $"Built an arrow cursor from {Path.GetFileName(imagePath)}");

            return folder;
        }

        public static string? SourceOf(string folder)
        {
            try
            {
                string note = Path.Combine(folder, "source.txt");
                return File.Exists(note) ? File.ReadAllText(note).Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
