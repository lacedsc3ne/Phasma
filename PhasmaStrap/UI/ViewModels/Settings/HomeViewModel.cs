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

        // --- launch by link ---

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

        public HomeViewModel()
        {
            LoadEntries();
        }

        private void LoadEntries()
        {
            ContinuePlaying.Clear();

            foreach (PlayTimeEntry entry in PlayTimeStore.GetAll().OrderByDescending(x => x.LastPlayed).Take(MaxContinuePlaying))
                ContinuePlaying.Add(entry);

            StatusText = ContinuePlaying.Count == 0
                ? "No games played yet - games you play will show up here."
                : $"Showing your {ContinuePlaying.Count} most recently played game(s).";

            OnPropertyChanged(nameof(IsEmpty));
        }

        private static void Launch(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            LaunchDeepLink($"roblox://experiences/start?placeId={entry.PlaceId}");
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
