using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Win32;

using PhasmaStrap.AppData;

namespace PhasmaStrap.UI.ViewModels.Installer
{
    internal class InstallCoordinator
    {
        public bool Busy { get; private set; }

        public event Action? Changed;

        public bool TryEnter()
        {
            if (Busy)
                return false;

            Busy = true;
            Changed?.Invoke();
            return true;
        }

        public void Exit()
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    internal class SilentBootstrapperDialog : IBootstrapperDialog
    {
        private readonly Action<string> _onMessage;

        public SilentBootstrapperDialog(Action<string> onMessage) => _onMessage = onMessage;

        public PhasmaStrap.Bootstrapper? Bootstrapper { get; set; }

        public string Message
        {
            get => _message;
            set { _message = value; _onMessage(value); }
        }
        private string _message = "";

        public System.Windows.Forms.ProgressBarStyle ProgressStyle { get; set; }
        public int ProgressValue { get; set; }
        public int ProgressMaximum { get; set; }
        public TaskbarItemProgressState TaskbarProgressState { get; set; }
        public double TaskbarProgressValue { get; set; }
        public bool CancelEnabled { get; set; }

        public void ShowBootstrapper() { }
        public void CloseBootstrapper() { }
        public void ShowSuccess(string message, Action? callback = null) => callback?.Invoke();
    }

    public class RobloxProductViewModel : NotifyPropertyChangedViewModel
    {
        private readonly IAppData _appData;
        private readonly LaunchMode _launchMode;
        private readonly InstallCoordinator _coordinator;

        private bool _isBusy = false;
        private string _progressText = "";

        public string DisplayName { get; }

        public string Description { get; }

        public bool IsInstalled => _appData.DistributionStateManager.IsSaved && !String.IsNullOrEmpty(_appData.DistributionState.VersionGuid);

        public string StatusText => IsInstalled
            ? String.Format(Strings.Installer_Manager_Status_Installed, _appData.DistributionState.VersionGuid)
            : Strings.Installer_Manager_Status_NotInstalled;

        public string InstallLocation => IsInstalled ? _appData.Directory : Paths.Versions;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanDelete));
            }
        }

        public string ProgressText
        {
            get => _progressText;
            private set { _progressText = value; OnPropertyChanged(nameof(ProgressText)); }
        }

        public bool CanInstall => !IsBusy && !_coordinator.Busy;

        public bool CanDelete => !IsBusy && !_coordinator.Busy && IsInstalled;

        public ICommand InstallCommand => new AsyncRelayCommand(InstallAsync);

        public ICommand OpenCommand => new RelayCommand(Open);

        public ICommand DeleteCommand => new RelayCommand(Delete);

        internal RobloxProductViewModel(IAppData appData, LaunchMode launchMode, string displayName, string description, InstallCoordinator coordinator)
        {
            _appData = appData;
            _launchMode = launchMode;
            _coordinator = coordinator;

            DisplayName = displayName;
            Description = description;

            _coordinator.Changed += () =>
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanDelete));
            };
        }

        private async Task InstallAsync()
        {
            if (IsBusy || !_coordinator.TryEnter())
                return;

            IsBusy = true;
            ProgressText = Strings.Installer_Manager_Status_Installing;

            bool originalNoLaunch = App.LaunchSettings.NoLaunchFlag.Active;
            bool originalUpgrade = App.LaunchSettings.UpgradeFlag.Active;
            bool originalQuiet = App.LaunchSettings.QuietFlag.Active;

            App.LaunchSettings.NoLaunchFlag.Active = true;
            App.LaunchSettings.UpgradeFlag.Active = true;
            App.LaunchSettings.QuietFlag.Active = true;

            try
            {
                var bootstrapper = new PhasmaStrap.Bootstrapper(_launchMode)
                {
                    MutexNamePrefix = "PhasmaStrap-InstallerManager",
                    Dialog = new SilentBootstrapperDialog(message =>
                        Application.Current.Dispatcher.Invoke(() => ProgressText = message))
                };

                await Task.Run(bootstrapper.Run);

                ProgressText = "";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RobloxProductViewModel::InstallAsync", ex);
                ProgressText = String.Format(Strings.Installer_Manager_Status_InstallFailed, ex.Message);
            }
            finally
            {
                App.LaunchSettings.NoLaunchFlag.Active = originalNoLaunch;
                App.LaunchSettings.UpgradeFlag.Active = originalUpgrade;
                App.LaunchSettings.QuietFlag.Active = originalQuiet;

                IsBusy = false;
                _coordinator.Exit();

                Refresh();
            }
        }

        private void Open()
        {
            Directory.CreateDirectory(InstallLocation);
            Process.Start("explorer.exe", InstallLocation);
        }

        private void Delete()
        {
            if (!IsInstalled)
                return;

            var result = Frontend.ShowMessageBox(
                String.Format(Strings.Installer_Manager_ConfirmDelete, DisplayName),
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                if (Directory.Exists(_appData.Directory))
                    Directory.Delete(_appData.Directory, true);

                _appData.DistributionState.VersionGuid = "";
                _appData.DistributionState.PackageHashes.Clear();
                _appData.DistributionState.Size = 0;
                _appData.DistributionState.ModManifest.Clear();

                _appData.DistributionStateManager.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RobloxProductViewModel::Delete", ex);
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }

            Refresh();
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(InstallLocation));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    public class ManagerViewModel : NotifyPropertyChangedViewModel
    {
        private readonly InstallCoordinator _coordinator = new();

        public RobloxProductViewModel Player { get; }

        public RobloxProductViewModel Studio { get; }

        public List<string> AvailableDrives { get; } = DriveInfo.GetDrives()
            .Where(x => x.IsReady && x.DriveType == System.IO.DriveType.Fixed)
            .Select(x => x.Name)
            .ToList();

        public bool CanChangeMapLocation => !Player.IsInstalled && !Studio.IsInstalled;

        public string SelectedDrive
        {
            get => Path.GetPathRoot(Paths.Base) ?? AvailableDrives.FirstOrDefault() ?? "";
            set => RelocateToDrive(value);
        }

        public ManagerViewModel()
        {
            Player = new RobloxProductViewModel(
                new RobloxPlayerData(),
                LaunchMode.Player,
                Strings.Installer_Manager_Player_Title,
                Strings.Installer_Manager_Player_Description,
                _coordinator
            );

            Studio = new RobloxProductViewModel(
                new RobloxStudioData(),
                LaunchMode.Studio,
                Strings.Installer_Manager_Studio_Title,
                Strings.Installer_Manager_Studio_Description,
                _coordinator
            );
        }

        private void RelocateToDrive(string driveRoot)
        {
            if (String.IsNullOrEmpty(driveRoot))
                return;

            string oldBase = Paths.Base;
            string currentRoot = Path.GetPathRoot(oldBase) ?? "";

            if (String.Equals(currentRoot, driveRoot, StringComparison.OrdinalIgnoreCase))
                return;

            if (!CanChangeMapLocation)
            {
                Frontend.ShowMessageBox(Strings.Installer_Manager_Map_CannotChange, MessageBoxImage.Warning);
                OnPropertyChanged(nameof(SelectedDrive));
                return;
            }

            string newBase = Path.Combine(driveRoot, Path.GetFileName(oldBase.TrimEnd(Path.DirectorySeparatorChar)));

            try
            {
                Directory.CreateDirectory(newBase);

                string testFile = Path.Combine(newBase, $"{App.ProjectName}WriteTest.txt");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);

                CopyDirectory(oldBase, newBase);
                Directory.Delete(oldBase, true);

                Paths.Initialize(newBase);

                using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
                {
                    uninstallKey.SetValueSafe("DisplayIcon", $"{Paths.Application},0");
                    uninstallKey.SetValueSafe("InstallLocation", Paths.Base);
                    uninstallKey.SetValueSafe("ModifyPath", $"\"{Paths.Application}\" -settings");
                    uninstallKey.SetValueSafe("QuietUninstallString", $"\"{Paths.Application}\" -uninstall -quiet");
                    uninstallKey.SetValueSafe("UninstallString", $"\"{Paths.Application}\" -uninstall");
                }

                WindowsRegistry.RegisterPlayer();

                RefreshShortcut(Path.Combine(Paths.Desktop, $"{App.ProjectName}.lnk"));
                RefreshShortcut(Path.Combine(Paths.WindowsStartMenu, $"{App.ProjectName}.lnk"));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ManagerViewModel::RelocateToDrive", ex);
                Frontend.ShowMessageBox(String.Format(Strings.Installer_Manager_Map_Failed, ex.Message), MessageBoxImage.Error);
            }

            OnPropertyChanged(nameof(SelectedDrive));
        }

        private static void RefreshShortcut(string lnkPath)
        {
            if (!File.Exists(lnkPath))
                return;

            File.Delete(lnkPath);
            Shortcut.Create(Paths.Application, "", lnkPath);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destDir));

            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                File.Copy(filePath, filePath.Replace(sourceDir, destDir), true);
        }

        public void Refresh()
        {
            Player.Refresh();
            Studio.Refresh();

            OnPropertyChanged(nameof(CanChangeMapLocation));
            OnPropertyChanged(nameof(SelectedDrive));
        }
    }
}
