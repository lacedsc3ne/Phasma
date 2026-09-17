using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class FriendRow
    {
        public long UserId { get; init; }
        public string Name { get; init; } = "";
        public string? AvatarUrl { get; init; }
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
    }

    /// <summary>
    /// Backs the Friends page: an online-status/current-game leaderboard (real "hours played"
    /// isn't buildable - Roblox never exposes another user's playtime through any API, only your
    /// own), join-a-friend, and the Friend Activity Alerts toggle (the actual polling/toast logic
    /// lives in Utility.FriendActivityMonitor, which runs independent of this page being open -
    /// same "works whether or not Settings is even the active window" shape as AutoCleanRam).
    /// </summary>
    public sealed class FriendsViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "FriendsViewModel";

        public ObservableCollection<FriendRow> Friends { get; } = new();

        private bool _loading;
        public bool Loading { get => _loading; private set { _loading = value; OnPropertyChanged(nameof(Loading)); } }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public bool HasFriends => Friends.Count > 0;
        public Visibility EmptyStateVisibility => !Loading && Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand { get; }
        public ICommand JoinCommand { get; }

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

        public FriendsViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            JoinCommand = new RelayCommand<FriendRow?>(Join);

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
                    Friends.Clear();
                    return;
                }

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId).ConfigureAwait(true);

                if (friends.Count == 0)
                {
                    Status = "No friends found (or this account's friends list is private).";
                    Friends.Clear();
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

                    return new FriendRow
                    {
                        UserId = f.UserId,
                        Name = string.IsNullOrWhiteSpace(f.DisplayName) ? f.Username : f.DisplayName,
                        AvatarUrl = avatar,
                        Type = type,
                        StatusText = statusText,
                        Joinable = p?.Joinable ?? false,
                        Presence = p,
                    };
                })
                .OrderBy(r => r.SortRank)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

                Friends.Clear();
                foreach (FriendRow row in rows)
                    Friends.Add(row);

                int online = rows.Count(r => r.Type != FriendPresenceType.Offline);
                Status = $"{online} of {rows.Count} friend(s) online.";
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

        private static void Join(FriendRow? row)
        {
            if (row?.Presence is null || !row.Joinable)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(FriendsService.GetJoinDeeplink(row.Presence)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Join failed: {ex.Message}");
                Frontend.ShowMessageBox($"Could not start Roblox: {ex.Message}", MessageBoxImage.Error);
            }
        }
    }
}
