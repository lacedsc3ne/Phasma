using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations.FrameGeneration;
using PhasmaStrap.Models.Persistable;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class PerformanceViewModel : NotifyPropertyChangedViewModel
    {
        public int CpuCoreLimit
        {
            get => App.Settings.Prop.CpuCoreLimit;
            set
            {
                App.Settings.Prop.CpuCoreLimit = value;
                CpuCoreLimiter.SetCpuCoreLimit(value == 0 ? Environment.ProcessorCount : value);
                OnPropertyChanged(nameof(CpuCoreLimitDisplay));
            }
        }

        public int MaxCpuCores { get; } = Environment.ProcessorCount;

        public string CpuCoreLimitDisplay => CpuCoreLimit == 0 ? "All cores" : $"{CpuCoreLimit} core(s)";

        public bool FakeExclusiveFullscreen
        {
            get => App.Settings.Prop.FakeExclusiveFullscreen;
            set
            {
                App.Settings.Prop.FakeExclusiveFullscreen = value;
                if (!value)
                    PhasmaStrap.Integrations.FakeExclusiveFullscreen.Restore();
            }
        }

        public bool DuckRobloxAudioOnUnfocus
        {
            get => App.Settings.Prop.DuckRobloxAudioOnUnfocus;
            set
            {
                App.Settings.Prop.DuckRobloxAudioOnUnfocus = value;
                if (!value)
                    PhasmaStrap.Integrations.AudioDucker.Shutdown();
            }
        }

        public bool HeadsetAudioEnabled
        {
            get => App.Settings.Prop.HeadsetAudioEnabled;
            set
            {
                App.Settings.Prop.HeadsetAudioEnabled = value;
                if (!value)
                    PhasmaStrap.Integrations.HeadsetAudio.Shutdown();
            }
        }

        public bool AntiAliasingEnabled
        {
            get => App.Settings.Prop.AntiAliasingEnabled;
            set
            {
                Integrations.AntiAliasing.AntiAliasingManager.SetEnabled(value);
                OnPropertyChanged(nameof(AntiAliasingEnabled));
            }
        }

        public string[] AntiAliasingMethodNames => Integrations.AntiAliasing.AntiAliasingSettings.MethodNames;

        public int AntiAliasingMethodIndex
        {
            get => Integrations.AntiAliasing.AntiAliasingSettings.MethodIndex;
            set
            {
                if (value < 0)
                    return;

                Integrations.AntiAliasing.AntiAliasingManager.SetMethod(value);
                OnPropertyChanged(nameof(AntiAliasingMethodIndex));
            }
        }

        public bool FrameGenEnabled
        {
            get => FrameGenSettings.ModeIndex > 0;
            set
            {
                bool confirmed = !value;

                if (value)
                {
                    MessageBoxResult result = Frontend.ShowMessageBox(
                        "Frame generation does not improve performance or actual FPS. It is intended only to make motion appear smoother on low end PCs. It will not help mid range or high end systems, so never enable it on those systems.\n\nEnable frame generation anyway?",
                        MessageBoxImage.Warning,
                        MessageBoxButton.YesNo,
                        MessageBoxResult.No);
                    confirmed = result == MessageBoxResult.Yes;
                }

                if (!FrameGenManager.SetMode(value ? 1 : 0, confirmed))
                {
                    OnPropertyChanged(nameof(FrameGenEnabled));
                    return;
                }

                OnPropertyChanged(nameof(FrameGenEnabled));
            }
        }

        public bool FrameLimiterAvailable => Integrations.Nvidia.FrameLimiter.Available;

        public string FrameLimitSteps
        {
            get => App.Settings.Prop.FrameLimitSteps;
            set
            {
                App.Settings.Prop.FrameLimitSteps = value ?? "";
                OnPropertyChanged(nameof(FrameLimitSteps));
                OnPropertyChanged(nameof(FrameLimitStatus));
            }
        }

        public string FrameLimitStatus
        {
            get
            {
                if (!Integrations.Nvidia.FrameLimiter.Available)
                    return Integrations.Nvidia.FrameLimiter.UnavailableReason;

                string order = string.Join(", ", Integrations.Nvidia.FrameLimiter.Steps);
                return $"The driver is set to {Integrations.Nvidia.FrameLimiter.Describe(Integrations.Nvidia.FrameLimiter.Current())}. Stepping through {order}, then no limit.";
            }
        }

        public ICommand RefreshFrameLimitCommand => new RelayCommand(() =>
        {
            OnPropertyChanged(nameof(FrameLimitStatus));
            OnPropertyChanged(nameof(FrameLimiterAvailable));
        });

        public int FrameGenQuality
        {
            get => FrameGenSettings.QualityIndex;
            set
            {
                FrameGenManager.SetQuality(value);
                OnPropertyChanged(nameof(FrameGenQuality));
                OnPropertyChanged(nameof(FrameGenQualityDisplay));
            }
        }

        public string FrameGenQualityDisplay => FrameGenQuality switch
        {
            0 => "Fast",
            2 => "Quality",
            _ => "Balanced",
        };

        public bool ForceHighPerformanceGpu
        {
            get => App.Settings.Prop.ForceHighPerformanceGpu;
            set
            {
                App.Settings.Prop.ForceHighPerformanceGpu = value;
                Integrations.SystemPerformanceBoost.ApplyGpuPreference();
            }
        }

        public bool DisableGameDVR
        {
            get => App.Settings.Prop.DisableGameDVR;
            set
            {
                App.Settings.Prop.DisableGameDVR = value;
                Integrations.SystemPerformanceBoost.ApplyGameDvr();
            }
        }

        public bool BoostTimerResolution
        {
            get => App.Settings.Prop.BoostTimerResolution;
            set => App.Settings.Prop.BoostTimerResolution = value;
        }

        public bool UseHighPerformancePowerPlan
        {
            get => App.Settings.Prop.UseHighPerformancePowerPlan;
            set => App.Settings.Prop.UseHighPerformancePowerPlan = value;
        }

        private bool _cleaningRam;

        public bool CleaningRam
        {
            get => _cleaningRam;
            private set { _cleaningRam = value; OnPropertyChanged(nameof(CleaningRam)); }
        }

        private string _cleanRamStatus = "";

        public string CleanRamStatus
        {
            get => _cleanRamStatus;
            private set { _cleanRamStatus = value; OnPropertyChanged(nameof(CleanRamStatus)); }
        }

        public ICommand CleanRamCommand => new AsyncRelayCommand(async () =>
        {
            if (CleaningRam)
                return;

            CleaningRam = true;
            CleanRamStatus = "Cleaning...";

            PhasmaStrap.Utility.SystemMemoryCleaner.TrimResult trim = await Task.Run(PhasmaStrap.Utility.SystemMemoryCleaner.TrimAllProcessWorkingSets);
            bool purged = await Task.Run(PhasmaStrap.Utility.SystemMemoryCleaner.PurgeStandbyListElevated);

            double freedMb = trim.BytesFreed / 1048576.0;
            CleanRamStatus = purged
                ? $"Trimmed {trim.ProcessesTrimmed} processes and purged the standby list (~{freedMb:0.#} MB reclaimed)."
                : $"Trimmed {trim.ProcessesTrimmed} processes (~{freedMb:0.#} MB reclaimed). Standby list purge needs administrator rights - it was declined or failed.";

            CleaningRam = false;
        });

        public bool AutoCleanRam
        {
            get => App.Settings.Prop.AutoCleanRam;
            set
            {
                App.Settings.Prop.AutoCleanRam = value;

                if (value)
                    PhasmaStrap.Utility.AutoRamCleaner.Start();
                else
                    PhasmaStrap.Utility.AutoRamCleaner.Stop();
            }
        }

        public string[] EnginePresetNames => Integrations.EnginePresets.PresetNames;

        public ObservableCollection<string> EngineExcludedPlaces { get; } = new(App.Settings.Prop.EngineExcludedPlaces);

        private string _engineExcludePlaceId = "";

        public string EngineExcludePlaceId
        {
            get => _engineExcludePlaceId;
            set { _engineExcludePlaceId = value; OnPropertyChanged(nameof(EngineExcludePlaceId)); }
        }

        public ICommand AddEngineExcludedPlaceCommand => new RelayCommand(() =>
        {
            string id = EngineExcludePlaceId.Trim();

            if (!long.TryParse(id, out _) || EngineExcludedPlaces.Contains(id))
                return;

            EngineExcludedPlaces.Add(id);
            App.Settings.Prop.EngineExcludedPlaces.Add(id);
            EngineExcludePlaceId = "";
        });

        public ICommand RemoveEngineExcludedPlaceCommand => new RelayCommand<string>(id =>
        {
            if (id is null)
                return;

            EngineExcludedPlaces.Remove(id);
            App.Settings.Prop.EngineExcludedPlaces.Remove(id);
        });

        public sealed record EnginePlaceAssignment(string PlaceId, string PresetName)
        {
            public string Display => $"{PlaceId} → {PresetName}";
        }

        public ObservableCollection<EnginePlaceAssignment> EngineProfileAssignments { get; } = new(
            App.Settings.Prop.EnginePlaceProfiles.Select(kv => new EnginePlaceAssignment(kv.Key, kv.Value)));

        private string _engineAssignPlaceId = "";

        public string EngineAssignPlaceId
        {
            get => _engineAssignPlaceId;
            set { _engineAssignPlaceId = value; OnPropertyChanged(nameof(EngineAssignPlaceId)); }
        }

        private string _engineAssignPresetName = Integrations.EnginePresets.PresetNames.FirstOrDefault() ?? "";

        public string EngineAssignPresetName
        {
            get => _engineAssignPresetName;
            set { _engineAssignPresetName = value; OnPropertyChanged(nameof(EngineAssignPresetName)); }
        }

        public ICommand AddEngineProfileAssignmentCommand => new RelayCommand(() =>
        {
            string id = EngineAssignPlaceId.Trim();

            if (!long.TryParse(id, out _) || string.IsNullOrEmpty(EngineAssignPresetName))
                return;

            var existing = EngineProfileAssignments.FirstOrDefault(a => a.PlaceId == id);
            if (existing is not null)
                EngineProfileAssignments.Remove(existing);

            EngineProfileAssignments.Add(new EnginePlaceAssignment(id, EngineAssignPresetName));
            App.Settings.Prop.EnginePlaceProfiles[id] = EngineAssignPresetName;
            EngineAssignPlaceId = "";
        });

        public ICommand RemoveEngineProfileAssignmentCommand => new RelayCommand<EnginePlaceAssignment>(assignment =>
        {
            if (assignment is null)
                return;

            EngineProfileAssignments.Remove(assignment);
            App.Settings.Prop.EnginePlaceProfiles.Remove(assignment.PlaceId);
        });

        private List<DisplayInfo>? _monitorOptions;
        public List<DisplayInfo> MonitorOptions => _monitorOptions ??= DisplaySystem.GetDisplays();

        public DisplayInfo? SelectedMonitor
        {
            get => MonitorOptions.FirstOrDefault(m => m.DeviceName == App.Settings.Prop.InGameResolutionMonitor)
                ?? MonitorOptions.FirstOrDefault(m => m.IsPrimary)
                ?? MonitorOptions.FirstOrDefault();
            set
            {
                if (value is null || value.DeviceName == App.Settings.Prop.InGameResolutionMonitor)
                    return;
                App.Settings.Prop.InGameResolutionMonitor = value.DeviceName;
                _modeOptions = null;
                OnPropertyChanged(nameof(SelectedMonitor));
                OnPropertyChanged(nameof(ModeOptions));
                OnPropertyChanged(nameof(SelectedMode));
            }
        }

        private List<DisplayMode>? _modeOptions;
        public List<DisplayMode> ModeOptions => _modeOptions ??= DisplaySystem.GetModes(SelectedMonitor?.DeviceName)
            .OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshRate).ToList();

        public DisplayMode? SelectedMode
        {
            get => ModeOptions.FirstOrDefault(m =>
                m.Width == App.Settings.Prop.InGameResolutionWidth &&
                m.Height == App.Settings.Prop.InGameResolutionHeight &&
                m.RefreshRate == App.Settings.Prop.InGameResolutionRefreshRate);
            set
            {
                if (value == null)
                    return;

                App.Settings.Prop.InGameResolutionWidth = value.Width;
                App.Settings.Prop.InGameResolutionHeight = value.Height;
                App.Settings.Prop.InGameResolutionRefreshRate = value.RefreshRate;
                OnPropertyChanged(nameof(SelectedMode));
            }
        }

        public bool ForceInGameResolution
        {
            get => App.Settings.Prop.ForceInGameResolution;
            set
            {
                if (App.Settings.Prop.ForceInGameResolution == value)
                    return;
                App.Settings.Prop.ForceInGameResolution = value;
                OnPropertyChanged(nameof(ForceInGameResolution));
            }
        }

        public sealed record ResolutionGameChoice(string PlaceId, string Name)
        {
            public override string ToString() => Name;
        }

        public List<ResolutionGameChoice> ResolutionRecentGames { get; } = Integrations.PlayTimeStore.GetAll()
            .Where(e => e.PlaceId > 0)
            .Take(30)
            .Select(e => new ResolutionGameChoice(e.PlaceId.ToString(), $"{(e.Name.Length > 0 ? e.DisplayName : "Place")}  ({e.PlaceId})"))
            .ToList();

        public ResolutionGameChoice? ResolutionPickedGame
        {
            get => null;
            set
            {
                if (value is null)
                    return;
                ResolutionAssignPlaceId = value.PlaceId;
            }
        }

        public sealed record ResolutionPlaceAssignment(string PlaceId, string Game, InGameResolutionProfile Profile)
        {
            public string Display => $"{Game} → {Profile.Width}x{Profile.Height} @ {Profile.RefreshRate}Hz";
        }

        private static string GameNameFor(string placeId) =>
            Integrations.PlayTimeStore.GetAll().FirstOrDefault(e => e.PlaceId.ToString() == placeId) is { Name.Length: > 0 } entry ? entry.DisplayName : $"Place {placeId}";

        public ObservableCollection<ResolutionPlaceAssignment> ResolutionPlaceAssignments { get; } = new(
            App.Settings.Prop.InGameResolutionPlaceProfiles.Select(kv => new ResolutionPlaceAssignment(kv.Key, GameNameFor(kv.Key), kv.Value)));

        private string _resolutionAssignPlaceId = "";
        public string ResolutionAssignPlaceId
        {
            get => _resolutionAssignPlaceId;
            set { _resolutionAssignPlaceId = value; OnPropertyChanged(nameof(ResolutionAssignPlaceId)); }
        }

        private string _resolutionAssignStatus = "";
        public string ResolutionAssignStatus
        {
            get => _resolutionAssignStatus;
            private set { _resolutionAssignStatus = value; OnPropertyChanged(nameof(ResolutionAssignStatus)); }
        }

        public ICommand AddResolutionPlaceCommand => new RelayCommand(() =>
        {
            string id = ResolutionAssignPlaceId.Trim();
            if (!long.TryParse(id, out long place) || place <= 0)
            {
                ResolutionAssignStatus = "Pick a game from the list, or type its place ID (the number in its roblox.com/games/ link).";
                return;
            }

            DisplayMode? mode = SelectedMode;
            if (mode is null)
            {
                ResolutionAssignStatus = "Pick a resolution above first - the game gets that monitor and resolution.";
                return;
            }

            var profile = new InGameResolutionProfile
            {
                Monitor = SelectedMonitor?.DeviceName ?? "",
                Width = mode.Width,
                Height = mode.Height,
                RefreshRate = mode.RefreshRate,
            };

            var existing = ResolutionPlaceAssignments.FirstOrDefault(a => a.PlaceId == id);
            if (existing is not null)
                ResolutionPlaceAssignments.Remove(existing);

            ResolutionPlaceAssignments.Add(new ResolutionPlaceAssignment(id, GameNameFor(id), profile));
            App.Settings.Prop.InGameResolutionPlaceProfiles[id] = profile;
            ResolutionAssignPlaceId = "";
            ResolutionAssignStatus = $"{GameNameFor(id)} will use {mode}. Press Save to keep it.";
        });

        public ICommand RemoveResolutionPlaceCommand => new RelayCommand<ResolutionPlaceAssignment>(assignment =>
        {
            if (assignment is null)
                return;
            ResolutionPlaceAssignments.Remove(assignment);
            App.Settings.Prop.InGameResolutionPlaceProfiles.Remove(assignment.PlaceId);
        });

        public ICommand IdentifyDisplaysCommand => new RelayCommand(() => DisplaySystem.IdentifyDisplays());
    }
}
