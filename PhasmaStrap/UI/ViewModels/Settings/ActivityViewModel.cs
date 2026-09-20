using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class TimelineCapture
    {
        public string Path { get; init; } = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public bool IsImage { get; init; }
        public Wpf.Ui.Common.SymbolRegular Symbol => IsImage ? Wpf.Ui.Common.SymbolRegular.Image24 : Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? Wpf.Ui.Common.SymbolRegular.Gif24 : Wpf.Ui.Common.SymbolRegular.VideoClip24;

        private System.Windows.Media.Imaging.BitmapImage? _thumbnail;

        public System.Windows.Media.Imaging.BitmapImage? Thumbnail
        {
            get
            {
                if (!IsImage || _thumbnail is not null)
                    return _thumbnail;

                try
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.DecodePixelHeight = 96;
                    image.UriSource = new Uri(Path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    _thumbnail = image;
                }
                catch
                {
                    _thumbnail = null;
                }

                return _thumbnail;
            }
        }

        public Visibility ThumbnailVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IconVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed class TimelineVisit
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string? IconUrl { get; init; }
        public string FpsText { get; init; } = "";
        public PointCollection Spark { get; init; } = new();
        public Visibility SparkVisibility => Spark.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        public string FriendsText { get; init; } = "";
        public Visibility FriendsVisibility => FriendsText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public List<TimelineCapture> Captures { get; init; } = new();
        public Visibility CapturesVisibility => Captures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class TimelineSession
    {
        public string Header { get; init; } = "";
        public string Summary { get; init; } = "";
        public List<TimelineVisit> Visits { get; init; } = new();
    }

    public sealed class ChartBar
    {
        public string Label { get; init; } = "";
        public string ValueText { get; init; } = "";
        public double Height { get; init; }
        public string ToolTip { get; init; } = "";
        public double Opacity { get; init; } = 1;
    }

    public sealed class GameBar
    {
        public string Name { get; init; } = "";
        public string? IconUrl { get; init; }
        public string TimeText { get; init; } = "";
        public string Detail { get; init; } = "";
        public double Width { get; init; }
    }

    public sealed class CompanionRow
    {
        public string Name { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public string Detail { get; init; } = "";
        public string Together { get; init; } = "";
    }

    public sealed class ActivityViewModel : NotifyPropertyChangedViewModel
    {
        private const int MaxSessionsShown = 60;
        private const double ChartHeight = 120;
        private const double GameBarWidth = 420;

        public ObservableCollection<TimelineSession> Sessions { get; } = new();
        public ObservableCollection<ChartBar> Days { get; } = new();
        public ObservableCollection<ChartBar> Weeks { get; } = new();
        public ObservableCollection<GameBar> Games { get; } = new();
        public ObservableCollection<CompanionRow> Companions { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand OpenCaptureCommand { get; }
        public ICommand ClearHistoryCommand { get; }

        public ActivityViewModel()
        {
            RefreshCommand = new RelayCommand(Refresh);
            OpenCaptureCommand = new RelayCommand<TimelineCapture?>(OpenCapture);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            Refresh();
        }

        public bool HistoryEnabled
        {
            get => App.Settings.Prop.SessionHistoryEnabled;
            set { App.Settings.Prop.SessionHistoryEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(HistoryEnabled)); }
        }

        public bool TrackFriends
        {
            get => App.Settings.Prop.SessionTrackFriends;
            set { App.Settings.Prop.SessionTrackFriends = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(TrackFriends)); OnPropertyChanged(nameof(CompanionsEmptyText)); }
        }

        private string _totalsText = "";
        public string TotalsText { get => _totalsText; private set { _totalsText = value; OnPropertyChanged(nameof(TotalsText)); } }

        private string _trackedSinceText = "";
        public string TrackedSinceText { get => _trackedSinceText; private set { _trackedSinceText = value; OnPropertyChanged(nameof(TrackedSinceText)); } }

        public Visibility TimelineEmptyVisibility => Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GamesEmptyVisibility => Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CompanionsEmptyVisibility => Companions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string CompanionsEmptyText => App.Settings.Prop.SessionTrackFriends
            ? "No friend has been seen on one of your servers yet. Only friends whose privacy settings let you join them can be recognised."
            : "Switch on \"Note which friends share my server\" above, then play - friends on the same server show up here.";

        public string[] RangeOptions { get; } = { "This week", "Last 30 days", "All time" };

        private string _selectedRange = "Last 30 days";
        public string SelectedRange
        {
            get => _selectedRange;
            set { _selectedRange = value; OnPropertyChanged(nameof(SelectedRange)); BuildGames(); }
        }

        private SessionData _data = new();

        private void Refresh()
        {
            _data = SessionStore.Shared.Load();

            BuildTimeline();
            BuildCharts();
            BuildGames();
            BuildCompanions();
        }

        private static string DayName(DateTime local)
        {
            DateTime today = DateTime.Now.Date;
            if (local.Date == today) return "Today";
            if (local.Date == today.AddDays(-1)) return "Yesterday";
            return local.Year == today.Year ? local.ToString("dddd d MMMM") : local.ToString("d MMMM yyyy");
        }

        private static string DayInline(DateTime local)
        {
            string name = DayName(local);
            return name is "Today" or "Yesterday" ? name.ToLowerInvariant() : name;
        }

        private void BuildTimeline()
        {
            Sessions.Clear();

            var captures = new List<(DateTime Time, string Path, bool Image)>();
            foreach ((string directory, bool image) in new[] { (ScreenshotCapture.ScreenshotsDir, true), (InstantReplayRecorder.ClipsDir, false) })
            {
                try
                {
                    if (!Directory.Exists(directory))
                        continue;

                    foreach (FileInfo file in new DirectoryInfo(directory).GetFiles())
                    {
                        string extension = file.Extension.ToLowerInvariant();
                        if (extension is ".png" or ".jpg" or ".mp4" or ".gif" && !file.Name.EndsWith(".editing.mp4", StringComparison.OrdinalIgnoreCase))
                            captures.Add((file.LastWriteTimeUtc, file.FullName, image));
                    }
                }
                catch
                {
                }
            }

            foreach (SessionRecord session in _data.Sessions.OrderByDescending(s => s.StartedUtc).Take(MaxSessionsShown))
            {
                if (session.Visits.Count == 0)
                    continue;

                DateTime start = session.Visits.Min(v => v.JoinedUtc).ToLocalTime();
                DateTime end = session.Visits.Max(v => v.LeftUtc).ToLocalTime();
                double minutes = session.Visits.Sum(v => v.Length.TotalMinutes);

                var item = new TimelineSession
                {
                    Header = $"{DayName(start)}  ·  {start:t} – {end:t}",
                    Summary = $"{SessionStats.Duration(minutes)} in {session.Visits.Count} server{(session.Visits.Count == 1 ? "" : "s")}",
                };

                foreach (ServerVisit visit in session.Visits.OrderBy(v => v.JoinedUtc))
                {
                    var parts = new List<string> { $"{visit.JoinedUtc.ToLocalTime():t} – {visit.LeftUtc.ToLocalTime():t}", SessionStats.Duration(visit.Length.TotalMinutes) };
                    if (visit.Region.Length > 0) parts.Add(visit.Region);
                    if (visit.ServerType.Length > 0 && visit.ServerType != "Public") parts.Add($"{visit.ServerType} server");

                    (int Average, int Low)? fps = SessionStats.FpsSummary(visit);

                    List<TimelineCapture> inside = captures
                        .Where(c => c.Time >= visit.JoinedUtc && c.Time <= visit.LeftUtc.AddSeconds(90))
                        .OrderBy(c => c.Time)
                        .Take(12)
                        .Select(c => new TimelineCapture { Path = c.Path, IsImage = c.Image })
                        .ToList();

                    item.Visits.Add(new TimelineVisit
                    {
                        Title = visit.GameName.Length > 0 ? visit.GameName : $"Place {visit.PlaceId}",
                        Subtitle = string.Join("  ·  ", parts),
                        IconUrl = visit.IconUrl.Length > 0 ? visit.IconUrl : null,
                        FpsText = fps is null ? "" : $"avg {fps.Value.Average} fps  ·  low {fps.Value.Low}",
                        Spark = BuildSpark(visit.Fps),
                        FriendsText = visit.Friends.Count == 0 ? "" : "With " + string.Join(", ", visit.Friends.Select(f => f.Name.Length > 0 ? f.Name : f.UserId.ToString())),
                        Captures = inside,
                    });
                }

                Sessions.Add(item);
            }

            OnPropertyChanged(nameof(TimelineEmptyVisibility));
        }

        private static PointCollection BuildSpark(List<int> readings)
        {
            var points = new PointCollection();
            if (readings.Count(r => r > 0) < 2)
                return points;

            const double width = 260, height = 36;
            double max = Math.Max(30, readings.Max()) * 1.05;

            for (int i = 0; i < readings.Count; i++)
            {
                if (readings[i] <= 0)
                    continue;

                double x = readings.Count == 1 ? 0 : i * width / (readings.Count - 1);
                points.Add(new Point(x, height - readings[i] / max * height));
            }

            points.Freeze();
            return points;
        }

        private void BuildCharts()
        {
            DateTime now = DateTime.Now;

            Fill(Days, SessionStats.PerDay(_data, 14, now), b => b.Start.ToString("ddd"), b => b.Start.ToString("dddd d MMMM"));
            Fill(Weeks, SessionStats.PerWeek(_data, 12, now), b => b.Start.ToString("d MMM"), b => $"Week of {b.Start:d MMMM}");

            double week = SessionStats.PerWeek(_data, 1, now)[0].Minutes;
            double month = SessionStats.PerDay(_data, 30, now).Sum(d => d.Minutes);
            double all = SessionStats.Visits(_data).Sum(v => v.Length.TotalMinutes);
            int streak = SessionStats.Streak(_data, now);

            TotalsText = $"This week {SessionStats.Duration(week)}   ·   Last 30 days {SessionStats.Duration(month)}   ·   Tracked in total {SessionStats.Duration(all)}" + (streak > 1 ? $"   ·   {streak} days in a row" : "");

            DateTime? first = _data.Sessions.Count > 0 ? _data.Sessions.Min(s => s.StartedUtc).ToLocalTime() : null;
            TrackedSinceText = first is null
                ? "Nothing recorded yet - sessions are written down from the next time you play."
                : $"Sessions have been written down since {first:d MMMM yyyy}. Play before that only exists as a running total per game (Home page), which is why it is not in these charts.";
        }

        private static void Fill(ObservableCollection<ChartBar> target, List<SessionStats.Bucket> buckets, Func<SessionStats.Bucket, string> label, Func<SessionStats.Bucket, string> tip)
        {
            target.Clear();
            double max = Math.Max(1, buckets.Max(b => b.Minutes));

            for (int i = 0; i < buckets.Count; i++)
            {
                SessionStats.Bucket bucket = buckets[i];
                target.Add(new ChartBar
                {
                    Label = label(bucket),
                    ValueText = bucket.Minutes >= 1 ? SessionStats.Duration(bucket.Minutes) : "",
                    Height = bucket.Minutes >= 1 ? Math.Max(3, bucket.Minutes / max * ChartHeight) : 0,
                    ToolTip = $"{tip(bucket)}: {SessionStats.Duration(bucket.Minutes)}",
                    Opacity = i == buckets.Count - 1 ? 1 : 0.6,
                });
            }
        }

        private void BuildGames()
        {
            Games.Clear();

            DateTime now = DateTime.Now;
            int sinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime since = _selectedRange switch
            {
                "This week" => now.Date.AddDays(-sinceMonday).ToUniversalTime(),
                "Last 30 days" => now.Date.AddDays(-29).ToUniversalTime(),
                _ => DateTime.MinValue,
            };

            List<SessionStats.GameTotal> totals = SessionStats.PerGame(_data, since).Where(g => g.Minutes >= 1).Take(15).ToList();
            double max = totals.Count > 0 ? totals.Max(g => g.Minutes) : 1;

            foreach (SessionStats.GameTotal game in totals)
            {
                Games.Add(new GameBar
                {
                    Name = game.Name,
                    IconUrl = game.IconUrl.Length > 0 ? game.IconUrl : null,
                    TimeText = SessionStats.Duration(game.Minutes),
                    Detail = $"{game.Visits} server{(game.Visits == 1 ? "" : "s")}  ·  last {DayInline(game.LastPlayedUtc.ToLocalTime())}",
                    Width = Math.Max(4, game.Minutes / max * GameBarWidth),
                });
            }

            OnPropertyChanged(nameof(GamesEmptyVisibility));
        }

        private void BuildCompanions()
        {
            Companions.Clear();

            foreach (SessionStats.Companion companion in SessionStats.PlayedWith(_data).Take(100))
            {
                Companions.Add(new CompanionRow
                {
                    Name = companion.Name.Length > 0 ? companion.Name : companion.UserId.ToString(),
                    AvatarUrl = AvatarCache.TryGetFresh(companion.UserId),
                    Detail = $"{companion.LastGame}  ·  {DayInline(companion.LastSeenUtc.ToLocalTime())}",
                    Together = $"{companion.Visits} server{(companion.Visits == 1 ? "" : "s")}  ·  {SessionStats.Duration(companion.Minutes)} together",
                });
            }

            OnPropertyChanged(nameof(CompanionsEmptyVisibility));
        }

        private static void OpenCapture(TimelineCapture? capture)
        {
            if (capture is null || !File.Exists(capture.Path))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(capture.Path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ActivityViewModel", $"Could not open '{capture.Path}': {ex.Message}");
            }
        }

        private void ClearHistory()
        {
            var answer = Frontend.ShowMessageBox(
                "Delete the whole session history?\n\nThe timeline, the playtime charts and \"played with\" start again from nothing. Your screenshots and clips are not touched, and neither are the per-game totals on the Home page.",
                MessageBoxImage.Warning, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            SessionStore.Shared.Clear();
            Refresh();
        }
    }
}
