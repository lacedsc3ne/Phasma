using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Win32;

using PhasmaStrap.AppData;

namespace PhasmaStrap.UI.ViewModels.Installer
{
    // Bootstrapper.Run() drives its progress purely through an IBootstrapperDialog, and mutates
    // some process-wide static state along the way (RobloxInterfaces.Deployment.BinaryType chief
    // among them), so only one Bootstrapper should ever be mid-flight per process at a time. This
    // small gate is shared between the Player and Studio cards below so their Install buttons
    // can't both kick off a real install run concurrently.
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

    // Minimal, headless IBootstrapperDialog - this is the same contract the real bootstrapper
    // dialog windows (Fluent/Classic/etc) implement, just without any actual window. It lets us
    // drive a genuine Bootstrapper install run from inside the installer wizard and surface its
    // status text back onto a card instead of faking a progress bar.
    internal class SilentBootstrapperDialog : IBootstrapperDialog
    {
        private readonly Action<string> _onMessage;

        public SilentBootstrapperDialog(Action<string> onMessage) => _onMessage = onMessage;

        // fully-qualified since this namespace (PhasmaStrap.UI.ViewModels.Installer) is a sibling
        // of PhasmaStrap.UI.ViewModels.Bootstrapper, which would otherwise shadow the actual
        // PhasmaStrap.Bootstrapper class for an unqualified "Bootstrapper" here
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

    // Drives one "Roblox Player" / "Roblox Studio" card on the Manager step. Everything here reads
    // real install state off the same IAppData/DistributionState plumbing Bootstrapper itself uses
    // (see AppData/RobloxPlayerData.cs, AppData/RobloxStudioData.cs, AppData/CommonAppData.cs), and
    // "Install" runs an actual Bootstrapper pass rather than simulating one.
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

        // internal, not public: IAppData is an internal interface (see AppData/IAppData.cs), so this
        // constructor's accessibility can't exceed that of its parameter type
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

            // the installer wizard already offered the user a self-update earlier on the Welcome
            // step, and NoLaunch keeps this from dropping the user into a running game mid-wizard -
            // both flags are restored afterwards so they don't leak into the rest of the process
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

        // the drive can only be remapped before either product has actually been installed - once
        // Roblox itself lives under Paths.Versions, moving it would mean relocating real install
        // data instead of just the still-empty scaffolding Installer.DoInstall() laid down
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

                // reuses the same write-test approach Installer.CheckInstallLocation() uses for the
                // main install location picker on the Install step
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
            // Shortcut.Create() is create-only (it no-ops if the .lnk already exists), so to point
            // an existing shortcut at the new executable path we need to delete it first
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
