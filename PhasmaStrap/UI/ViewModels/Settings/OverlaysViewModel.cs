using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations.Overlays;
using PhasmaStrap.Models.Persistable;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class OverlaysViewModel : NotifyPropertyChangedViewModel
    {
        public bool HudEnabled
        {
            get => App.Settings.Prop.OverlayHudEnabled;
            set { App.Settings.Prop.OverlayHudEnabled = value; OverlayHub.Refresh(); }
        }

        public bool DiagnosticsEnabled
        {
            get => App.Settings.Prop.OverlayDiagnosticsEnabled;
            set { App.Settings.Prop.OverlayDiagnosticsEnabled = value; OnPropertyChanged(nameof(DiagnosticsEnabled)); }
        }

        // --- extra HUD rows (Phase 7). The recorder for these only runs inside an active Watcher
        // session (same process-separation limitation as everywhere else this page's siblings
        // note) - this page only edits Settings.Prop. ---

        public bool ShowFrameTime
        {
            get => App.Settings.Prop.OverlayHudShowFrameTime;
            set => App.Settings.Prop.OverlayHudShowFrameTime = value;
        }

        public bool ShowCpu
        {
            get => App.Settings.Prop.OverlayHudShowCpu;
            set => App.Settings.Prop.OverlayHudShowCpu = value;
        }

        public bool ShowRam
        {
            get => App.Settings.Prop.OverlayHudShowRam;
            set => App.Settings.Prop.OverlayHudShowRam = value;
        }

        public bool ShowPing
        {
            get => App.Settings.Prop.OverlayHudShowPing;
            set => App.Settings.Prop.OverlayHudShowPing = value;
        }

        // --- Overlay Focus Mode: a manual suppress-all switch for the HUD/crosshair, toggleable
        // here or via the Toggle Overlay Focus Mode hotkey (bind one on the Hotkeys page) -
        // there's no reliable general Windows API for detecting "something is capturing my screen
        // right now", so this is deliberately a manual switch rather than automatic detection. ---

        public bool FocusModeEnabled
        {
            get => App.Settings.Prop.OverlayFocusModeEnabled;
            set { App.Settings.Prop.OverlayFocusModeEnabled = value; OverlayHub.Refresh(); }
        }

        // --- per-game overlay profiles: places with an assignment here override HUD/crosshair
        // enabled state while joined to that place, regardless of the global toggles above.
        // Mirrors PerformanceViewModel's per-game engine preset assignment pattern. ---

        public sealed record OverlayPlaceAssignment(string PlaceId, bool HudEnabled, bool CrosshairEnabled)
        {
            public string Display => $"{PlaceId} → HUD {(HudEnabled ? "on" : "off")}, crosshair {(CrosshairEnabled ? "on" : "off")}";
        }

        public ObservableCollection<OverlayPlaceAssignment> PlaceProfiles { get; } = new(
            App.Settings.Prop.OverlayPlaceProfiles.Select(kv => new OverlayPlaceAssignment(kv.Key, kv.Value.HudEnabled, kv.Value.CrosshairEnabled)));

        private string _assignPlaceId = "";

        public string AssignPlaceId
        {
            get => _assignPlaceId;
            set { _assignPlaceId = value; OnPropertyChanged(nameof(AssignPlaceId)); }
        }

        private bool _assignHudEnabled = true;

        public bool AssignHudEnabled
        {
            get => _assignHudEnabled;
            set { _assignHudEnabled = value; OnPropertyChanged(nameof(AssignHudEnabled)); }
        }

        private bool _assignCrosshairEnabled;

        public bool AssignCrosshairEnabled
        {
            get => _assignCrosshairEnabled;
            set { _assignCrosshairEnabled = value; OnPropertyChanged(nameof(AssignCrosshairEnabled)); }
        }

        public ICommand AddPlaceProfileCommand => new RelayCommand(() =>
        {
            string id = AssignPlaceId.Trim();

            if (!long.TryParse(id, out _))
                return;

            var existing = PlaceProfiles.FirstOrDefault(a => a.PlaceId == id);
            if (existing is not null)
                PlaceProfiles.Remove(existing);

            PlaceProfiles.Add(new OverlayPlaceAssignment(id, AssignHudEnabled, AssignCrosshairEnabled));
            App.Settings.Prop.OverlayPlaceProfiles[id] = new OverlayPlaceProfile { HudEnabled = AssignHudEnabled, CrosshairEnabled = AssignCrosshairEnabled };
            AssignPlaceId = "";
        });

        public ICommand RemovePlaceProfileCommand => new RelayCommand<OverlayPlaceAssignment>(assignment =>
        {
            if (assignment is null)
                return;

            PlaceProfiles.Remove(assignment);
            App.Settings.Prop.OverlayPlaceProfiles.Remove(assignment.PlaceId);
        });

        public bool CrosshairEnabled
        {
            get => App.Settings.Prop.Crosshair;
            set { App.Settings.Prop.Crosshair = value; OverlayHub.Refresh(); }
        }

        public int CrosshairShapeIndex
        {
            get => App.Settings.Prop.CrosshairShapeIndex;
            set => App.Settings.Prop.CrosshairShapeIndex = value;
        }

        public int CrosshairSize
        {
            get => App.Settings.Prop.CrosshairSize;
            set => App.Settings.Prop.CrosshairSize = value;
        }

        public int CrosshairLineThickness
        {
            get => App.Settings.Prop.CrosshairLineThickness;
            set => App.Settings.Prop.CrosshairLineThickness = value;
        }

        public int CrosshairGap
        {
            get => App.Settings.Prop.CrosshairGap;
            set => App.Settings.Prop.CrosshairGap = value;
        }

        public double CrosshairOpacity
        {
            get => App.Settings.Prop.CrosshairOpacity;
            set { App.Settings.Prop.CrosshairOpacity = value; OnPropertyChanged(nameof(CrosshairOpacity)); }
        }

        public string CrosshairColorHex
        {
            get => App.Settings.Prop.CrosshairColorHex;
            set => App.Settings.Prop.CrosshairColorHex = value;
        }

        public string CrosshairOutlineColorHex
        {
            get => App.Settings.Prop.CrosshairOutlineColorHex;
            set => App.Settings.Prop.CrosshairOutlineColorHex = value;
        }
    }
}
