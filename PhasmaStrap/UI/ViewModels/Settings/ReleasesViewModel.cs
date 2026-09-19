using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>
    /// Backs the combined Releases / What's New page. Pulls straight from PhasmaStrap's own GitHub
    /// releases (<see cref="App.ProjectRepository"/>) rather than a hosted news feed/CMS, since
    /// PhasmaStrap has no such feed of its own to point a separate News page at - each release's
    /// notes (usually short markdown) double as the "what's new" entry, rendered with the existing
    /// <see cref="PhasmaStrap.UI.Elements.Controls.MarkdownTextBlock"/> control rather than a new
    /// full rich-content renderer.
    /// </summary>
    public class ReleasesViewModel : NotifyPropertyChangedViewModel
    {
        private const int MaxReleasesToShow = 30;
        private const string LOG_IDENT = "ReleasesViewModel";

        public class ReleaseItem
        {
            public string Name { get; }
            public string TagName { get; }
            public string Body { get; }
            public string PublishedText { get; }
            public bool IsInstalled { get; }
            public string HtmlUrl { get; }
            public int AssetCount { get; }

            public ReleaseItem(GithubRelease release, bool isInstalled)
            {
                TagName = release.TagName ?? "";
                Name = string.IsNullOrWhiteSpace(release.Name) ? TagName : release.Name;
                string body = StripBoilerplate(release.Body);
                Body = string.IsNullOrWhiteSpace(body) ? Strings.Menu_Releases_NoNotes : body;
                IsInstalled = isInstalled;
                HtmlUrl = $"https://github.com/{App.ProjectRepository}/releases/tag/{TagName}";
                AssetCount = release.Assets?.Count ?? 0;

                if (DateTime.TryParse(release.CreatedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime published))
                    PublishedText = published.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
                else
                    PublishedText = release.CreatedAt ?? "";
            }
        }

        // every release body ends with the same two download notes (unsigned build, .NET 6) -
        // they're not news
        private static string StripBoilerplate(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";

            var kept = body.Replace("\r\n", "\n").Split("\n\n")
                .Where(p => !p.TrimStart().StartsWith("**Note:** this build is unsigned", StringComparison.OrdinalIgnoreCase)
                         && !p.TrimStart().StartsWith("**Requires the [.NET", StringComparison.OrdinalIgnoreCase));
            return string.Join("\n\n", kept).Trim();
        }

        // ---- "What's new": this version's section of the changelog built into the app

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

        // the "## <version>" section's text (without its heading); the newest section when the
        // version has none
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
                var releases = await Http.GetJson<GithubRelease[]>($"https://api.github.com/repos/{App.ProjectRepository}/releases?per_page={MaxReleasesToShow}");

                _allReleases = (releases ?? Array.Empty<GithubRelease>())
                    .Take(MaxReleasesToShow)
                    .Select(r => new ReleaseItem(r, IsInstalledRelease(r)))
                    .ToList();

                ApplyFilter();

                StatusText = _allReleases.Count == 0
                    ? Strings.Menu_Releases_Empty
                    : string.Format(Strings.Menu_Releases_Count, _allReleases.Count);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to load releases: {ex.Message}");
                StatusText = Strings.Menu_Releases_Error;
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
                // some tags (e.g. hand-written non-numeric tags) don't parse as a System.Version -
                // just treat those as "not the installed release" rather than failing the whole load
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
        }
    }
}
