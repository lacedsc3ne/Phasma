using System.Windows;
using System.Windows.Threading;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
        }

        // --- cleanup (moved here from PerformancePage - "what to delete after Roblox closes" is a
        // deployment/launch-behavior concern, not a live-performance tweak) ---

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

        // --- Roblox process optimizer (moved here from PerformancePage - ported from Voidstrap
        // RobloxProcessOptimizer) ---

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

        // one-time apply action, like RiShadeViewModel.SelectedPreset - not a persisted selection,
        // since the individual toggles above may not match any named preset once hand-tweaked
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
            }
        }

        // disables the RobloxCrashHandler.exe process Roblox spawns alongside the game client -
        // see Bootstrapper.DisableCrashHandlerIfNeeded. Ported from Voidstrap's DisableCrash.
        public bool DisableRobloxCrashHandler
        {
            get => App.Settings.Prop.DisableRobloxCrashHandler;
            set => App.Settings.Prop.DisableRobloxCrashHandler = value;
        }

        public bool BackgroundUpdates
        {
            get => App.Settings.Prop.BackgroundUpdatesEnabled;
            set => App.Settings.Prop.BackgroundUpdatesEnabled = value;
        }

        public bool IsRobloxInstallationMissing => !App.IsPlayerInstalled && !App.IsStudioInstalled;

        public bool ForceRobloxReinstallation
        {
            get => App.State.Prop.ForceReinstall || IsRobloxInstallationMissing;
            set => App.State.Prop.ForceReinstall = value;
        }

        // the Matchmaker tab reuses ServerBrowserViewModel's existing matchmaker properties
        // (enable/prefer-empty/preferred-datacenter/candidate-count/datacenter grid/excluded
        // games) rather than duplicating that logic here - see ServerBrowserPage's own
        // "Browser" (manual server search/join) section, which is the only part that stayed
        // on that page.
        public ServerBrowserViewModel Matchmaker { get; } = new();

        // --- Roblox tab: live game-window customization (Integrations/RobloxWindowCustomizer) ---

        public string RobloxTitle
        {
            get => App.Settings.Prop.RobloxTitle;
            set => App.Settings.Prop.RobloxTitle = value ?? "";
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
    }
}
