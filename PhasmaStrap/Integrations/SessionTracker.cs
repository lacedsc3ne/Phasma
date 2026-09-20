using PhasmaStrap.Models.Entities;
using PhasmaStrap.Utility;

namespace PhasmaStrap.Integrations
{
    public sealed class SessionTracker : IDisposable
    {
        private const string LOG_IDENT = "SessionTracker";
        private const int FriendPollSeconds = 90;
        private const int SaveSeconds = 60;

        private readonly ActivityWatcher _activityWatcher;
        private readonly SessionRecord _session = new() { Id = Guid.NewGuid().ToString("N"), StartedUtc = DateTime.UtcNow };
        private readonly object _lock = new();
        private readonly System.Threading.Timer _timer;

        private ServerVisit? _visit;
        private int _ticks;
        private int _friendPollRunning;
        private List<FriendInfo>? _friends;
        private bool _disposed;

        public SessionTracker(ActivityWatcher activityWatcher)
        {
            SessionStore.Log ??= message => App.Logger.WriteLine("SessionStore", message);

            _activityWatcher = activityWatcher;
            _activityWatcher.OnGameJoin += OnGameJoin;
            _activityWatcher.OnGameLeave += OnGameLeave;

            _timer = new System.Threading.Timer(_ => Tick(), null, SessionStore.FpsSampleSeconds * 1000, SessionStore.FpsSampleSeconds * 1000);
        }

        private void OnGameJoin(object? sender, EventArgs e)
        {
            ActivityData data = _activityWatcher.Data;
            if (data.PlaceId <= 0)
                return;

            ServerVisit visit;

            lock (_lock)
            {
                CloseVisit();

                visit = _visit = new ServerVisit
                {
                    PlaceId = data.PlaceId,
                    UniverseId = data.UniverseId,
                    JobId = data.JobId ?? "",
                    ServerType = data.ServerType.ToString(),
                    UserId = data.UserId,
                    JoinedUtc = DateTime.UtcNow,
                    LeftUtc = DateTime.UtcNow,
                };

                _session.Visits.Add(visit);
                _ticks = 0;
            }

            Save();
            _ = NameAsync(visit, data);
        }

        private void OnGameLeave(object? sender, EventArgs e)
        {
            lock (_lock)
                CloseVisit();

            Save();
        }

        private void CloseVisit()
        {
            if (_visit is null)
                return;

            _visit.LeftUtc = DateTime.UtcNow;
            if (_visit.Region.Length == 0)
                _visit.Region = ServerRegion.Current;

            _visit = null;
        }

        private async Task NameAsync(ServerVisit visit, ActivityData data)
        {
            if (visit.UniverseId <= 0)
                return;

            try
            {
                UniverseDetails? details = UniverseDetails.LoadFromCache(visit.UniverseId);
                if (details is null)
                {
                    await UniverseDetails.FetchSingle(visit.UniverseId);
                    details = UniverseDetails.LoadFromCache(visit.UniverseId);
                }

                lock (_lock)
                {
                    visit.GameName = details?.Data?.Name ?? "";
                    visit.IconUrl = details?.Thumbnail?.ImageUrl ?? "";
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not look up the name of universe {visit.UniverseId}: {ex.Message}");
            }
        }

        private void Tick()
        {
            if (_disposed)
                return;

            try
            {
                bool save = false, pollFriends = false;

                lock (_lock)
                {
                    if (_visit is null)
                        return;

                    _ticks++;
                    _visit.LeftUtc = DateTime.UtcNow;
                    _visit.Fps.Add((int)Math.Round(FpsFeed.Latest));

                    if (_visit.Region.Length == 0)
                        _visit.Region = ServerRegion.Current;

                    save = _ticks % (SaveSeconds / SessionStore.FpsSampleSeconds) == 0;

                    pollFriends = App.Settings.Prop.SessionTrackFriends
                        && (_ticks == 2 || _ticks % (FriendPollSeconds / SessionStore.FpsSampleSeconds) == 0);
                }

                if (pollFriends && Interlocked.Exchange(ref _friendPollRunning, 1) == 0)
                    _ = PollFriendsAsync();

                if (save)
                    Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Tick failed: {ex.Message}");
            }
        }

        private async Task PollFriendsAsync()
        {
            try
            {
                ServerVisit? visit;
                lock (_lock)
                    visit = _visit;

                if (visit is null || string.IsNullOrEmpty(visit.JobId))
                    return;

                if (_friends is null)
                {
                    RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();
                    if (me is null)
                        return;

                    _friends = await FriendsService.GetFriendsAsync(me.UserId);
                }

                if (_friends.Count == 0)
                    return;

                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(_friends.Select(f => f.UserId));

                lock (_lock)
                {
                    if (!ReferenceEquals(visit, _visit))
                        return;

                    foreach (FriendPresence friend in presence.Values)
                    {
                        if (friend.Type != FriendPresenceType.InGame || !string.Equals(friend.GameId, visit.JobId, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (visit.Friends.Any(f => f.UserId == friend.UserId))
                            continue;

                        FriendInfo? info = _friends.FirstOrDefault(f => f.UserId == friend.UserId);
                        string name = !string.IsNullOrWhiteSpace(info?.DisplayName) ? info!.DisplayName : info?.Username ?? friend.Username;

                        visit.Friends.Add(new SessionFriend { UserId = friend.UserId, Name = name ?? "" });
                        App.Logger.WriteLine(LOG_IDENT, $"Friend {friend.UserId} is on this server");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Friend check failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _friendPollRunning, 0);
            }
        }

        private void Save()
        {
            SessionRecord copy;

            lock (_lock)
            {
                _session.EndedUtc = DateTime.UtcNow;

                copy = new SessionRecord
                {
                    Id = _session.Id,
                    StartedUtc = _session.StartedUtc,
                    EndedUtc = _session.EndedUtc,
                    Visits = _session.Visits.Select(v => new ServerVisit
                    {
                        PlaceId = v.PlaceId, UniverseId = v.UniverseId, GameName = v.GameName, IconUrl = v.IconUrl, JobId = v.JobId,
                        ServerType = v.ServerType, Region = v.Region, UserId = v.UserId, JoinedUtc = v.JoinedUtc, LeftUtc = v.LeftUtc,
                        Fps = new List<int>(v.Fps), Friends = v.Friends.Select(f => new SessionFriend { UserId = f.UserId, Name = f.Name }).ToList(),
                    }).ToList(),
                };
            }

            SessionStore.Shared.Save(copy);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Dispose();
            _activityWatcher.OnGameJoin -= OnGameJoin;
            _activityWatcher.OnGameLeave -= OnGameLeave;

            lock (_lock)
                CloseVisit();

            Save();
            GC.SuppressFinalize(this);
        }
    }
}
