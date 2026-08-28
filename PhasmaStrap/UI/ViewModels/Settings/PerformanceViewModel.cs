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
    }
}
