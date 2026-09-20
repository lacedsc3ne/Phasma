using System.Text.Json;

namespace PhasmaStrap.Integrations
{
    public sealed class SessionFriend
    {
        public long UserId { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class ServerVisit
    {
        public long PlaceId { get; set; }
        public long UniverseId { get; set; }
        public string GameName { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string JobId { get; set; } = "";
        public string ServerType { get; set; } = "";
        public string Region { get; set; } = "";
        public long UserId { get; set; }
        public DateTime JoinedUtc { get; set; }
        public DateTime LeftUtc { get; set; }

        public List<int> Fps { get; set; } = new();

        public List<SessionFriend> Friends { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public TimeSpan Length => LeftUtc > JoinedUtc ? LeftUtc - JoinedUtc : TimeSpan.Zero;
    }

    public sealed class SessionRecord
    {
        public string Id { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public List<ServerVisit> Visits { get; set; } = new();
    }

    public sealed class SessionData
    {
        public int SchemaVersion { get; set; } = 1;
        public List<SessionRecord> Sessions { get; set; } = new();
    }

    public sealed class SessionStore
    {
        public static Action<string>? Log;

        public const int FpsSampleSeconds = 5;
        private const int MaxSessions = 600;
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(400);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

        private readonly string _path;

        public SessionStore(string path)
        {
            _path = path;
        }

        public static SessionStore Shared => _shared ??= new SessionStore(Path.Combine(Paths.PlayTime, "Sessions.json"));
        private static SessionStore? _shared;

        public SessionData Load() => TryLoad(out SessionData data) ? data : new SessionData();

        private bool TryLoad(out SessionData result)
        {
            result = new SessionData();

            try
            {
                if (!File.Exists(_path))
                    return true;

                SessionData data = JsonSerializer.Deserialize<SessionData>(File.ReadAllText(_path), JsonOptions) ?? new SessionData();
                data.Sessions ??= new();
                data.Sessions.RemoveAll(s => s is null);
                foreach (SessionRecord session in data.Sessions)
                {
                    session.Visits ??= new();
                    session.Visits.RemoveAll(v => v is null);
                }

                result = data;
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read {_path}: {ex.Message}");
                return false;
            }
        }

        public void Save(SessionRecord session)
        {
            if (string.IsNullOrEmpty(session.Id))
                return;

            using var mutex = new Mutex(false, "PhasmaStrap-SessionStore");
            bool owned = false;

            try
            {
                try { owned = mutex.WaitOne(3000); }
                catch (AbandonedMutexException) { owned = true; }

                if (!owned)
                {
                    Log?.Invoke("The session file is busy - skipping this save");
                    return;
                }

                if (!TryLoad(out SessionData data))
                {
                    string aside = _path + $".unreadable-{DateTime.Now:yyyyMMdd_HHmmss}";
                    try { File.Move(_path, aside, true); Log?.Invoke($"Kept the unreadable history as {aside}"); } catch { }
                }

                data.Sessions.RemoveAll(s => s.Id == session.Id);
                if (session.Visits.Count > 0)
                    data.Sessions.Add(session);

                DateTime oldest = DateTime.UtcNow - MaxAge;
                data.Sessions = data.Sessions
                    .Where(s => s.StartedUtc >= oldest)
                    .OrderBy(s => s.StartedUtc)
                    .TakeLast(MaxSessions)
                    .ToList();

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
                File.Move(temp, _path, true);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not write {_path}: {ex.Message}");
            }
            finally
            {
                if (owned)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        public void Clear()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception ex) { Log?.Invoke($"Could not delete {_path}: {ex.Message}"); }
        }
    }

    public static class SessionStats
    {
        public sealed class GameTotal
        {
            public long UniverseId, PlaceId;
            public string Name = "", IconUrl = "";
            public double Minutes;
            public int Visits;
            public DateTime LastPlayedUtc;
        }

        public sealed class Bucket
        {
            public DateTime Start;
            public double Minutes;
        }

        public sealed class Companion
        {
            public long UserId;
            public string Name = "";
            public int Visits;
            public double Minutes;
            public DateTime LastSeenUtc;
            public string LastGame = "";
            public long LastPlaceId;
        }

        public static IEnumerable<ServerVisit> Visits(SessionData data) => data.Sessions.SelectMany(s => s.Visits);

        private static string KeyOf(ServerVisit visit) => visit.UniverseId > 0 ? $"u{visit.UniverseId}" : $"p{visit.PlaceId}";

        public static List<GameTotal> PerGame(SessionData data, DateTime sinceUtc)
        {
            return Visits(data)
                .Where(v => v.LeftUtc > sinceUtc && v.Length > TimeSpan.Zero)
                .GroupBy(KeyOf)
                .Select(group =>
                {
                    ServerVisit newest = group.OrderByDescending(v => v.JoinedUtc).First();
                    ServerVisit? named = group.OrderByDescending(v => v.JoinedUtc).FirstOrDefault(v => v.GameName.Length > 0);

                    return new GameTotal
                    {
                        UniverseId = newest.UniverseId,
                        PlaceId = newest.PlaceId,
                        Name = named?.GameName ?? $"Place {newest.PlaceId}",
                        IconUrl = group.OrderByDescending(v => v.JoinedUtc).FirstOrDefault(v => v.IconUrl.Length > 0)?.IconUrl ?? "",
                        Minutes = group.Sum(v => Overlap(v, sinceUtc, DateTime.MaxValue).TotalMinutes),
                        Visits = group.Count(),
                        LastPlayedUtc = group.Max(v => v.LeftUtc),
                    };
                })
                .OrderByDescending(g => g.Minutes)
                .ToList();
        }

        private static TimeSpan Overlap(ServerVisit visit, DateTime fromUtc, DateTime toUtc)
        {
            DateTime start = visit.JoinedUtc > fromUtc ? visit.JoinedUtc : fromUtc;
            DateTime end = visit.LeftUtc < toUtc ? visit.LeftUtc : toUtc;
            return end > start ? end - start : TimeSpan.Zero;
        }

        public static List<Bucket> PerDay(SessionData data, int count, DateTime nowLocal)
        {
            DateTime first = nowLocal.Date.AddDays(-(count - 1));
            return Buckets(data, first, count, TimeSpan.FromDays(1));
        }

        public static List<Bucket> PerWeek(SessionData data, int count, DateTime nowLocal)
        {
            int sinceMonday = ((int)nowLocal.DayOfWeek + 6) % 7;
            DateTime thisWeek = nowLocal.Date.AddDays(-sinceMonday);
            return Buckets(data, thisWeek.AddDays(-7 * (count - 1)), count, TimeSpan.FromDays(7));
        }

        private static List<Bucket> Buckets(SessionData data, DateTime firstLocal, int count, TimeSpan size)
        {
            var buckets = Enumerable.Range(0, count).Select(i => new Bucket { Start = firstLocal + TimeSpan.FromTicks(size.Ticks * i) }).ToList();
            List<ServerVisit> visits = Visits(data).ToList();

            foreach (Bucket bucket in buckets)
            {
                DateTime fromUtc = bucket.Start.ToUniversalTime(), toUtc = (bucket.Start + size).ToUniversalTime();
                bucket.Minutes = visits.Sum(v => Overlap(v, fromUtc, toUtc).TotalMinutes);
            }

            return buckets;
        }

        public static int Streak(SessionData data, DateTime nowLocal)
        {
            List<Bucket> days = PerDay(data, 400, nowLocal);
            int index = days.Count - 1;

            if (days[index].Minutes < 1)
                index--;

            int streak = 0;
            while (index >= 0 && days[index].Minutes >= 1)
            {
                streak++;
                index--;
            }

            return streak;
        }

        public static List<Companion> PlayedWith(SessionData data)
        {
            var result = new Dictionary<long, Companion>();

            Dictionary<string, string> names = Visits(data)
                .Where(v => v.GameName.Length > 0)
                .GroupBy(KeyOf)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.JoinedUtc).First().GameName);

            foreach (ServerVisit visit in Visits(data).OrderBy(v => v.JoinedUtc))
            {
                foreach (SessionFriend friend in visit.Friends)
                {
                    if (!result.TryGetValue(friend.UserId, out Companion? companion))
                        result[friend.UserId] = companion = new Companion { UserId = friend.UserId };

                    if (friend.Name.Length > 0)
                        companion.Name = friend.Name;

                    companion.Visits++;
                    companion.Minutes += visit.Length.TotalMinutes;
                    companion.LastSeenUtc = visit.LeftUtc > visit.JoinedUtc ? visit.LeftUtc : visit.JoinedUtc;
                    companion.LastGame = visit.GameName.Length > 0 ? visit.GameName : names.TryGetValue(KeyOf(visit), out string? known) ? known : $"Place {visit.PlaceId}";
                    companion.LastPlaceId = visit.PlaceId;
                }
            }

            return result.Values.OrderByDescending(c => c.LastSeenUtc).ToList();
        }

        public static (int Average, int Low)? FpsSummary(ServerVisit visit)
        {
            List<int> readings = visit.Fps.Where(f => f > 0).ToList();
            if (readings.Count == 0)
                return null;

            List<int> sorted = readings.OrderBy(f => f).ToList();
            int low = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.05))];
            return ((int)Math.Round(readings.Average()), low);
        }

        public static string Duration(double minutes)
        {
            int total = (int)Math.Round(minutes);
            return total >= 60 ? $"{total / 60}h {total % 60}m" : $"{total}m";
        }
    }
}
