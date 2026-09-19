namespace PhasmaStrap.Utility
{
    public sealed record GameInfo(long UniverseId, long RootPlaceId, string Name, string IconUrl = "");

    // Finding games for the per-game flag rules: which game a place belongs to (remembered on
    // disk, so a launch rarely has to ask Roblox), where a friend is playing (for "join a friend"
    // launches, whose link has no place in it), and searching games by name.
    public static class GameLookup
    {
        private const string LOG_IDENT = "GameLookup";

        private static readonly object _lock = new();
        private static Dictionary<long, long>? _places;

        private static string CachePath => Path.Combine(Paths.Base, "Cache", "PlaceUniverses.json");

        private static Dictionary<long, long> Places()
        {
            lock (_lock)
            {
                if (_places is not null)
                    return _places;

                _places = new Dictionary<long, long>();

                try
                {
                    if (File.Exists(CachePath))
                        _places = JsonSerializer.Deserialize<Dictionary<long, long>>(File.ReadAllText(CachePath)) ?? new();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not read the place cache: {ex.Message}");
                }

                return _places;
            }
        }

        public static void Remember(long placeId, long universeId)
        {
            if (placeId <= 0 || universeId <= 0)
                return;

            lock (_lock)
            {
                Dictionary<long, long> places = Places();
                if (places.TryGetValue(placeId, out long known) && known == universeId)
                    return;

                places[placeId] = universeId;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    File.WriteAllText(CachePath, JsonSerializer.Serialize(places));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not write the place cache: {ex.Message}");
                }
            }
        }

        // the game a place belongs to, from what is already known locally
        public static long? KnownUniverseOf(long placeId)
        {
            if (placeId <= 0)
                return null;

            lock (_lock)
            {
                if (Places().TryGetValue(placeId, out long universeId))
                    return universeId;
            }

            try
            {
                long fromHistory = Integrations.PlayTimeStore.GetAll().FirstOrDefault(e => e.PlaceId == placeId && e.UniverseId > 0)?.UniverseId ?? 0;
                if (fromHistory > 0)
                    return fromHistory;
            }
            catch
            {
            }

            return null;
        }

        public static async Task<long?> UniverseOfAsync(long placeId, TimeSpan timeout)
        {
            long? known = KnownUniverseOf(placeId);
            if (known is not null)
                return known;

            using var cts = new CancellationTokenSource(timeout);
            long? universeId = await Integrations.UniversePlaces.GetUniverseIdAsync(placeId, cts.Token);

            if (universeId is not null)
                Remember(placeId, universeId.Value);

            return universeId;
        }

        // where a user is playing right now, when their presence can be seen by the signed-in account
        public static async Task<(long PlaceId, long UniverseId)?> WhereIsUserAsync(long userId, TimeSpan timeout)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                Dictionary<long, Integrations.FriendPresence> presence = await Integrations.FriendsService.GetPresenceAsync(new[] { userId }, cts.Token);

                if (!presence.TryGetValue(userId, out Integrations.FriendPresence? info) || info.Type != Integrations.FriendPresenceType.InGame)
                    return null;

                long placeId = info.PlaceId > 0 ? info.PlaceId : info.RootPlaceId;
                if (placeId <= 0)
                    return null;

                if (info.UniverseId > 0)
                    Remember(placeId, info.UniverseId);

                return (placeId, info.UniverseId);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Presence lookup for user {userId} failed: {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------ search (settings page)

        public static async Task<List<GameInfo>> SearchAsync(string query, CancellationToken ct = default)
        {
            var result = new List<GameInfo>();

            try
            {
                string url = $"https://apis.roblox.com/search-api/omni-search?searchQuery={Uri.EscapeDataString(query)}&sessionId={Guid.NewGuid()}&pageType=all";
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url, ct));

                if (!doc.RootElement.TryGetProperty("searchResults", out JsonElement groups) || groups.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (JsonElement group in groups.EnumerateArray())
                {
                    if (!group.TryGetProperty("contentGroupType", out JsonElement type) || type.GetString() != "Game")
                        continue;

                    if (!group.TryGetProperty("contents", out JsonElement contents) || contents.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (JsonElement game in contents.EnumerateArray())
                    {
                        long universeId = game.TryGetProperty("universeId", out JsonElement u) && u.TryGetInt64(out long uid) ? uid : 0;
                        long rootPlaceId = game.TryGetProperty("rootPlaceId", out JsonElement r) && r.TryGetInt64(out long rid) ? rid : 0;
                        string name = game.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";

                        if (universeId > 0 && result.All(g => g.UniverseId != universeId))
                            result.Add(new GameInfo(universeId, rootPlaceId, name));

                        if (result.Count >= 12)
                            break;
                    }

                    if (result.Count >= 12)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Search for '{query}' failed: {ex.Message}");
            }

            return await WithIconsAsync(result, ct);
        }

        // a single game from a link or place ID
        public static async Task<GameInfo?> FromPlaceAsync(long placeId, CancellationToken ct = default)
        {
            long? universeId = await Integrations.UniversePlaces.GetUniverseIdAsync(placeId, ct);
            if (universeId is null)
                return null;

            Remember(placeId, universeId.Value);

            Integrations.UniverseSummary? summary = await Integrations.UniversePlaces.GetSummaryAsync(universeId.Value, ct);
            var game = new GameInfo(universeId.Value, summary?.RootPlaceId ?? placeId, summary?.Name ?? $"Game {universeId}");

            return (await WithIconsAsync(new List<GameInfo> { game }, ct)).FirstOrDefault();
        }

        public static async Task<List<GameInfo>> WithIconsAsync(List<GameInfo> games, CancellationToken ct = default)
        {
            List<long> missing = games.Where(g => string.IsNullOrEmpty(g.IconUrl)).Select(g => g.UniverseId).Distinct().ToList();
            if (missing.Count == 0)
                return games;

            var icons = new Dictionary<long, string>();

            try
            {
                string url = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={string.Join(",", missing.Take(100))}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false";
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url, ct));

                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in data.EnumerateArray())
                    {
                        long id = item.TryGetProperty("targetId", out JsonElement t) && t.TryGetInt64(out long tid) ? tid : 0;
                        string image = item.TryGetProperty("imageUrl", out JsonElement i) ? i.GetString() ?? "" : "";
                        if (id > 0 && image.Length > 0)
                            icons[id] = image;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Icon lookup failed: {ex.Message}");
            }

            return games.Select(g => icons.TryGetValue(g.UniverseId, out string? icon) && string.IsNullOrEmpty(g.IconUrl) ? g with { IconUrl = icon } : g).ToList();
        }
    }
}
