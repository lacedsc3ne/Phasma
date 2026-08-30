using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations.FrameGeneration;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class PerformanceViewModel : NotifyPropertyChangedViewModel
    {
        private readonly GBSEditor _gbs = new();

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

        public bool SettingsFileReadOnly
        {
            get => _gbs.GetReadOnly();
            set => _gbs.SetReadOnly(value);
        }

        public int FramerateCap
        {
            get => _gbs.GetInt("FramerateCap", 0);
            set { _gbs.SetInt("FramerateCap", value); _gbs.Save(); }
        }

        public int SavedQualityLevel
        {
            get => _gbs.GetInt("SavedQualityLevel", 10);
            set { _gbs.SetInt("SavedQualityLevel", value); _gbs.Save(); }
        }

        public float MouseSensitivity
        {
            get => _gbs.GetFloat("MouseSensitivity", 0.5f);
            set { _gbs.SetFloat("MouseSensitivity", value); _gbs.Save(); }
        }

        public bool ReducedMotion
        {
            get => _gbs.GetBool("ReducedMotion");
            set { _gbs.SetBool("ReducedMotion", value); _gbs.Save(); }
        }

        public bool VREnabled
        {
            get => _gbs.GetBool("VREnabled");
            set { _gbs.SetBool("VREnabled", value); _gbs.Save(); }
        }

        public bool PerformanceStatsVisible
        {
            get => _gbs.GetBool("PerformanceStatsVisible");
            set { _gbs.SetBool("PerformanceStatsVisible", value); _gbs.Save(); }
        }

        public IEnumerable<CleanerOptions> CleanerScheduleOptions { get; } = Enum.GetValues(typeof(CleanerOptions)).Cast<CleanerOptions>();

        public CleanerOptions CleanerSchedule
        {
            get => App.Settings.Prop.CleanerOptions;
            set => App.Settings.Prop.CleanerOptions = value;
        }

        public IEnumerable<string> CleanerAvailableDirectories { get; } = Cleaner.Directories.Keys;

        public bool CleanerLogsEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("PhasmaStrapLogs");
            set => SetCleanerDirectory("PhasmaStrapLogs", value);
        }

        public bool CleanerCacheEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("PhasmaStrapCache");
            set => SetCleanerDirectory("PhasmaStrapCache", value);
        }

        public bool CleanerRobloxLogsEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("RobloxLogs");
            set => SetCleanerDirectory("RobloxLogs", value);
        }

        public bool CleanerRobloxCacheEnabled
        {
            get => App.Settings.Prop.CleanerDirectories.Contains("RobloxCache");
            set => SetCleanerDirectory("RobloxCache", value);
        }

        private static void SetCleanerDirectory(string key, bool enabled)
        {
            var directories = App.Settings.Prop.CleanerDirectories;

            if (enabled && !directories.Contains(key))
                directories.Add(key);
            else if (!enabled)
                directories.Remove(key);
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

        // --- Roblox process optimizer (ported from Voidstrap RobloxProcessOptimizer) ---

        public bool OptimizeRoblox
        {
            get => App.Settings.Prop.OptimizeRoblox;
            set => App.Settings.Prop.OptimizeRoblox = value;
        }

        public bool RobloxEfficiencyMode
        {
            get => App.Settings.Prop.RobloxEfficiencyMode;
            set => App.Settings.Prop.RobloxEfficiencyMode = value;
        }

        public bool ReduceMemoryOutOfFocus
        {
            get => App.Settings.Prop.ReduceMemoryOutOfFocus;
            set => App.Settings.Prop.ReduceMemoryOutOfFocus = value;
        }

        public IEnumerable<string> CpuPriorityOptions => BuildCpuPriorityOptions();

        public string SelectedCpuPriority
        {
            get => App.Settings.Prop.SelectedCpuPriority;
            set => App.Settings.Prop.SelectedCpuPriority = value;
        }

        public string[] RobloxPriorityLimitOptions { get; } = { "Idle", "Below Normal", "Normal", "Above Normal", "High", "Realtime" };

        public string RobloxPriorityLimit
        {
            get => App.Settings.Prop.RobloxPriorityLimit;
            set
            {
                // Showing a modal confirmation dialog synchronously from inside a ComboBox.SelectedItem
                // binding update is unreliable in WPF - the ComboBox can re-push its already-committed
                // value back to the source once the nested dispatcher frame from ShowDialog() unwinds,
                // silently overwriting any revert attempted from within this same call. Instead, accept
                // the value immediately (letting the binding update finish cleanly), then defer the
                // confirmation to run afterward as its own, non-nested dispatcher operation.
                string previous = App.Settings.Prop.RobloxPriorityLimit;
                App.Settings.Prop.RobloxPriorityLimit = value;

                if (value.Equals("Realtime", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageBoxResult result = Frontend.ShowMessageBox(
                            "Realtime priority makes Roblox preempt almost everything else on your system, including your mouse and keyboard driver. If Roblox spikes CPU usage even briefly, your whole system can freeze or become unresponsive until it settles down. This is not recommended unless you know exactly what you're doing.\n\nSet Roblox to Realtime priority anyway?",
                            MessageBoxImage.Warning,
                            MessageBoxButton.YesNo,
                            MessageBoxResult.No);

                        if (result != MessageBoxResult.Yes && App.Settings.Prop.RobloxPriorityLimit.Equals("Realtime", StringComparison.OrdinalIgnoreCase))
                        {
                            App.Settings.Prop.RobloxPriorityLimit = previous;
                            OnPropertyChanged(nameof(RobloxPriorityLimit));
                        }
                    }), DispatcherPriority.Background);
                }
            }
        }

        // --- system-level FPS tweaks that don't touch a single FastFlag (SystemPerformanceBoost) ---

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

        private static IEnumerable<string> BuildCpuPriorityOptions()
        {
            List<string> options = new() { "Automatic" };
            int processorCount = Environment.ProcessorCount;
            if (processorCount <= IntPtr.Size * 8)
            {
                for (int i = 1; i <= processorCount; i++)
                    options.Add($"{i} Core{(i > 1 ? "s" : "")}");
            }
            return options;
        }

        // --- engine presets (bulk-apply the 5 properties above) + per-game overrides ---

        public string[] EnginePresetNames => Integrations.EnginePresets.PresetNames;

        // one-time apply action, like RiShadeViewModel.SelectedPreset - not a persisted selection,
        // since the individual toggles above may not match any named preset once hand-tweaked
        public string SelectedEnginePreset
        {
            get => "";
            set
            {
                if (string.IsNullOrEmpty(value) || !Integrations.EnginePresets.Presets.TryGetValue(value, out var preset))
                    return;

                Integrations.EnginePresets.Apply(preset, App.Settings.Prop);

                OnPropertyChanged(nameof(OptimizeRoblox));
                OnPropertyChanged(nameof(RobloxEfficiencyMode));
                OnPropertyChanged(nameof(ReduceMemoryOutOfFocus));
                OnPropertyChanged(nameof(SelectedCpuPriority));
                OnPropertyChanged(nameof(RobloxPriorityLimit));
            }
        }

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

        // --- launcher memory manager (ported from Voidstrap MemoryManager) ---

        public bool LauncherMemoryManagerEnabled
        {
            get => App.Settings.Prop.LauncherMemoryManagerEnabled;
            set
            {
                App.Settings.Prop.LauncherMemoryManagerEnabled = value;
                if (value)
                    MemoryManager.Start();
                else
                    MemoryManager.Shutdown();
            }
        }

        // --- render acceleration (reuses the existing WPFSoftwareRender setting) ---

        public bool SoftwareRenderingEnabled
        {
            get => App.Settings.Prop.WPFSoftwareRender;
            set
            {
                App.Settings.Prop.WPFSoftwareRender = value;
                RenderAcceleration.ApplyProcess();
            }
        }

        // --- multi-monitor / forced in-game resolution (ported from Voidstrap DisplaySystem /
        // InGameResolutionApplier) ---

        public List<DisplayInfo> MonitorOptions => DisplaySystem.GetDisplays();

        public DisplayInfo? SelectedMonitor
        {
            get => MonitorOptions.FirstOrDefault(m => m.DeviceName == App.Settings.Prop.InGameResolutionMonitor)
                ?? MonitorOptions.FirstOrDefault(m => m.IsPrimary)
                ?? MonitorOptions.FirstOrDefault();
            set
            {
                App.Settings.Prop.InGameResolutionMonitor = value?.DeviceName ?? "";
                OnPropertyChanged(nameof(SelectedMonitor));
                OnPropertyChanged(nameof(ModeOptions));
                OnPropertyChanged(nameof(SelectedMode));
            }
        }

        public List<DisplayMode> ModeOptions => DisplaySystem.GetModes(SelectedMonitor?.DeviceName);

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
                App.Settings.Prop.ForceInGameResolution = value;
                if (!value)
                    Integrations.ForcedResolution.Shutdown();
            }
        }

        public ICommand IdentifyDisplaysCommand => new RelayCommand(() => DisplaySystem.IdentifyDisplays());
    }
}
