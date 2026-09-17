using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class FastFlagPresetRow
    {
        public FastFlagSnapshot Snapshot { get; init; } = null!;
        public string Name => Snapshot.Name;
        public string CreatedDisplay => Snapshot.CreatedUtc.ToLocalTime().ToString("g");
        public int FlagCount => Snapshot.Flags.Count;
    }

    /// <summary>
    /// Backs the Fast Flag Editor page's Presets section: save the current flag set as a named
    /// preset (reusing the same FastFlagSnapshotManager already backing Developer Tools' snapshot
    /// A/B/diff feature - both are just different front ends onto the same saved snapshots), then
    /// assign which preset a specific place ID should launch with. Replaces the old per-game
    /// "Engine Settings" scope toggle - see Bootstrapper.TryApplyFastFlagPlacePresetAsync for where
    /// an assignment actually gets applied, at launch time, to that one session's flag file only.
    /// </summary>
    public sealed class FastFlagPresetsViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<FastFlagPresetRow> Presets { get; } = new();

        private string _newPresetName = "";
        public string NewPresetName
        {
            get => _newPresetName;
            set { _newPresetName = value; OnPropertyChanged(nameof(NewPresetName)); }
        }

        public ICommand SavePresetCommand => new RelayCommand(SavePreset);
        public ICommand DeletePresetCommand => new RelayCommand<FastFlagPresetRow>(DeletePreset);

        public ICommand EditPresetCommand => new RelayCommand<FastFlagPresetRow>(EditPreset);

        private void EditPreset(FastFlagPresetRow? row)
        {
            if (row is null)
                return;

            var dialog = new UI.Elements.Dialogs.PresetEditorDialog(row.Snapshot)
            {
                Owner = System.Windows.Application.Current.Windows.OfType<UI.Elements.Settings.MainWindow>().FirstOrDefault()
            };

            if (dialog.ShowDialog() == true)
            {
                PresetStatus = $"Saved changes to '{row.Name}'.";
                RefreshPresets();
            }
        }

        // When building a preset from the flags selected in the grid: take them OUT of the global
        // list so they only apply where the preset is assigned. This is the whole point of a
        // per-place preset - a flag left in the global list still applies to every game.
        private bool _removeFromGlobal = true;
        public bool RemoveFromGlobal
        {
            get => _removeFromGlobal;
            set { _removeFromGlobal = value; OnPropertyChanged(nameof(RemoveFromGlobal)); }
        }

        private string _presetStatus = "";
        public string PresetStatus
        {
            get => _presetStatus;
            private set { _presetStatus = value; OnPropertyChanged(nameof(PresetStatus)); }
        }

        /// <summary>Raised after flags were removed from the global list so the editor grid reloads.</summary>
        public event EventHandler? GlobalFlagsChanged;

        // "Create preset from all flags" - honours the same toggle as the selected-flags button
        private void SavePreset()
        {
            SavePresetFromFlags(App.FastFlags.Prop.Keys.ToList(), all: true);
        }

        /// <summary>
        /// Saves only the given flags as a preset and (optionally) removes them from the global
        /// flag list, so they take effect only for places the preset is assigned to. Returns true
        /// when the editor's flag list changed and should be reloaded.
        /// </summary>
        public bool SavePresetFromFlags(IReadOnlyList<string> flagNames, bool all = false)
        {
            if (string.IsNullOrWhiteSpace(NewPresetName))
            {
                PresetStatus = "Give the preset a name first.";
                return false;
            }

            if (flagNames.Count == 0)
            {
                PresetStatus = all
                    ? "There are no flags in the list to save."
                    : "Select one or more flags in the list below first (Ctrl+click for several).";
                return false;
            }

            var flags = new List<KeyValuePair<string, object>>();
            foreach (string name in flagNames)
            {
                if (App.FastFlags.Prop.TryGetValue(name, out object? value) && value is not null)
                    flags.Add(new KeyValuePair<string, object>(name, value));
            }

            string presetName = NewPresetName.Trim();
            FastFlagSnapshotManager.Save(presetName, flags);

            bool changedGlobal = false;
            if (RemoveFromGlobal)
            {
                foreach (var flag in flags)
                    App.FastFlags.SetValue(flag.Key, null);

                // persist right away - the preset file was written already, and leaving the global
                // removal pending until the Save button made it look like nothing happened
                App.FastFlags.Save();
                changedGlobal = flags.Count > 0;
            }

            PresetStatus = RemoveFromGlobal
                ? $"Saved '{presetName}' with {flags.Count} flag(s) and removed them from your global flags - they now apply only to places you assign this preset to."
                : $"Saved '{presetName}' with {flags.Count} flag(s). They're still in your global flags too.";

            NewPresetName = "";
            RefreshPresets();

            if (changedGlobal)
                GlobalFlagsChanged?.Invoke(this, EventArgs.Empty);

            return changedGlobal;
        }

        private void DeletePreset(FastFlagPresetRow? row)
        {
            if (row is null)
                return;

            FastFlagSnapshotManager.Delete(row.Name);

            var toRemove = PlaceAssignments.Where(a => a.PresetName == row.Name).ToList();
            foreach (var assignment in toRemove)
            {
                PlaceAssignments.Remove(assignment);
                App.Settings.Prop.FastFlagPlacePresets.Remove(assignment.PlaceId);
            }

            RefreshPresets();
        }

        private void RefreshPresets()
        {
            Presets.Clear();
            foreach (FastFlagSnapshot snapshot in FastFlagSnapshotManager.List())
                Presets.Add(new FastFlagPresetRow { Snapshot = snapshot });
        }

        // --- per-place assignment: which preset (if any) a place launches with ---

        public sealed record PlacePresetAssignment(string PlaceId, string PresetName)
        {
            public string Display => $"{PlaceId} → {PresetName}";
        }

        public ObservableCollection<PlacePresetAssignment> PlaceAssignments { get; } = new(
            App.Settings.Prop.FastFlagPlacePresets.Select(kv => new PlacePresetAssignment(kv.Key, kv.Value)));

        private string _assignPlaceId = "";
        public string AssignPlaceId
        {
            get => _assignPlaceId;
            set { _assignPlaceId = value; OnPropertyChanged(nameof(AssignPlaceId)); }
        }

        private string _assignPresetName = "";
        public string AssignPresetName
        {
            get => _assignPresetName;
            set { _assignPresetName = value; OnPropertyChanged(nameof(AssignPresetName)); }
        }

        public ICommand AddPlaceAssignmentCommand => new RelayCommand(() =>
        {
            string id = AssignPlaceId.Trim();

            if (!long.TryParse(id, out _) || string.IsNullOrEmpty(AssignPresetName))
                return;

            PlacePresetAssignment? existing = PlaceAssignments.FirstOrDefault(a => a.PlaceId == id);
            if (existing is not null)
                PlaceAssignments.Remove(existing);

            PlaceAssignments.Add(new PlacePresetAssignment(id, AssignPresetName));
            App.Settings.Prop.FastFlagPlacePresets[id] = AssignPresetName;
            AssignPlaceId = "";
        });

        public ICommand RemovePlaceAssignmentCommand => new RelayCommand<PlacePresetAssignment>(assignment =>
        {
            if (assignment is null)
                return;

            PlaceAssignments.Remove(assignment);
            App.Settings.Prop.FastFlagPlacePresets.Remove(assignment.PlaceId);
        });

        public FastFlagPresetsViewModel()
        {
            RefreshPresets();
        }
    }
}
