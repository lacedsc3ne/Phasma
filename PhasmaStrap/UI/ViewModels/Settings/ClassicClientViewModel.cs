using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ClassicClientViewModel : NotifyPropertyChangedViewModel
    {
        public bool ClassicClientEnabled
        {
            get => App.Settings.Prop.ClassicClientEnabled;
            set
            {
                App.Settings.Prop.ClassicClientEnabled = value;
                OnPropertyChanged(nameof(ClassicClientEnabled));
            }
        }

        public string ClassicClientInstallLocation
        {
            get => App.Settings.Prop.ClassicClientInstallLocation;
            set => App.Settings.Prop.ClassicClientInstallLocation = value;
        }

        public string SelectedClassicClient
        {
            get => App.Settings.Prop.SelectedClassicClient;
            set => App.Settings.Prop.SelectedClassicClient = value;
        }

        public ObservableCollection<string> InstalledClients { get; } = new(ClassicClients.ListInstalledClients());

        public string EngineStatus => ClassicClients.ServerEngineInstalled
            ? $"Found at {ClassicClients.ServerPath}"
            : $"Not found. Place {ClassicClients.ServerExecutableName} (built from the PhasmaStrap.Server project) at {ClassicClients.ServerPath}.";

        public string RedirectStatus => ClassicHostRedirect.IsApplied()
            ? "The classic client hosts redirect is currently active."
            : "The classic client hosts redirect is not active.";

        public bool IsServerRunning => ClassicServerManager.IsRunning;

        public ICommand BrowseInstallLocationCommand => new RelayCommand(BrowseInstallLocation);

        public ICommand RefreshClientsCommand => new RelayCommand(RefreshClients);

        public ICommand LaunchClientCommand => new RelayCommand(LaunchClient);

        public ICommand StopServerCommand => new RelayCommand(StopServer);

        private void BrowseInstallLocation()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            ClassicClientInstallLocation = dialog.SelectedPath;
            OnPropertyChanged(nameof(ClassicClientInstallLocation));
            RefreshClients();
        }

        private void RefreshClients()
        {
            InstalledClients.Clear();
            foreach (string client in ClassicClients.ListInstalledClients())
                InstalledClients.Add(client);

            OnPropertyChanged(nameof(EngineStatus));
            OnPropertyChanged(nameof(RedirectStatus));
        }

        private void LaunchClient()
        {
            if (string.IsNullOrWhiteSpace(SelectedClassicClient))
                return;

            string? startError = ClassicServerManager.Start(SelectedClassicClient);
            if (startError != null)
            {
                Frontend.ShowMessageBox(startError, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (!ClassicClients.LaunchClient(SelectedClassicClient, out string? launchError))
            {
                Frontend.ShowMessageBox(launchError ?? "Failed to launch the classic client.", System.Windows.MessageBoxImage.Error);
            }

            OnPropertyChanged(nameof(IsServerRunning));
            OnPropertyChanged(nameof(RedirectStatus));
        }

        private void StopServer()
        {
            ClassicServerManager.Stop();
            OnPropertyChanged(nameof(IsServerRunning));
            OnPropertyChanged(nameof(RedirectStatus));
        }
    }
}
