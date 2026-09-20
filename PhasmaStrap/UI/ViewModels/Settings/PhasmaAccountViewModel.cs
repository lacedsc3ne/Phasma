using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class PhasmaAccountViewModel : NotifyPropertyChangedViewModel
    {
        private bool _accountBusy;
        private string _accountStatus = "";

        public string AccountHeader => PhasmaStrap.Utility.PhasmaAccount.SignedIn
            ? $"Signed in as {PhasmaStrap.Utility.PhasmaAccount.DisplayName}"
            : "Not signed in";

        public string AccountDetail
        {
            get
            {
                if (!string.IsNullOrEmpty(_accountStatus))
                    return _accountStatus;

                if (PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                    return $"Your account ID is {App.State.Prop.AccountId}. Give it to support if you ever need help.";

                return "Optional. Signing in lets support find your reports when you ask for help, and is the same account as on phasmastrap.com.";
            }
        }

        public string AccountActionText => PhasmaStrap.Utility.PhasmaAccount.SignedIn ? "Sign out" : "Sign in";

        public bool AccountActionEnabled => !_accountBusy;

        public ICommand AccountActionCommand => new RelayCommand(RunAccountAction);

        public ICommand LinkRobloxCommand => new RelayCommand(() => Utilities.ShellExecute("https://phasmastrap.com/link-roblox"));

        private void RefreshAccountCard()
        {
            OnPropertyChanged(nameof(AccountHeader));
            OnPropertyChanged(nameof(AccountDetail));
            OnPropertyChanged(nameof(AccountActionText));
            OnPropertyChanged(nameof(AccountActionEnabled));
            OnPropertyChanged(nameof(BackupVisibility));
        }

        private void SetAccountStatus(string text)
        {
            _accountStatus = text;
            RefreshAccountCard();
        }

        private async void RunAccountAction()
        {
            if (_accountBusy)
                return;

            _accountBusy = true;
            RefreshAccountCard();

            try
            {
                if (PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                {
                    await PhasmaStrap.Utility.PhasmaAccount.SignOutAsync();
                    SetAccountStatus("");
                    return;
                }

                using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(12));
                bool ok = await PhasmaStrap.Utility.PhasmaAccount.SignInAsync(SetAccountStatus, cancel.Token);

                if (ok)
                    SetAccountStatus("");
            }
            catch (OperationCanceledException)
            {
                SetAccountStatus("That took too long. Try again when you are ready.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PhasmaStrapViewModel::RunAccountAction", ex);
                SetAccountStatus("Something went wrong signing in. The log has the details.");
            }
            finally
            {
                _accountBusy = false;
                RefreshAccountCard();
            }
        }

        private bool _backupBusy;
        private string _backupStatus = "";

        public Visibility BackupVisibility => PhasmaStrap.Utility.PhasmaAccount.SignedIn ? Visibility.Visible : Visibility.Collapsed;

        public bool BackupEnabled => !_backupBusy;

        public string BackupDetail => string.IsNullOrEmpty(_backupStatus)
            ? "Keep a copy of your settings, FastFlag profiles and crosshairs on your account, so another PC can pick them up."
            : _backupStatus;

        public bool AutoBackupEnabled
        {
            get => App.Settings.Prop.AutoBackupEnabled;
            set
            {
                App.Settings.Prop.AutoBackupEnabled = value;
                App.Settings.SaveDeferred();

                if (value)
                    _ = PhasmaStrap.Utility.PhasmaAccount.MaybeAutoBackUpAsync();
            }
        }

        public ICommand BackUpCommand => new RelayCommand(RunBackUp);

        public ICommand RestoreCommand => new RelayCommand(RunRestore);

        private void RefreshBackupCard()
        {
            OnPropertyChanged(nameof(BackupVisibility));
            OnPropertyChanged(nameof(BackupEnabled));
            OnPropertyChanged(nameof(BackupDetail));
        }

        private async void RunBackUp()
        {
            if (_backupBusy)
                return;

            _backupBusy = true;
            _backupStatus = "Uploading...";
            RefreshBackupCard();

            try
            {
                int count = await PhasmaStrap.Utility.PhasmaAccount.BackUpNowAsync();

                _backupStatus = count switch
                {
                    0 => "There was nothing to back up.",
                    -1 => "That did not go through. Check your connection and try again.",
                    _ => $"Backed up {count} file(s) just now.",
                };
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PhasmaStrapViewModel::RunBackUp", ex);
                _backupStatus = "Something went wrong backing up. The log has the details.";
            }
            finally
            {
                _backupBusy = false;
                RefreshBackupCard();
            }
        }

        private async void RunRestore()
        {
            if (_backupBusy)
                return;

            var confirm = Frontend.ShowMessageBox(
                "This replaces your settings, FastFlag profiles and crosshairs on this PC with your last backup.\n\n"
                + "PhasmaStrap will close afterwards so the restored files are read fresh. Carry on?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo
            );

            if (confirm != MessageBoxResult.Yes)
                return;

            _backupBusy = true;
            _backupStatus = "Downloading...";
            RefreshBackupCard();

            try
            {
                var files = await PhasmaStrap.Utility.PhasmaAccount.RestoreAsync();

                if (files is null || files.Count == 0)
                {
                    _backupStatus = "There is no backup on your account yet.";
                    return;
                }

                int written = 0;
                var targets = PhasmaStrap.Utility.PhasmaAccount.BackupTargets();

                foreach (var (name, contents) in files)
                {
                    if (!targets.TryGetValue(name, out string? file))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);

                    if (File.Exists(file))
                        File.Copy(file, file + ".before-restore", true);

                    await File.WriteAllTextAsync(file, contents);
                    written++;
                }

                _backupStatus = $"Put back {written} file(s). Closing so they are read fresh.";
                RefreshBackupCard();

                App.Logger.WriteLine("PhasmaStrapViewModel::RunRestore", $"Restored {written} file(s) from the backup");
                await Task.Delay(1200);
                App.SoftTerminate();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PhasmaStrapViewModel::RunRestore", ex);
                _backupStatus = "Something went wrong restoring. The log has the details.";
            }
            finally
            {
                _backupBusy = false;
                RefreshBackupCard();
            }
        }
    }
}
