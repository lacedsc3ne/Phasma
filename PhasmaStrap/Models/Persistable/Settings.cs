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

        // download tuning (Settings > Roblox > Installer, ported from Voidstrap's Installer tab),
        // read directly by Bootstrapper.DownloadPackage/UpgradeRoblox. Defaults exactly reproduce
        // PhasmaStrap's original hardcoded download behaviour - one package at a time, a 4KB read
        // buffer, no segmentation - so raising any of these is opt-in only. See
        // Utility/DownloadConfiguration.cs for the choice lists/normalization and caps.
        public int DownloadBufferKb { get; set; } = 4;
        public int MaxConcurrentDownloads { get; set; } = 1;
        public int MaxDownloadSegments { get; set; } = 1;

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

        // full client-side spoofer (self vs. others), matching Voidstrap's breakdown - distinct
        // from the older single-field UsernameSpoofName above, which rewrites every profile in a
        // response uniformly. See Networking/UsernameSpoofer.cs.
        public string SpoofOthersName { get; set; } = "";
        public bool SpoofOthersApplyIngame { get; set; } = false;
        public bool SpoofOthersVerified { get; set; } = false;
        public string SpoofSelfName { get; set; } = "";
        public bool SpoofSelfApplyIngame { get; set; } = false;
        public bool SpoofSelfVerified { get; set; } = false;
        public bool SpoofSelfGameCreator { get; set; } = false;

        // AssetWarp preloading - learns which assets a game/avatar actually needs and serves them
        // from a local disk cache instead of letting Roblox fetch them fresh every time. See
        // Networking/AssetPreloadCache.cs.
        public bool AssetWarpPreloadEnabled { get; set; } = false;
        public int AssetWarpPreloadCacheMb { get; set; } = 1024;
        public bool AssetWarpPreloadAvatar { get; set; } = false;
        public bool AssetWarpPreloadCrossGame { get; set; } = false;

        // integration configuration
        public bool EnableActivityTracking { get; set; } = true;
        public bool UseDiscordRichPresence { get; set; } = true;
        public bool HideRPCButtons { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = false;

        // Discord shows the presence under the NAME (and icon) of the Discord application it was
        // registered with. On = PhasmaStrap's own application ("Playing PhasmaStrap" with the
        // logo); off = the shared "Roblox" application Bloxstrap uses.
        public bool DiscordShowAsPhasmaStrap { get; set; } = true;
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

        // extra HUD rows on top of FPS - each is an opt-in addition, off by default so the HUD
        // looks exactly as it always has unless the user turns these on individually
        public bool OverlayHudShowFrameTime { get; set; } = false;
        public bool OverlayHudShowCpu { get; set; } = false;
        public bool OverlayHudShowRam { get; set; } = false;
        public bool OverlayHudShowPing { get; set; } = false;

        // manual "hide overlays for a moment" switch - toggled from the Overlays page or a global
        // hotkey (HotkeyActions.ToggleOverlayFocusMode). Only suppresses the informational HUD/
        // crosshair, not the RiShade/Anti-Aliasing/Frame Generation render effects, since those
        // aren't "overlay UI" in the same sense.
        public bool OverlayFocusModeEnabled { get; set; } = false;

        // per-game overlay profile: places with an entry here override the HUD/crosshair enabled
        // state whenever that place is joined, without touching the global toggles above. Keyed by
        // placeId (string, matching EnginePlaceProfiles' convention).
        public Dictionary<string, OverlayPlaceProfile> OverlayPlaceProfiles { get; set; } = new();

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

        // which Roblox gamejoin API version the matchmaker's join-instance probe/resolve requests use
        // (see Matchmaker.BuildJoinRequest) - 1 is the long-stable endpoint, 2 is newer. Change only if
        // joins stop resolving. Ported from Voidstrap's VoidstrapMatchmakerGamejoinApiVersion.
        public int MatchmakerGamejoinApiVersion { get; set; } = 1;

        // auto-rejoin: if Roblox's process disappears while ActivityWatcher.InGame is still true
        // (i.e. no clean disconnect log line was ever seen - see Watcher.Run/TryAutoRejoinAsync),
        // treat it as a crash and relaunch into the same place/server (falls back to same place,
        // any server, if the job ID wasn't captured yet). Off by default since silently relaunching
        // Roblox after any process exit is a meaningful behavior change.
        public bool AutoRejoinOnCrash { get; set; } = false;
        public int AutoRejoinMaxAttempts { get; set; } = 3;
        public int AutoRejoinDelaySeconds { get; set; } = 5;

        // Instant Replay: an always-on rolling capture buffer (see InstantReplayRecorder), saved
        // to a real MP4 clip on demand. Quality: 0=Low (854px wide, 8fps, 2Mbps), 1=Medium
        // (1280px, 12fps, 4Mbps), 2=High (1600px, 20fps, 8Mbps) - see
        // InstantReplayRecorder.CaptureFps/MaxCaptureWidth/BitrateForQuality for the exact values.
        public bool InstantReplayEnabled { get; set; } = false;
        public int InstantReplayClipSeconds { get; set; } = 20;
        public int InstantReplayQuality { get; set; } = 1;
        // target capture rate (15/24/30/60) and the tallest frame to keep (0 = the game's own
        // resolution) - see InstantReplayRecorder's header; Quality above now only picks bitrate
        public int InstantReplayFps { get; set; } = 30;
        public int InstantReplayMaxHeight { get; set; } = 0;

        // disables the RobloxCrashHandler.exe process Roblox spawns alongside the game client, shortly
        // after launch - see Bootstrapper.DisableCrashHandlerIfNeeded. Ported from Voidstrap's DisableCrash.
        public bool DisableRobloxCrashHandler { get; set; } = false;

        // Roblox game window customization (ported from Voidstrap's WindowManipulation) - applied
        // directly to the live Roblox game window via Win32 (SetWindowText/WM_SETICON), not a FastFlag.
        // See Integrations/RobloxWindowCustomizer.cs. Blank RobloxTitle leaves Roblox's own title alone.
        public string RobloxTitle { get; set; } = "";
        public bool CycleTitleWithGameName { get; set; } = false;
        public bool ShowServerInfoInTitle { get; set; } = false;
        public bool UseGameIconForRobloxWindow { get; set; } = true;

        // in-app notification center (NotificationCenter/NotificationToast) - master switch plus
        // per-event-type toggles for the custom toast popup, independent of NotifyIconWrapper's
        // Windows balloon-tip alerts, which are unaffected by these settings
        public bool NotificationsEnabled { get; set; } = true;
        public bool NotificationsJoinToastEnabled { get; set; } = false;
        public bool NotificationsLeaveToastEnabled { get; set; } = false;

        // suppresses the toast popup only (NotificationCenter.ShowToast) while still recording
        // history, so nothing's lost - just not popped up on screen during the session
        public bool DoNotDisturbEnabled { get; set; } = false;

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

        // system-level FPS tweaks that don't touch a single FastFlag (see SystemPerformanceBoost):
        // pin Roblox to the discrete/high-performance GPU, stop Game Bar/DVR from hooking the
        // process, raise the multimedia timer resolution, and use the High performance power plan
        // while a session is active
        public bool ForceHighPerformanceGpu { get; set; } = false;
        public bool DisableGameDVR { get; set; } = false;
        public bool BoostTimerResolution { get; set; } = false;
        public bool UseHighPerformancePowerPlan { get; set; } = false;

        // auto-trims process working sets while playing whenever system memory usage is high (see
        // Utility.AutoRamCleaner) - the unelevated half of the manual "Clean RAM" button, run
        // automatically only when actually needed
        public bool AutoCleanRam { get; set; } = false;

        // launcher memory manager (ported from Voidstrap MemoryManager): tiered memory-pressure
        // handling for PhasmaStrap's own process while it's backgrounded, not Roblox's
        public bool LauncherMemoryManagerEnabled { get; set; } = false;

        // toasts when a friend comes online or starts a game (see Utility.FriendActivityMonitor) -
        // runs for the lifetime of any "normal" PhasmaStrap process, same as AutoCleanRam above,
        // since friends can come online regardless of whether Roblox itself is running
        public bool FriendActivityAlertsEnabled { get; set; } = false;
        public int FriendActivityPollSeconds { get; set; } = 60;

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

        // global hotkey bindings: HotkeyActions.Id -> a WPF gesture string ("Ctrl+Alt+R"), parsed
        // by GlobalHotkeyManager.TryParseGesture. Unbound (missing/empty) actions register nothing.
        public Dictionary<string, string> HotkeyBindings { get; set; } = new();

        // per-place FastFlag presets (Fast Flag Editor page): places with an entry here get that
        // saved snapshot's flags applied for that one launch only - the user's actual saved
        // FastFlags.json is never touched, same as the per-game engine preset scoping this
        // replaced. Keyed by placeId (string), value is a FastFlagSnapshotManager snapshot name.
        // See Bootstrapper.TryApplyFastFlagPlacePresetAsync.
        public Dictionary<string, string> FastFlagPlacePresets { get; set; } = new();

        // bootstrapper theme editor (BootstrapperEditorWindow): remembers which detected external
        // editor (its full .exe path, from Utility.ExternalEditor.Detect) "Open in External Editor"
        // should launch directly next time, skipping the picker dialog. Empty means always ask.
        public string PreferredExternalEditorPath { get; set; } = "";

        // ModsPage "Preset Mod" tab: which Roblox executable(s) file mods and Mod Management
        // packages get applied to - see Bootstrapper.ApplyModifications
        public ModApplyTarget ModApplyTarget { get; set; } = ModApplyTarget.Both;

        // ModsPage "Overlays" tab - Roblox homepage background customization (distinct from the
        // in-game crosshair HUD overlay on OverlaysPage/OverlayCompositor). Off by default.
        public bool HomepageBackgroundEnabled { get; set; } = false;
        public HomepageBackgroundMode HomepageBackgroundMode { get; set; } = HomepageBackgroundMode.None;
        public string HomepageBackgroundColor { get; set; } = "#1A1A2E";
        public string HomepageBackgroundGradientColor { get; set; } = "#16213E";
        public double HomepageBackgroundGradientAngle { get; set; } = 45.0;
    }
}
