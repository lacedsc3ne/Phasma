using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class PlaceRow
    {
        public long PlaceId { get; init; }
        public string Name { get; init; } = "";
        public bool IsRootPlace { get; init; }
        public string Subtitle => IsRootPlace ? $"Start place  ·  {PlaceId}" : PlaceId.ToString();
    }

    public class HomeViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "HomeViewModel";

        private const int MaxContinuePlaying = 6;

        public const string FilterRecent = "recent";
        public const string FilterAll = "all";
        public const string FilterPrivate = "private";

        public const string SectionCatalog = "catalog";
        public const string SectionServers = "servers";
        public const string SectionPrivateServers = "privateservers";
        public const string SectionHistory = "history";

        private string _statusText = "";

        public ObservableCollection<PlayTimeEntry> ContinuePlaying { get; } = new();

        public string WelcomeText => $"Welcome back, {Environment.UserName}.";

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsEmpty => ContinuePlaying.Count == 0;

        public ICommand RefreshCommand => new RelayCommand(LoadEntries);

        public ICommand LaunchCommand => new RelayCommand<PlayTimeEntry>(Launch);

        public ICommand CopyLinkCommand => new RelayCommand<PlayTimeEntry>(CopyLink);

        public ICommand LaunchRobloxCommand => new RelayCommand(LaunchRoblox);

        private string _linkInput = "";
        public string LinkInput
        {
            get => _linkInput;
            set { _linkInput = value ?? ""; OnPropertyChanged(nameof(LinkInput)); }
        }

        private string _linkStatus = "Paste a game link, a private server link, a share link, or just a place ID.";
        public string LinkStatus
        {
            get => _linkStatus;
            private set { _linkStatus = value; OnPropertyChanged(nameof(LinkStatus)); }
        }

        private string _placesTitle = "";
        public string PlacesTitle
        {
            get => _placesTitle;
            private set { _placesTitle = value; OnPropertyChanged(nameof(PlacesTitle)); OnPropertyChanged(nameof(PlacesVisibility)); }
        }

        public ObservableCollection<PlaceRow> Places { get; } = new();

        public Visibility PlacesVisibility => Places.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private bool _linkBusy;
        public bool LinkBusy
        {
            get => _linkBusy;
            private set { _linkBusy = value; OnPropertyChanged(nameof(LinkBusy)); OnPropertyChanged(nameof(LinkNotBusy)); }
        }

        public bool LinkNotBusy => !LinkBusy;

        public ICommand LaunchLinkCommand => new AsyncRelayCommand(LaunchLinkAsync);

        public ICommand ShowPlacesCommand => new AsyncRelayCommand(ShowPlacesAsync);

        public ICommand LaunchPlaceCommand => new RelayCommand<PlaceRow>(LaunchPlace);

        private PlayTimeEntry? _selectedGame;
        public PlayTimeEntry? SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (ReferenceEquals(_selectedGame, value))
                    return;

                _selectedGame = value;

                if (value is not null)
                    Section = "";

                Servers.Clear();
                ServerStatus = value is null
                    ? "Pick a game on the left to see its servers."
                    : "Nothing looked up yet. Find servers asks Roblox which public servers are running right now.";

                OnPropertyChanged(nameof(SelectedGame));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectionVisibility));
                OnPropertyChanged(nameof(NoSelectionVisibility));
                OnPropertyChanged(nameof(PlaceIdText));
                OnPropertyChanged(nameof(ProfileSummary));
                OnPropertyChanged(nameof(OverlaySummary));
                OnPropertyChanged(nameof(ResolutionSummary));
            }
        }

        public bool HasSelection => _selectedGame is not null;

        public Visibility SelectionVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoSelectionVisibility => HasSelection ? Visibility.Collapsed : Visibility.Visible;

        public string PlaceIdText => _selectedGame is null ? "" : $"Place ID {_selectedGame.PlaceId}";

        public string ProfileSummary
        {
            get
            {
                if (_selectedGame is null)
                    return "";

                FlagProfile? profile = FlagLayers.ProfileFor(App.FlagProfiles.Prop, _selectedGame.PlaceId, _selectedGame.UniverseId);

                if (profile is null)
                    return "None, this place starts with your usual flags.";

                return profile.ChangeCount == 0
                    ? $"{profile.Name}, empty so far."
                    : $"{profile.Name}  ·  {profile.ChangeCount} flag(s)";
            }
        }

        public string OverlaySummary
        {
            get
            {
                if (_selectedGame is null)
                    return "";

                if (!App.Settings.Prop.OverlayPlaceProfiles.TryGetValue(_selectedGame.PlaceId.ToString(), out var profile))
                    return "None, this place uses your usual overlay settings.";

                return $"HUD {(profile.HudEnabled ? "on" : "off")}  ·  crosshair {(profile.CrosshairEnabled ? "on" : "off")}";
            }
        }

        public string ResolutionSummary
        {
            get
            {
                if (_selectedGame is null)
                    return "";

                if (!App.Settings.Prop.InGameResolutionPlaceProfiles.TryGetValue(_selectedGame.PlaceId.ToString(), out var profile))
                    return "None, this place uses your usual resolution.";

                string text = $"{profile.Width} × {profile.Height}";

                if (profile.RefreshRate > 0)
                    text += $" at {profile.RefreshRate} Hz";

                if (!string.IsNullOrEmpty(profile.Monitor))
                    text += $" on {profile.Monitor}";

                return text;
            }
        }

        private string _gamesFilter = FilterRecent;
        public string GamesFilter
        {
            get => _gamesFilter;
            private set { _gamesFilter = value; OnPropertyChanged(nameof(GamesFilter)); }
        }

        public ICommand FilterCommand => new RelayCommand<string>(ApplyFilter);

        private void ApplyFilter(string? filter)
        {
            if (string.IsNullOrEmpty(filter))
                return;

            Section = "";

            if (filter == GamesFilter)
                return;

            GamesFilter = filter;

            if (filter == FilterPrivate && _privatePlaceIds is null)
            {
                _ = LoadPrivatePlacesAsync();
                return;
            }

            LoadEntries();
        }

        private HashSet<long>? _privatePlaceIds;
        private HashSet<long>? _privateUniverseIds;

        private async Task LoadPrivatePlacesAsync()
        {
            StatusText = "Checking which games you have a private server in...";

            try
            {
                List<PrivateServerInfo> servers = await PrivateServers.ListAsync();

                _privatePlaceIds = servers.Where(s => s.PlaceId > 0).Select(s => s.PlaceId).ToHashSet();
                _privateUniverseIds = servers.Where(s => s.UniverseId > 0).Select(s => s.UniverseId).ToHashSet();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list private servers: {ex.Message}");

                _privatePlaceIds = new HashSet<long>();
                _privateUniverseIds = new HashSet<long>();

                ContinuePlaying.Clear();
                OnPropertyChanged(nameof(IsEmpty));
                StatusText = "Couldn't read your private servers. Sign in to Roblox in your browser, then try again.";
                return;
            }

            LoadEntries();
        }

        private string _section = "";
        public string Section
        {
            get => _section;
            private set
            {
                _section = value;
                OnPropertyChanged(nameof(Section));
                OnPropertyChanged(nameof(SectionVisibility));
                OnPropertyChanged(nameof(DetailVisibility));
            }
        }

        private Uri? _sectionSource;
        public Uri? SectionSource
        {
            get => _sectionSource;
            private set { _sectionSource = value; OnPropertyChanged(nameof(SectionSource)); }
        }

        public Visibility SectionVisibility => _section.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DetailVisibility => _section.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

        public ICommand ShowSectionCommand => new RelayCommand<string>(ShowSection);

        private void ShowSection(string? section)
        {
            if (string.IsNullOrEmpty(section) || section == Section)
            {
                Section = "";
                return;
            }

            string? page = section switch
            {
                SectionCatalog => "FastFlagGamesPage.xaml",
                SectionServers => "ServerBrowserPage.xaml",
                SectionPrivateServers => "PrivateServersPage.xaml",
                SectionHistory => "HistoryPage.xaml",
                _ => null,
            };

            if (page is null)
                return;

            SectionSource = new Uri($"/UI/Elements/Settings/Pages/{page}", UriKind.Relative);
            Section = section;
        }

        public ObservableCollection<ServerListItem> Servers { get; } = new();

        private string _serverStatus = "Pick a game on the left to see its servers.";
        public string ServerStatus
        {
            get => _serverStatus;
            private set { _serverStatus = value; OnPropertyChanged(nameof(ServerStatus)); }
        }

        private bool _serversBusy;
        public bool ServersBusy
        {
            get => _serversBusy;
            private set { _serversBusy = value; OnPropertyChanged(nameof(ServersBusy)); OnPropertyChanged(nameof(ServersNotBusy)); }
        }

        public bool ServersNotBusy => !_serversBusy;

        public ICommand FindServersCommand => new AsyncRelayCommand(FindServersAsync);

        public ICommand JoinServerCommand => new RelayCommand<ServerListItem>(JoinServer);

        private async Task FindServersAsync()
        {
            if (ServersBusy || _selectedGame is null || _selectedGame.PlaceId <= 0)
                return;

            ServersBusy = true;
            Servers.Clear();
            ServerStatus = "Looking for servers...";

            try
            {
                List<ServerListItem> servers = await PhasmaStrap.Integrations.ServerBrowser.ListPublicServersAsync(PhasmaStrap.Utility.PlaceNames.StartPlaceOf(_selectedGame.PlaceId));

                foreach (ServerListItem server in servers)
                    Servers.Add(server);

                ServerStatus = servers.Count == 0
                    ? "No public servers are running for this place right now."
                    : $"{servers.Count} public server(s) running right now.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Server lookup failed: {ex.Message}");
                ServerStatus = $"Server lookup failed: {ex.Message}";
            }
            finally
            {
                ServersBusy = false;
            }
        }

        private void JoinServer(ServerListItem? server)
        {
            if (server is null || _selectedGame is null || _selectedGame.PlaceId <= 0)
                return;

            PhasmaStrap.Integrations.ServerBrowser.JoinServer(PhasmaStrap.Utility.PlaceNames.StartPlaceOf(_selectedGame.PlaceId), server.JobId);
        }

        public HomeViewModel()
        {
            LoadEntries();
        }

        private void LoadEntries()
        {
            long previous = _selectedGame?.PlaceId ?? 0;

            List<PlayTimeEntry> entries = PlayTimeStore.GetAll().OrderByDescending(x => x.LastPlayed).ToList();

            if (GamesFilter == FilterRecent)
                entries = entries.Take(MaxContinuePlaying).ToList();
            else if (GamesFilter == FilterPrivate)
                entries = _privatePlaceIds is null
                    ? new List<PlayTimeEntry>()
                    : entries.Where(e => _privatePlaceIds.Contains(e.PlaceId) || (e.UniverseId > 0 && _privateUniverseIds!.Contains(e.UniverseId))).ToList();

            ContinuePlaying.Clear();

            foreach (PlayTimeEntry entry in entries)
                ContinuePlaying.Add(entry);

            _ = FillPlaceNamesAsync();

            StatusText = ContinuePlaying.Count == 0 ? EmptyStatus() : FilledStatus();

            OnPropertyChanged(nameof(IsEmpty));

            SelectedGame = previous == 0 ? null : ContinuePlaying.FirstOrDefault(e => e.PlaceId == previous);
        }

        private string EmptyStatus() => GamesFilter switch
        {
            FilterPrivate => "None of the games you have played have a private server of yours in them.",
            _ => "No games played yet, games you play will show up here.",
        };

        private string FilledStatus() => GamesFilter switch
        {
            FilterPrivate => $"{ContinuePlaying.Count} played game(s) you have a private server in.",
            FilterAll => $"All {ContinuePlaying.Count} game(s) you have played.",
            _ => $"Your {ContinuePlaying.Count} most recently played game(s).",
        };

        private async Task FillPlaceNamesAsync()
        {
            if (await PhasmaStrap.Utility.PlaceNames.FillAsync(ContinuePlaying.ToList()))
                LoadEntries();
        }

        private static void Launch(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            LaunchDeepLink($"roblox://experiences/start?placeId={PhasmaStrap.Utility.PlaceNames.StartPlaceOf(entry.PlaceId)}");
        }

        private static void CopyLink(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            try
            {
                Clipboard.SetText($"https://www.roblox.com/games/{entry.PlaceId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to copy link: {ex.Message}");
            }
        }

        private static void LaunchRoblox()
        {
            LaunchDeepLink("roblox://");
        }

        private static void LaunchDeepLink(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            Process.Start(Paths.Process, $"-player \"{uri}\"");
        }

        private async Task<RobloxLaunchTarget?> ResolveInputAsync()
        {
            RobloxLaunchTarget? target = await RobloxLinkParser.ResolveAsync(LinkInput);
            if (target is null || target.Kind == RobloxLinkKind.Unknown)
            {
                LinkStatus = "That doesn't look like a Roblox link or place ID. Try a roblox.com/games/... link, a private server link, a roblox.com/share link, or a numeric place ID.";
                return null;
            }

            return target;
        }

        private async Task LaunchLinkAsync()
        {
            if (LinkBusy)
                return;

            LinkBusy = true;
            try
            {
                RobloxLaunchTarget? target = await ResolveInputAsync();
                if (target is null)
                    return;

                LinkStatus = $"Launching {target.Describe().ToLowerInvariant()}...";
                LaunchDeepLink(target.ToDeepLink());
            }
            finally
            {
                LinkBusy = false;
            }
        }

        private async Task ShowPlacesAsync()
        {
            if (LinkBusy)
                return;

            LinkBusy = true;
            try
            {
                Places.Clear();
                PlacesTitle = "";

                RobloxLaunchTarget? target = await ResolveInputAsync();
                if (target is null)
                    return;

                if (target.Kind != RobloxLinkKind.Place)
                {
                    LinkStatus = "Places can only be listed for a game link or place ID - share links don't say which game they belong to until Roblox opens them.";
                    return;
                }

                LinkStatus = "Looking up the experience...";

                long? universeId = await UniversePlaces.GetUniverseIdAsync(target.PlaceId);
                if (universeId is null)
                {
                    LinkStatus = $"Couldn't find an experience for place {target.PlaceId} - the ID may be wrong or the place may be private.";
                    return;
                }

                UniverseSummary? summary = await UniversePlaces.GetSummaryAsync(universeId.Value);
                List<UniversePlace> places = await UniversePlaces.GetPlacesAsync(universeId.Value, summary?.RootPlaceId ?? target.PlaceId);

                foreach (UniversePlace place in places)
                    Places.Add(new PlaceRow { PlaceId = place.PlaceId, Name = place.Name, IsRootPlace = place.IsRootPlace });

                string name = string.IsNullOrEmpty(summary?.Name) ? $"Experience {universeId}" : summary!.Name;
                PlacesTitle = Places.Count == 0 ? "" : $"{name}  ·  {Places.Count} place(s)";
                LinkStatus = Places.Count == 0
                    ? $"{name} has no places visible to this account."
                    : $"{name}{(string.IsNullOrEmpty(summary?.Creator) ? "" : $" by {summary!.Creator}")} - pick a place below to launch straight into it.";
                OnPropertyChanged(nameof(PlacesVisibility));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place lookup failed: {ex.Message}");
                LinkStatus = $"Place lookup failed: {ex.Message}";
            }
            finally
            {
                LinkBusy = false;
            }
        }

        private static void LaunchPlace(PlaceRow? place)
        {
            if (place is null || place.PlaceId <= 0)
                return;

            LaunchDeepLink($"roblox://experiences/start?placeId={place.PlaceId}");
        }
    }
}
