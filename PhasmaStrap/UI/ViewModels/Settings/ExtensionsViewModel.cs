using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    // Rojo is auto-installed/managed by PhasmaStrap rather than "browse to an existing
    // install" like the rest of ExtensionManager's entries, so it gets its own small
    // view model driving a dedicated card on the Extensions page instead of slotting into
    // the generic ExtensionEntry template (whose Browse/Clear actions don't apply to it).
    public class RojoViewModel : NotifyPropertyChangedViewModel
    {
        private bool _isBusy;
        private string _statusText = "";

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanInstallOrUpdate));
                OnPropertyChanged(nameof(CanToggleServe));
                OnPropertyChanged(nameof(CanUninstall));
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsInstalled => RojoManager.IsInstalled;

        public bool IsServing => RojoManager.IsServing;

        public string InstalledVersionText => RojoManager.InstalledVersion is string v ? $"Installed ({v})" : "Not installed";

        public string ProjectPath
        {
            get => App.Settings.Prop.RojoLastProjectPath;
            private set
            {
                App.Settings.Prop.RojoLastProjectPath = value;
                App.Settings.Save();
                OnPropertyChanged(nameof(ProjectPath));
                OnPropertyChanged(nameof(ProjectPathText));
                OnPropertyChanged(nameof(CanServe));
                OnPropertyChanged(nameof(CanToggleServe));
            }
        }

        public bool CanServe => !string.IsNullOrEmpty(ProjectPath) && File.Exists(ProjectPath);

        public string ProjectPathText => string.IsNullOrEmpty(ProjectPath) ? "No project file selected" : ProjectPath;

        public string InstallOrUpdateButtonText => IsInstalled ? "Update" : "Install";
        public string ServeButtonText => IsServing ? "Stop serve" : "Start serve";

        // CanExecute is intentionally not used here - like the rest of this page
        // (see BrowseCommand/ClearCommand above), enabled state is driven directly from
        // XAML IsEnabled bindings against these computed booleans instead, since
        // CommunityToolkit's RelayCommand doesn't auto-requery on its own. Likewise the
        // page has no BoolToVisibility converter registered in App.xaml, so instead of
        // toggling button visibility, Install/Update and Start/Stop serve are each a
        // single button whose text and behavior swap based on current state.
        public bool CanInstallOrUpdate => !IsBusy && !IsServing;
        public bool CanToggleServe => IsServing || (!IsBusy && IsInstalled && CanServe);
        public bool CanUninstall => !IsBusy && IsInstalled && !IsServing;
        public ICommand InstallOrUpdateCommand => new AsyncRelayCommand(InstallOrUpdateAsync);
        public ICommand BrowseProjectCommand => new RelayCommand(BrowseProject);
        public ICommand ToggleServeCommand => new RelayCommand(ToggleServe);
        public ICommand UninstallCommand => new AsyncRelayCommand(UninstallAsync);

        // marshals progress text from RojoManager's background download/extract work back
        // onto the UI thread - see Frontend.ShowMessageBox for the same Dispatcher.Invoke
        // convention used elsewhere in this codebase
        private void ReportProgress(string message) =>
            Application.Current.Dispatcher.Invoke(() => StatusText = message);

        private async Task InstallOrUpdateAsync()
        {
            IsBusy = true;
            StatusText = IsInstalled ? "Checking for updates..." : "Installing Rojo...";

            try
            {
                await RojoManager.EnsureInstalledAsync(ReportProgress, CancellationToken.None);
                StatusText = $"Rojo {RojoManager.InstalledVersion} ready.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RojoViewModel::InstallOrUpdateAsync", ex);
                StatusText = $"Failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Refresh();
            }
        }

        private async Task UninstallAsync()
        {
            IsBusy = true;
            StatusText = "Uninstalling Rojo...";

            try
            {
                await RojoManager.UninstallAsync();
                StatusText = "Rojo uninstalled.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RojoViewModel::UninstallAsync", ex);
                StatusText = $"Failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Refresh();
            }
        }

        private void BrowseProject()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Locate a Rojo project file",
                Filter = "Rojo project files (*.project.json)|*.project.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
                ProjectPath = dialog.FileName;
        }

        private void ToggleServe()
        {
            if (IsServing)
            {
                RojoManager.StopServe();
                StatusText = "rojo serve stopped.";
            }
            else
            {
                string? dir = Path.GetDirectoryName(ProjectPath);
                if (dir is null)
                    return;

                StatusText = RojoManager.StartServe(dir) ? $"Serving {ProjectPath}" : "Failed to start rojo serve.";
            }

            Refresh();
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(IsServing));
            OnPropertyChanged(nameof(InstalledVersionText));
            OnPropertyChanged(nameof(CanServe));
            OnPropertyChanged(nameof(InstallOrUpdateButtonText));
            OnPropertyChanged(nameof(ServeButtonText));
            OnPropertyChanged(nameof(CanInstallOrUpdate));
            OnPropertyChanged(nameof(CanToggleServe));
            OnPropertyChanged(nameof(CanUninstall));
        }
    }

    public class ExtensionEntry : NotifyPropertyChangedViewModel
    {
        public Extension Extension { get; }

        public ExtensionEntry(Extension extension) => Extension = extension;

        public string DisplayName => Extension.DisplayName;
        public string Description => Extension.Description;

        public bool IsInstalled => ExtensionManager.IsInstalled(Extension.Id);

        public string StatusText => IsInstalled
            ? ExtensionManager.GetSavedPath(Extension.Id)!
            : "Not located - browse to the executable to enable this";

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public class ExtensionsViewModel : NotifyPropertyChangedViewModel
    {
        public RojoViewModel Rojo { get; } = new();

        public ObservableCollection<ExtensionEntry> Extensions { get; } =
            new(ExtensionManager.KnownExtensions.Select(x => new ExtensionEntry(x)));

        public ICommand BrowseCommand => new RelayCommand<ExtensionEntry>(Browse);
        public ICommand LaunchCommand => new RelayCommand<ExtensionEntry>(entry => ExtensionManager.Launch(entry!.Extension.Id));
        public ICommand ClearCommand => new RelayCommand<ExtensionEntry>(Clear);

        private void Browse(ExtensionEntry? entry)
        {
            if (entry is null)
                return;

            var dialog = new OpenFileDialog
            {
                Title = $"Locate {entry.DisplayName}",
                Filter = $"{entry.Extension.ExecutableName}|{entry.Extension.ExecutableName}|Executable files (*.exe)|*.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                ExtensionManager.SetSavedPath(entry.Extension.Id, dialog.FileName);
                App.Settings.Save();
                entry.Refresh();
            }
        }

        private void Clear(ExtensionEntry? entry)
        {
            if (entry is null)
                return;

            ExtensionManager.ClearSavedPath(entry.Extension.Id);
            App.Settings.Save();
            entry.Refresh();
        }
    }
}
