using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class PrivateServerRow : NotifyPropertyChangedViewModel
    {
        public PrivateServerInfo Server { get; init; } = null!;

        public string Title => Server.GameName.Length > 0 ? Server.GameName : Server.Name.Length > 0 ? Server.Name : "Private server";

        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                parts.Add(Server.Name.Length > 0 ? $"Server \"{Server.Name}\"" : "Private server");
                if (!Server.Owned && Server.OwnerName.Length > 0)
                    parts.Add($"owned by {Server.OwnerName}");
                return string.Join("  ·  ", parts);
            }
        }

        public string StatusText
        {
            get
            {
                if (!Server.Active)
                    return "Inactive";
                if (Server.Expires is not DateTime expires)
                    return Server.Owned ? "Free" : "";
                // free servers come back with an end date a century away
                if (expires > DateTime.UtcNow.AddYears(20))
                    return "Doesn't expire";

                string when = expires.ToLocalTime().ToString("d MMM yyyy");
                return Server.WillRenew ? $"Renews {when}" : $"Ends {when}";
            }
        }

        private string? _icon;
        public string? IconUrl { get => _icon; set { _icon = value; OnPropertyChanged(nameof(IconUrl)); } }

        public Visibility OwnerVisibility => Server.Owned ? Visibility.Visible : Visibility.Collapsed;

        public bool CanJoin => Server.Active && Server.PlaceId > 0;
    }

    /// <summary>
    /// Servers page > Private servers: the ones you own and the ones shared with you, with join,
    /// invite link and new link. Nothing is fetched until you ask for it.
    /// </summary>
    public sealed class PrivateServersViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<PrivateServerRow> Owned { get; } = new();

        public ObservableCollection<PrivateServerRow> Shared { get; } = new();

        private string _status = "Shows the private servers of the Roblox account signed in on this PC. Nothing is fetched until you press Load.";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        private bool _busy;
        public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(NotBusy)); } }
        public bool NotBusy => !_busy;

        private bool _loaded;
        public Visibility ListsVisibility => _loaded ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoOwnedVisibility => _loaded && Owned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoSharedVisibility => _loaded && Shared.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand LoadCommand => new AsyncRelayCommand(LoadAsync);

        private async Task LoadAsync()
        {
            IsBusy = true;
            Status = "Loading...";

            try
            {
                List<PrivateServerInfo> servers = await PrivateServers.ListAsync();

                Owned.Clear();
                Shared.Clear();

                var rows = servers.Select(s => new PrivateServerRow { Server = s }).ToList();
                foreach (PrivateServerRow row in rows.OrderByDescending(r => r.Server.Active).ThenBy(r => r.Server.GameName, StringComparer.OrdinalIgnoreCase))
                    (row.Server.Owned ? Owned : Shared).Add(row);

                _loaded = true;
                Status = servers.Count == 0 ? "This account has no private servers." : $"{Owned.Count} of your own, {Shared.Count} shared with you.";

                // icons after the list shows - they're only decoration
                var games = await PhasmaStrap.Utility.GameLookup.WithIconsAsync(servers.Where(s => s.UniverseId > 0).Select(s => new PhasmaStrap.Utility.GameInfo(s.UniverseId, s.PlaceId, s.GameName)).DistinctBy(g => g.UniverseId).ToList());
                foreach (PrivateServerRow row in rows)
                    row.IconUrl = games.FirstOrDefault(g => g.UniverseId == row.Server.UniverseId)?.IconUrl is { Length: > 0 } icon ? icon : null;
            }
            catch (PrivateServers.NotSignedInException)
            {
                Status = "Roblox isn't signed in on this PC - sign in to Roblox (or pick an account on the Accounts page), then try again.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PrivateServersViewModel::Load", ex);
                Status = $"Couldn't load your private servers: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(ListsVisibility));
                OnPropertyChanged(nameof(NoOwnedVisibility));
                OnPropertyChanged(nameof(NoSharedVisibility));
            }
        }

        public ICommand JoinCommand => new AsyncRelayCommand<PrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            try
            {
                Status = $"Joining {row.Title}...";
                Status = await PrivateServers.JoinAsync(row.Server)
                    ? $"Starting Roblox in {row.Title}."
                    : $"Roblox didn't give a way into {row.Title} - it may be inactive, or you may no longer have access.";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't join: {ex.Message}";
            }
        });

        public ICommand CopyLinkCommand => new AsyncRelayCommand<PrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            try
            {
                string? link = await PrivateServers.GetLinkAsync(row.Server);
                if (link is null)
                {
                    Status = $"{row.Title} has no invite link right now - use New link to make one.";
                    return;
                }

                PhasmaStrap.Utility.ClipboardShare.CopyText(link);
                Status = $"Invite link for {row.Title} copied. Anyone with it can join.";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't get the link: {ex.Message}";
            }
        });

        public ICommand NewLinkCommand => new AsyncRelayCommand<PrivateServerRow>(async row =>
        {
            if (row is null)
                return;

            var answer = Frontend.ShowMessageBox(
                $"Make a new invite link for \"{row.Title}\"?\n\nThe current link stops working, so anyone you gave it to can't use it to join any more. People already added to the server keep access.",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                string? link = await PrivateServers.NewLinkAsync(row.Server);
                if (link is null)
                {
                    Status = "Roblox made a new link but didn't send it back - use Copy link.";
                    return;
                }

                PhasmaStrap.Utility.ClipboardShare.CopyText(link);
                Status = $"New invite link for {row.Title} copied. The old one no longer works.";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't make a new link: {ex.Message}";
            }
        });

        public ICommand OpenOnRobloxCommand => new RelayCommand<PrivateServerRow>(row =>
        {
            if (row is not null && row.Server.PlaceId > 0)
                Utilities.ShellExecute($"https://www.roblox.com/games/{row.Server.PlaceId}#!/game-instances");
        });
    }
}
