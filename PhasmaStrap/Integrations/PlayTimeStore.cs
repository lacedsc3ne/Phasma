using PhasmaStrap.Models;
using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
    // Persists per-place total playtime and last-played timestamps across app restarts, so the
    // History page can show a running total instead of only the current session. Simplified from
    // Voidstrap's PlayTimeStore: no live in-session ticking (a completed session's join/leave times
    // are already both known by the time ActivityWatcher reports a game leave, via its History
    // list), no remote merge/replace API, and no separate per-session dedup bookkeeping - a session
    // is only ever recorded once, right when it completes.
    public static class PlayTimeStore
    {
        private const string LOG_IDENT = "PlayTimeStore";
        private const int MaxEntries = 500;
        private const double MaxSessionMinutes = 1440.0; // clamp against clock skew / sleep-resume weirdness

        private static readonly object _lock = new();
        private static PlayTimeData _data = new();
        private static bool _loaded;

        private static readonly JsonSerializerOptions StoreJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string FolderPath => Paths.PlayTime;
        private static string FilePath => Path.Combine(FolderPath, "Data.json");

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (_lock)
            {
                if (_loaded)
                    return;

                Load();
                _loaded = true;
            }
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(FolderPath);
                    _data = new PlayTimeData();
                    return;
                }

                PlayTimeData? loaded = JsonSerializer.Deserialize<PlayTimeData>(File.ReadAllText(FilePath), StoreJsonOptions);
                _data = NormalizeData(loaded ?? new PlayTimeData());
                App.Logger.WriteLine(LOG_IDENT, $"Loaded {_data.Places.Count} play time entries");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Load failed, starting fresh: {ex.Message}");
                _data = new PlayTimeData();
            }
        }

        private static PlayTimeData NormalizeData(PlayTimeData data)
        {
            data.Places = data.Places
                .Where(item => item.Key > 0 && item.Value is not null)
                .OrderByDescending(item => item.Value.LastPlayed)
                .Take(MaxEntries)
                .ToDictionary(item => item.Key, item => item.Value);

            return data;
        }

        public static IReadOnlyList<PlayTimeEntry> GetAll()
        {
            EnsureLoaded();

            lock (_lock)
            {
                return _data.Places.Values
                    .OrderByDescending(entry => entry.LastPlayed)
                    .ToList();
            }
        }

        public static void RecordSession(ActivityData? activity)
        {
            if (activity is null || activity.PlaceId <= 0)
                return;

            DateTime end = activity.TimeLeft ?? DateTime.Now;

            if (activity.TimeJoined == default || end <= activity.TimeJoined)
                return;

            double minutes = Math.Min((end - activity.TimeJoined).TotalMinutes, MaxSessionMinutes);

            if (minutes <= 0)
                return;

            EnsureLoaded();

            lock (_lock)
            {
                if (!_data.Places.TryGetValue(activity.PlaceId, out PlayTimeEntry? entry))
                {
                    entry = new PlayTimeEntry { PlaceId = activity.PlaceId };
                    _data.Places[activity.PlaceId] = entry;
                }

                if (activity.UniverseId > 0)
                    entry.UniverseId = activity.UniverseId;

                entry.TotalMinutes += minutes;

                if (end > entry.LastPlayed)
                    entry.LastPlayed = end;

                string? name = activity.UniverseDetails?.Data?.Name;
                if (!string.IsNullOrEmpty(name))
                    entry.Name = name;

                string? icon = activity.UniverseDetails?.Thumbnail?.ImageUrl;
                if (!string.IsNullOrEmpty(icon))
                    entry.IconUrl = icon;

                _data = NormalizeData(_data);
            }

            SaveNow();
        }

        public static void SaveNow()
        {
            string contents;
            try
            {
                lock (_lock)
                {
                    contents = JsonSerializer.Serialize(_data, StoreJsonOptions);
                }

                Directory.CreateDirectory(FolderPath);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save failed: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            if (_loaded)
                SaveNow();
        }
    }
}
