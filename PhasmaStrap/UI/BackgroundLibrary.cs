using System.Security.Cryptography;

namespace PhasmaStrap.UI
{
    // The settings-window backgrounds the user has added, kept as copies in <install>\Backgrounds.
    // Copies rather than links to wherever the file came from, because a linked background
    // silently disappears the moment the original is moved, renamed or cleaned out of Downloads,
    // and because every copy gets a name derived from its content - two different pictures can
    // never end up sharing a path (and with it a cache entry).
    internal static class BackgroundLibrary
    {
        private const string LOG_IDENT = "BackgroundLibrary";

        public static readonly string[] Extensions = { ".gif", ".png", ".jpg", ".jpeg", ".bmp" };

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

        // newest first
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

        // Copies `source` into the library and returns the copy's path. Adding the same picture
        // twice returns the existing copy.
        public static string Import(string source)
        {
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

        // "sunset-1a2b3c4d5e.gif" -> "sunset"
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
