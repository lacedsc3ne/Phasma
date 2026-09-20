using System.Windows;
using System.Windows.Threading;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        public BehaviourViewModel()
        {
            Matchmaker.PropertyChanged += (_, _) => RefreshSummaries();
        }

        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set
            {
                App.Settings.Prop.ConfirmLaunches = value;
                RefreshSummaries();
            }
        }

        public IEnumerable<CleanerOptions> CleanerScheduleOptions { get; } = Enum.GetValues(typeof(CleanerOptions)).Cast<CleanerOptions>();

        public CleanerOptions CleanerSchedule
        {
            get => App.Settings.Prop.CleanerOptions;
            set
            {
                App.Settings.Prop.CleanerOptions = value;
                RefreshSummaries();
            }
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

        public bool OptimizeRoblox
        {
            get => App.Settings.Prop.OptimizeRoblox;
            set
            {
                App.Settings.Prop.OptimizeRoblox = value;
                RefreshSummaries();
            }
        }

        public bool RobloxEfficiencyMode
        {
            get => App.Settings.Prop.RobloxEfficiencyMode;
            set
            {
                App.Settings.Prop.RobloxEfficiencyMode = value;
                RefreshSummaries();
            }
        }

        public bool ReduceMemoryOutOfFocus
        {
            get => App.Settings.Prop.ReduceMemoryOutOfFocus;
            set
            {
                App.Settings.Prop.ReduceMemoryOutOfFocus = value;
                RefreshSummaries();
            }
        }

        public IEnumerable<string> CpuPriorityOptions => BuildCpuPriorityOptions();

        public string SelectedCpuPriority
        {
            get => App.Settings.Prop.SelectedCpuPriority;
            set
            {
                App.Settings.Prop.SelectedCpuPriority = value;
                RefreshSummaries();
            }
        }

        public string[] RobloxPriorityLimitOptions { get; } = { "Idle", "Below Normal", "Normal", "Above Normal", "High", "Realtime" };

        public string RobloxPriorityLimit
        {
            get => App.Settings.Prop.RobloxPriorityLimit;
            set
            {
                string previous = App.Settings.Prop.RobloxPriorityLimit;
                App.Settings.Prop.RobloxPriorityLimit = value;
                RefreshSummaries();

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
                            RefreshSummaries();
                        }
                    }), DispatcherPriority.Background);
                }
            }
        }

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

        public bool SoftwareRenderingEnabled
        {
            get => App.Settings.Prop.WPFSoftwareRender;
            set
            {
                App.Settings.Prop.WPFSoftwareRender = value;
                RenderAcceleration.ApplyProcess();
            }
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

        public string[] EnginePresetNames => Integrations.EnginePresets.PresetNames;

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
                RefreshSummaries();
            }
        }

        public bool DisableRobloxCrashHandler
        {
            get => App.Settings.Prop.DisableRobloxCrashHandler;
            set
            {
                App.Settings.Prop.DisableRobloxCrashHandler = value;
                RefreshSummaries();
            }
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set
            {
                App.Settings.Prop.BackgroundUpdatesEnabled = value;
                RefreshSummaries();
            }
        }

        public bool IsRobloxInstallationMissing => !App.IsPlayerInstalled && !App.IsStudioInstalled;

        public bool ForceRobloxReinstallation
        {
            get => App.State.Prop.ForceReinstall || IsRobloxInstallationMissing;
            set
            {
                App.State.Prop.ForceReinstall = value;
                RefreshSummaries();
            }
        }

        public ServerBrowserViewModel Matchmaker { get; } = new();

        public string RobloxTitle
        {
            get => App.Settings.Prop.RobloxTitle;
            set
            {
                App.Settings.Prop.RobloxTitle = value ?? "";
                RefreshSummaries();
            }
        }

        public bool CycleTitleWithGameName
        {
            get => App.Settings.Prop.CycleTitleWithGameName;
            set => App.Settings.Prop.CycleTitleWithGameName = value;
        }

        public bool ShowServerInfoInTitle
        {
            get => App.Settings.Prop.ShowServerInfoInTitle;
            set => App.Settings.Prop.ShowServerInfoInTitle = value;
        }

        public bool UseGameIconForRobloxWindow
        {
            get => App.Settings.Prop.UseGameIconForRobloxWindow;
            set => App.Settings.Prop.UseGameIconForRobloxWindow = value;
        }

        public string CurrentEnginePresetName
        {
            get
            {
                Integrations.EnginePresetValues current = Integrations.EnginePresets.FromSettings(App.Settings.Prop);

                foreach (KeyValuePair<string, Integrations.EnginePresetValues> preset in Integrations.EnginePresets.Presets)
                {
                    if (preset.Value == current)
                        return preset.Key;
                }

                return "Custom";
            }
        }

        public string LaunchSummaryConfirm => ConfirmLaunches
            ? "You will be asked to confirm before each launch."
            : "Launches start straight away, with no confirmation prompt.";

        public string LaunchSummaryUpdates
        {
            get
            {
                if (ForceRobloxReinstallation)
                    return "Roblox will be reinstalled from scratch before the game opens.";

                return BackgroundUpdates
                    ? "Roblox will start on the version you already have, and a newer one is fetched in the background."
                    : "Roblox will be brought up to date first if a newer version is out.";
            }
        }

        public string LaunchSummaryMatchmaker
        {
            get
            {
                if (!Matchmaker.MatchmakerEnabled)
                    return "The matchmaker is off, so Roblox picks the server for you.";

                string key = Matchmaker.PreferredDatacenter;
                string region = String.IsNullOrEmpty(key)
                    ? "closest to you"
                    : $"in {Matchmaker.DatacenterChoices.FirstOrDefault(choice => choice.Key == key)?.Display ?? key}";

                string candidates = Matchmaker.MatchmakerAutoCandidates
                    ? "as many candidates as it needs"
                    : $"up to {Matchmaker.MatchmakerMaxCandidates} candidates";

                string empty = Matchmaker.MatchmakerPreferEmpty ? ", preferring emptier ones" : "";

                return $"The matchmaker will pick a server {region}, looking at {candidates}{empty}.";
            }
        }

        public string LaunchSummaryProcess =>
            $"Roblox will run with the {CurrentEnginePresetName.ToLowerInvariant()} process preset, at {RobloxPriorityLimit.ToLowerInvariant()} priority.";

        public string LaunchSummaryRejoin => Matchmaker.AutoRejoinOnCrash
            ? $"If Roblox crashes, PhasmaStrap will rejoin up to {Matchmaker.AutoRejoinMaxAttempts} times, {Matchmaker.AutoRejoinDelaySeconds} seconds apart."
            : "If Roblox crashes, PhasmaStrap will not rejoin for you.";

        public string LaunchingGroupSummary =>
            $"Launch confirmation and the Roblox crash handler. Confirmation is {(ConfirmLaunches ? "on" : "off")}, the crash handler is {(DisableRobloxCrashHandler ? "disabled" : "left alone")}.";

        public string UpdatesGroupSummary =>
            $"Background updates, forced reinstalls, the install location, installed versions and the update channel. Background updates are {(BackgroundUpdates ? "on" : "off")}.";

        public string CleanupGroupSummary =>
            $"Which logs and caches are deleted, and how often. {(CleanerSchedule == CleanerOptions.Never ? "Nothing is cleaned automatically" : "Cleaning runs on a schedule")}.";

        public string ProcessGroupSummary =>
            $"Process preset, CPU priority, memory handling and software rendering. Currently {CurrentEnginePresetName.ToLowerInvariant()}, at {RobloxPriorityLimit.ToLowerInvariant()} priority.";

        public string MatchmakerGroupSummary =>
            $"Server picking, excluded datacenters and places, and rejoining after a crash. The matchmaker is {(Matchmaker.MatchmakerEnabled ? "on" : "off")}.";

        public string WindowTitleGroupSummary =>
            $"The Roblox window title, game name cycling, server info and the window icon. Title: {(String.IsNullOrWhiteSpace(RobloxTitle) ? "Roblox" : RobloxTitle)}.";

        private void RefreshSummaries()
        {
            OnPropertyChanged(nameof(CurrentEnginePresetName));
            OnPropertyChanged(nameof(LaunchSummaryConfirm));
            OnPropertyChanged(nameof(LaunchSummaryUpdates));
            OnPropertyChanged(nameof(LaunchSummaryMatchmaker));
            OnPropertyChanged(nameof(LaunchSummaryProcess));
            OnPropertyChanged(nameof(LaunchSummaryRejoin));
            OnPropertyChanged(nameof(LaunchingGroupSummary));
            OnPropertyChanged(nameof(UpdatesGroupSummary));
            OnPropertyChanged(nameof(CleanupGroupSummary));
            OnPropertyChanged(nameof(ProcessGroupSummary));
            OnPropertyChanged(nameof(MatchmakerGroupSummary));
            OnPropertyChanged(nameof(WindowTitleGroupSummary));
        }
    }
}
