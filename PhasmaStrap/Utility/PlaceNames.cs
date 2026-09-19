using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    // Names of the places inside games made of several places (Blade Ball: lobby, ranked, duels...),
    // so the recently played lists can tell them apart - playtime is kept per place. Also knows each
    // place's start place, because the other places often can't be joined directly.
    // Cached in Cache\PlaceNames.json (its own file: the Watcher writes the playtime file).
    public static class PlaceNames
    {
        private const string LOG_IDENT = "PlaceNames";

        private sealed class PlaceInfo
        {
            public string Name { get; set; } = "";
            public long StartPlaceId { get; set; }
        }

        private static readonly object _lock = new();
        private static Dictionary<long, PlaceInfo>? _places;

        private static string CachePath => Path.Combine(Paths.Base, "Cache", "PlaceNames.json");

        private static Dictionary<long, PlaceInfo> Places
        {
            get
            {
                lock (_lock)
                {
                    if (_places is null)
                    {
                        try
                        {
                            _places = File.Exists(CachePath)
                                ? JsonSerializer.Deserialize<Dictionary<long, PlaceInfo>>(File.ReadAllText(CachePath)) ?? new()
                                : new();
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Cache unreadable, starting fresh: {ex.Message}");
                            _places = new();
                        }
                    }
                    return _places;
                }
            }
        }

        // "" for a start place (or one not looked up) - those just show the game's name
        public static string NameOf(long placeId)
        {
            lock (_lock)
                return Places.TryGetValue(placeId, out PlaceInfo? info) ? info.Name : "";
        }

        public static long StartPlaceOf(long placeId)
        {
            lock (_lock)
                return Places.TryGetValue(placeId, out PlaceInfo? info) && info.StartPlaceId > 0 ? info.StartPlaceId : placeId;
        }

        // "Blade Ball" + "Blade Ball Ranked" -> "Blade Ball · Ranked"
        public static string Display(string gameName, long placeId)
        {
            string place = NameOf(placeId).Trim();
            if (place.Length == 0 || place.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                return gameName;

            if (gameName.Length > 0 && place.StartsWith(gameName, StringComparison.OrdinalIgnoreCase))
            {
                string rest = place[gameName.Length..].Trim(' ', '-', ':', '|', '·', '(', ')', '[', ']');
                if (rest.Length > 0)
                    place = rest;
            }

            return gameName.Length > 0 ? $"{gameName} · {place}" : place;
        }

        // Looks up the games that appear with more than one place. True when something new was learned.
        public static async Task<bool> FillAsync(IEnumerable<PlayTimeEntry> entries)
        {
            List<IGrouping<long, PlayTimeEntry>> games;
            lock (_lock)
            {
                games = entries
                    .Where(e => e.UniverseId > 0)
                    .GroupBy(e => e.UniverseId)
                    .Where(g => g.Select(e => e.PlaceId).Distinct().Count() > 1 && g.Any(e => !Places.ContainsKey(e.PlaceId)))
                    .ToList();
            }

            bool learned = false;

            foreach (IGrouping<long, PlayTimeEntry> game in games)
            {
                try
                {
                    UniverseSummary? summary = await UniversePlaces.GetSummaryAsync(game.Key);
                    if (summary is null || summary.RootPlaceId <= 0)
                        continue;

                    List<UniversePlace> places = await UniversePlaces.GetPlacesAsync(game.Key, summary.RootPlaceId);

                    lock (_lock)
                    {
                        foreach (UniversePlace place in places)
                            Places[place.PlaceId] = new PlaceInfo { Name = place.IsRootPlace ? "" : place.Name, StartPlaceId = summary.RootPlaceId };

                        // places Roblox no longer lists (removed or private) - don't ask again every time
                        foreach (PlayTimeEntry entry in game)
                            Places.TryAdd(entry.PlaceId, new PlaceInfo { StartPlaceId = summary.RootPlaceId });
                    }

                    learned = true;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Lookup failed for universe {game.Key}: {ex.Message}");
                }
            }

            if (learned)
                Save();

            return learned;
        }

        private static void Save()
        {
            try
            {
                string json;
                lock (_lock)
                    json = JsonSerializer.Serialize(Places);

                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllText(CachePath, json);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Cache not saved: {ex.Message}");
            }
        }
    }
}
