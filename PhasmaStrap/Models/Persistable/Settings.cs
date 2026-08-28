using System.Collections.ObjectModel;

using PhasmaStrap.Networking;

namespace PhasmaStrap.Models.Persistable
{
    public class Settings
    {
        // bloxstrap configuration
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FluentDialog;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconPhasmaStrap;
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public Theme Theme { get; set; } = Theme.Default;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool DeveloperMode { get; set; } = false;
        public bool CheckForUpdates { get; set; } = true;
        public bool ConfirmLaunches { get; set; } = false;
        public string Locale { get; set; } = "nil";
        public bool UseFastFlagManager { get; set; } = true;
        public bool WPFSoftwareRender { get; set; } = false;
        public bool EnableAnalytics { get; set; } = true;
        public bool BackgroundUpdatesEnabled { get; set; } = false;
        public bool DebugDisableVersionPackageCleanup { get; set; } = false;
        public string? SelectedCustomTheme { get; set; } = null;
        public WebEnvironment WebEnvironment { get; set; } = WebEnvironment.Production;

        // channel management
        public string RobloxChannel { get; set; } = "";
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Automatic;
        public string PreferredMirror { get; set; } = "";

        // performance tweaks
        public int CpuCoreLimit { get; set; } = 0;
        public bool FakeExclusiveFullscreen { get; set; } = false;

        // extension manager: extension id -> saved executable path
        public Dictionary<string, string> ExtensionPaths { get; set; } = new();

        // roblox studio companion plugin
        public bool StudioPluginEnabled { get; set; } = false;
        public bool StudioRichPresenceEnabled { get; set; } = false;

        // networking / local asset proxy
        public bool NetworkingProxyEnabled { get; set; } = false;
        public PresenceSpoofMode PresenceSpoofMode { get; set; } = PresenceSpoofMode.Off;
        public string RobuxSpoofAmount { get; set; } = "";
        public string UsernameSpoofName { get; set; } = "";

        // integration configuration
        public bool EnableActivityTracking { get; set; } = true;
        public bool UseDiscordRichPresence { get; set; } = true;
        public bool HideRPCButtons { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = false;
        public bool ShowServerDetails { get; set; } = false;
        public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = new();

        // mod preset configuration
        public bool UseDisableAppPatch { get; set; } = false;

        // cleaner
        public CleanerOptions CleanerOptions { get; set; } = CleanerOptions.Never;
        public List<string> CleanerDirectories { get; set; } = new();

        // server matchmaker
        public bool MatchmakerEnabled { get; set; } = false;
        public bool MatchmakerAutoCandidates { get; set; } = true;
        public int MatchmakerMaxCandidates { get; set; } = 40;
        public bool MatchmakerPreferEmpty { get; set; } = false;
        public string MatchmakerPreferredDatacenter { get; set; } = "";
        public List<string> MatchmakerDisabledDatacenters { get; set; } = new();

        // telemetry blocker
        public bool BlockRobloxTelemetry { get; set; } = false;

        // audio ducking
        public bool DuckRobloxAudioOnUnfocus { get; set; } = false;

        // headset audio compressor
        public bool HeadsetAudioEnabled { get; set; } = false;

        // rojo integration: auto-installed CLI, remembers the last project file used for
        // "rojo serve" so re-launching doesn't require rebrowsing every time
        public string RojoLastProjectPath { get; set; } = "";

        // asset warp: selectively blocks specific asset types (fetched through the local
        // proxy's assetdelivery.roblox.com batch-resolution request) for a performance boost.
        // Off by default - see AssetWarpPolicy.cs for the scoping notes.
        public bool AssetWarpEnabled { get; set; } = false;
        public bool AssetWarpDisableAllTextures { get; set; } = false;
        public bool AssetWarpDisableAllDecals { get; set; } = false;
        public bool AssetWarpDisableAllImages { get; set; } = false;
        public bool AssetWarpDisableAllAnimations { get; set; } = false;
        public bool AssetWarpDisableAllMeshes { get; set; } = false;

        // user-authored per-game Discord Rich Presence templates, applied as the baseline
        // presence when not overridden by a game's own BloxstrapRPC messages
        public ObservableCollection<RPCTemplate> RPCTemplates { get; set; } = new();

        // controller navigation: drive the settings window with an XInput gamepad
        public bool ControllerNavigationEnabled { get; set; } = false;

        // settings window nav rail: PageTag values (explicitly set per-item in MainWindow.xaml,
        // not locale-dependent) pinned to a "Pinned" group at the top of the rail
        public List<string> PinnedNavItems { get; set; } = new();
    }
}
