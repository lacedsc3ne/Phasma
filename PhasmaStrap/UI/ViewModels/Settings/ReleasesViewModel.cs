using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ReleasesViewModel : NotifyPropertyChangedViewModel
    {
        private const int MaxReleasesToShow = 30;
        private const string LOG_IDENT = "ReleasesViewModel";

        public class ReleaseAsset
        {
            public string Name { get; }
            public string SizeText { get; }
            public string DownloadUrl { get; }

            public ReleaseAsset(GithubReleaseAsset asset)
            {
                Name = asset.Name ?? "";
                SizeText = FormatSize(asset.Size);
                DownloadUrl = asset.BrowserDownloadUrl ?? "";
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", bytes / 1024.0 / 1024.0);

            if (bytes >= 1024)
                return string.Format(CultureInfo.CurrentCulture, "{0:0.0} KB", bytes / 1024.0);

            return string.Format(CultureInfo.CurrentCulture, "{0} bytes", bytes);
        }

        public class ReleaseItem
        {
            public string Name { get; }
            public string TagName { get; }
            public string Body { get; }
            public string PublishedText { get; }
            public bool IsInstalled { get; }
            public string HtmlUrl { get; }
            public int AssetCount { get; }
            public List<ReleaseAsset> Assets { get; }

            public System.Windows.Visibility AssetsVisibility => Assets.Count > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            public string? InstallerUrl => Assets
                .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.DownloadUrl;

            public ReleaseItem(GithubRelease release, bool isInstalled)
            {
                TagName = release.TagName ?? "";
                Name = string.IsNullOrWhiteSpace(release.Name) ? TagName : release.Name;
                string body = StripBoilerplate(release.Body);
                Body = string.IsNullOrWhiteSpace(body) ? Strings.Menu_Releases_NoNotes : body;
                IsInstalled = isInstalled;
                HtmlUrl = $"https://github.com/{App.ProjectRepository}/releases/tag/{TagName}";
                AssetCount = release.Assets?.Count ?? 0;
                Assets = (release.Assets ?? new List<GithubReleaseAsset>())
                    .Select(a => new ReleaseAsset(a))
                    .ToList();

                if (DateTime.TryParse(release.CreatedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime published))
                    PublishedText = published.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
                else
                    PublishedText = release.CreatedAt ?? "";
            }
        }

        private static string StripBoilerplate(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";

            var kept = body.Replace("\r\n", "\n").Split("\n\n")
                .Where(p => !p.TrimStart().StartsWith("**Note:** this build is unsigned", StringComparison.OrdinalIgnoreCase)
                         && !p.TrimStart().StartsWith("**Requires the [.NET", StringComparison.OrdinalIgnoreCase));
            return string.Join("\n\n", kept).Trim();
        }

        public string WhatsNewTitle { get; } = $"What's new in {App.Version}";

        private string _whatsNew = "";
        public string WhatsNew
        {
            get => _whatsNew;
            private set { _whatsNew = value; OnPropertyChanged(nameof(WhatsNew)); OnPropertyChanged(nameof(WhatsNewVisibility)); }
        }

        public System.Windows.Visibility WhatsNewVisibility => WhatsNew.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        private async Task LoadWhatsNewAsync()
        {
            try
            {
                string changelog = await Resource.GetString("Changelog.md");
                WhatsNew = ChangelogSection(changelog, App.Version);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Changelog unreadable: {ex.Message}");
            }
        }

        public static string ChangelogSection(string changelog, string version)
        {
            var sections = new List<(string Version, string Text)>();
            string? current = null;
            var text = new StringBuilder();

            foreach (string raw in changelog.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("## "))
                {
                    if (current is not null)
                        sections.Add((current, text.ToString().Trim()));
                    current = raw[3..].Trim();
                    text.Clear();
                }
                else if (current is not null)
                {
                    text.AppendLine(raw);
                }
            }
            if (current is not null)
                sections.Add((current, text.ToString().Trim()));

            string wanted = version.TrimStart('v');
            return sections.FirstOrDefault(s => s.Version.TrimStart('v') == wanted).Text
                ?? sections.FirstOrDefault().Text
                ?? "";
        }

        private List<ReleaseItem> _allReleases = new();

        public ObservableCollection<ReleaseItem> Releases { get; } = new();

        private ReleaseItem? _selectedRelease;
        public ReleaseItem? SelectedRelease
        {
            get => _selectedRelease;
            set
            {
                _selectedRelease = value;
                OnPropertyChanged(nameof(SelectedRelease));
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(NoSelectionVisibility));
            }
        }

        public System.Windows.Visibility DetailVisibility => SelectedRelease is null
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

        public System.Windows.Visibility NoSelectionVisibility => SelectedRelease is null
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

        public string InstalledVersionText => string.Format(CultureInfo.CurrentCulture, "You are running {0}.", App.Version);

        private ReleaseItem? _updateRelease;

        private string _updateHeadline = "Checking for a newer version";
        public string UpdateHeadline
        {
            get => _updateHeadline;
            private set { _updateHeadline = value; OnPropertyChanged(nameof(UpdateHeadline)); }
        }

        private string _updateDetail = "";
        public string UpdateDetail
        {
            get => _updateDetail;
            private set { _updateDetail = value; OnPropertyChanged(nameof(UpdateDetail)); }
        }

        public System.Windows.Visibility UpdateActionVisibility => _updateRelease is null
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

        public ICommand UpdateCommand => new RelayCommand(RunUpdate);

        private void RunUpdate()
        {
            if (_updateRelease is null)
                return;

            Utilities.ShellExecute(_updateRelease.InstallerUrl ?? _updateRelease.HtmlUrl);
        }

        private void RefreshUpdateState()
        {
            ReleaseItem? newest = _allReleases.FirstOrDefault();
            ReleaseItem? available = null;

            if (newest is not null && !string.IsNullOrWhiteSpace(newest.TagName))
            {
                try
                {
                    if (Utilities.CompareVersions(App.Version, newest.TagName) == VersionComparison.LessThan)
                        available = newest;
                }
                catch
                {
                    available = null;
                }
            }

            _updateRelease = available;

            if (available is not null)
            {
                UpdateHeadline = string.Format(CultureInfo.CurrentCulture, "{0} is ready to install", available.Name);
                UpdateDetail = string.Format(CultureInfo.CurrentCulture, "{0} It was published on {1}.", InstalledVersionText, available.PublishedText);
            }
            else if (newest is null)
            {
                UpdateHeadline = "Could not check for a newer version";
                UpdateDetail = InstalledVersionText;
            }
            else
            {
                UpdateHeadline = "PhasmaStrap is up to date";
                UpdateDetail = InstalledVersionText;
            }

            OnPropertyChanged(nameof(UpdateActionVisibility));
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        private string _statusText = Strings.Menu_Releases_Loading;
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? "";
                OnPropertyChanged(nameof(SearchText));
                ScheduleFilter();
            }
        }

        private readonly System.Windows.Threading.DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private bool _filterDebounceHooked;

        private void ScheduleFilter()
        {
            if (!_filterDebounceHooked)
            {
                _filterDebounce.Tick += (_, _) =>
                {
                    _filterDebounce.Stop();
                    ApplyFilter();
                };
                _filterDebounceHooked = true;
            }

            _filterDebounce.Stop();
            _filterDebounce.Start();
        }

        public ICommand RefreshCommand => new AsyncRelayCommand(LoadAsync);

        public ReleasesViewModel()
        {
            _ = LoadWhatsNewAsync();
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            IsLoading = true;
            StatusText = Strings.Menu_Releases_Loading;

            try
            {
                var releases = await App.GetReleaseJson<GithubRelease[]>("/v1/releases", $"/releases?per_page={MaxReleasesToShow}");

                _allReleases = (releases ?? Array.Empty<GithubRelease>())
                    .Take(MaxReleasesToShow)
                    .Select(r => new ReleaseItem(r, IsInstalledRelease(r)))
                    .ToList();

                ApplyFilter();
                RefreshUpdateState();

                StatusText = _allReleases.Count == 0
                    ? Strings.Menu_Releases_Empty
                    : string.Format(Strings.Menu_Releases_Count, _allReleases.Count);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to load releases: {ex.Message}");
                StatusText = Strings.Menu_Releases_Error;
                RefreshUpdateState();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static bool IsInstalledRelease(GithubRelease release)
        {
            if (string.IsNullOrWhiteSpace(release.TagName))
                return false;

            try
            {
                return Utilities.CompareVersions(App.Version, release.TagName) == VersionComparison.Equal;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyFilter()
        {
            Releases.Clear();

            IEnumerable<ReleaseItem> filtered = _allReleases;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string query = SearchText.Trim();
                filtered = filtered.Where(r =>
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.TagName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Body.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            foreach (ReleaseItem item in filtered)
                Releases.Add(item);

            if (SelectedRelease is null || !Releases.Contains(SelectedRelease))
                SelectedRelease = Releases.FirstOrDefault(r => r.IsInstalled) ?? Releases.FirstOrDefault();
        }
    }
}
