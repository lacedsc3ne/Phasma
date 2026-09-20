namespace PhasmaStrap.Utility
{
    public static class SettingsBackups
    {
        public static Action<string>? Log;

        public const int MaxPerFile = 40;
        public static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(10);

        private const string Stamp = "yyyyMMdd_HHmmss";

        public sealed class Backup
        {
            public string Path = "";
            public string FileName = "";
            public DateTime Taken;
            public long Bytes;
        }

        private static readonly string[] Tracked = { "Settings.json", "ClientAppSettings.json", "FastFlagProfiles.json" };

        public static bool IsTracked(string fileLocation) =>
            Tracked.Contains(System.IO.Path.GetFileName(fileLocation), StringComparer.OrdinalIgnoreCase);

        private static string FolderFor(string backupRoot, string fileLocation) =>
            System.IO.Path.Combine(backupRoot, System.IO.Path.GetFileName(fileLocation));

        public static List<Backup> List(string backupRoot, string fileLocation)
        {
            var result = new List<Backup>();
            string folder = FolderFor(backupRoot, fileLocation);
            if (!Directory.Exists(folder))
                return result;

            foreach (FileInfo file in new DirectoryInfo(folder).GetFiles("*.json"))
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                if (stem.Length < Stamp.Length || !DateTime.TryParseExact(stem[..Stamp.Length], Stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime taken))
                    continue;

                result.Add(new Backup { Path = file.FullName, FileName = System.IO.Path.GetFileName(fileLocation), Taken = taken, Bytes = file.Length });
            }

            return result.OrderByDescending(b => b.Taken).ToList();
        }

        public static string? BeforeSave(string backupRoot, string fileLocation, string? newContents, bool force = false, DateTime? now = null)
        {
            try
            {
                if (!IsTracked(fileLocation) || !File.Exists(fileLocation))
                    return null;

                string current = File.ReadAllText(fileLocation);
                if (string.IsNullOrWhiteSpace(current))
                    return null;

                if (!force && newContents is not null && current == newContents)
                    return null;

                DateTime time = now ?? DateTime.Now;
                List<Backup> existing = List(backupRoot, fileLocation);

                if (existing.Count > 0)
                {
                    if (File.ReadAllText(existing[0].Path) == current)
                        return null;

                    if (!force && time - existing[0].Taken < QuietPeriod)
                        return null;
                }

                string folder = FolderFor(backupRoot, fileLocation);
                Directory.CreateDirectory(folder);

                string target = System.IO.Path.Combine(folder, time.ToString(Stamp) + ".json");
                for (int n = 2; File.Exists(target); n++)
                    target = System.IO.Path.Combine(folder, $"{time.ToString(Stamp)}_{n}.json");

                File.WriteAllText(target, current);
                Log?.Invoke($"Snapshot of {System.IO.Path.GetFileName(fileLocation)} -> {target}");

                foreach (Backup old in List(backupRoot, fileLocation).Skip(MaxPerFile))
                {
                    try { File.Delete(old.Path); } catch { }
                }

                return target;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Snapshot of {fileLocation} failed: {ex.Message}");
                return null;
            }
        }

        public static void Restore(string backupRoot, string fileLocation, Backup backup)
        {
            string contents = File.ReadAllText(backup.Path);

            using (System.Text.Json.JsonDocument.Parse(contents)) { }

            BeforeSave(backupRoot, fileLocation, null, force: true);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fileLocation)!);
            File.WriteAllText(fileLocation, contents);
            Log?.Invoke($"Restored {System.IO.Path.GetFileName(fileLocation)} from {backup.Path}");
        }

        public static int CountDifferences(string jsonA, string jsonB)
        {
            try
            {
                using var a = System.Text.Json.JsonDocument.Parse(jsonA);
                using var b = System.Text.Json.JsonDocument.Parse(jsonB);

                var left = a.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
                var right = b.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());

                return left.Keys.Union(right.Keys).Count(key =>
                    !left.TryGetValue(key, out string? l) || !right.TryGetValue(key, out string? r) || Normalise(l) != Normalise(r));
            }
            catch
            {
                return -1;
            }
        }

        private static string Normalise(string raw) => string.Concat(raw.Where(c => !char.IsWhiteSpace(c)));
    }
}
