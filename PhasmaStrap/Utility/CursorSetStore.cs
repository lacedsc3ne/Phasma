namespace PhasmaStrap.Utility
{
    public sealed class CursorSetRecord
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class CursorSetIndexFile
    {
        public int Version { get; set; } = 1;

        public List<CursorSetRecord> Sets { get; set; } = new();
    }

    public static class CursorSetStore
    {
        public static readonly Dictionary<string, string> FileMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MouseLockedCursor.png", @"content\textures\MouseLockedCursor.png" },
            { "ArrowCursor.png",       @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png" },
            { "ArrowFarCursor.png",    @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png" },
            { "IBeamCursor.png",       @"content\textures\Cursors\KeyboardMouse\IBeamCursor.png" }
        };

        private const long MaxIndexBytes = 2 * 1024 * 1024;
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public static IReadOnlyList<CursorSetRecord> Load()
        {
            lock (Sync)
                return LoadCore().Select(Clone).ToArray();
        }

        public static CursorSetRecord Create(string name)
        {
            lock (Sync)
            {
                List<CursorSetRecord> records = LoadCore();
                CursorSetRecord record = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = NormalizeName(name),
                    CreatedUtc = DateTime.UtcNow
                };
                Directory.CreateDirectory(GetFolderCore(record.Id));
                records.Add(record);
                SaveCore(records);
                return Clone(record);
            }
        }

        public static void Rename(string id, string name) => Mutate(id, record => record.Name = NormalizeName(name));

        public static void Delete(string id)
        {
            lock (Sync)
            {
                List<CursorSetRecord> records = LoadCore();
                if (records.RemoveAll(record => string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase)) == 0)
                    return;
                string folder = GetFolderCore(id);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                SaveCore(records);
            }
        }

        public static string GetFolder(string id)
        {
            if (!IsValidId(id))
                throw new InvalidDataException("The cursor set identifier is invalid.");
            return GetFolderCore(id);
        }

        public static void Apply(string id)
        {
            string folder = GetFolder(id);
            foreach (var pair in FileMap)
            {
                string? source = CursorImages.FindSource(folder, pair.Key);
                string target = Path.Combine(Paths.Modifications, pair.Value);

                if (source is null)
                    continue;

                CursorImages.WritePng(source, target);
            }
        }

        public static int FetchFromCurrent(string id)
        {
            string folder = GetFolder(id);
            int copied = 0;
            foreach (var pair in FileMap)
            {
                string source = Path.Combine(Paths.Modifications, pair.Value);
                if (!File.Exists(source))
                    continue;

                Directory.CreateDirectory(folder);
                File.Copy(source, Path.Combine(folder, pair.Key), true);
                copied++;
            }
            return copied;
        }

        public static void Export(string id, string destinationZipPath)
        {
            string folder = GetFolder(id);
            if (File.Exists(destinationZipPath))
                File.Delete(destinationZipPath);
            var fastZip = new ICSharpCode.SharpZipLib.Zip.FastZip();
            fastZip.CreateZip(destinationZipPath, folder, false, null);
        }

        public static CursorSetRecord Import(string zipPath, string name)
        {
            lock (Sync)
            {
                List<CursorSetRecord> records = LoadCore();
                CursorSetRecord record = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = NormalizeName(name),
                    CreatedUtc = DateTime.UtcNow
                };
                string folder = GetFolderCore(record.Id);
                Directory.CreateDirectory(folder);

                var fastZip = new ICSharpCode.SharpZipLib.Zip.FastZip();
                fastZip.ExtractZip(zipPath, folder, null);

                if (!CursorImages.AnyIn(folder, FileMap.Keys))
                {
                    string[] subdirectories = Directory.GetDirectories(folder);
                    if (subdirectories.Length == 1)
                    {
                        foreach (string file in Directory.GetFiles(subdirectories[0]))
                            File.Move(file, Path.Combine(folder, Path.GetFileName(file)), true);
                        Directory.Delete(subdirectories[0], true);
                    }
                }

                if (!CursorImages.AnyIn(folder, FileMap.Keys))
                {
                    Directory.Delete(folder, true);
                    throw new InvalidDataException("That archive doesn't contain any recognized cursor images. It needs at least one of ArrowCursor, ArrowFarCursor, IBeamCursor or MouseLockedCursor, as " + CursorImages.ReadableList + ".");
                }

                records.Add(record);
                SaveCore(records);
                return Clone(record);
            }
        }

        private static void Mutate(string id, Action<CursorSetRecord> mutation)
        {
            lock (Sync)
            {
                List<CursorSetRecord> records = LoadCore();
                CursorSetRecord? record = records.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
                if (record is null)
                    throw new InvalidOperationException("The selected cursor set no longer exists.");
                mutation(record);
                SaveCore(records);
            }
        }

        private static List<CursorSetRecord> LoadCore()
        {
            Directory.CreateDirectory(Paths.CursorSetsRoot);
            List<CursorSetRecord> records = ReadIndex();
            HashSet<string> indexed = new(records.Select(record => record.Id), StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (string folder in Directory.EnumerateDirectories(Paths.CursorSetsRoot))
            {
                string folderName = Path.GetFileName(folder);
                if (IsValidId(folderName) && indexed.Add(folderName))
                {
                    records.Add(new CursorSetRecord
                    {
                        Id = folderName.ToLowerInvariant(),
                        Name = "Cursor Set " + folderName[..8],
                        CreatedUtc = Directory.GetCreationTimeUtc(folder)
                    });
                    changed = true;
                }
            }
            int removed = records.RemoveAll(record => !Directory.Exists(GetFolderCore(record.Id)));
            changed |= removed > 0;
            if (changed || !File.Exists(Paths.CursorSetsIndex))
                SaveCore(records);
            return records;
        }

        private static List<CursorSetRecord> ReadIndex()
        {
            if (!File.Exists(Paths.CursorSetsIndex))
                return new();
            try
            {
                FileInfo info = new(Paths.CursorSetsIndex);
                if (info.Length <= 0 || info.Length > MaxIndexBytes)
                    return new();
                using FileStream stream = new(Paths.CursorSetsIndex, FileMode.Open, FileAccess.Read, FileShare.Read);
                CursorSetIndexFile? index = JsonSerializer.Deserialize<CursorSetIndexFile>(stream, JsonOptions);
                List<CursorSetRecord> records = new();
                HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
                foreach (CursorSetRecord record in index?.Sets ?? new())
                {
                    if (!IsValidId(record.Id) || !ids.Add(record.Id))
                        continue;
                    record.Id = record.Id.ToLowerInvariant();
                    record.Name = NormalizeName(record.Name, "Cursor Set " + record.Id[..8]);
                    records.Add(record);
                }
                return records;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("CursorSetStore::ReadIndex", "Could not read the cursor set index: " + ex.Message);
                return new();
            }
        }

        private static void SaveCore(List<CursorSetRecord> records)
        {
            Directory.CreateDirectory(Paths.CursorSets);
            string temporary = Paths.CursorSetsIndex + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    JsonSerializer.Serialize(stream, new CursorSetIndexFile { Sets = records }, JsonOptions);
                File.Move(temporary, Paths.CursorSetsIndex, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static bool IsValidId(string id) => id.Length == 32 && Guid.TryParseExact(id, "N", out _);

        private static string GetFolderCore(string id)
        {
            if (!IsValidId(id))
                throw new InvalidDataException("The cursor set identifier is invalid.");
            return Path.Combine(Paths.CursorSetsRoot, id.ToLowerInvariant());
        }

        private static string NormalizeName(string name, string? fallback = null)
        {
            string normalized = string.Join(" ", (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            normalized = new string(normalized.Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();
            if (normalized.Length == 0)
                normalized = fallback ?? throw new ArgumentException("Enter a name for the cursor set.", nameof(name));
            return normalized;
        }

        private static CursorSetRecord Clone(CursorSetRecord record) => new() { Id = record.Id, Name = record.Name, CreatedUtc = record.CreatedUtc };
    }
}
