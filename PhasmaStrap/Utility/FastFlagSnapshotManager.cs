namespace PhasmaStrap.Utility
{
    public sealed class FastFlagSnapshot
    {
        public string Name { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public Dictionary<string, object> Flags { get; set; } = new();
    }

    public sealed class FastFlagDiffEntry
    {
        public string Key { get; init; } = "";
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
        public string ChangeType { get; init; } = "";
    }

    public static class FastFlagSnapshotManager
    {
        private static string SnapshotDir => Path.Combine(Paths.Base, "FastFlagSnapshots");

        public static List<FastFlagSnapshot> List()
        {
            if (!Directory.Exists(SnapshotDir))
                return new List<FastFlagSnapshot>();

            var result = new List<FastFlagSnapshot>();

            foreach (string file in Directory.GetFiles(SnapshotDir, "*.json"))
            {
                try
                {
                    FastFlagSnapshot? snapshot = JsonSerializer.Deserialize<FastFlagSnapshot>(File.ReadAllText(file));
                    if (snapshot is not null)
                        result.Add(snapshot);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("FastFlagSnapshotManager", $"Failed to read snapshot '{file}': {ex.Message}");
                }
            }

            return result.OrderByDescending(s => s.CreatedUtc).ToList();
        }

        public static void Save(string name) => Save(name, App.FastFlags.Prop);

        public static void Save(string name, IEnumerable<KeyValuePair<string, object>> flags)
        {
            Directory.CreateDirectory(SnapshotDir);

            var snapshot = new FastFlagSnapshot
            {
                Name = name,
                CreatedUtc = DateTime.UtcNow,
                Flags = flags.ToDictionary(kv => kv.Key, kv => kv.Value),
            };

            File.WriteAllText(SnapshotPath(name), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void Delete(string name)
        {
            string path = SnapshotPath(name);
            if (File.Exists(path))
                File.Delete(path);
        }

        public static void Apply(FastFlagSnapshot snapshot)
        {
            foreach (string existingKey in App.FastFlags.Prop.Keys.ToList())
            {
                if (!snapshot.Flags.ContainsKey(existingKey))
                    App.FastFlags.SetValue(existingKey, null);
            }

            foreach (var (key, value) in snapshot.Flags)
                App.FastFlags.SetValue(key, value);

            App.FastFlags.Save();
        }

        public static List<FastFlagDiffEntry> Diff(IReadOnlyDictionary<string, object> a, IReadOnlyDictionary<string, object> b)
        {
            var result = new List<FastFlagDiffEntry>();

            foreach (string key in a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                bool inA = a.TryGetValue(key, out object? aVal);
                bool inB = b.TryGetValue(key, out object? bVal);

                if (inA && !inB)
                    result.Add(new FastFlagDiffEntry { Key = key, OldValue = aVal?.ToString(), NewValue = null, ChangeType = "Removed" });
                else if (!inA && inB)
                    result.Add(new FastFlagDiffEntry { Key = key, OldValue = null, NewValue = bVal?.ToString(), ChangeType = "Added" });
                else if (aVal?.ToString() != bVal?.ToString())
                    result.Add(new FastFlagDiffEntry { Key = key, OldValue = aVal?.ToString(), NewValue = bVal?.ToString(), ChangeType = "Changed" });
            }

            return result;
        }

        private static string SnapshotPath(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return Path.Combine(SnapshotDir, $"{name}.json");
        }
    }
}
