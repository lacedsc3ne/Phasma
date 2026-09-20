using System.Collections.ObjectModel;

using PhasmaStrap.Networking;

namespace PhasmaStrap.Models.Persistable
{
    public class Settings
    {
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

        public string RobloxChannel { get; set; } = "";
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Automatic;
        public string PreferredMirror { get; set; } = "";

        public int DownloadBufferKb { get; set; } = 4;
        public int MaxConcurrentDownloads { get; set; } = 1;
        public int MaxDownloadSegments { get; set; } = 1;

        public int CpuCoreLimit { get; set; } = 0;
        public bool FakeExclusiveFullscreen { get; set; } = false;

        public Dictionary<string, string> ExtensionPaths { get; set; } = new();

        public bool StudioPluginEnabled { get; set; } = false;
        public bool StudioRichPresenceEnabled { get; set; } = false;

        public bool NetworkingProxyEnabled { get; set; } = false;
        public PresenceSpoofMode PresenceSpoofMode { get; set; } = PresenceSpoofMode.Off;
        public string RobuxSpoofAmount { get; set; } = "";
        public string UsernameSpoofName { get; set; } = "";

        public string SpoofOthersName { get; set; } = "";
        public bool SpoofOthersApplyIngame { get; set; } = false;
        public bool SpoofOthersVerified { get; set; } = false;
        public string SpoofSelfName { get; set; } = "";
        public bool SpoofSelfApplyIngame { get; set; } = false;
        public bool SpoofSelfVerified { get; set; } = false;
        public bool SpoofSelfGameCreator { get; set; } = false;

        public bool AssetWarpPreloadEnabled { get; set; } = false;
        public int AssetWarpPreloadCacheMb { get; set; } = 1024;
        public bool AssetWarpPreloadAvatar { get; set; } = false;
        public bool AssetWarpPreloadCrossGame { get; set; } = false;

        public bool EnableActivityTracking { get; set; } = true;
        public bool UseDiscordRichPresence { get; set; } = true;
        public bool HideRPCButtons { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = false;

        public bool DiscordShowAsPhasmaStrap { get; set; } = true;

        public bool DiscordNativeJoin { get; set; } = false;

        public string RobloxVersionMode { get; set; } = "Latest";
        public string RobloxPinnedVersion { get; set; } = "";

        public bool RobloxKeepPreviousVersion { get; set; } = false;

        public bool RobloxCheckFlagsAfterUpdate { get; set; } = false;

        public bool AccountGuardEnabled { get; set; } = false;
        public bool AccountGuardBackground { get; set; } = false;

        public string LowEndModeLevel { get; set; } = "";

        public bool StreamSafeEnabled { get; set; } = false;
        public List<PhasmaStrap.Integrations.Overlays.StreamSafeRegion> StreamSafeRegions { get; set; } = PhasmaStrap.Integrations.Overlays.StreamSafe.Defaults();
        public string StreamSafeStyle { get; set; } = "Pixelate";
        public bool StreamSafeCrosshair { get; set; } = true;
        public Dictionary<string, string> LowEndBackupSettings { get; set; } = new();
        public Dictionary<string, string?> LowEndBackupFlags { get; set; } = new();
        public bool ShowServerDetails { get; set; } = false;
        public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = new();

        public bool UseDisableAppPatch { get; set; } = false;

        public bool TopBarPhasmaLogo { get; set; } = true;

        public CleanerOptions CleanerOptions { get; set; } = CleanerOptions.Never;
        public List<string> CleanerDirectories { get; set; } = new();

        public bool MatchmakerEnabled { get; set; } = false;
        public bool MatchmakerAutoCandidates { get; set; } = true;
        public int MatchmakerMaxCandidates { get; set; } = 40;
        public bool MatchmakerPreferEmpty { get; set; } = false;
        public string MatchmakerPreferredDatacenter { get; set; } = "";
        public List<string> MatchmakerDisabledDatacenters { get; set; } = new();

        public bool BlockRobloxTelemetry { get; set; } = false;

        public bool DuckRobloxAudioOnUnfocus { get; set; } = false;

        public bool HeadsetAudioEnabled { get; set; } = false;

        public string RojoLastProjectPath { get; set; } = "";

        public bool AssetWarpEnabled { get; set; } = false;
        public bool AssetWarpDisableAllTextures { get; set; } = false;
        public bool AssetWarpDisableAllDecals { get; set; } = false;
        public bool AssetWarpDisableAllImages { get; set; } = false;
        public bool AssetWarpDisableAllAnimations { get; set; } = false;
        public bool AssetWarpDisableAllMeshes { get; set; } = false;

        public ObservableCollection<RPCTemplate> RPCTemplates { get; set; } = new();

        public bool ControllerNavigationEnabled { get; set; } = false;

        public List<string> PinnedNavItems { get; set; } = new();

        public bool ClassicClientEnabled { get; set; } = false;
        public string ClassicClientInstallLocation { get; set; } = "";
        public string SelectedClassicClient { get; set; } = "";

        public bool GameChatEnabled { get; set; } = false;
        public string GameChatServerUrl { get; set; } = "";
        public int GameChatWindowWidth { get; set; } = 500;
        public int GameChatWindowHeight { get; set; } = 400;
        public int GameChatOffsetX { get; set; } = 2;
        public int GameChatOffsetY { get; set; } = 9;
        public string GameChatFilterPreference { get; set; } = "default";
        public long GameChatRobloxUserId { get; set; } = 0;

        public bool OverlayHudEnabled { get; set; } = false;

        public string OverlayHudPosition { get; set; } = "TopLeft";
        public int OverlayHudOffsetX { get; set; } = 18;
        public int OverlayHudOffsetY { get; set; } = 18;
        public int OverlayHudBackgroundOpacity { get; set; } = 75;
        public string OverlayHudBackgroundColor { get; set; } = "#0C0D10";
        public string OverlayHudLabelColor { get; set; } = "#E2E5E9";
        public string OverlayHudValueColor { get; set; } = "#96E296";
        public int OverlayHudScale { get; set; } = 100;
        public string OverlayHudLayout { get; set; } = "List";
        public bool OverlayHudShowLabels { get; set; } = true;
        public bool OverlayHudTextShadow { get; set; } = true;
        public int OverlayHudCornerRadius { get; set; } = 6;
        public bool OverlayDiagnosticsEnabled { get; set; } = true;
        public bool Crosshair { get; set; } = false;
        public int CrosshairShapeIndex { get; set; } = 0;
        public int CrosshairSize { get; set; } = 10;
        public int CrosshairLineThickness { get; set; } = 2;
        public int CrosshairGap { get; set; } = 4;
        public double CrosshairOpacity { get; set; } = 1.0;
        public string CrosshairColorHex { get; set; } = "#00FF00";
        public string CrosshairOutlineColorHex { get; set; } = "#000000";

        public Integrations.Overlays.CrosshairStyle? CrosshairActive { get; set; } = null;
        public List<Integrations.Overlays.CrosshairStyle> CrosshairLibrary { get; set; } = new();

        public bool OverlayHudShowFrameTime { get; set; } = false;
        public bool OverlayHudShowCpu { get; set; } = false;
        public bool OverlayHudShowRam { get; set; } = false;
        public bool OverlayHudShowPing { get; set; } = false;

        public bool OverlayHudShowRegion { get; set; } = false;

        public bool OverlayHudShowGame { get; set; } = false;

        public bool OverlayHudShowSessionTime { get; set; } = false;

        public bool OverlayFocusModeEnabled { get; set; } = false;

        public Dictionary<string, OverlayPlaceProfile> OverlayPlaceProfiles { get; set; } = new();

        public bool RiShadeEnabled { get; set; } = false;
        public RiShadeSettings RiShade { get; set; } = new();

        public bool AntiAliasingEnabled { get; set; } = false;
        public int AntiAliasingMethodIndex { get; set; } = 0;

        public int FrameGenModeIndex { get; set; } = 0;
        public int FrameGenQuality { get; set; } = 1;

        public string ClassicDownloadBaseUrl { get; set; } = "";

        public bool LaunchAtStartup { get; set; } = false;

        public bool MinimizeToTrayOnClose { get; set; } = false;

        public bool SettingsSidebarCollapsed { get; set; } = false;

        public bool AutoBackupEnabled { get; set; } = false;

        public TrayDoubleClickAction TrayDoubleClickAction { get; set; } = TrayDoubleClickAction.OpenSettings;

        public List<string> MatchmakerExcludedPlaces { get; set; } = new();

        public int MatchmakerGamejoinApiVersion { get; set; } = 1;

        public bool AutoRejoinOnCrash { get; set; } = false;
        public int AutoRejoinMaxAttempts { get; set; } = 3;
        public int AutoRejoinDelaySeconds { get; set; } = 5;

        public bool InstantReplayEnabled { get; set; } = false;
        public int InstantReplayClipSeconds { get; set; } = 20;
        public int InstantReplayQuality { get; set; } = 1;

        public int InstantReplayFps { get; set; } = 30;
        public int InstantReplayMaxHeight { get; set; } = 0;

        public bool InstantReplayGpuEncoding { get; set; } = true;
        public bool InstantReplayAudio { get; set; } = true;
        public bool InstantReplayMicrophone { get; set; } = false;

        public string InstantReplayMicrophoneDevice { get; set; } = "";

        public bool ScreenshotPickArea { get; set; } = false;

        public bool AssetRouteEnabled { get; set; } = false;
        public bool AssetCacheEnabled { get; set; } = true;
        public int AssetCacheLimitMb { get; set; } = 4096;
        public bool TextureShrinkEnabled { get; set; } = false;
        public int TextureShrinkMaxSize { get; set; } = 512;
        public bool SwapPacksEnabled { get; set; } = true;
        public bool TrafficReportEnabled { get; set; } = true;

        public bool JoinServerPickerEnabled { get; set; } = false;

        public bool CrashAnalyzerEnabled { get; set; } = true;

        public bool SessionHistoryEnabled { get; set; } = true;
        public bool SessionTrackFriends { get; set; } = false;

        public bool FriendActivityFavouritesOnly { get; set; } = false;

        public bool CaptureCopyScreenshotToClipboard { get; set; } = true;
        public bool CaptureCopyReplayToClipboard { get; set; } = false;

        public int CaptureStorageLimitMB { get; set; } = 0;
        public int CaptureMaxAgeDays { get; set; } = 0;

        public bool DisableRobloxCrashHandler { get; set; } = false;

        public string RobloxTitle { get; set; } = "";
        public bool CycleTitleWithGameName { get; set; } = false;
        public bool ShowServerInfoInTitle { get; set; } = false;
        public bool UseGameIconForRobloxWindow { get; set; } = true;

        public bool NotificationsEnabled { get; set; } = true;
        public bool NotificationsJoinToastEnabled { get; set; } = false;
        public bool NotificationsLeaveToastEnabled { get; set; } = false;

        public bool NotificationServerRegionEnabled { get; set; } = true;
        public bool NotificationRobloxClosedEnabled { get; set; } = true;
        public bool NotificationAutoRejoinEnabled { get; set; } = true;
        public bool NotificationFastFlagProfileEnabled { get; set; } = true;
        public bool NotificationFrameRateLimitEnabled { get; set; } = true;
        public bool NotificationRamCleanedEnabled { get; set; } = true;
        public bool NotificationOverlayFocusEnabled { get; set; } = true;
        public bool NotificationPerformanceRunEnabled { get; set; } = true;
        public bool NotificationInviteLinkEnabled { get; set; } = true;
        public bool NotificationScreenshotEnabled { get; set; } = true;
        public bool NotificationReplayEnabled { get; set; } = true;
        public bool NotificationReplayNotReadyEnabled { get; set; } = true;
        public bool NotificationFriendOnlineEnabled { get; set; } = true;
        public bool NotificationFriendPlayingEnabled { get; set; } = true;
        public bool NotificationAccountGuardEnabled { get; set; } = true;
        public bool NotificationProxyCertificateEnabled { get; set; } = true;
        public bool NotificationFlagsRemovedEnabled { get; set; } = true;

        public bool DoNotDisturbEnabled { get; set; } = false;

        public string NotificationPosition { get; set; } = "TopRight";
        public int NotificationOffsetX { get; set; } = 10;
        public int NotificationOffsetY { get; set; } = 10;
        public int NotificationWidth { get; set; } = 420;

        public BackdropStyle WindowBackdropStyle { get; set; } = BackdropStyle.Default;

        public bool ThemeTransitionEnabled { get; set; } = true;

        public bool SmoothProgressBarsEnabled { get; set; } = true;

        public bool GlobalBackgroundEnabled { get; set; } = false;
        public string GlobalBackgroundFilePath { get; set; } = "";
        public double GlobalBackgroundOverlayOpacity { get; set; } = 0.55;

        public bool GlobalBackgroundVideoPauseInactive { get; set; } = true;

        public bool SnowEffectEnabled { get; set; } = false;

        public bool OptimizeRoblox { get; set; } = false;
        public bool RobloxEfficiencyMode { get; set; } = false;
        public bool ReduceMemoryOutOfFocus { get; set; } = false;
        public string SelectedCpuPriority { get; set; } = "Automatic";
        public string RobloxPriorityLimit { get; set; } = "Normal";

        public bool ForceHighPerformanceGpu { get; set; } = false;
        public bool DisableGameDVR { get; set; } = false;
        public bool BoostTimerResolution { get; set; } = false;
        public bool UseHighPerformancePowerPlan { get; set; } = false;

        public bool AutoCleanRam { get; set; } = false;

        public bool LauncherMemoryManagerEnabled { get; set; } = false;

        public bool FriendActivityAlertsEnabled { get; set; } = false;
        public int FriendActivityPollSeconds { get; set; } = 60;

        public bool ForceInGameResolution { get; set; } = false;
        public string InGameResolutionMonitor { get; set; } = "";
        public int InGameResolutionWidth { get; set; } = 1920;
        public int InGameResolutionHeight { get; set; } = 1080;
        public int InGameResolutionRefreshRate { get; set; } = 60;

        public Dictionary<string, InGameResolutionProfile> InGameResolutionPlaceProfiles { get; set; } = new();

        public bool CustomColorThemeEnabled { get; set; } = false;

        public string AppFontPath { get; set; } = "";

        public bool AutoTranslate { get; set; } = false;
        public bool RpcAutoTranslate { get; set; } = false;
        public string AutoTranslateLanguage { get; set; } = "en";

        public Dictionary<string, string> EnginePlaceProfiles { get; set; } = new();
        public List<string> EngineExcludedPlaces { get; set; } = new();

        public Dictionary<string, string> HotkeyBindings { get; set; } = new();

        public Dictionary<string, string> FastFlagPlacePresets { get; set; } = new();

        public bool FastFlagPresetCloseRunningRoblox { get; set; } = false;

        public string FrameLimitSteps { get; set; } = "60,120,144,240";

        public string PreferredExternalEditorPath { get; set; } = "";

        public ModApplyTarget ModApplyTarget { get; set; } = ModApplyTarget.Both;

        public bool HomepageBackgroundEnabled { get; set; } = false;
        public HomepageBackgroundMode HomepageBackgroundMode { get; set; } = HomepageBackgroundMode.None;
        public string HomepageBackgroundColor { get; set; } = "#1A1A2E";
        public string HomepageBackgroundGradientColor { get; set; } = "#16213E";
        public double HomepageBackgroundGradientAngle { get; set; } = 45.0;
    }
}
