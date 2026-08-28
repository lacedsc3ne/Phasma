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
    /// maintain an entire schema-import/reflection pipeline for a couple dozen entries.
    ///
    /// Headers/descriptions are read directly from the same <c>Strings.*</c> resource properties the pages'
    /// XAML binds to (via <c>{x:Static resources:Strings.Foo}</c>), so this file stays in sync with the UI
    /// text automatically and with any active translation - it does not re-implement Voidstrap's
    /// runtime "Strings.Foo" string-parsing/regex resolution, since here we can just reference the resource
    /// property directly in C#.
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

                // Behaviour
                Entry(Strings.Menu_Behaviour_ConfirmLaunches_Title, Strings.Menu_Behaviour_ConfirmLaunches_Description, typeof(BehaviourPage), behaviour),
                Entry(Strings.Menu_Behaviour_BackgroundUpdates_Title, Strings.Menu_Behaviour_BackgroundUpdates_Description, typeof(BehaviourPage), behaviour),
                Entry(Strings.Menu_Behaviour_ForceRobloxReinstall_Title, Strings.Menu_Behaviour_ForceRobloxReinstall_Description, typeof(BehaviourPage), behaviour),

                // Mods
                Entry(Strings.Menu_Mods_OpenModsFolder_Title, Strings.Menu_Mods_OpenModsFolder_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Misc_CompatibilitySettings_Title, "", typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_MouseCursor_Title, Strings.Menu_Mods_Presets_MouseCursor_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_OldAvatarEditor_Title, Strings.Menu_Mods_Presets_OldAvatarEditor_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_OldCharacterSounds_Title, Strings.Menu_Mods_Presets_OldCharacterSounds_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Presets_EmojiType_Title, Strings.Menu_Mods_Presets_EmojiType_Description, typeof(ModsPage), mods),
                Entry(Strings.Menu_Mods_Misc_CustomFont_Title, "", typeof(ModsPage), mods),

                // Extensions (page has no individually-labelled options, just a list of installed extensions)
                Entry("", "Locate and launch third-party tools alongside Roblox.", typeof(ExtensionsPage), extensions),

                // Engine Settings (FastFlags)
                Entry(Strings.Menu_FastFlags_ManagerEnabled_Title, Strings.Menu_FastFlags_ManagerEnabled_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Presets_MSAA_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Presets_FixDisplayScaling_Title, Strings.Menu_FastFlags_Presets_FixDisplayScaling_Description, typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Presets_TextureQuality_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlags_Reset_Title, "", typeof(FastFlagsPage), fastFlags),
                Entry(Strings.Menu_FastFlagEditor_Title, Strings.Menu_FastFlags_Editor_Description, typeof(FastFlagsPage), fastFlags),

                // Channel
                Entry("Currently active channel", "Which Roblox deployment channel is currently running.", typeof(ChannelPage), channel),
                Entry("Roblox channel", "Leave blank to use Roblox's default (production) channel.", typeof(ChannelPage), channel),
                Entry("If Roblox's channel changes", "Automatic, Prompt, or Ignore.", typeof(ChannelPage), channel),

                // Performance
                Entry("CPU core limit", "Restricts how many logical processors PhasmaStrap itself can use.", typeof(PerformancePage), performance),
                Entry("Lock settings file", "Marks the file read-only so Roblox can't silently overwrite these changes.", typeof(PerformancePage), performance),
                Entry("Framerate cap", "0 leaves it uncapped.", typeof(PerformancePage), performance),
                Entry("Graphics quality level", "0 to 10, where 10 is Roblox's highest preset.", typeof(PerformancePage), performance),
                Entry("Mouse sensitivity", "", typeof(PerformancePage), performance),
                Entry("Reduced motion", "Cuts down on in-game UI animation.", typeof(PerformancePage), performance),
                Entry("VR enabled", "", typeof(PerformancePage), performance),
                Entry("Show performance stats", "Roblox's built-in FPS/ping/memory overlay.", typeof(PerformancePage), performance),

                // Appearance
                Entry(Strings.Menu_Appearance_Global_Theme_Title, "", typeof(AppearancePage), appearance),
                Entry(Strings.Menu_Appearance_Language_Title, Strings.Menu_Appearance_Language_Description, typeof(AppearancePage), appearance),
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
                Entry("Studio companion", "Installs a Roblox Studio plugin that reports what you're working on back to PhasmaStrap.", typeof(ShortcutsPage), shortcuts),

                // PhasmaStrap (app settings)
                Entry(Strings.Menu_Behaviour_AutoUpdate_Title, Strings.Menu_Behaviour_AutoUpdate_Description, typeof(PhasmaStrapPage), phasmaStrap),
                Entry(Strings.Menu_PhasmaStrap_Analytics_Title, Strings.Menu_PhasmaStrap_Analytics_Description, typeof(PhasmaStrapPage), phasmaStrap),
                Entry(Strings.Menu_PhasmaStrap_ExportData_Title, Strings.Menu_PhasmaStrap_ExportData_Description, typeof(PhasmaStrapPage), phasmaStrap),
            };

            return entries;
        }
    }
}
