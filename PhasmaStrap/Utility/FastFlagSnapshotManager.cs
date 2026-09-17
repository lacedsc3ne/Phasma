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

    // Named, on-disk snapshots of App.FastFlags.Prop (the full applied flag set), used for two
    // things: a "live A/B toggle" (Apply swaps the ENTIRE current flag set for a saved one, so
    // flipping between two snapshots is a clean compare, never a merge), and a diff viewer
    // (Diff between any two snapshots, or between a snapshot and the live current set).
    //
    // Deliberately not "your flags vs Roblox's real defaults" - PhasmaStrap's FastFlags.json only
    // ever stores explicit overrides to begin with (there's no bundled/fetched copy of Roblox's
    // actual default values to diff against), so a snapshot-to-snapshot diff is the honest,
    // actually-buildable version of this feature.
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

        public static void Save(string name)
        {
            Directory.CreateDirectory(SnapshotDir);

            var snapshot = new FastFlagSnapshot
            {
                Name = name,
                CreatedUtc = DateTime.UtcNow,
                Flags = new Dictionary<string, object>(App.FastFlags.Prop),
            };

            File.WriteAllText(SnapshotPath(name), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void Delete(string name)
        {
            string path = SnapshotPath(name);
            if (File.Exists(path))
                File.Delete(path);
        }

        // Replaces the entire current flag set with the snapshot's - clears any key not present
        // in the snapshot too, so applying A then B always leaves you with exactly B, never a
        // merge of whatever you'd hand-tweaked in between.
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
