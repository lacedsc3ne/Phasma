using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class FriendRow : NotifyPropertyChangedViewModel
    {
        public long UserId { get; init; }
        public string Name { get; init; } = "";

        private string? _avatarUrl;

        public string? AvatarUrl
        {
            get => _avatarUrl;
            set { _avatarUrl = value; OnPropertyChanged(nameof(AvatarUrl)); }
        }
        public FriendPresenceType Type { get; init; }
        public string StatusText { get; init; } = "";
        public bool Joinable { get; init; }
        public FriendPresence? Presence { get; init; }

        private static int Rank(FriendPresenceType type) => type switch
        {
            FriendPresenceType.InGame => 0,
            FriendPresenceType.Online => 1,
            FriendPresenceType.InStudio => 2,
            _ => 3,
        };

        public int SortRank => Rank(Type);

        public string GroupName => Type switch
        {
            FriendPresenceType.InGame => "In game",
            FriendPresenceType.Offline => "Offline",
            _ => "Online",
        };

        public int GroupRank => Type switch
        {
            FriendPresenceType.InGame => 0,
            FriendPresenceType.Offline => 2,
            _ => 1,
        };

        public Action<FriendRow>? FavouriteChanged;

        private bool _isFavourite;
        public bool IsFavourite
        {
            get => _isFavourite;
            set
            {
                if (_isFavourite == value)
                    return;

                _isFavourite = value;
                PhasmaStrap.Utility.FriendNotesStore.Shared.Set(UserId, _isFavourite, _note);
                OnPropertyChanged(nameof(IsFavourite));
                OnPropertyChanged(nameof(StarSymbol));
                OnPropertyChanged(nameof(StarFilled));
                FavouriteChanged?.Invoke(this);
            }
        }

        public Wpf.Ui.Common.SymbolRegular StarSymbol => Wpf.Ui.Common.SymbolRegular.Star24;
        public bool StarFilled => _isFavourite;

        private string _note = "";
        public string Note
        {
            get => _note;
            set
            {
                value ??= "";
                if (_note == value)
                    return;

                _note = value;
                PhasmaStrap.Utility.FriendNotesStore.Shared.Set(UserId, _isFavourite, _note);
                OnPropertyChanged(nameof(Note));
            }
        }

        public void LoadNote(bool favourite, string note)
        {
            _isFavourite = favourite;
            _note = note;
        }

        public bool Matches(string search) =>
            search.Length == 0
            || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Username.Contains(search, StringComparison.OrdinalIgnoreCase)
            || _note.Contains(search, StringComparison.OrdinalIgnoreCase)
            || StatusText.Contains(search, StringComparison.OrdinalIgnoreCase);

        public string Username { get; init; } = "";
    }

    public sealed class FriendsViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "FriendsViewModel";

        private static async Task CacheAvatarsAsync(List<FriendRow> rows, Dictionary<long, string> remote)
        {
            foreach (FriendRow row in rows)
            {
                if (AvatarCache.TryGetFresh(row.UserId) is not null)
                    continue;

                if (!remote.TryGetValue(row.UserId, out string? url) || string.IsNullOrEmpty(url))
                    continue;

                string? local = await AvatarCache.DownloadAsync(row.UserId, url).ConfigureAwait(false);
                if (local is null)
                    continue;

                Application.Current?.Dispatcher.BeginInvoke(new Action(() => row.AvatarUrl = local));
            }
        }

        public ObservableCollection<FriendRow> Friends { get; } = new();

        private List<FriendRow> _all = new();

        private string _search = "";
        public string Search
        {
            get => _search;
            set { _search = value ?? ""; OnPropertyChanged(nameof(Search)); ApplyView(); }
        }

        private bool _favouritesOnly;
        public bool FavouritesOnly
        {
            get => _favouritesOnly;
            set { _favouritesOnly = value; OnPropertyChanged(nameof(FavouritesOnly)); ApplyView(); }
        }

        public bool AlertFavouritesOnly
        {
            get => App.Settings.Prop.FriendActivityFavouritesOnly;
            set { App.Settings.Prop.FriendActivityFavouritesOnly = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(AlertFavouritesOnly)); }
        }

        private void ApplyView()
        {
            string search = _search.Trim();
            long? selectedId = _selectedFriend?.UserId;

            List<FriendRow> rows = _all
                .Where(r => (!_favouritesOnly || r.IsFavourite) && r.Matches(search))
                .OrderBy(r => r.GroupRank)
                .ThenByDescending(r => r.IsFavourite)
                .ThenBy(r => r.SortRank)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Friends.Clear();
            foreach (FriendRow row in rows)
                Friends.Add(row);

            SelectedFriend = selectedId is null ? null : rows.FirstOrDefault(r => r.UserId == selectedId.Value);

            OnPropertyChanged(nameof(HasFriends));
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        private FriendRow? _selectedFriend;

        public FriendRow? SelectedFriend
        {
            get => _selectedFriend;
            set
            {
                if (ReferenceEquals(_selectedFriend, value))
                    return;

                _selectedFriend = value;
                OnPropertyChanged(nameof(SelectedFriend));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectionVisibility));
                OnPropertyChanged(nameof(NoSelectionVisibility));
            }
        }

        public bool HasSelection => _selectedFriend is not null;
        public Visibility SelectionVisibility => _selectedFriend is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoSelectionVisibility => _selectedFriend is null ? Visibility.Visible : Visibility.Collapsed;

        private bool _loading;
        public bool Loading { get => _loading; private set { _loading = value; OnPropertyChanged(nameof(Loading)); } }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public bool HasFriends => Friends.Count > 0;
        public Visibility EmptyStateVisibility => !Loading && Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand { get; }
        public ICommand JoinCommand { get; }
        public ICommand InviteToPartyCommand { get; }

        public bool AlertsEnabled
        {
            get => App.Settings.Prop.FriendActivityAlertsEnabled;
            set
            {
                App.Settings.Prop.FriendActivityAlertsEnabled = value;

                if (value)
                    PhasmaStrap.Utility.FriendActivityMonitor.Start();
                else
                    PhasmaStrap.Utility.FriendActivityMonitor.Stop();
            }
        }

        public int PollSeconds
        {
            get => App.Settings.Prop.FriendActivityPollSeconds;
            set => App.Settings.Prop.FriendActivityPollSeconds = Math.Max(20, value);
        }

        public Visibility PartyInviteVisibility => App.Settings.Prop.PartyEnabled ? Visibility.Visible : Visibility.Collapsed;

        private static async Task InviteToPartyAsync(FriendRow? row)
        {
            if (row is null)
                return;

            bool sent = await PhasmaStrap.Utility.PartyService.InviteAsync(row.UserId);

            NotificationCenter.Notify(
                sent ? "Party invite sent" : "Could not invite them",
                sent
                    ? $"{row.Name} will see it in PhasmaStrap."
                    : $"{row.Name} needs PhasmaStrap with a linked Roblox account, and you need to be in a party.",
                NotificationCategory.General,
                kind: NotificationKindId.Party);
        }

        public FriendsViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            JoinCommand = new AsyncRelayCommand<FriendRow?>(JoinAsync);
            InviteToPartyCommand = new AsyncRelayCommand<FriendRow?>(InviteToPartyAsync);

            var view = CollectionViewSource.GetDefaultView(Friends);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FriendRow.GroupName)));

            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            Loading = true;
            OnPropertyChanged(nameof(EmptyStateVisibility));
            Status = "Loading...";

            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync().ConfigureAwait(true);

                if (me is null)
                {
                    Status = "Sign into Roblox to see your friends list.";
                    _all = new();
                    Friends.Clear();
                    SelectedFriend = null;
                    return;
                }

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId).ConfigureAwait(true);

                if (friends.Count == 0)
                {
                    Status = "No friends found (or this account's friends list is private).";
                    _all = new();
                    Friends.Clear();
                    SelectedFriend = null;
                    return;
                }

                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId)).ConfigureAwait(true);
                Dictionary<long, string> avatars = await FriendsService.GetAvatarsAsync(friends.Select(f => f.UserId)).ConfigureAwait(true);

                var rows = friends.Select(f =>
                {
                    presence.TryGetValue(f.UserId, out FriendPresence? p);
                    avatars.TryGetValue(f.UserId, out string? avatar);
                    FriendPresenceType type = p?.Type ?? FriendPresenceType.Offline;

                    string statusText = type switch
                    {
                        FriendPresenceType.InGame => string.IsNullOrEmpty(p?.LastLocation) ? "In a game" : p!.LastLocation,
                        FriendPresenceType.InStudio => "In Roblox Studio",
                        FriendPresenceType.Online => "Online",
                        _ => "Offline",
                    };

                    PhasmaStrap.Utility.FriendNotesStore.Entry mine = PhasmaStrap.Utility.FriendNotesStore.Shared.Get(f.UserId);

                    var row = new FriendRow
                    {
                        UserId = f.UserId,
                        Username = f.Username ?? "",
                        Name = string.IsNullOrWhiteSpace(f.DisplayName) ? f.Username : f.DisplayName,
                        AvatarUrl = AvatarCache.TryGetFresh(f.UserId) ?? avatar,
                        Type = type,
                        StatusText = statusText,
                        Joinable = p?.Joinable ?? false,
                        Presence = p,
                    };

                    row.LoadNote(mine.Favourite, mine.Note);
                    row.FavouriteChanged = _ => ApplyView();
                    return row;
                })
                .ToList();

                _all = rows;
                ApplyView();

                _ = CacheAvatarsAsync(rows, avatars);

                int online = rows.Count(r => r.Type != FriendPresenceType.Offline);
                int starred = rows.Count(r => r.IsFavourite);
                Status = $"{online} of {rows.Count} friend(s) online." + (starred > 0 ? $"  {starred} favourite(s)." : "");
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                App.Logger.WriteLine(LOG_IDENT, $"RefreshAsync failed: {ex.Message}");
            }
            finally
            {
                Loading = false;
                OnPropertyChanged(nameof(HasFriends));
                OnPropertyChanged(nameof(EmptyStateVisibility));
            }
        }

        private static async Task JoinAsync(FriendRow? row)
        {
            if (row is null)
                return;

            FriendPresence? now = await FreshPresenceAsync(row);

            if (now is null || now.Type != FriendPresenceType.InGame)
            {
                Frontend.ShowMessageBox($"{row.Name} is not in a game any more.", MessageBoxImage.Information);
                return;
            }

            if (!now.Joinable)
            {
                Frontend.ShowMessageBox(
                    $"{row.Name} is in a game, but Roblox is not saying which server. That happens when their joins are set to friends only or off, or when the game blocks joining.",
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(Paths.Process, $"-player \"{FriendsService.GetJoinDeeplink(now)}\"");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Join failed: {ex.Message}");
                Frontend.ShowMessageBox($"Could not start Roblox: {ex.Message}", MessageBoxImage.Error);
            }
        }

        private static async Task<FriendPresence?> FreshPresenceAsync(FriendRow row)
        {
            try
            {
                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(new[] { row.UserId });
                return presence.TryGetValue(row.UserId, out FriendPresence? found) ? found : row.Presence;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not re-check where {row.Name} is: {ex.Message}");
                return row.Presence;
            }
        }
    }
}
