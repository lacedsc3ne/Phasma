using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ServerBrowserViewModel : NotifyPropertyChangedViewModel
    {
        private string _placeId = "";
        private bool _isSearching;
        private string _statusText = Strings.Menu_ServerBrowser_Status_EnterPlaceId;

        public string PlaceId
        {
            get => _placeId;
            set { _placeId = value; OnPropertyChanged(nameof(PlaceId)); }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set { _isSearching = value; OnPropertyChanged(nameof(IsSearching)); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public ObservableCollection<ServerListItem> Servers { get; } = new();

        public ServerBrowserViewModel()
        {
            _ = LoadDatacenterDistancesAsync();
        }

        public bool MatchmakerEnabled
        {
            get => App.Settings.Prop.MatchmakerEnabled;
            set { App.Settings.Prop.MatchmakerEnabled = value; OnPropertyChanged(nameof(MatchmakerEnabled)); }
        }

        public bool MatchmakerPreferEmpty
        {
            get => App.Settings.Prop.MatchmakerPreferEmpty;
            set => App.Settings.Prop.MatchmakerPreferEmpty = value;
        }

        public sealed record DatacenterChoice(string Display, string Key);

        // "" represents no preference (closest available)
        public IEnumerable<DatacenterChoice> DatacenterChoices { get; } =
            new[] { new DatacenterChoice(Strings.Menu_ServerBrowser_ClosestAvailable, "") }
                .Concat(RobloxDatacenterMap.AllDatacenters()
                    .OrderBy(dc => dc.City)
                    .Select(dc => new DatacenterChoice($"{dc.City}, {dc.Country}", Matchmaker.DatacenterKey(dc))));

        public string PreferredDatacenter
        {
            get => App.Settings.Prop.MatchmakerPreferredDatacenter;
            set => App.Settings.Prop.MatchmakerPreferredDatacenter = value ?? "";
        }

        public bool MatchmakerAutoCandidates
        {
            get => App.Settings.Prop.MatchmakerAutoCandidates;
            set { App.Settings.Prop.MatchmakerAutoCandidates = value; OnPropertyChanged(nameof(MatchmakerAutoCandidates)); }
        }

        public int MatchmakerMaxCandidates
        {
            get => App.Settings.Prop.MatchmakerMaxCandidates;
            set => App.Settings.Prop.MatchmakerMaxCandidates = Math.Clamp(value, Matchmaker.MinCandidateCount, Matchmaker.MaxCandidateCount);
        }

        public sealed class DatacenterExclusion : NotifyPropertyChangedViewModel
        {
            public string Display { get; init; } = "";
            public string Key { get; init; } = "";
            public double Lat { get; init; }
            public double Lon { get; init; }

            private double _distanceKm = -1.0;
            private string _pingDisplay = "...";

            public double DistanceKm
            {
                get => _distanceKm;
                set
                {
                    if (_distanceKm == value)
                        return;
                    _distanceKm = value;
                    OnPropertyChanged(nameof(DistanceKm));
                    OnPropertyChanged(nameof(DistanceDisplay));
                }
            }

            public string DistanceDisplay => _distanceKm < 0.0 ? "" : $"{(int)_distanceKm} km away";

            public string PingDisplay
            {
                get => _pingDisplay;
                set
                {
                    if (_pingDisplay == value)
                        return;
                    _pingDisplay = value;
                    OnPropertyChanged(nameof(PingDisplay));
                }
            }

            public bool IsAllowed
            {
                get => !IsBlocked;
                set => IsBlocked = !value;
            }

            public bool IsBlocked
            {
                get => App.Settings.Prop.MatchmakerDisabledDatacenters.Contains(Key);
                set
                {
                    var blocked = App.Settings.Prop.MatchmakerDisabledDatacenters;
                    if (value && !blocked.Contains(Key))
                        blocked.Add(Key);
                    else if (!value)
                        blocked.Remove(Key);

                    OnPropertyChanged(nameof(IsBlocked));
                    OnPropertyChanged(nameof(IsAllowed));
                }
            }
        }

        // per-datacenter allow/block grid, with an estimated distance/ping column filled in
        // once the user's own location resolves (see LoadDatacenterDistancesAsync) - the
        // estimate uses the same haversine-distance heuristic the matchmaker itself scores
        // candidates with (Matchmaker.HaversineKm/EstimatePingMs), not a live network probe.
        public ObservableCollection<DatacenterExclusion> DatacenterExclusions { get; } = new(
            RobloxDatacenterMap.AllDatacenters()
                .OrderBy(dc => dc.City)
                .Select(dc => new DatacenterExclusion { Display = $"{dc.City}, {dc.Country}", Key = Matchmaker.DatacenterKey(dc), Lat = dc.Lat, Lon = dc.Lon }));

        private async Task LoadDatacenterDistancesAsync()
        {
            try
            {
                UserGeo? geo = await Matchmaker.GetUserGeoAsync();
                if (geo is null)
                    return;

                foreach (DatacenterExclusion dc in DatacenterExclusions)
                {
                    if (dc.Lat == 0.0 && dc.Lon == 0.0)
                        continue;

                    double km = Matchmaker.HaversineKm(geo.Lat, geo.Lon, dc.Lat, dc.Lon);
                    dc.DistanceKm = km;
                    dc.PingDisplay = $"~{Matchmaker.EstimatePingMs(km)} ms";
                }
            }
            catch
            {
                // best-effort only - the grid still works for allow/block without distances
            }
        }

        // which Roblox gamejoin API version the matchmaker uses to resolve/join candidate
        // servers (see Matchmaker.BuildJoinRequest) - change only if joins stop resolving
        public sealed record GamejoinApiOption(string Display, int Value);

        public IEnumerable<GamejoinApiOption> GamejoinApiOptions { get; } = new[]
        {
            new GamejoinApiOption("V1 (stable)", 1),
            new GamejoinApiOption("V2 (newer)", 2),
        };

        public int MatchmakerGamejoinApiVersion
        {
            get => App.Settings.Prop.MatchmakerGamejoinApiVersion;
            set => App.Settings.Prop.MatchmakerGamejoinApiVersion = value;
        }

        public ObservableCollection<string> ExcludedPlaces { get; } = new(App.Settings.Prop.MatchmakerExcludedPlaces);

        private string _excludePlaceId = "";

        public string ExcludePlaceId
        {
            get => _excludePlaceId;
            set { _excludePlaceId = value; OnPropertyChanged(nameof(ExcludePlaceId)); }
        }

        public ICommand AddExcludedPlaceCommand => new RelayCommand(() =>
        {
            string id = ExcludePlaceId.Trim();

            if (!long.TryParse(id, out _) || ExcludedPlaces.Contains(id))
                return;

            ExcludedPlaces.Add(id);
            App.Settings.Prop.MatchmakerExcludedPlaces.Add(id);
            ExcludePlaceId = "";
        });

        public ICommand RemoveExcludedPlaceCommand => new RelayCommand<string>(id =>
        {
            if (id is null)
                return;

            ExcludedPlaces.Remove(id);
            App.Settings.Prop.MatchmakerExcludedPlaces.Remove(id);
        });

        public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);

        public ICommand JoinCommand => new RelayCommand<ServerListItem>(server =>
        {
            if (server is null || !long.TryParse(PlaceId.Trim(), out long placeId))
                return;

            ServerBrowser.JoinServer(placeId, server.JobId);
        });

        private async Task SearchAsync()
        {
            if (!long.TryParse(PlaceId.Trim(), out long placeId) || placeId <= 0)
            {
                StatusText = Strings.Menu_ServerBrowser_Status_InvalidPlaceId;
                return;
            }

            IsSearching = true;
            StatusText = Strings.Menu_ServerBrowser_Status_Searching;
            Servers.Clear();

            try
            {
                List<ServerListItem> servers = await ServerBrowser.ListPublicServersAsync(placeId);

                foreach (ServerListItem server in servers)
                    Servers.Add(server);

                StatusText = servers.Count > 0
                    ? string.Format(Strings.Menu_ServerBrowser_Status_FoundServers, servers.Count)
                    : Strings.Menu_ServerBrowser_Status_NoServersFound;
            }
            catch (Exception ex)
            {
                StatusText = string.Format(Strings.Menu_ServerBrowser_Status_SearchFailed, ex.Message);
            }
            finally
            {
                IsSearching = false;
            }
        }
    }
}
