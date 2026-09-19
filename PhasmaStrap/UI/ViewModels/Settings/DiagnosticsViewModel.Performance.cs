using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class RunRow
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Numbers { get; init; } = "";
        public string Verdict { get; init; } = "";
        public List<string> Findings { get; init; } = new();
        public Visibility FindingsVisibility => Findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public PointCollection Graph { get; init; } = new();
        public string GraphScale { get; init; } = "";
    }

    public sealed class TunerCandidate
    {
        public TunerVariant Variant { get; init; } = new();
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public bool IsSelected { get; set; }
    }

    public sealed class TunerStepRow
    {
        public string Text { get; init; } = "";
        public Wpf.Ui.Common.SymbolRegular Symbol { get; init; }
        public Brush Color { get; init; } = Brushes.Gray;
    }

    public sealed class TunerResultRow
    {
        public string Variant { get; init; } = "";
        public string Low { get; init; } = "";
        public string Average { get; init; } = "";
        public string Stutters { get; init; } = "";
        public string Runs { get; init; } = "";
        public FontWeight Weight { get; init; } = FontWeights.Normal;
    }

    public sealed partial class DiagnosticsViewModel
    {
        private DispatcherTimer? _measureTimer;
        private string _lastSeenStatus = "";

        partial void InitialisePerformance()
        {
            RefreshRuns();
            RefreshTuner();

            // the Watcher reports through status.json; this is the only way to hear from it
            _measureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _measureTimer.Tick += (_, _) => PollMeasurement();
            _measureTimer.Start();
            PollMeasurement();
        }

        // ------------------------------------------------------------------ stutter

        public ObservableCollection<RunRow> Runs { get; } = new();
        public Visibility RunsEmptyVisibility => Runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string[] MeasureOptions { get; } = { "30 seconds", "60 seconds", "2 minutes", "5 minutes" };
        private static readonly int[] MeasureSeconds = { 30, 60, 120, 300 };

        private string _selectedMeasure = "60 seconds";
        public string SelectedMeasure { get => _selectedMeasure; set { _selectedMeasure = value; OnPropertyChanged(nameof(SelectedMeasure)); } }

        private string _measureStatus = "";
        public string MeasureStatusText { get => _measureStatus; private set { _measureStatus = value; OnPropertyChanged(nameof(MeasureStatusText)); } }

        private double _measureProgress;
        public double MeasureProgress { get => _measureProgress; private set { _measureProgress = value; OnPropertyChanged(nameof(MeasureProgress)); } }

        private bool _measurePending;
        public Visibility MeasurePendingVisibility => _measurePending ? Visibility.Visible : Visibility.Collapsed;
        public bool MeasureIdle => !_measurePending;

        public ICommand RequestMeasureCommand => new RelayCommand(() =>
        {
            int seconds = MeasureSeconds[Math.Max(0, Array.IndexOf(MeasureOptions, _selectedMeasure))];
            PerformanceRuns.WriteRequest(new MeasureRequest { Label = "Measured on request", Seconds = seconds });
            PollMeasurement();
        });

        public ICommand CancelMeasureCommand => new RelayCommand(() =>
        {
            PerformanceRuns.ClearRequest();
            PerformanceRuns.WriteStatus(new MeasureStatus { State = "", Message = "" });
            PollMeasurement();
        });

        public ICommand DeleteRunCommand => new RelayCommand<RunRow?>(row =>
        {
            if (row is null)
                return;

            PerformanceRuns.Delete(row.Id);
            RefreshRuns();
            RefreshTuner();
        });

        private void PollMeasurement()
        {
            MeasureRequest? request = PerformanceRuns.ReadRequest();
            MeasureStatus? status = PerformanceRuns.ReadStatus();

            bool measuring = status?.State == "measuring" && (DateTime.UtcNow - status.UpdatedUtc).TotalSeconds < 15;
            bool pending = request is not null || measuring;

            if (pending != _measurePending)
            {
                _measurePending = pending;
                OnPropertyChanged(nameof(MeasurePendingVisibility));
                OnPropertyChanged(nameof(MeasureIdle));
            }

            MeasureProgress = measuring ? status!.Progress : 0;

            if (request is not null && !measuring)
            {
                bool roblox = Process.GetProcessesByName(App.RobloxPlayerAppName).Length > 0;
                MeasureStatusText = roblox
                    ? status?.Message.Length > 0 && status.RequestId == request.Id ? status.Message : "Ordered - it starts once you are in a game."
                    : "Ordered. Start Roblox through PhasmaStrap and join a game - it is measured there, and you get a notification when it is done.";
            }
            else
            {
                MeasureStatusText = status?.Message ?? "";
            }

            // a run came in since the last look
            string stamp = $"{status?.RequestId}|{status?.State}";
            if (stamp != _lastSeenStatus)
            {
                _lastSeenStatus = stamp;
                if (status?.State is "done" or "failed")
                {
                    RefreshRuns();
                    RefreshTuner();
                }
            }
        }

        private void RefreshRuns()
        {
            Runs.Clear();

            foreach (PerformanceReport report in PerformanceRuns.List())
            {
                string game = report.Game.Length > 0 ? report.Game : report.PlaceId > 0 ? $"Place {report.PlaceId}" : "Roblox";

                Runs.Add(new RunRow
                {
                    Id = report.Id,
                    Title = $"{game}  ·  {report.WhenLocal:ddd d MMM, HH:mm}{(report.Label.Length > 0 ? $"  ·  {report.Label}" : "")}",
                    Numbers = $"{report.AverageFps:0} fps average  ·  slowest 1 %: {report.Low1Fps:0} fps  ·  slowest 0.1 %: {report.Low01Fps:0} fps  ·  worst frame {report.WorstFrameMs:0} ms  ·  {report.Stutters.Count} stutter(s) in {report.Seconds:0} s",
                    Verdict = report.Verdict,
                    Findings = report.Findings,
                    Graph = FrameGraph(report, out string scale),
                    GraphScale = scale,
                });
            }

            OnPropertyChanged(nameof(RunsEmptyVisibility));
        }

        // worst frame time per quarter second, 0 at the bottom; clipped so one monster frame cannot flatten the rest
        private static PointCollection FrameGraph(PerformanceReport report, out string scale)
        {
            var points = new PointCollection();
            scale = "";

            List<double> data = report.WorstPerSlice;
            if (data.Count < 2)
                return points;

            const double width = 900, height = 70;
            double top = Math.Clamp(data.OrderBy(d => d).ElementAt((int)(data.Count * 0.98)) * 1.6, report.MedianFrameMs * 3, 250);
            top = Math.Max(top, 20);
            scale = $"frame time, 0 - {top:0} ms";

            for (int i = 0; i < data.Count; i++)
                points.Add(new Point(i * width / (data.Count - 1), height - Math.Min(data[i], top) / top * height));

            points.Freeze();
            return points;
        }

        // ------------------------------------------------------------------ auto-tuner

        public ObservableCollection<TunerCandidate> TunerCandidates { get; } = new();
        public ObservableCollection<TunerStepRow> TunerSteps { get; } = new();
        public ObservableCollection<TunerResultRow> TunerResults { get; } = new();
        public ObservableCollection<string> TunerNotes { get; } = new();
        public ObservableCollection<string> TunerKeepOptions { get; } = new();

        private TunerExperiment? _experiment;

        public Visibility TunerSetupVisibility => _experiment is null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TunerRunningVisibility => _experiment is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility TunerResultsVisibility => TunerResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string[] TunerRoundOptions { get; } = { "1 round (quick, less certain)", "2 rounds (recommended)", "3 rounds" };
        private string _tunerRounds = "2 rounds (recommended)";
        public string SelectedTunerRounds { get => _tunerRounds; set { _tunerRounds = value; OnPropertyChanged(nameof(SelectedTunerRounds)); } }

        public string[] TunerLengthOptions { get; } = { "60 seconds per run", "90 seconds per run", "2 minutes per run", "3 minutes per run" };
        private static readonly int[] TunerLengthSeconds = { 60, 90, 120, 180 };
        private string _tunerLength = "90 seconds per run";
        public string SelectedTunerLength { get => _tunerLength; set { _tunerLength = value; OnPropertyChanged(nameof(SelectedTunerLength)); } }

        private string _tunerMessage = "";
        public string TunerMessage { get => _tunerMessage; private set { _tunerMessage = value; OnPropertyChanged(nameof(TunerMessage)); } }

        private string _tunerNext = "";
        public string TunerNextText { get => _tunerNext; private set { _tunerNext = value; OnPropertyChanged(nameof(TunerNextText)); } }

        private string _tunerArmLabel = "";
        public string TunerArmLabel { get => _tunerArmLabel; private set { _tunerArmLabel = value; OnPropertyChanged(nameof(TunerArmLabel)); OnPropertyChanged(nameof(TunerArmVisibility)); } }
        public Visibility TunerArmVisibility => _tunerArmLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private string _tunerVerdict = "";
        public string TunerVerdict { get => _tunerVerdict; private set { _tunerVerdict = value; OnPropertyChanged(nameof(TunerVerdict)); } }

        private string _tunerKeep = "";
        public string SelectedTunerKeep { get => _tunerKeep; set { _tunerKeep = value ?? ""; OnPropertyChanged(nameof(SelectedTunerKeep)); } }

        private const string KeepOriginal = "The flags I had before the experiment";

        public ICommand StartTunerCommand => new RelayCommand(StartTuner);
        public ICommand ArmTunerCommand => new RelayCommand(ArmTuner);
        public ICommand FinishTunerCommand => new RelayCommand(FinishTuner);

        private void RefreshTuner()
        {
            _experiment = FlagTuner.Load();

            if (_experiment is null)
            {
                // keep ticks the user has set while the list is rebuilt
                HashSet<string> chosen = TunerCandidates.Where(c => c.IsSelected).Select(c => c.Title).ToHashSet();
                bool first = TunerCandidates.Count == 0;

                TunerCandidates.Clear();

                int flags = App.FastFlags.Prop.Count;
                TunerCandidates.Add(new TunerCandidate { Title = "My current flags", Detail = $"{flags} flag(s), as they are right now", Variant = new TunerVariant { Name = "My current flags", Kind = "current" } });
                TunerCandidates.Add(new TunerCandidate { Title = "No FastFlags at all", Detail = "Roblox exactly as it ships - the baseline every flag set has to beat", Variant = new TunerVariant { Name = "No FastFlags", Kind = "none" } });

                foreach (FastFlagSnapshot snapshot in FastFlagSnapshotManager.List().Where(s => !s.Name.StartsWith("Before auto-tuner", StringComparison.Ordinal)))
                    TunerCandidates.Add(new TunerCandidate { Title = snapshot.Name, Detail = $"Saved snapshot, {snapshot.Flags.Count} flag(s), from {snapshot.CreatedUtc.ToLocalTime():d MMM yyyy}", Variant = new TunerVariant { Name = snapshot.Name, Kind = "snapshot", Snapshot = snapshot.Name } });

                foreach (TunerCandidate candidate in TunerCandidates)
                    candidate.IsSelected = first ? candidate.Variant.Kind != "snapshot" : chosen.Contains(candidate.Title);
            }
            else
            {
                List<PerformanceReport> runs = FlagTuner.RunsOf(_experiment);
                Dictionary<string, int> done = runs.GroupBy(r => r.Variant).ToDictionary(g => g.Key, g => g.Count());
                (TunerVariant Variant, int Round)? next = FlagTuner.Next(_experiment);

                TunerSteps.Clear();
                foreach ((TunerVariant variant, int round) in FlagTuner.Plan(_experiment))
                {
                    bool finished = done.GetValueOrDefault(variant.Name) >= round;
                    bool isNext = next is not null && next.Value.Variant.Name == variant.Name && next.Value.Round == round;

                    TunerSteps.Add(new TunerStepRow
                    {
                        Text = $"Round {round}  ·  {variant.Name}" + (isNext && _experiment.AppliedVariant == variant.Name && PerformanceRuns.ReadRequest()?.Experiment == _experiment.Id ? "   (flags in place - waiting for your game)" : ""),
                        Symbol = finished ? Wpf.Ui.Common.SymbolRegular.CheckmarkCircle24 : isNext ? Wpf.Ui.Common.SymbolRegular.ArrowCircleRight24 : Wpf.Ui.Common.SymbolRegular.Circle24,
                        Color = finished ? Green : isNext ? Amber : Grey,
                    });
                }

                bool armed = next is not null && _experiment.AppliedVariant == next.Value.Variant.Name && PerformanceRuns.ReadRequest()?.Experiment == _experiment.Id;

                TunerArmLabel = next is null || armed ? "" : $"Put \"{next.Value.Variant.Name}\" in place for my next game";
                TunerNextText = next is null
                    ? "Every run is in. Pick which flags to keep below."
                    : armed
                        ? $"\"{next.Value.Variant.Name}\" is in place. Close Roblox if it is open (flags are only read when it starts), join the SAME game as in the other runs and play the same way. The measurement starts {FlagTuner.SettleSeconds} seconds after you join and takes {_experiment.SecondsPerRun} seconds - you get a notification when it is done. Then come back here for the next one."
                        : $"Next: round {next.Value.Round} with \"{next.Value.Variant.Name}\".";

                TunerComparison comparison = FlagTunerStats.Compare(runs);

                TunerResults.Clear();
                foreach (TunerVariantResult result in comparison.Results)
                {
                    TunerResults.Add(new TunerResultRow
                    {
                        Variant = result.Variant,
                        Low = $"{result.Low1Fps:0} fps",
                        Average = $"{result.AverageFps:0} fps",
                        Stutters = $"{result.StuttersPerMinute:0.#} / min",
                        Runs = result.SpreadPercent >= 0 ? $"{result.Runs} runs, {result.SpreadPercent:0} % apart" : $"{result.Runs} run",
                        Weight = result.Variant == comparison.Winner ? FontWeights.Bold : FontWeights.Normal,
                    });
                }

                TunerVerdict = runs.Count > 0 ? comparison.Verdict : "";
                TunerNotes.Clear();
                if (runs.Count > 0)
                {
                    foreach (string note in comparison.Notes)
                        TunerNotes.Add(note);
                }

                string previousKeep = _tunerKeep;
                TunerKeepOptions.Clear();
                TunerKeepOptions.Add(KeepOriginal);
                foreach (TunerVariant variant in _experiment.Variants.Where(v => v.Kind != "current"))
                    TunerKeepOptions.Add(variant.Name);

                SelectedTunerKeep = TunerKeepOptions.Contains(previousKeep) ? previousKeep
                    : comparison.Winner.Length > 0 && TunerKeepOptions.Contains(comparison.Winner) ? comparison.Winner
                    : KeepOriginal;
            }

            OnPropertyChanged(nameof(TunerSetupVisibility));
            OnPropertyChanged(nameof(TunerRunningVisibility));
            OnPropertyChanged(nameof(TunerResultsVisibility));
        }

        private void StartTuner()
        {
            List<TunerVariant> chosen = TunerCandidates.Where(c => c.IsSelected).Select(c => c.Variant).ToList();

            if (chosen.Count < 2)
            {
                TunerMessage = "Tick at least two flag sets to compare.";
                return;
            }

            if (chosen.Count > 4)
            {
                TunerMessage = "Four sets at most - every set is one game launch per round.";
                return;
            }

            try
            {
                int rounds = Math.Max(0, Array.IndexOf(TunerRoundOptions, _tunerRounds)) + 1;
                int seconds = TunerLengthSeconds[Math.Max(0, Array.IndexOf(TunerLengthOptions, _tunerLength))];

                TunerExperiment experiment = FlagTuner.Start(chosen, rounds, seconds);
                TunerMessage = $"Your current flags are saved as the snapshot \"{experiment.BackupSnapshot}\".";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DiagnosticsViewModel::StartTuner", ex);
                TunerMessage = $"Could not start: {ex.Message}";
            }

            RefreshTuner();
        }

        private void ArmTuner()
        {
            if (_experiment is null || FlagTuner.Next(_experiment) is not { } next)
                return;

            try
            {
                FlagTuner.Arm(_experiment, next.Variant, next.Round);
                TunerMessage = "";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DiagnosticsViewModel::ArmTuner", ex);
                TunerMessage = ex.Message;
            }

            RefreshTuner();
            PollMeasurement();
        }

        private void FinishTuner()
        {
            if (_experiment is null)
                return;

            TunerVariant? keep = _tunerKeep == KeepOriginal ? null : _experiment.Variants.FirstOrDefault(v => v.Name == _tunerKeep);

            try
            {
                FlagTuner.Finish(_experiment, keep);
                TunerMessage = keep is null
                    ? "Experiment closed. Your original flags are back in place."
                    : $"Experiment closed. \"{keep.Name}\" is now your flag set (your old one is still there as the snapshot \"{_experiment.BackupSnapshot}\").";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DiagnosticsViewModel::FinishTuner", ex);
                TunerMessage = ex.Message;
            }

            TunerResults.Clear();
            RefreshTuner();
            PollMeasurement();
        }
    }
}
