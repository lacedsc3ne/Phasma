using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class HealthRow
    {
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public Wpf.Ui.Common.SymbolRegular Symbol { get; init; }
        public Brush Color { get; init; } = Brushes.Gray;
        public string FixLabel { get; init; } = "";
        public Visibility FixVisibility => FixLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public ICommand? FixCommand { get; init; }
    }

    public sealed class PingRow
    {
        public string Label { get; init; } = "";
        public string Address { get; init; } = "";
        public string Numbers { get; init; } = "";
        public PointCollection Spark { get; init; } = new();
    }

    public sealed partial class DiagnosticsViewModel : NotifyPropertyChangedViewModel
    {
        private static readonly Brush Green = Frozen("#2ECC71"), Amber = Frozen("#F5B301"), Red = Frozen("#E74C3C"), Grey = Frozen("#8A8F98");

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public DiagnosticsViewModel()
        {
            RunHealthCommand = new AsyncRelayCommand(RunHealthAsync);
            RunConnectionCommand = new AsyncRelayCommand(RunConnectionAsync);
            CancelConnectionCommand = new RelayCommand(() => _connectionCancel?.Cancel());

            RefreshServer();
            InitialiseMore();
        }

        partial void InitialiseMore();

        public ObservableCollection<HealthRow> Health { get; } = new();
        public ICommand RunHealthCommand { get; }

        private string _healthSummary = "Checks the things that stop Roblox from starting or Play links from working. Nothing is changed unless you press a Fix button.";
        public string HealthSummary { get => _healthSummary; private set { _healthSummary = value; OnPropertyChanged(nameof(HealthSummary)); } }

        private bool _healthBusy;
        public bool HealthIdle => !_healthBusy;

        private async Task RunHealthAsync()
        {
            if (_healthBusy)
                return;

            _healthBusy = true;
            OnPropertyChanged(nameof(HealthIdle));
            Health.Clear();
            HealthSummary = "Checking...";

            try
            {
                List<HealthResult> results = await HealthCheck.RunAsync(
                    result => Application.Current.Dispatcher.BeginInvoke(new Action(() => Health.Add(ToRow(result)))),
                    CancellationToken.None);

                int problems = results.Count(r => r.Status == HealthStatus.Problem), warnings = results.Count(r => r.Status == HealthStatus.Warning);

                HealthSummary = problems > 0
                    ? $"{problems} problem{(problems == 1 ? "" : "s")} found{(warnings > 0 ? $" and {warnings} thing{(warnings == 1 ? "" : "s")} worth a look" : "")}."
                    : warnings > 0
                        ? $"Nothing broken. {warnings} thing{(warnings == 1 ? "" : "s")} worth a look."
                        : "Everything checked out.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DiagnosticsViewModel::RunHealth", ex);
                HealthSummary = $"The check stopped: {ex.Message}";
            }
            finally
            {
                _healthBusy = false;
                OnPropertyChanged(nameof(HealthIdle));
            }
        }

        private HealthRow ToRow(HealthResult result) => new()
        {
            Title = result.Title,
            Detail = result.Detail,
            Symbol = result.Status switch
            {
                HealthStatus.Ok => Wpf.Ui.Common.SymbolRegular.CheckmarkCircle24,
                HealthStatus.Info => Wpf.Ui.Common.SymbolRegular.Info24,
                HealthStatus.Warning => Wpf.Ui.Common.SymbolRegular.Warning24,
                _ => Wpf.Ui.Common.SymbolRegular.ErrorCircle24,
            },
            Color = result.Status switch { HealthStatus.Ok => Green, HealthStatus.Info => Grey, HealthStatus.Warning => Amber, _ => Red },
            FixLabel = result.Fix is null ? "" : result.FixLabel ?? "Fix",
            FixCommand = result.Fix is null ? null : new RelayCommand(() =>
            {
                try
                {
                    result.Fix();
                    App.Logger.WriteLine("DiagnosticsViewModel", $"Applied fix: {result.FixLabel} ({result.Title})");
                }
                catch (Exception ex)
                {
                    Frontend.ShowMessageBox($"That did not work: {ex.Message}", MessageBoxImage.Warning);
                }

                _ = RunHealthAsync();
            }),
        };

        public ObservableCollection<PingRow> PingRows { get; } = new();
        public ObservableCollection<string> ConnectionFindings { get; } = new();
        public ObservableCollection<string> RouteRows { get; } = new();

        public ICommand RunConnectionCommand { get; }
        public ICommand CancelConnectionCommand { get; }

        private CancellationTokenSource? _connectionCancel;
        private string? _server;

        public string[] DurationOptions { get; } = { "15 seconds", "30 seconds", "60 seconds", "2 minutes" };
        private static readonly int[] DurationSeconds = { 15, 30, 60, 120 };

        private string _selectedDuration = "30 seconds";
        public string SelectedDuration { get => _selectedDuration; set { _selectedDuration = value; OnPropertyChanged(nameof(SelectedDuration)); } }

        private string _serverText = "";
        public string ServerText { get => _serverText; private set { _serverText = value; OnPropertyChanged(nameof(ServerText)); } }

        private string _connectionStatus = "";
        public string ConnectionStatus { get => _connectionStatus; private set { _connectionStatus = value; OnPropertyChanged(nameof(ConnectionStatus)); } }

        private string _connectionVerdict = "";
        public string ConnectionVerdict { get => _connectionVerdict; private set { _connectionVerdict = value; OnPropertyChanged(nameof(ConnectionVerdict)); OnPropertyChanged(nameof(ConnectionVerdictVisibility)); } }
        public Visibility ConnectionVerdictVisibility => _connectionVerdict.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private double _connectionProgress;
        public double ConnectionProgress { get => _connectionProgress; private set { _connectionProgress = value; OnPropertyChanged(nameof(ConnectionProgress)); } }

        private bool _connectionBusy;
        public bool ConnectionIdle => !_connectionBusy;
        public Visibility ConnectionBusyVisibility => _connectionBusy ? Visibility.Visible : Visibility.Collapsed;

        private void RefreshServer()
        {
            _server = ConnectionDoctor.FindLastServer(Paths.RobloxLogs);

            if (_server is null)
            {
                ServerText = "No game server found in Roblox's logs yet - join a game first, then come back.";
                return;
            }

            string region = "";
            try
            {
                ServerFetchStore.EnsureLoaded();
                region = ServerRegion.Describe(RobloxDatacenterMap.Map(_server));
            }
            catch
            {
            }

            ServerText = $"Tests the way to the server you joined last: {_server}{(region.Length > 0 ? $" ({region})" : "")}. For a live problem, run it while you are in that game.";
        }

        private async Task RunConnectionAsync()
        {
            if (_connectionBusy)
                return;

            RefreshServer();
            if (_server is null)
                return;

            _connectionBusy = true;
            OnPropertyChanged(nameof(ConnectionIdle));
            OnPropertyChanged(nameof(ConnectionBusyVisibility));

            PingRows.Clear();
            ConnectionFindings.Clear();
            RouteRows.Clear();
            ConnectionVerdict = "";
            ConnectionProgress = 0;

            _connectionCancel = new CancellationTokenSource();
            int seconds = DurationSeconds[Math.Max(0, Array.IndexOf(DurationOptions, _selectedDuration))];

            try
            {
                ConnectionDoctor.Log ??= message => App.Logger.WriteLine("ConnectionDoctor", message);

                ConnectionReport report = await Task.Run(() => ConnectionDoctor.RunAsync(_server, seconds,
                    status => Application.Current.Dispatcher.BeginInvoke(new Action(() => ConnectionStatus = status)),
                    progress => Application.Current.Dispatcher.BeginInvoke(new Action(() => ConnectionProgress = progress)),
                    _connectionCancel.Token));

                ConnectionVerdict = report.Verdict;
                foreach (string finding in report.Findings)
                    ConnectionFindings.Add(finding);

                foreach (PingTarget target in report.Targets)
                {
                    PingRows.Add(new PingRow
                    {
                        Label = target.Label,
                        Address = target.Address,
                        Numbers = target.Answers
                            ? $"{target.Average:0} ms  ·  jitter {target.Jitter:0.0}  ·  worst {target.Worst}  ·  lost {target.LossPercent:0.#} %"
                            : "no answer",
                        Spark = Spark(target.Samples),
                    });
                }

                foreach (RouteHop hop in report.Route)
                    RouteRows.Add($"{hop.Ttl,2}   {(hop.Address.Length > 0 ? hop.Address : "no answer")}{(hop.IsDestination ? "   (the server)" : ConnectionDoctor.IsPrivate(hop.Address) ? "   (your network)" : "")}");

                ConnectionStatus = $"Done. {(report.Wireless ? "Connected over Wi-Fi" : "Connected by cable")}{(report.Adapter.Length > 0 ? $" ({report.Adapter})" : "")}.";
            }
            catch (OperationCanceledException)
            {
                ConnectionStatus = "Stopped.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DiagnosticsViewModel::RunConnection", ex);
                ConnectionStatus = $"The test failed: {ex.Message}";
            }
            finally
            {
                _connectionBusy = false;
                OnPropertyChanged(nameof(ConnectionIdle));
                OnPropertyChanged(nameof(ConnectionBusyVisibility));
            }
        }

        private static PointCollection Spark(List<int> samples)
        {
            var points = new PointCollection();
            if (samples.Count < 2)
                return points;

            const double width = 300, height = 40;
            double max = Math.Max(20, samples.Max()) * 1.1;

            for (int i = 0; i < samples.Count; i++)
                points.Add(new Point(i * width / (samples.Count - 1), samples[i] < 0 ? height : height - samples[i] / max * height));

            points.Freeze();
            return points;
        }
    }
}
