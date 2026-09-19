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

        public bool ShowRegion
        {
            get => App.Settings.Prop.OverlayHudShowRegion;
            set => App.Settings.Prop.OverlayHudShowRegion = value;
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

        // ---- the crosshair editor

        public System.Windows.Media.ImageSource? CrosshairPreview
        {
            get
            {
                var style = PhasmaStrap.Integrations.Overlays.CrosshairStyles.Current;
                using System.Drawing.Bitmap bitmap = PhasmaStrap.Integrations.Overlays.CrosshairRenderer.Render(style, 96);
                var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, 96, 96), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var source = System.Windows.Media.Imaging.BitmapSource.Create(96, 96, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, data.Scan0, data.Stride * 96, data.Stride);
                    source.Freeze();
                    return source;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        public string CrosshairName
        {
            get
            {
                var style = PhasmaStrap.Integrations.Overlays.CrosshairStyles.Current;
                return style.Name.Length > 0 ? style.Name : "Your crosshair";
            }
        }

        public ICommand OpenCrosshairEditorCommand => new RelayCommand(() =>
        {
            var editor = new PhasmaStrap.UI.Elements.Dialogs.CrosshairEditorWindow
            {
                Owner = System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault()
            };

            editor.ShowDialog();

            OnPropertyChanged(nameof(CrosshairPreview));
            OnPropertyChanged(nameof(CrosshairName));
            OverlayHub.Refresh();
        });

        // ---- stream-safe mode (Integrations/Overlays/StreamSafe)

        public bool StreamSafeEnabled
        {
            get => App.Settings.Prop.StreamSafeEnabled;
            set
            {
                if (App.Settings.Prop.StreamSafeEnabled == value)
                    return;
                App.Settings.Prop.StreamSafeEnabled = value;
                OnPropertyChanged(nameof(StreamSafeEnabled));
                OverlayHub.Refresh();
            }
        }

        public int StreamSafeStyleIndex
        {
            get => App.Settings.Prop.StreamSafeStyle == StreamSafe.StyleBlack ? 1 : 0;
            set => App.Settings.Prop.StreamSafeStyle = value == 1 ? StreamSafe.StyleBlack : StreamSafe.StylePixelate;
        }

        public bool StreamSafeCrosshair
        {
            get => App.Settings.Prop.StreamSafeCrosshair;
            set => App.Settings.Prop.StreamSafeCrosshair = value;
        }

        public string StreamSafeSummary
        {
            get
            {
                var names = StreamSafe.Regions.Select((r, i) => r.Name.Length > 0 ? r.Name : $"Area {i + 1}").ToList();
                return names.Count switch
                {
                    0 => "Nothing is marked yet, so the stream shows the whole game.",
                    1 => $"1 area hidden: {names[0]}.",
                    _ => $"{names.Count} areas hidden: {string.Join(", ", names)}.",
                };
            }
        }

        public ICommand OpenStreamSafeEditorCommand => new RelayCommand(() =>
        {
            var editor = new PhasmaStrap.UI.Elements.Dialogs.StreamSafeEditorWindow
            {
                Owner = System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault()
            };

            editor.ShowDialog();
            OnPropertyChanged(nameof(StreamSafeSummary));
        });

        public ICommand CopyStreamViewNameCommand => new RelayCommand(() =>
        {
            try
            {
                System.Windows.Clipboard.SetText(StreamSafe.WindowTitle);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("OverlaysViewModel", $"Could not copy the window name: {ex.Message}");
            }
        });

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
