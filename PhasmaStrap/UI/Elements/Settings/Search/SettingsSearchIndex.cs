namespace PhasmaStrap.UI.Elements.Settings.Search
{
    /// <summary>
    /// The complete list of searchable settings. The entries themselves live in the generated
    /// partial (SettingsSearchIndex.g.cs) - see tools/gen_settings_search_index.py, which scans
    /// every page's XAML so nothing has to be kept in sync by hand.
    /// </summary>
    internal static partial class SettingsSearchIndex
    {
        private static IReadOnlyList<SettingsSearchEntry>? _entries;

        public static IReadOnlyList<SettingsSearchEntry> Entries => _entries ??= Load();

        private static IReadOnlyList<SettingsSearchEntry> Load()
        {
            List<SettingsSearchEntry> generated;

            try
            {
                generated = BuildGenerated();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("SettingsSearchIndex", ex);
                return Array.Empty<SettingsSearchEntry>();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<SettingsSearchEntry>(generated.Count);

            foreach (SettingsSearchEntry entry in generated)
            {
                if (entry.Header.Length == 0 || entry.PageType is null)
                    continue;

                // the same header can legitimately appear under different tabs/groups (e.g. "Enabled"),
                // but an exact duplicate of kind+header+location is just noise
                string key = $"{entry.Kind}|{entry.PageType.Name}|{entry.Tab}|{entry.Section}|{entry.Group}|{entry.Header}";
                if (seen.Add(key))
                    result.Add(entry);
            }

            App.Logger.WriteLine("SettingsSearchIndex", $"Indexed {result.Count} searchable settings");
            return result;
        }
    }
}
