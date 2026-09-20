namespace PhasmaStrap.UI.Elements.Settings.Search
{
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

            foreach (var action in PhasmaStrap.Utility.HotkeyActions.All)
                generated.Add(new SettingsSearchEntry(SettingsSearchEntryKind.Option, action.DisplayName, action.Description, typeof(Pages.HotkeysPage), "Hotkeys", "", "", "", null));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<SettingsSearchEntry>(generated.Count);

            foreach (SettingsSearchEntry entry in generated)
            {
                if (entry.Header.Length == 0 || entry.PageType is null)
                    continue;

                string key = $"{entry.Kind}|{entry.PageType.Name}|{entry.Tab}|{entry.Section}|{entry.Group}|{entry.Header}";
                if (seen.Add(key))
                    result.Add(entry);
            }

            App.Logger.WriteLine("SettingsSearchIndex", $"Indexed {result.Count} searchable settings");
            return result;
        }
    }
}
