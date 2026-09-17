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

        private void SavePreset()
        {
            if (string.IsNullOrWhiteSpace(NewPresetName))
                return;

            FastFlagSnapshotManager.Save(NewPresetName.Trim());
            NewPresetName = "";
            RefreshPresets();
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
