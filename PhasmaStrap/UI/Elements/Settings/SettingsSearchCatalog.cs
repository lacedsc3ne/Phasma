using PhasmaStrap.UI.Elements.Settings.Pages;

namespace PhasmaStrap.UI.Elements.Settings
{
    /// <summary>
    /// A single searchable entry in the settings window - one option/control (or an entire page,
    /// for pages that don't have individually-labelled options) that the user can jump straight to.
    /// </summary>
    internal sealed class SettingsSearchEntry
    {
        /// <summary>
        /// The option's own label, e.g. "Confirm launches".
        /// </summary>
        public string Header = "";

        /// <summary>
        /// The option's helper text, if any. Included in the search text but not shown as a separate line.
        /// </summary>
        public string Description = "";

        /// <summary>
        /// The settings page this option lives on.
        /// </summary>
        public Type PageType = null!;

        /// <summary>
        /// The nav item label for <see cref="PageType"/>, e.g. "Behaviour".
        /// </summary>
        public string PageName = "";

        /// <summary>
        /// Text shown in the search dropdown for this entry.
        /// </summary>
        public string DisplayText => string.IsNullOrEmpty(Header) ? PageName : $"{Header}  -  {PageName}";
    }

    /// <summary>
    /// Hand-maintained index of every option shown across the Settings window's pages, used to power
    /// the top-of-nav search box (see <see cref="MainWindow"/>).
    /// </summary>
    /// <remarks>
    /// Voidstrap builds its equivalent catalog from a build-time-generated schema file
    /// (SettingsSchemaImporter/SettingsCatalogImporter) plus a runtime reflection pass over each page's
    /// visual tree, because it indexes many hundreds of FastFlags/behaviour/mod options spread across a
    /// much larger settings surface. PhasmaStrap's settings surface is comparatively small and has no such
    /// schema pipeline, so this is a plain hand-maintained list instead - it costs a small amount of upkeep
    /// (a new OptionControl needs a matching entry added here) in exchange for not needing to build or
    /// maintain an entire schema-import/reflection pipeline for a couple hundred entries.
    ///
    /// Headers/descriptions are read directly from the same <c>Strings.*</c> resource properties the pages'
    /// XAML binds to (via <c>{x:Static resources:Strings.Foo}</c>) where one exists, so this file stays in
    /// sync with the UI text automatically and with any active translation. Options that were added without
    /// a resource entry (most FastFlags toggles, and everything on the newer Networking/Servers/History/
    /// NVIDIA pages) use their literal XAML header/description text instead, same as those pages do.
    /// </remarks>
    internal static class SettingsSearchCatalog
    {
        public static IReadOnlyList<SettingsSearchEntry> Entries { get; } = Build();

        private static SettingsSearchEntry Entry(string header, string description, Type pageType, string pageName) =>
            new()
            {
                Header = header ?? "",
                Description = description ?? "",
                PageType = pageType,
                PageName = pageName
            };

        private static List<SettingsSearchEntry> Build()
        {
            string integrations = Strings.Menu_Integrations_Title;
            string behaviour = Strings.Menu_Behaviour_Title;
            string mods = Strings.Menu_Mods_Title;
            string extensions = "Extensions";
            string fastFlags = Strings.Menu_FastFlags_Title;
            string fastFlagProfiles = Strings.Menu_FastFlagProfiles_Title;
            string channel = "Channel";
            string performance = "Performance";
            string nvidia = "NVIDIA";
            string networking = "Networking";
            string servers = "Servers";
            string history = "History";
            string appearance = Strings.Menu_Appearance_Title;
            string shortcuts = Strings.Common_Shortcuts;
            string phasmaStrap = "PhasmaStrap";

            var entries = new List<SettingsSearchEntry>
            {
                // Integrations
                Entry(Strings.Menu_Integrations_EnableActivityTracking_Title, Strings.Menu_Integrations_EnableActivityTracking_Description, typeof(IntegrationsPage), integrations),
                Entry(Strings.Menu_Integrations_QueryServerLocation_Title, Strings.Menu_Integrations_QueryServerLocation_Description, typeof(IntegrationsPage), integrations),
                Entry(Strings.Menu_Integrations_DesktopApp_Title, Strings.Menu_Integrations_DesktopApp_Description, typeof(IntegrationsPage), integrations),
                Entry(Strings.Menu_Integrations_ShowGameActivity_Title, Strings.Menu_Integrations_ShowGameActivity_Description, typeof(IntegrationsPage), integrations),
                Entry(Strings.Menu_Integrations_AllowActivityJoining_Title, Strings.Menu_Integrations_AllowActivityJoining_Description, typeof(IntegrationsPage), integrations),
                Entry(Strings.Menu_Integrations_ShowAccountOnProfile_Title, Strings.Menu_Integrations_ShowAccountOnProfile_Description, typeof(IntegrationsPage), integrations),
                Entry(Strings.Menu_Integrations_Custom_Title, Strings.Menu_Integrations_Custom_Description, typeof(IntegrationsPage), integrations),
                Entry("Custom Rich Presence Templates", "Author your own Discord Rich Presence text/images/button for a specific game.", typeof(IntegrationsPage), integrations),

                // Behaviour
                Entry(Strings.Menu_Behaviour_ConfirmLaunches_Title, Strings.Menu_Behaviour_ConfirmLaunches_Description, typeof(BehaviourPage), behaviour),
                Entry(Strings.Menu_Behaviour_BackgroundUpdates_Title, Strings.Menu_Behaviour_BackgroundUpdates_Description, typeof(BehaviourPage), behaviour),
                Entry(Strings.Menu_Behaviour_ForceRobloxReinstall_Title, Strings.Menu_Behaviour_ForceRobloxReinstall_Description, typeof(BehaviourPage), behaviour),

                // Mods
                Entry(Strings.Menu_Mods_OpenModsFolder_Title, Strings.Menu_Mods_OpenModsFolder_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Misc_CompatibilitySettings_Title, "", typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_MouseCursor_Title, Strings.Menu_Mods_Presets_MouseCursor_Description, typeof(ModsPage), mods),
                Entry("Custom Cursor Set", "Apply your own cursor images from a local folder instead of a bundled preset.", typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_OldAvatarEditor_Title, Strings.Menu_Mods_Presets_OldAvatarEditor_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_OldCharacterSounds_Title, Strings.Menu_Mods_Presets_OldCharacterSounds_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_EmojiType_Title, Strings.Menu_Mods_Presets_EmojiType_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Misc_CustomFont_Title, "", typeof(ModsPage), mods),

                // Extensions
                Entry("Rojo", "Syncs external files into Roblox Studio for development. PhasmaStrap downloads and manages it automatically.", typeof(ExtensionsPage), extensions),
                Entry("", "Locate and launch third-party tools alongside Roblox.", typeof(ExtensionsPage), extensions),

                // Engine Settings (FastFlags) - general
                Entry(Strings.Menu_FastFlags_ManagerEnabled_Title, Strings.Menu_FastFlags_ManagerEnabled_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Presets_MSAA_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Presets_FixDisplayScaling_Title, Strings.Menu_FastFlags_Presets_FixDisplayScaling_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Presets_TextureQuality_Title, "", typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Telemetry
                Entry(Strings.Menu_FastFlags_DisableTelemetry_Title, Strings.Menu_FastFlags_DisableTelemetry_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableWebview2Telemetry_Title, Strings.Menu_FastFlags_DisableWebview2Telemetry_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableVoiceChatTelemetry_Title, Strings.Menu_FastFlags_DisableVoiceChatTelemetry_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_BlockTencent_Title, Strings.Menu_FastFlags_BlockTencent_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_PingBreakdown_Title, Strings.Menu_FastFlags_PingBreakdown_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ShowChunks_Title, Strings.Menu_FastFlags_ShowChunks_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FlagState_Title, Strings.Menu_FastFlags_FlagState_Description, typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Voice / Chat
                Entry(Strings.Menu_FastFlags_ChatBubbles_Title, Strings.Menu_FastFlags_ChatBubbles_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ChatTranslation_Title, Strings.Menu_FastFlags_ChatTranslation_Description, typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Rendering
                Entry(Strings.Menu_FastFlags_LightCulling_Title, Strings.Menu_FastFlags_LightCulling_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RainbowTheme_Title, Strings.Menu_FastFlags_RainbowTheme_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FRMQualityOverride_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FRMQualityOverrideLevel_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MeshQuality_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MeshQualityLevel_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_UnlimitedCameraZoom_Title, Strings.Menu_FastFlags_UnlimitedCameraZoom_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_BGRATextureSupport_Title, Strings.Menu_FastFlags_BGRATextureSupport_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_NewFpsSystem_Title, Strings.Menu_FastFlags_NewFpsSystem_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_WorserParticles_Title, Strings.Menu_FastFlags_WorserParticles_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_LowPolyMeshes_Title, Strings.Menu_FastFlags_LowPolyMeshes_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RenderingMode_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MoreLighting_Title, Strings.Menu_FastFlags_MoreLighting_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MinGrassDistance_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MaxGrassDistance_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_GrassMovementFactor_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_IGMenuVersion_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_LightingMode_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FullscreenTitlebarDelay_Title, Strings.Menu_FastFlags_FullscreenTitlebarDelay_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_TextureSkipping_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DistanceRendering_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DynamicResolution_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RomarkStartGraphic_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FRMQualityLevel_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisablePostFX_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_AvoidTaskSchedulerSleep_Title, Strings.Menu_FastFlags_AvoidTaskSchedulerSleep_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisablePlayerShadows_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RenderOcclusionChecks_Title, Strings.Menu_FastFlags_RenderOcclusionChecks_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_GraySky_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_WhiteSky_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RedFontDebugHighlight_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableLayeredClothing_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableTerrainTextures_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Prerender_Title, Strings.Menu_FastFlags_Prerender_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ForceBuggyVulkan_Title, Strings.Menu_FastFlags_ForceBuggyVulkan_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_BypassVulkan_Title, Strings.Menu_FastFlags_BypassVulkan_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ChromeUI_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_OldChromeUI_Title, Strings.Menu_FastFlags_OldChromeUI_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ShaderGlintLevel_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ShadersEnabled_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ShadersRoughnessClamp_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_TargetRefreshRate_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MinimalRendering_Title, Strings.Menu_FastFlags_MinimalRendering_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableSkyBloom_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FramerateBufferPercentage_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FramerateLimitFFlag_Title, Strings.Menu_FastFlags_FramerateLimitFFlag_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Pseudolocalization_Title, Strings.Menu_FastFlags_Pseudolocalization_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisplayFpsFFlag_Title, Strings.Menu_FastFlags_DisplayFpsFFlag_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_GrayAvatarThumbnails_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_UIFontSizePadding_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_HideGUIGroup_Title, Strings.Menu_FastFlags_HideGUIGroup_Description, typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Networking
                Entry(Strings.Menu_FastFlags_LessLagSpikes_Title, Strings.Menu_FastFlags_LessLagSpikes_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RobloxCoreTuning_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_NoPayloadLimit_Title, Strings.Menu_FastFlags_NoPayloadLimit_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_EnableLargeReplicator_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FasterLoading_Title, Strings.Menu_FastFlags_FasterLoading_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_BetterPacketSending_Title, Strings.Menu_FastFlags_BetterPacketSending_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MtuSize_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ResendBufferArrayLength_Title, "", typeof(FastFlagsPage), fastFlags),

                // Engine Settings - UI / Misc
                Entry(Strings.Menu_FastFlags_MoreSensitivityNumbers_Title, Strings.Menu_FastFlags_MoreSensitivityNumbers_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_NoGuiBlur_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_PreferredTextSizeScaling_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_TextureRemover_Title, Strings.Menu_FastFlags_TextureRemover_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ThreadingDebugCheck_Title, Strings.Menu_FastFlags_ThreadingDebugCheck_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableAds_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_EnableDarkMode_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_RemoveMiddleDetailsPage_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_PreloadAssets_Title, Strings.Menu_FastFlags_PreloadAssets_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_OptimizeCFrameUpdates_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_CustomDisconnectError_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_CustomDisconnectErrorMessage_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_FakeVerify_Title, Strings.Menu_FastFlags_FakeVerify_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_NewCameraControls_Title, Strings.Menu_FastFlags_NewCameraControls_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_ChatUIOverride_Title, Strings.Menu_FastFlags_ChatUIOverride_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_OldRobloxStudioCoreUI_Title, Strings.Menu_FastFlags_OldRobloxStudioCoreUI_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_AlwaysShowVRToggle_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_DisableFeedbackSoothsayerCheck_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_LanguageSelector_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_HapticsToggle_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_InMenuFramerateCapToggle_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MemoryProbing_Title, Strings.Menu_FastFlags_MemoryProbing_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_CacheSizeImprovement_Title, Strings.Menu_FastFlags_CacheSizeImprovement_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_CPUThreadsUsed_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_MinimumCPUCoreThreadCount_Title, "", typeof(FastFlagsPage), fastFlags),

                Entry(Strings.Menu_FastFlags_Reset_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlagEditor_Title, Strings.Menu_FastFlags_Editor_Description, typeof(FastFlagsPage), fastFlags),

                // FastFlag Profiles
                Entry(Strings.Menu_FastFlagProfiles_Title, Strings.Menu_FastFlagProfiles_Description, typeof(FastFlagProfilesPage), fastFlagProfiles),
                Entry(Strings.Menu_FastFlagProfiles_PlacesHeader, Strings.Menu_FastFlagProfiles_PlacesDescription, typeof(FastFlagProfilesPage), fastFlagProfiles),

                // Channel
                Entry("Currently active channel", "Which Roblox deployment channel is currently running.", typeof(ChannelPage), channel),
                Entry("Roblox channel", "Leave blank to use Roblox's default (production) channel.", typeof(ChannelPage), channel),
                Entry("If Roblox's channel changes", "Automatic, Prompt, or Ignore.", typeof(ChannelPage), channel),
                Entry("Preferred mirror", "Which Roblox CDN mirror to download from. Auto picks whichever responds fastest.", typeof(ChannelPage), channel),
                Entry("Update heatmap", "Shows which days of the week an experience typically ships updates on, from its public badge-award history.", typeof(ChannelPage), channel),

                // Performance - System
                Entry("CPU core limit", "Restricts how many logical processors PhasmaStrap itself can use.", typeof(PerformancePage), performance),
                Entry("Fake exclusive fullscreen", "Strips the Roblox window's border for lower input latency than Roblox's own windowed fullscreen.", typeof(PerformancePage), performance),
                Entry("Duck Roblox audio when unfocused", "Automatically lowers Roblox's volume while you're tabbed away from it.", typeof(PerformancePage), performance),
                Entry("Headset loudness", "Boosts quiet sounds without letting loud moments blow out your ears.", typeof(PerformancePage), performance),

                // Performance - Roblox game settings
                Entry("Lock settings file", "Marks the file read-only so Roblox can't silently overwrite these changes.", typeof(PerformancePage), performance),
                Entry("Framerate cap", "0 leaves it uncapped.", typeof(PerformancePage), performance),
                Entry("Graphics quality level", "0 to 10, where 10 is Roblox's highest preset.", typeof(PerformancePage), performance),
                Entry("Mouse sensitivity", "", typeof(PerformancePage), performance),
                Entry("Reduced motion", "Cuts down on in-game UI animation.", typeof(PerformancePage), performance),
                Entry("VR enabled", "", typeof(PerformancePage), performance),
                Entry("Show performance stats", "Roblox's built-in FPS/ping/memory overlay.", typeof(PerformancePage), performance),

                // Performance - Cleanup
                Entry("Schedule", "How often old PhasmaStrap/Roblox files are automatically deleted after Roblox closes.", typeof(PerformancePage), performance),
                Entry("PhasmaStrap logs", "", typeof(PerformancePage), performance),
                Entry("PhasmaStrap download cache", "", typeof(PerformancePage), performance),
                Entry("Roblox logs", "", typeof(PerformancePage), performance),
                Entry("Roblox cache", "", typeof(PerformancePage), performance),

                // NVIDIA
                Entry("Low Latency Mode", "NVIDIA Reflex-style control panel setting that reduces input latency.", typeof(NvidiaPage), nvidia),
                Entry("FRL Low Latency Mode", "Couples the frame rate limiter to the driver's low-latency frame pacing path.", typeof(NvidiaPage), nvidia),
                Entry("Frame rate limiter", "Driver-level frame cap for Roblox, in FPS.", typeof(NvidiaPage), nvidia),
                Entry("Background frame rate limit", "Separate driver-level FPS cap applied only while Roblox is unfocused/minimized.", typeof(NvidiaPage), nvidia),
                Entry("Resizable BAR", "Lets the CPU access the entire GPU memory range at once, if supported.", typeof(NvidiaPage), nvidia),
                Entry("DLSS Super Resolution override", "Forces the driver-level DLSS Super Resolution override on.", typeof(NvidiaPage), nvidia),
                Entry("DLSS Frame Generation override", "Forces the driver-level DLSS Frame Generation override on.", typeof(NvidiaPage), nvidia),
                Entry("MFAA", "Multi-Frame sampled Anti-Aliasing, a driver-level AA technique.", typeof(NvidiaPage), nvidia),
                Entry("FXAA", "Driver-level Fast Approximate Anti-Aliasing.", typeof(NvidiaPage), nvidia),
                Entry("Gamma correction", "Applies gamma-correct antialiasing/blending at the driver level.", typeof(NvidiaPage), nvidia),
                Entry("SILK smoothness", "NVIDIA's driver-level frame smoothing/pacing filter.", typeof(NvidiaPage), nvidia),
                Entry("Texture filtering LOD bias", "", typeof(NvidiaPage), nvidia),

                // Networking
                Entry("Enable local proxy", "Local TLS proxy required for presence spoofing, telemetry blocking, and AssetWarp below.", typeof(NetworkingPage), networking),
                Entry("Local proxy certificate", "Certificate trusted by your account to avoid TLS errors when the proxy terminates Roblox's HTTPS connections.", typeof(NetworkingPage), networking),
                Entry("Presence mode", "Changes what session type Roblox's own servers think you're connecting from.", typeof(NetworkingPage), networking),
                Entry("Displayed balance", "Shows a different Robux balance on your own screen only.", typeof(NetworkingPage), networking),
                Entry("Display name override", "Overrides the name shown in-game for any profile PhasmaStrap looks up.", typeof(NetworkingPage), networking),
                Entry("Block Roblox telemetry", "Blackholes Roblox's own telemetry and crash-upload domains at the hosts-file level.", typeof(NetworkingPage), networking),
                Entry("Enable AssetWarp", "Blocks whole categories of asset from loading in-game, for a performance boost.", typeof(NetworkingPage), networking),
                Entry("Block textures", "", typeof(NetworkingPage), networking),
                Entry("Block decals", "", typeof(NetworkingPage), networking),
                Entry("Block images", "", typeof(NetworkingPage), networking),
                Entry("Block animations", "", typeof(NetworkingPage), networking),
                Entry("Block meshes", "", typeof(NetworkingPage), networking),

                // Servers (matchmaker + server browser)
                Entry("Enable matchmaker", "Probes a batch of a game's public servers and joins whichever is estimated to have the lowest ping.", typeof(ServerBrowserPage), servers),
                Entry("Prefer empty servers", "Off prefers active, reasonably full servers instead.", typeof(ServerBrowserPage), servers),
                Entry("Preferred datacenter", "Sticks to this location when it has servers available.", typeof(ServerBrowserPage), servers),
                Entry("Auto candidate count", "Off lets you set a fixed number of servers to probe per search.", typeof(ServerBrowserPage), servers),
                Entry("Max candidates", "Only used when auto candidate count is off.", typeof(ServerBrowserPage), servers),
                Entry("Excluded datacenters", "The matchmaker will never pick a server in a checked datacenter.", typeof(ServerBrowserPage), servers),
                Entry("Place ID", "Lists a game's public servers using Roblox's own public server list.", typeof(ServerBrowserPage), servers),

                // History
                Entry("", "Every game you've played through PhasmaStrap, with playtime and last-played date, kept across restarts.", typeof(HistoryPage), history),

                // Appearance
                Entry(Strings.Menu_Appearance_Global_Theme_Title, "", typeof(AppearancePage), appearance),
                Entry(Strings.Menu_Appearance_Language_Title, Strings.Menu_Appearance_Language_Description, typeof(AppearancePage), appearance),
                Entry("Controller navigation", "Navigate this settings window with an Xbox/generic gamepad.", typeof(AppearancePage), appearance),
                Entry(Strings.Menu_Appearance_Bootstrapper_Title, Strings.Menu_Appearance_Bootstrapper_Description, typeof(AppearancePage), appearance),
                Entry(Strings.Menu_Appearance_Style_Title, Strings.Menu_Appearance_Style_Description, typeof(AppearancePage), appearance),
                Entry(Strings.Menu_Appearance_Icon_Title, Strings.Menu_Appearance_Icon_Description, typeof(AppearancePage), appearance),
                Entry(Strings.Menu_Appearance_Customisation_Title, Strings.Menu_Appearance_Customisation_Description, typeof(AppearancePage), appearance),

                // Shortcuts
                Entry(Strings.Menu_Shortcuts_ExtractIcons_Title, Strings.Menu_Shortcuts_ExtractIcons_Description, typeof(ShortcutsPage), shortcuts),
                Entry(Strings.Common_Shortcuts_Desktop, "", typeof(ShortcutsPage), shortcuts),
                Entry(Strings.Common_Shortcuts_StartMenu, "", typeof(ShortcutsPage), shortcuts),
                Entry(Strings.LaunchMenu_LaunchRoblox, "", typeof(ShortcutsPage), shortcuts),
                Entry(Strings.LaunchMenu_LaunchRobloxStudio, "", typeof(ShortcutsPage), shortcuts),
                Entry(Strings.Menu_Title, "Shortcut for launching the PhasmaStrap settings window itself.", typeof(ShortcutsPage), shortcuts),
                Entry("Enable Studio companion", "Installs a Roblox Studio plugin that reports what you're working on back to PhasmaStrap.", typeof(ShortcutsPage), shortcuts),
                Entry("Studio Rich Presence", "Shows a separate Discord status while Roblox Studio is open.", typeof(ShortcutsPage), shortcuts),

                // PhasmaStrap (app settings)
                Entry(Strings.Menu_Behaviour_AutoUpdate_Title, Strings.Menu_Behaviour_AutoUpdate_Description, typeof(PhasmaStrapPage), phasmaStrap),
                Entry(Strings.Menu_PhasmaStrap_Analytics_Title, Strings.Menu_PhasmaStrap_Analytics_Description, typeof(PhasmaStrapPage), phasmaStrap),
                Entry(Strings.Menu_PhasmaStrap_ExportData_Title, Strings.Menu_PhasmaStrap_ExportData_Description, typeof(PhasmaStrapPage), phasmaStrap),
            };

            return entries;
        }
    }
}
