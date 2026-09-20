using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class ShortcutTargetOption
    {
        public string Name { get; init; } = "";

        public string Flags { get; init; } = "";

        public string FileName { get; init; } = "";

        public bool IsGame { get; init; }

        public override string ToString() => Name;
    }

    public sealed class ShortcutEntry
    {
        public string Name { get; init; } = "";

        public string Path { get; init; } = "";

        public string Launches { get; init; } = "";

        public string Location { get; init; } = "";

        public ImageSource? Icon { get; init; }
    }

    public class ShortcutsViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "ShortcutsViewModel";

        public ShortcutsViewModel()
        {
            SelectedShortcutTarget = ShortcutTargets[0];
            RefreshShortcuts();
        }

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

        public IReadOnlyList<ShortcutTargetOption> ShortcutTargets { get; } = new[]
        {
            new ShortcutTargetOption { Name = Strings.Menu_Title, Flags = "-settings", FileName = $"{Strings.Menu_Title}.lnk" },
            new ShortcutTargetOption { Name = Strings.LaunchMenu_LaunchRoblox, Flags = "-player", FileName = $"{Strings.LaunchMenu_LaunchRoblox}.lnk" },
            new ShortcutTargetOption { Name = Strings.LaunchMenu_LaunchRobloxStudio, Flags = "-studio", FileName = $"{Strings.LaunchMenu_LaunchRobloxStudio}.lnk" },
            new ShortcutTargetOption { Name = "A specific game", IsGame = true },
        };

        private ShortcutTargetOption _selectedShortcutTarget = null!;

        public ShortcutTargetOption SelectedShortcutTarget
        {
            get => _selectedShortcutTarget;
            set
            {
                _selectedShortcutTarget = value;
                OnPropertyChanged(nameof(SelectedShortcutTarget));
                OnPropertyChanged(nameof(IsGameTargetSelected));
            }
        }

        public bool IsGameTargetSelected => _selectedShortcutTarget?.IsGame == true;

        public IReadOnlyList<string> ShortcutLocations { get; } = new[] { "Desktop", "Start menu" };

        private string _selectedShortcutLocation = "Desktop";

        public string SelectedShortcutLocation
        {
            get => _selectedShortcutLocation;
            set { _selectedShortcutLocation = value; OnPropertyChanged(nameof(SelectedShortcutLocation)); }
        }

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
            private set { _gameShortcutBusy = value; OnPropertyChanged(nameof(GameShortcutBusy)); OnPropertyChanged(nameof(NotBusy)); }
        }

        public bool NotBusy => !_gameShortcutBusy;

        public ObservableCollection<ShortcutEntry> ExistingShortcuts { get; } = new();

        public bool HasExistingShortcuts => ExistingShortcuts.Count > 0;

        public ICommand RefreshShortcutsCommand => new RelayCommand(RefreshShortcuts);

        public ICommand CreateShortcutCommand => new AsyncRelayCommand(CreateShortcutAsync);

        public ICommand RemoveShortcutCommand => new RelayCommand<ShortcutEntry>(RemoveShortcut);

        public ICommand CreateGameShortcutCommand => new AsyncRelayCommand(async () =>
        {
            if (string.IsNullOrWhiteSpace(GameShortcutInput))
                return;

            GameShortcutBusy = true;
            GameShortcutStatus = "";

            GameShortcutCreator.Result result = await GameShortcutCreator.CreateAsync(GameShortcutInput, TargetFolder());

            GameShortcutStatus = result.Message;
            GameShortcutBusy = false;

            if (result.Success)
                GameShortcutInput = "";

            RefreshShortcuts();
        });

        private string TargetFolder() =>
            string.Equals(SelectedShortcutLocation, "Start menu", StringComparison.OrdinalIgnoreCase) ? Paths.WindowsStartMenu : Paths.Desktop;

        private async Task CreateShortcutAsync()
        {
            ShortcutTargetOption target = SelectedShortcutTarget;

            if (target is null)
                return;

            string folder = TargetFolder();

            if (target.IsGame)
            {
                if (string.IsNullOrWhiteSpace(GameShortcutInput))
                {
                    GameShortcutStatus = "Enter a place ID or a roblox.com/games link first.";
                    return;
                }

                GameShortcutBusy = true;
                GameShortcutStatus = "Fetching that game's name and icon...";

                GameShortcutCreator.Result result = await GameShortcutCreator.CreateAsync(GameShortcutInput, folder);

                GameShortcutStatus = result.Message;
                GameShortcutBusy = false;

                if (result.Success)
                    GameShortcutInput = "";

                RefreshShortcuts();
                return;
            }

            string path = Path.Combine(folder, target.FileName);
            bool existed = File.Exists(path);

            try
            {
                Shortcut.Create(Paths.Application, target.Flags, path);

                GameShortcutStatus = existed
                    ? $"\"{target.Name}\" is already on your {SelectedShortcutLocation.ToLowerInvariant()}."
                    : File.Exists(path) ? $"Created \"{target.Name}\" in {SelectedShortcutLocation}." : "The shortcut could not be created.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::CreateShortcutAsync", ex);
                GameShortcutStatus = $"Failed to create the shortcut: {ex.Message}";
            }

            SyncTasks();
            RefreshShortcuts();
        }

        private void RemoveShortcut(ShortcutEntry? entry)
        {
            if (entry is null)
                return;

            try
            {
                if (File.Exists(entry.Path))
                    File.Delete(entry.Path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::RemoveShortcut", ex);
                GameShortcutStatus = $"Could not remove that shortcut: {ex.Message}";
                return;
            }

            GameShortcutStatus = $"Removed \"{entry.Name}\".";

            SyncTasks();
            RefreshShortcuts();
        }

        private void SyncTasks()
        {
            DesktopIconTask.OriginalState = File.Exists(Path.Combine(Paths.Desktop, $"{App.ProjectName}.lnk"));
            StartMenuIconTask.OriginalState = File.Exists(Path.Combine(Paths.WindowsStartMenu, $"{App.ProjectName}.lnk"));
            PlayerIconTask.OriginalState = File.Exists(Path.Combine(Paths.Desktop, $"{Strings.LaunchMenu_LaunchRoblox}.lnk"));
            StudioIconTask.OriginalState = File.Exists(Path.Combine(Paths.Desktop, $"{Strings.LaunchMenu_LaunchRobloxStudio}.lnk"));
            SettingsIconTask.OriginalState = File.Exists(Path.Combine(Paths.Desktop, $"{Strings.Menu_Title}.lnk"));

            OnPropertyChanged(nameof(DesktopIconTask));
            OnPropertyChanged(nameof(StartMenuIconTask));
            OnPropertyChanged(nameof(PlayerIconTask));
            OnPropertyChanged(nameof(StudioIconTask));
            OnPropertyChanged(nameof(SettingsIconTask));
        }

        public void RefreshShortcuts()
        {
            ExistingShortcuts.Clear();

            Collect(Paths.Desktop, "Desktop");
            Collect(Paths.WindowsStartMenu, "Start menu");

            OnPropertyChanged(nameof(HasExistingShortcuts));
        }

        private void Collect(string folder, string location)
        {
            string[] files;

            try
            {
                files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine($"{LOG_IDENT}::Collect", $"Could not read '{folder}': {ex.Message}");
                return;
            }

            foreach (string file in files)
            {
                ShortcutEntry? entry = Describe(file, location);

                if (entry is not null)
                    ExistingShortcuts.Add(entry);
            }
        }

        private static ShortcutEntry? Describe(string file, string location)
        {
            string target = "";
            string arguments = "";
            string iconLocation = "";

            try
            {
                ShellLink.Shortcut link = ShellLink.Shortcut.ReadFromFile(file);

                target = link.LinkInfo?.LocalBasePath ?? "";

                if (target.Length == 0)
                    target = link.LinkInfo?.LocalBasePathUnicode ?? "";

                arguments = link.StringData?.CommandLineArguments ?? "";
                iconLocation = link.StringData?.IconLocation ?? "";
            }
            catch
            {
                return null;
            }

            if (!string.Equals(target, Paths.Application, StringComparison.OrdinalIgnoreCase))
                return null;

            return new ShortcutEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Path = file,
                Location = location,
                Launches = DescribeArguments(arguments),
                Icon = LoadIcon(iconLocation)
            };
        }

        private static string DescribeArguments(string arguments)
        {
            string trimmed = arguments.Trim();

            if (trimmed.Length == 0)
                return App.ProjectName;

            if (trimmed.Contains("-settings", StringComparison.OrdinalIgnoreCase))
                return Strings.Menu_Title;

            if (trimmed.Contains("-studio", StringComparison.OrdinalIgnoreCase))
                return Strings.LaunchMenu_LaunchRobloxStudio;

            if (trimmed.Contains("-player", StringComparison.OrdinalIgnoreCase))
                return Strings.LaunchMenu_LaunchRoblox;

            int index = trimmed.IndexOf("placeId=", StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                string tail = new string(trimmed[(index + 8)..].TakeWhile(char.IsDigit).ToArray());
                return tail.Length > 0 ? $"Roblox game {tail}" : "A Roblox game";
            }

            return App.ProjectName;
        }

        private static ImageSource? LoadIcon(string iconLocation)
        {
            string path = iconLocation.Trim().Trim('"');

            if (path.Length == 0 || !File.Exists(path))
                return null;

            if (!string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var image = new BitmapImage();

                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = 48;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();

                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
