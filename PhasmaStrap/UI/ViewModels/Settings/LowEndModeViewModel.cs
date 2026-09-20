using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class LowEndModeViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<LowEndChange> Changes { get; } = new();

        private readonly LowEndHardware _hardware = LowEndMode.DetectHardware();

        public LowEndModeViewModel()
        {
            _preview = LowEndMode.Active.Length > 0 ? LowEndMode.Active : _hardware.Recommended.Length > 0 ? _hardware.Recommended : LowEndMode.Light;
            RefreshChanges();
        }

        public string HardwareText => "This PC: " + _hardware.Describe() + ".";

        public string RecommendationText => _hardware.Recommended switch
        {
            "" => "It doesn't need low-end mode - Roblox should run well as it is.",
            LowEndMode.Light => "Recommended: Light." + Reasons(),
            _ => "Recommended: Strong." + Reasons(),
        };

        private string Reasons()
        {
            var why = new List<string>();
            if (_hardware.WeakGpu) why.Add(_hardware.IntegratedGpu ? "graphics built into the processor" : "little graphics memory");
            if (_hardware.LowRam) why.Add("under 8 GB of memory");
            if (_hardware.FewThreads) why.Add("4 or fewer processor threads");
            return why.Count > 0 ? " (" + string.Join(", ", why) + ")" : "";
        }

        public string StatusText => LowEndMode.Active.Length == 0
            ? "Off."
            : $"{LowEndMode.Active} is on" + (App.Settings.Prop.LowEndBackupFlags.Count > 0 ? " - press Save to keep it, Turn off puts back what you had before." : ".");

        private string _preview;
        public string Preview { get => _preview; private set { _preview = value; OnPropertyChanged(nameof(Preview)); RefreshChanges(); } }

        public string ActiveLevel => LowEndMode.Active;

        public Visibility TurnOffVisibility => LowEndMode.Active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private void RefreshChanges()
        {
            Changes.Clear();
            foreach (LowEndChange change in LowEndMode.Describe(_preview))
                Changes.Add(change);
            OnPropertyChanged(nameof(ChangesHeader));
        }

        public string ChangesHeader => $"What {_preview} changes:";

        public ICommand ShowCommand => new RelayCommand<string>(level =>
        {
            if (level is LowEndMode.Light or LowEndMode.Strong)
                Preview = level;
        });

        public ICommand ApplyCommand => new RelayCommand<string>(level =>
        {
            if (level is not (LowEndMode.Light or LowEndMode.Strong))
                return;

            LowEndMode.Apply(level);
            Preview = level;
            Changed();
        });

        public ICommand TurnOffCommand => new RelayCommand(() =>
        {
            LowEndMode.Undo();
            Changed();
        });

        private void Changed()
        {
            OnPropertyChanged(nameof(ActiveLevel));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(TurnOffVisibility));

            Integrations.Overlays.OverlayHub.Refresh();
        }
    }
}
