using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ShortcutsViewModel : NotifyPropertyChangedViewModel
    {
        public bool IsStudioOptionVisible => App.IsStudioInstalled;

        public bool StudioPluginEnabled
        {
            get => App.Settings.Prop.StudioPluginEnabled;
            set
            {
                App.Settings.Prop.StudioPluginEnabled = value;

                if (value)
                {
                    StudioBridge.Start();
                    StudioPluginInstaller.EnsureInstalled(force: true);
                }
                else
                {
                    StudioBridge.Stop();
                    StudioPluginInstaller.Uninstall();
                }

                OnPropertyChanged(nameof(StudioPluginStatus));
            }
        }

        public string StudioPluginStatus =>
            StudioPluginInstaller.IsInstalled
                ? $"Installed at {StudioPluginInstaller.PluginPath}"
                : "Not installed. Turning this on writes the plugin to your Roblox Studio plugins folder.";

        public bool StudioRichPresenceEnabled
        {
            get => App.Settings.Prop.StudioRichPresenceEnabled;
            set
            {
                App.Settings.Prop.StudioRichPresenceEnabled = value;

                if (value)
                {
                    App.StudioRichPresence ??= new StudioRichPresence();
                }
                else
                {
                    App.StudioRichPresence?.Dispose();
                    App.StudioRichPresence = null;
                }
            }
        }

        public ICommand ReinstallStudioPluginCommand => new RelayCommand(() =>
        {
            StudioPluginInstaller.Reinstall();
            OnPropertyChanged(nameof(StudioPluginStatus));
        });

        public ShortcutTask DesktopIconTask { get; } = new("Desktop", Paths.Desktop, $"{App.ProjectName}.lnk");

        public ShortcutTask StartMenuIconTask { get; } = new("StartMenu", Paths.WindowsStartMenu, $"{App.ProjectName}.lnk");

        public ShortcutTask PlayerIconTask { get; } = new("RobloxPlayer", Paths.Desktop, $"{Strings.LaunchMenu_LaunchRoblox}.lnk", "-player");

        public ShortcutTask StudioIconTask { get; } = new("RobloxStudio", Paths.Desktop, $"{Strings.LaunchMenu_LaunchRobloxStudio}.lnk", "-studio");

        public ShortcutTask SettingsIconTask { get; } = new("Settings", Paths.Desktop, $"{Strings.Menu_Title}.lnk", "-settings");

        public ExtractIconsTask ExtractIconsTask { get; } = new();

        // --- per-game shortcut creator (deep-links into one specific game, with that game's own icon) ---

        private string _gameShortcutInput = "";

        public string GameShortcutInput
        {
            get => _gameShortcutInput;
            set { _gameShortcutInput = value; OnPropertyChanged(nameof(GameShortcutInput)); }
        }

        private string _gameShortcutStatus = "";

        public string GameShortcutStatus
        {
            get => _gameShortcutStatus;
            private set { _gameShortcutStatus = value; OnPropertyChanged(nameof(GameShortcutStatus)); }
        }

        private bool _gameShortcutBusy;

        public bool GameShortcutBusy
        {
            get => _gameShortcutBusy;
            private set { _gameShortcutBusy = value; OnPropertyChanged(nameof(GameShortcutBusy)); }
        }

        public ICommand CreateGameShortcutCommand => new AsyncRelayCommand(async () =>
        {
            if (string.IsNullOrWhiteSpace(GameShortcutInput))
                return;

            GameShortcutBusy = true;
            GameShortcutStatus = "";

            GameShortcutCreator.Result result = await GameShortcutCreator.CreateAsync(GameShortcutInput, Paths.Desktop);

            GameShortcutStatus = result.Message;
            GameShortcutBusy = false;

            if (result.Success)
                GameShortcutInput = "";
        });
    }
}
