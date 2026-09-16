namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class BehaviourViewModel : NotifyPropertyChangedViewModel
    {
        public bool ConfirmLaunches
        {
            get => App.Settings.Prop.ConfirmLaunches;
            set => App.Settings.Prop.ConfirmLaunches = value;
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
