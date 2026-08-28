using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

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
    }
}
