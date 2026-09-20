using PhasmaStrap.Integrations;
using PhasmaStrap.UI;

namespace PhasmaStrap.Utility
{
    internal static class FriendActivityMonitor
    {
        private const string LOG_IDENT = "FriendActivityMonitor";
        private const int FriendListRefreshEveryNPolls = 10;

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;

        public static void Start()
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _loopTask = null;
        }

        private static async Task LoopAsync(CancellationToken token)
        {
            var lastPresence = new Dictionary<long, FriendPresence>();
            List<FriendInfo> friends = new();
            bool firstPoll = true;
            int pollsSinceFriendRefresh = FriendListRefreshEveryNPolls;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync(token).ConfigureAwait(false);

                    if (me is not null)
                    {
                        if (pollsSinceFriendRefresh >= FriendListRefreshEveryNPolls)
                        {
                            friends = await FriendsService.GetFriendsAsync(me.UserId, token).ConfigureAwait(false);
                            pollsSinceFriendRefresh = 0;
                        }
                        else
                        {
                            pollsSinceFriendRefresh++;
                        }

                        if (friends.Count > 0)
                        {
                            Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId), token).ConfigureAwait(false);

                            if (!firstPoll)
                                RaiseAlerts(friends, lastPresence, presence);

                            lastPresence = presence;
                            firstPoll = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }

                try
                {
                    int delaySeconds = Math.Max(20, App.Settings.Prop.FriendActivityPollSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static void RaiseAlerts(List<FriendInfo> friends, Dictionary<long, FriendPresence> before, Dictionary<long, FriendPresence> after)
        {
            if (!App.Settings.Prop.FriendActivityAlertsEnabled)
                return;

            HashSet<long>? favourites = App.Settings.Prop.FriendActivityFavouritesOnly ? FriendNotesStore.Shared.Favourites() : null;

            foreach (FriendInfo friend in friends)
            {
                if (favourites is not null && !favourites.Contains(friend.UserId))
                    continue;

                if (!after.TryGetValue(friend.UserId, out FriendPresence? now))
                    continue;

                before.TryGetValue(friend.UserId, out FriendPresence? was);
                FriendPresenceType wasType = was?.Type ?? FriendPresenceType.Offline;

                if (wasType == now.Type)
                    continue;

                string name = string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.Username : friend.DisplayName;

                if (wasType == FriendPresenceType.Offline && now.Type != FriendPresenceType.Offline)
                {
                    NotificationCenter.Notify(
                        $"{name} is now online",
                        string.IsNullOrEmpty(now.LastLocation) ? "Online" : now.LastLocation,
                        NotificationCategory.General,
                        kind: NotificationKindId.FriendOnline);
                }
                else if (wasType != FriendPresenceType.InGame && now.Type == FriendPresenceType.InGame)
                {
                    NotificationCenter.Notify(
                        $"{name} started playing",
                        string.IsNullOrEmpty(now.LastLocation) ? "In a game" : now.LastLocation,
                        NotificationCategory.General,
                        kind: NotificationKindId.FriendPlaying);
                }
            }
        }
    }
}
