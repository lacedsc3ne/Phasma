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
                Entry("Disable telemetry", "Disables a large batch of client analytics and telemetry endpoints.", typeof(FastFlagsPage), fastFlags),
                Entry("Disable Webview2 telemetry", "Disables telemetry sent by the embedded Webview2 browser component.", typeof(FastFlagsPage), fastFlags),
                Entry("Disable voice chat telemetry", "Disables analytics and telemetry sent by the voice chat system.", typeof(FastFlagsPage), fastFlags),
                Entry("Block Tencent", "Redirects and disables Tencent-related endpoints and policy checks.", typeof(FastFlagsPage), fastFlags),
                Entry("Ping breakdown", "Shows a breakdown of ping contributors in the debug stats.", typeof(FastFlagsPage), fastFlags),
                Entry("Show chunks", "Shows lighting chunk boundaries for debugging.", typeof(FastFlagsPage), fastFlags),
                Entry("Flag state", "Sets the FStringDebugShowFlagState flag to the given value.", typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Voice / Chat
                Entry("Chat bubbles", "Toggles legacy chat bubbles above characters.", typeof(FastFlagsPage), fastFlags),
                Entry("Chat translation", "Toggles the in-experience chat translation setting.", typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Rendering
                Entry("Light culling", "Enables GPU and CPU light culling.", typeof(FastFlagsPage), fastFlags),
                Entry("Rainbow theme", "Renders unthemed UI instances with a rainbow debug color.", typeof(FastFlagsPage), fastFlags),
                Entry("FRM quality override", "", typeof(FastFlagsPage), fastFlags),
                Entry("FRM quality override level", "", typeof(FastFlagsPage), fastFlags),
                Entry("Mesh quality", "", typeof(FastFlagsPage), fastFlags),
                Entry("Mesh quality level", "", typeof(FastFlagsPage), fastFlags),
                Entry("Unlimited camera zoom", "Removes the maximum camera zoom distance.", typeof(FastFlagsPage), fastFlags),
                Entry("BGRA texture support", "Enables Direct3D 11 BGRA texture support.", typeof(FastFlagsPage), fastFlags),
                Entry("New FPS system", "Enables the newer FPS and frame time tracking system.", typeof(FastFlagsPage), fastFlags),
                Entry("Worser particles", "Reduces particle fidelity fixes for a performance boost.", typeof(FastFlagsPage), fastFlags),
                Entry("Low poly meshes", "Forces the lowest level of detail for meshes.", typeof(FastFlagsPage), fastFlags),
                Entry("Rendering mode", "", typeof(FastFlagsPage), fastFlags),
                Entry("More lighting", "Brightens rendering by fixing fog contribution.", typeof(FastFlagsPage), fastFlags),
                Entry("Minimum grass distance", "", typeof(FastFlagsPage), fastFlags),
                Entry("Maximum grass distance", "", typeof(FastFlagsPage), fastFlags),
                Entry("Grass movement factor", "", typeof(FastFlagsPage), fastFlags),
                Entry("In-game menu version", "", typeof(FastFlagsPage), fastFlags),
                Entry("Lighting mode", "", typeof(FastFlagsPage), fastFlags),
                Entry("Disable fullscreen title bar delay", "Removes the delay before the title bar hides in fullscreen.", typeof(FastFlagsPage), fastFlags),
                Entry("Texture skipping", "", typeof(FastFlagsPage), fastFlags),
                Entry("Distance rendering", "", typeof(FastFlagsPage), fastFlags),
                Entry("Dynamic resolution", "", typeof(FastFlagsPage), fastFlags),
                Entry("Romark start graphic", "", typeof(FastFlagsPage), fastFlags),
                Entry("FRM quality level", "", typeof(FastFlagsPage), fastFlags),
                Entry("Disable post processing effects", "", typeof(FastFlagsPage), fastFlags),
                Entry("Avoid task scheduler sleep", "Prevents the task scheduler from sleeping, at the cost of higher CPU usage.", typeof(FastFlagsPage), fastFlags),
                Entry("Disable player shadows", "", typeof(FastFlagsPage), fastFlags),
                Entry("Render occlusion checks", "Enables visibility bug checks used for render occlusion.", typeof(FastFlagsPage), fastFlags),
                Entry("Gray sky", "", typeof(FastFlagsPage), fastFlags),
                Entry("White sky", "", typeof(FastFlagsPage), fastFlags),
                Entry("Red font debug highlight", "", typeof(FastFlagsPage), fastFlags),
                Entry("Disable layered clothing", "", typeof(FastFlagsPage), fastFlags),
                Entry("Disable terrain textures", "", typeof(FastFlagsPage), fastFlags),
                Entry("Prerender", "Enables the prerender pipeline and its V2 variant.", typeof(FastFlagsPage), fastFlags),
                Entry("Force buggy Vulkan renderpass list", "Overrides the renderpass allowlist used to force Vulkan on buggy GPUs.", typeof(FastFlagsPage), fastFlags),
                Entry("Bypass Vulkan buggy GPU list", "Overrides the renderpass allowlist used to bypass Vulkan blacklisting.", typeof(FastFlagsPage), fastFlags),
                Entry("Chrome in-game menu UI", "", typeof(FastFlagsPage), fastFlags),
                Entry("Old Chrome UI", "Reverts several in-game menu Chrome UI elements to their older versions.", typeof(FastFlagsPage), fastFlags),
                Entry("Shader glint level", "", typeof(FastFlagsPage), fastFlags),
                Entry("Shaders enabled", "", typeof(FastFlagsPage), fastFlags),
                Entry("Shaders roughness clamp", "", typeof(FastFlagsPage), fastFlags),
                Entry("Target refresh rate", "", typeof(FastFlagsPage), fastFlags),
                Entry("Minimal rendering", "Forces deterministic, minimal rendering for debugging.", typeof(FastFlagsPage), fastFlags),
                Entry("Disable sky bloom", "", typeof(FastFlagsPage), fastFlags),
                Entry("Framerate buffer percentage", "", typeof(FastFlagsPage), fastFlags),
                Entry("Framerate limit (FFlag)", "Sets an FFlag-based framerate cap, separate from the performance page's framerate cap.", typeof(FastFlagsPage), fastFlags),
                Entry("Pseudolocalization", "Enables debug pseudolocalization of UI text.", typeof(FastFlagsPage), fastFlags),
                Entry("Display FPS (FFlag)", "Shows the client's built-in FFlag-based FPS counter, separate from the performance page's stats overlay.", typeof(FastFlagsPage), fastFlags),
                Entry("Gray avatar thumbnails", "", typeof(FastFlagsPage), fastFlags),
                Entry("UI font size padding", "", typeof(FastFlagsPage), fastFlags),
                Entry("Hide GUI group", "Hides the core GUI for the given group ID.", typeof(FastFlagsPage), fastFlags),

                // Engine Settings - Networking
                Entry("Less lag spikes", "Increases bandwidth manager throughput targets.", typeof(FastFlagsPage), fastFlags),
                Entry("Roblox Core (SignalR) tuning", "", typeof(FastFlagsPage), fastFlags),
                Entry("No payload limit", "Raises several networking payload size limits to the maximum.", typeof(FastFlagsPage), fastFlags),
                Entry("Enable large replicator", "", typeof(FastFlagsPage), fastFlags),
                Entry("Faster loading", "Raises asset preload limits and speeds up mesh preloading.", typeof(FastFlagsPage), fastFlags),
                Entry("Better packet sending", "Tunes packet batching and processing thresholds.", typeof(FastFlagsPage), fastFlags),
                Entry("MTU size", "", typeof(FastFlagsPage), fastFlags),
                Entry("Resend buffer array length", "", typeof(FastFlagsPage), fastFlags),

                // Engine Settings - UI / Misc
                Entry("More sensitivity numbers", "Shows more precise sensitivity values in the settings menu.", typeof(FastFlagsPage), fastFlags),
                Entry("No GUI blur", "", typeof(FastFlagsPage), fastFlags),
                Entry("Preferred text size scaling", "", typeof(FastFlagsPage), fastFlags),
                Entry("Texture remover", "Aggressively compresses/removes textures via the asset content refresh system.", typeof(FastFlagsPage), fastFlags),
                Entry("Threading debug check", "Enables the render thread checking debug flag.", typeof(FastFlagsPage), fastFlags),
                Entry("Disable ads", "", typeof(FastFlagsPage), fastFlags),
                Entry("Enable dark mode", "", typeof(FastFlagsPage), fastFlags),
                Entry("Remove middle details page", "", typeof(FastFlagsPage), fastFlags),
                Entry("Preload assets", "Enables mesh, sound, texture, font, item and teleport asset preloading.", typeof(FastFlagsPage), fastFlags),
                Entry("Optimize CFrame updates", "", typeof(FastFlagsPage), fastFlags),
                Entry("Custom disconnect error", "", typeof(FastFlagsPage), fastFlags),
                Entry("Custom disconnect error message", "", typeof(FastFlagsPage), fastFlags),
                Entry("Fake verify", "Sets FStringWhitelistVerifiedUserId to the given user ID.", typeof(FastFlagsPage), fastFlags),
                Entry("New camera controls", "Sets FFlagNewCameraControls to the given value.", typeof(FastFlagsPage), fastFlags),
                Entry("Chat UI override", "Sets FFlagDebugForceChatDisabled to the given value.", typeof(FastFlagsPage), fastFlags),
                Entry("Old Roblox Studio core UI", "Only applies to Roblox Studio.", typeof(FastFlagsPage), fastFlags),
                Entry("Always show VR toggle", "", typeof(FastFlagsPage), fastFlags),
                Entry("Disable feedback soothsayer check", "", typeof(FastFlagsPage), fastFlags),
                Entry("Language selector", "", typeof(FastFlagsPage), fastFlags),
                Entry("Haptics toggle", "", typeof(FastFlagsPage), fastFlags),
                Entry("In-menu framerate cap toggle", "", typeof(FastFlagsPage), fastFlags),
                Entry("Memory probing", "Enables the client's memory probing performance control.", typeof(FastFlagsPage), fastFlags),
                Entry("Cache size improvement", "Raises several disk and memory cache size limits.", typeof(FastFlagsPage), fastFlags),
                Entry("CPU threads used", "", typeof(FastFlagsPage), fastFlags),
                Entry("Minimum CPU core thread count", "", typeof(FastFlagsPage), fastFlags),

                Entry(Strings.Menu_FastFlags_Reset_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlagEditor_Title, Strings.Menu_FastFlags_Editor_Description, typeof(FastFlagsPage), fastFlags),

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
