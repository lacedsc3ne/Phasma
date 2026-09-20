using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ClassicClientViewModel : NotifyPropertyChangedViewModel
    {
        private bool _isBusy;
        private double _progressValue;
        private string _progressText = "";
        private CancellationTokenSource? _operationCts;

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsNotBusy)); }
        }

        public bool IsNotBusy => !_isBusy;

        public double ProgressValue
        {
            get => _progressValue;
            private set { _progressValue = value; OnPropertyChanged(nameof(ProgressValue)); }
        }

        public string ProgressText
        {
            get => _progressText;
            private set { _progressText = value; OnPropertyChanged(nameof(ProgressText)); }
        }

        public ObservableCollection<ClassicCatalogEntry> AvailableClients { get; } = new();

        private ClassicCatalogEntry? _selectedAvailableClient;

        public ClassicCatalogEntry? SelectedAvailableClient
        {
            get => _selectedAvailableClient;
            set { _selectedAvailableClient = value; OnPropertyChanged(nameof(SelectedAvailableClient)); }
        }

        public string EngineDataStatus => ClassicClients.EngineDataInstalled
            ? "The classic engine data pack (scripts, assets, maps) is installed."
            : "The classic engine data pack is not installed yet - install it before installing a classic client.";

        public ICommand InstallEngineCommand => new AsyncRelayCommand(InstallEngineAsync);

        public ICommand InstallSelectedClientCommand => new AsyncRelayCommand(InstallSelectedClientAsync);

        public ICommand UpdateAllCommand => new AsyncRelayCommand(UpdateAllAsync);

        public ICommand CancelOperationCommand => new RelayCommand(CancelOperation);

        public ClassicClientViewModel()
        {
            foreach (ClassicCatalogEntry entry in ClassicClients.Catalog)
                AvailableClients.Add(entry);

            RebuildInstalledClientCards();

            _ = RefreshCatalogAsync();
        }

        public sealed class InstalledClientItem : NotifyPropertyChangedViewModel
        {
            private bool _isRunning;

            public string Code { get; init; } = "";

            public string Name { get; init; } = "";

            public string VersionDisplay { get; init; } = "";

            public bool IsRunning
            {
                get => _isRunning;
                set
                {
                    _isRunning = value;
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsNotRunning));
                }
            }

            public bool IsNotRunning => !_isRunning;
        }

        public ObservableCollection<InstalledClientItem> InstalledClientCards { get; } = new();

        public bool HasInstalledClients => InstalledClientCards.Count > 0;

        public bool HasNoInstalledClients => InstalledClientCards.Count == 0;

        private string _runningClientCode = "";

        public ICommand LaunchInstalledClientCommand => new RelayCommand<string>(LaunchInstalledClient);

        private void RebuildInstalledClientCards()
        {
            InstalledClientCards.Clear();

            foreach (string code in InstalledClients)
            {
                ClassicClients.ClassicClientConfig? config = ClassicClients.GetInstalledConfig(code);
                ClassicCatalogEntry? entry = ClassicClients.Catalog.FirstOrDefault(item => String.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

                string name = !String.IsNullOrWhiteSpace(config?.Name)
                    ? config!.Name
                    : !String.IsNullOrWhiteSpace(entry?.Name) ? entry!.Name : code;

                InstalledClientCards.Add(new InstalledClientItem
                {
                    Code = code,
                    Name = name,
                    VersionDisplay = $"Version {code}"
                });
            }

            UpdateRunningClientCards();

            OnPropertyChanged(nameof(HasInstalledClients));
            OnPropertyChanged(nameof(HasNoInstalledClients));
        }

        private void UpdateRunningClientCards()
        {
            bool running = ClassicServerManager.IsRunning;

            foreach (InstalledClientItem item in InstalledClientCards)
                item.IsRunning = running && String.Equals(_runningClientCode, item.Code, StringComparison.OrdinalIgnoreCase);
        }

        private void LaunchInstalledClient(string? code)
        {
            if (String.IsNullOrWhiteSpace(code))
                return;

            SelectedClassicClient = code;
            OnPropertyChanged(nameof(SelectedClassicClient));

            LaunchClient();

            _runningClientCode = ClassicServerManager.IsRunning ? code : "";
            UpdateRunningClientCards();
        }

        private async Task RefreshCatalogAsync()
        {
            try
            {
                List<ClassicCatalogEntry> clients = await ClassicClients.FetchManifestClientsAsync(CancellationToken.None);
                if (clients.Count == 0)
                    return;

                AvailableClients.Clear();
                foreach (ClassicCatalogEntry entry in clients)
                    AvailableClients.Add(entry);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ClassicClientViewModel::RefreshCatalog", $"Failed to fetch remote catalog, keeping built-in list: {ex.Message}");
            }
        }

        private void ReportProgress(double percent, string text)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ProgressValue = Math.Clamp(percent, 0, 100);
                ProgressText = text;
            });
        }

        private async Task InstallEngineAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            ProgressValue = 0;
            ProgressText = Strings.Menu_ClassicClient_Progress_Starting;
            _operationCts = new CancellationTokenSource();

            try
            {
                await ClassicClients.InstallEngineAsync(ReportProgress, _operationCts.Token);
                ProgressText = Strings.Menu_ClassicClient_Progress_EngineInstalled;
            }
            catch (OperationCanceledException)
            {
                ProgressText = Strings.Menu_ClassicClient_Progress_Cancelled;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ClassicClientViewModel::InstallEngine", ex);
                Frontend.ShowMessageBox($"Failed to install the classic engine data pack: {ex.Message}", System.Windows.MessageBoxImage.Error);
                ProgressText = Strings.Menu_ClassicClient_Progress_Failed;
            }
            finally
            {
                IsBusy = false;
                _operationCts?.Dispose();
                _operationCts = null;
                OnPropertyChanged(nameof(EngineDataStatus));
            }
        }

        private async Task InstallSelectedClientAsync()
        {
            if (IsBusy || SelectedAvailableClient is null)
                return;

            string code = SelectedAvailableClient.Code;

            IsBusy = true;
            ProgressValue = 0;
            ProgressText = Strings.Menu_ClassicClient_Progress_Starting;
            _operationCts = new CancellationTokenSource();

            try
            {
                await ClassicClients.InstallClientAsync(code, ReportProgress, _operationCts.Token);
                ProgressText = string.Format(Strings.Menu_ClassicClient_Progress_ClientInstalled, code);
                RefreshClients();
            }
            catch (OperationCanceledException)
            {
                ProgressText = Strings.Menu_ClassicClient_Progress_Cancelled;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ClassicClientViewModel::InstallClient", ex);
                Frontend.ShowMessageBox($"Failed to install classic client {code}: {ex.Message}", System.Windows.MessageBoxImage.Error);
                ProgressText = Strings.Menu_ClassicClient_Progress_Failed;
            }
            finally
            {
                IsBusy = false;
                _operationCts?.Dispose();
                _operationCts = null;
                OnPropertyChanged(nameof(EngineDataStatus));
            }
        }

        private async Task UpdateAllAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            ProgressValue = 0;
            ProgressText = Strings.Menu_ClassicClient_Progress_CheckingUpdates;
            _operationCts = new CancellationTokenSource();

            try
            {
                await ClassicClients.UpdateEverythingAsync(
                    SelectedClassicClient,
                    text => ReportProgress(ProgressValue, text),
                    ReportProgress,
                    _operationCts.Token);
                ProgressText = Strings.Menu_ClassicClient_Progress_UpToDate;
                RefreshClients();
            }
            catch (OperationCanceledException)
            {
                ProgressText = Strings.Menu_ClassicClient_Progress_Cancelled;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ClassicClientViewModel::UpdateAll", ex);
                Frontend.ShowMessageBox($"Failed to update classic clients: {ex.Message}", System.Windows.MessageBoxImage.Error);
                ProgressText = Strings.Menu_ClassicClient_Progress_Failed;
            }
            finally
            {
                IsBusy = false;
                _operationCts?.Dispose();
                _operationCts = null;
                OnPropertyChanged(nameof(EngineDataStatus));
            }
        }

        private void CancelOperation()
        {
            try { _operationCts?.Cancel(); }
            catch { }
        }

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

            RebuildInstalledClientCards();

            OnPropertyChanged(nameof(EngineStatus));
            OnPropertyChanged(nameof(EngineDataStatus));
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
            _runningClientCode = "";
            UpdateRunningClientCards();
            OnPropertyChanged(nameof(IsServerRunning));
            OnPropertyChanged(nameof(RedirectStatus));
        }
    }
}
