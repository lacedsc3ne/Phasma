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
                Body = string.IsNullOrWhiteSpace(release.Body) ? Strings.Menu_Releases_NoNotes : release.Body;
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
                ApplyFilter();
            }
        }

        public ICommand RefreshCommand => new AsyncRelayCommand(LoadAsync);

        public ReleasesViewModel()
        {
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            IsLoading = true;
            StatusText = Strings.Menu_Releases_Loading;

            try
            {
                var releases = await Http.GetJson<GithubRelease[]>($"https://api.github.com/repos/{App.ProjectRepository}/releases");

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
