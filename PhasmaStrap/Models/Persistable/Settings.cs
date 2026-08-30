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

        // classic client / private server (ported ClientServer subsystem)
        // master switch - defaults OFF, this redirects roblox.com/www.roblox.com to 127.0.0.1
        // via the hosts file whenever a classic client session is active, which is a significant
        // behaviour change that must require explicit opt-in
        public bool ClassicClientEnabled { get; set; } = false;
        public string ClassicClientInstallLocation { get; set; } = "";
        public string SelectedClassicClient { get; set; } = "";

        // game chat overlay integration
        public bool GameChatEnabled { get; set; } = false;
        public string GameChatServerUrl { get; set; } = "";
        public int GameChatWindowWidth { get; set; } = 500;
        public int GameChatWindowHeight { get; set; } = 400;
        public int GameChatOffsetX { get; set; } = 2;
        public int GameChatOffsetY { get; set; } = 9;
        public string GameChatFilterPreference { get; set; } = "default";
        public long GameChatRobloxUserId { get; set; } = 0;

        // overlays: GPU compositor (HUD, crosshair) drawn on top of the Roblox window
        public bool OverlayHudEnabled { get; set; } = false;
        public bool OverlayDiagnosticsEnabled { get; set; } = true;
        public bool Crosshair { get; set; } = false;
        public int CrosshairShapeIndex { get; set; } = 0;
        public int CrosshairSize { get; set; } = 10;
        public int CrosshairLineThickness { get; set; } = 2;
        public int CrosshairGap { get; set; } = 4;
        public double CrosshairOpacity { get; set; } = 1.0;
        public string CrosshairColorHex { get; set; } = "#00FF00";
        public string CrosshairOutlineColorHex { get; set; } = "#000000";

        // RiShade shader post-processing (ported from Voidstrap, screen-space effects only)
        // defaults to off: this is GPU shader injection and is a significant perf/behaviour change
        public bool RiShadeEnabled { get; set; } = false;
        public RiShadeSettings RiShade { get; set; } = new();

        // anti-aliasing overlay
        public bool AntiAliasingEnabled { get; set; } = false;
        public int AntiAliasingMethodIndex { get; set; } = 0;

        // frame generation (shader-based frame interpolation overlay)
        public int FrameGenModeIndex { get; set; } = 0;
        public int FrameGenQuality { get; set; } = 1;

        // classic client acquisition (Integrations.ClassicClients): where the classic engine/client archives are
        // downloaded from. Left blank uses ClassicClients.DefaultBaseUrl - a third-party GitHub release archive
        // (see the comment on that constant). Only ever used if it resolves to an https:// GitHub releases URL.
        public string ClassicDownloadBaseUrl { get; set; } = "";

        // launch PhasmaStrap (to the settings window, minimized to tray if MinimizeToTrayOnStartup is set) when
        // Windows starts, via a per-user Run registry key - see WindowsRegistry.RegisterStartup/UnregisterStartup
        public bool LaunchAtStartup { get; set; } = false;

        // when closing the settings window while a classic client / matchmaker background session is active,
        // minimize to the tray instead of exiting - see MainWindowViewModel's window-closing handling
        public bool MinimizeToTrayOnClose { get; set; } = false;

        // place IDs the matchmaker should never suggest as a candidate, regardless of MatchmakerAutoCandidates
        public List<string> MatchmakerExcludedPlaces { get; set; } = new();

        // per-game FastFlag profiles: a named bundle of flag overrides (real FFlag name -> value, same shape as
        // FastFlagManager's own Prop) merged on top of the global ClientAppSettings.json at launch, only for
        // places listed in FastFlagPlaceProfiles. See Bootstrapper.TryApplyFastFlagProfileAsync.
        public Dictionary<string, Dictionary<string, object>> FastFlagProfiles { get; set; } = new();

        // place ID (string) -> profile name (key into FastFlagProfiles)
        public Dictionary<string, string> FastFlagPlaceProfiles { get; set; } = new();

        // in-app notification center (NotificationCenter/NotificationToast) - master switch plus
        // per-event-type toggles for the custom toast popup, independent of NotifyIconWrapper's
        // Windows balloon-tip alerts, which are unaffected by these settings
        public bool NotificationsEnabled { get; set; } = true;
        public bool NotificationsJoinToastEnabled { get; set; } = false;
        public bool NotificationsLeaveToastEnabled { get; set; } = false;

        // UI polish (ported from Voidstrap): window backdrop material for wpfui-based windows.
        // Default preserves WpfUiWindow's existing hardcoded Acrylic behaviour.
        public BackdropStyle WindowBackdropStyle { get; set; } = BackdropStyle.Default;

        // cross-fade animation when switching between light/dark theme, instead of an instant cut
        public bool ThemeTransitionEnabled { get; set; } = true;

        // smooth/eased ProgressBar value transitions instead of instant jumps
        public bool SmoothProgressBarsEnabled { get; set; } = true;

        // optional background image (static or animated GIF) behind the settings window content
        public bool GlobalBackgroundEnabled { get; set; } = false;
        public string GlobalBackgroundFilePath { get; set; } = "";
        public double GlobalBackgroundOverlayOpacity { get; set; } = 0.55;

        // decorative animated snow overlay on the settings window (cosmetic, off by default)
        public bool SnowEffectEnabled { get; set; } = false;

        // Roblox process optimizer (ported from Voidstrap RobloxProcessOptimizer): live tuning of
        // the running Roblox process's priority/affinity/working set, separate from CpuCoreLimit
        // above which only restricts PhasmaStrap's own process
        public bool OptimizeRoblox { get; set; } = false;
        public bool RobloxEfficiencyMode { get; set; } = false;
        public bool ReduceMemoryOutOfFocus { get; set; } = false;
        public string SelectedCpuPriority { get; set; } = "Automatic";
        public string RobloxPriorityLimit { get; set; } = "Normal";

        // launcher memory manager (ported from Voidstrap MemoryManager): tiered memory-pressure
        // handling for PhasmaStrap's own process while it's backgrounded, not Roblox's
        public bool LauncherMemoryManagerEnabled { get; set; } = false;

        // forced in-game resolution + multi-monitor targeting (ported from Voidstrap
        // InGameResolutionApplier/DisplaySystem)
        public bool ForceInGameResolution { get; set; } = false;
        public string InGameResolutionMonitor { get; set; } = "";
        public int InGameResolutionWidth { get; set; } = 1920;
        public int InGameResolutionHeight { get; set; } = 1080;
        public int InGameResolutionRefreshRate { get; set; } = 60;

        // app UI colour theme (AppColorTheme, ported from Voidstrap's custom theme editor) - a
        // user-edited colour/brush override merged on top of the active Dark/Light skin, saved to
        // Paths.CustomColorThemeXaml. Unrelated to the pre-existing custom *bootstrapper dialog*
        // theme feature (Paths.CustomThemes / SelectedCustomTheme below).
        public bool CustomColorThemeEnabled { get; set; } = false;

        // app UI font override (ported from Voidstrap's AppFont): absolute path to a .ttf/.otf
        // file applied to PhasmaStrap's own WPF windows, separate from the Roblox client's
        // custom font mod (Paths.CustomFont). Empty means "use the default app font".
        public string AppFontPath { get; set; } = "";

        // runtime machine translation (ported from Voidstrap): auto-translates GameChat overlay
        // messages and, optionally, Discord Rich Presence strings via Google's unofficial
        // translate endpoint. Off by default - sends chat/presence text to an external Google
        // endpoint when enabled. AutoTranslateLanguage defaults to "en" (a no-op target language,
        // matching TranslationService's own short-circuit for "en") so turning AutoTranslate on
        // without picking a language does nothing rather than translating to an unexpected locale.
        public bool AutoTranslate { get; set; } = false;
        public bool RpcAutoTranslate { get; set; } = false;
        public string AutoTranslateLanguage { get; set; } = "en";

        // per-game overrides for the Roblox process optimizer ("engine" settings on the
        // Performance page) - see Integrations/EnginePresets.cs. Place ID (string) -> preset name
        // (one of EnginePresets.Presets' keys); places in EngineExcludedPlaces are never optimized
        // regardless of the global toggles or any assigned preset.
        public Dictionary<string, string> EnginePlaceProfiles { get; set; } = new();
        public List<string> EngineExcludedPlaces { get; set; } = new();

        // scope for FastFlagsPage's curated toggle set ("Engine Settings" - the ~83-toggle panel,
        // not to be confused with the runtime optimizer above despite the similar name). All (the
        // default) applies those flags everywhere as before; the other two modes restrict them to
        // only the listed places, or to every place except the listed ones. See
        // Bootstrapper.TryApplyEngineSettingsScopeAsync.
        public EngineSettingsScopeMode EngineSettingsScope { get; set; } = EngineSettingsScopeMode.All;
        public List<string> EngineSettingsScopedPlaces { get; set; } = new();

        // bootstrapper theme editor (BootstrapperEditorWindow): remembers which detected external
        // editor (its full .exe path, from Utility.ExternalEditor.Detect) "Open in External Editor"
        // should launch directly next time, skipping the picker dialog. Empty means always ask.
        public string PreferredExternalEditorPath { get; set; } = "";
    }
}
