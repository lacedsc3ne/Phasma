using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class CrashRow
    {
        public string When { get; init; } = "";
        public string Cause { get; init; } = "";
        public string Confidence { get; init; } = "";
        public Brush ConfidenceColor { get; init; } = Brushes.Gray;
        public Visibility ConfidenceVisibility => Confidence.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public List<string> Suggestions { get; init; } = new();
        public List<string> Evidence { get; init; } = new();
        public Visibility SuggestionsVisibility => Suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EvidenceVisibility => Evidence.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed partial class DiagnosticsViewModel
    {
        public ObservableCollection<CrashRow> Crashes { get; } = new();

        public ICommand AnalyzeLastSessionCommand => new AsyncRelayCommand(AnalyzeLastSessionAsync);
        public ICommand RefreshCrashesCommand => new RelayCommand(RefreshCrashes);

        public Visibility CrashesEmptyVisibility => Crashes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private string _crashStatus = "";
        public string CrashStatus { get => _crashStatus; private set { _crashStatus = value; OnPropertyChanged(nameof(CrashStatus)); } }

        public bool CrashAnalyzerEnabled
        {
            get => App.Settings.Prop.CrashAnalyzerEnabled;
            set { App.Settings.Prop.CrashAnalyzerEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(CrashAnalyzerEnabled)); }
        }

        private static CrashRow ToRow(CrashReport report) => new()
        {
            When = $"{report.WhenLocal:dddd d MMMM, HH:mm}" + (report.SessionMinutes >= 1 ? $"  ·  after {report.SessionMinutes:0} min" : ""),
            Cause = report.Cause,
            Confidence = report.Confidence switch { "Strong" => "Clear evidence", "Likely" => "Likely", "Unclear" => "Unclear", _ => "" },
            ConfidenceColor = report.Confidence switch { "Strong" => Green, "Likely" => Amber, _ => Grey },
            Suggestions = report.Suggestions,
            Evidence = report.Evidence,
        };

        private void RefreshCrashes()
        {
            Crashes.Clear();
            foreach (CrashReport report in CrashReports.List())
                Crashes.Add(ToRow(report));

            OnPropertyChanged(nameof(CrashesEmptyVisibility));
        }

        private async Task AnalyzeLastSessionAsync()
        {
            string? log = CrashReports.NewestLog();
            if (log is null)
            {
                CrashStatus = "There is no Roblox log yet.";
                return;
            }

            CrashStatus = "Reading the log and Windows' event logs...";

            try
            {
                CrashReport report = await Task.Run(() => CrashReports.Analyze(log));

                if (!(report.CleanExit && report.Confidence.Length == 0))
                    CrashReports.Save(report);

                RefreshCrashes();

                if (report.CleanExit && report.Confidence.Length == 0)
                    Crashes.Insert(0, ToRow(report));

                OnPropertyChanged(nameof(CrashesEmptyVisibility));
                CrashStatus = $"Looked at {System.IO.Path.GetFileName(log)}.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DiagnosticsViewModel::AnalyzeLastSession", ex);
                CrashStatus = $"Could not analyse it: {ex.Message}";
            }
        }

        partial void InitialiseMore()
        {
            RefreshCrashes();
            InitialisePerformance();
        }

        partial void InitialisePerformance();
    }
}
