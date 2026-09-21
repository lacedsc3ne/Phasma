using System.Net;
using System.Text;

namespace PhasmaStrap.Integrations
{
    public enum FriendPresenceType
    {
        Offline = 0,
        Online = 1,
        InGame = 2,
        InStudio = 3,
    }

    public sealed class FriendInfo
    {
        public long UserId { get; init; }
        public string Username { get; init; } = "";
        public string DisplayName { get; init; } = "";
    }

    public sealed class FriendPresence
    {
        public long UserId { get; init; }
        public string Username { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public FriendPresenceType Type { get; init; }
        public string LastLocation { get; init; } = "";
        public long PlaceId { get; init; }
        public long RootPlaceId { get; init; }
        public long UniverseId { get; init; }
        public string GameId { get; init; } = "";
        public string? AvatarUrl { get; set; }

        public bool Joinable => Type == FriendPresenceType.InGame && RootPlaceId != 0 && !string.IsNullOrEmpty(GameId);
    }

    public static class FriendsService
    {
        private const string LOG_IDENT = "FriendsService";

        private static readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private static string? _csrfToken;

        public static async Task<List<FriendInfo>> GetFriendsAsync(long userId, CancellationToken ct = default)
        {
            var result = new List<FriendInfo>();

            try
            {
                using HttpRequestMessage req = BuildRequest(HttpMethod.Get, $"https://friends.roblox.com/v1/users/{userId}/friends", null);
                using HttpResponseMessage res = await _client.SendAsync(req, ct).ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return result;

                using JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));

                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (JsonElement el in data.EnumerateArray())
                {
                    long id = el.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long l) ? l : 0;
                    string name = el.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                    string displayName = el.TryGetProperty("displayName", out var dnEl) ? (dnEl.GetString() ?? "") : name;

                    if (id != 0)
                        result.Add(new FriendInfo { UserId = id, Username = name, DisplayName = displayName });
                }

                List<long> nameless = result.Where(f => string.IsNullOrEmpty(f.Username)).Select(f => f.UserId).ToList();
                if (nameless.Count > 0)
                {
                    Dictionary<long, (string Name, string DisplayName)> names = await GetUserNamesAsync(nameless, ct).ConfigureAwait(false);
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (names.TryGetValue(result[i].UserId, out var n))
                            result[i] = new FriendInfo { UserId = result[i].UserId, Username = n.Name, DisplayName = string.IsNullOrEmpty(n.DisplayName) ? n.Name : n.DisplayName };
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GetFriendsAsync failed: {ex.Message}");
            }

            return result;
        }

        public static async Task<Dictionary<long, (string Name, string DisplayName)>> GetUserNamesAsync(IEnumerable<long> userIds, CancellationToken ct = default)
        {
            var map = new Dictionary<long, (string, string)>();
            var ids = userIds.Distinct().ToList();

            try
            {
                for (int i = 0; i < ids.Count; i += 100)
                {
                    List<long> chunk = ids.GetRange(i, Math.Min(100, ids.Count - i));
                    using HttpRequestMessage req = BuildRequest(HttpMethod.Post, "https://users.roblox.com/v1/users", null);
                    req.Content = new StringContent(JsonSerializer.Serialize(new { userIds = chunk, excludeBannedUsers = false }), Encoding.UTF8, "application/json");

                    using HttpResponseMessage res = await _client.SendAsync(req, ct).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode)
                        continue;

                    using JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (JsonElement el in data.EnumerateArray())
                    {
                        long id = el.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out long l) ? l : 0;
                        string name = el.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                        string displayName = el.TryGetProperty("displayName", out var dnEl) ? (dnEl.GetString() ?? "") : "";
                        if (id != 0)
                            map[id] = (name, displayName);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GetUserNamesAsync failed: {ex.Message}");
            }

            return map;
        }

        public static async Task<Dictionary<long, FriendPresence>> GetPresenceAsync(IEnumerable<long> userIds, CancellationToken ct = default)
        {
            var result = new Dictionary<long, FriendPresence>();
            var ids = userIds.Distinct().ToList();

            if (ids.Count == 0)
                return result;

            string cookie = RobloxCookie.Get() ?? "";

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    string body = JsonSerializer.Serialize(new { userIds = ids });
                    using HttpRequestMessage req = BuildRequest(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users", cookie);
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(_csrfToken))
                        req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);

                    using HttpResponseMessage res = await _client.SendAsync(req, ct).ConfigureAwait(false);

                    if (res.StatusCode == HttpStatusCode.Forbidden && res.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? values))
                    {
                        _csrfToken = values.FirstOrDefault();
                        continue;
                    }

                    if (!res.IsSuccessStatusCode)
                        return result;

                    using JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));

                    if (!doc.RootElement.TryGetProperty("userPresences", out var arr) || arr.ValueKind != JsonValueKind.Array)
                        return result;

                    foreach (JsonElement el in arr.EnumerateArray())
                    {
                        long id = el.TryGetProperty("userId", out var idEl) && idEl.TryGetInt64(out long l) ? l : 0;
                        if (id == 0)
                            continue;

                        int typeVal = el.TryGetProperty("userPresenceType", out var tEl) && tEl.TryGetInt32(out int t) ? t : 0;

                        result[id] = new FriendPresence
                        {
                            UserId = id,
                            Type = Enum.IsDefined(typeof(FriendPresenceType), typeVal) ? (FriendPresenceType)typeVal : FriendPresenceType.Offline,
                            LastLocation = el.TryGetProperty("lastLocation", out var locEl) ? (locEl.GetString() ?? "") : "",
                            PlaceId = el.TryGetProperty("placeId", out var pEl) && pEl.ValueKind == JsonValueKind.Number ? pEl.GetInt64() : 0,
                            RootPlaceId = el.TryGetProperty("rootPlaceId", out var rpEl) && rpEl.ValueKind == JsonValueKind.Number ? rpEl.GetInt64() : 0,
                            UniverseId = el.TryGetProperty("universeId", out var uEl) && uEl.ValueKind == JsonValueKind.Number ? uEl.GetInt64() : 0,
                            GameId = el.TryGetProperty("gameId", out var gEl) ? (gEl.GetString() ?? "") : "",
                        };
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"GetPresenceAsync failed: {ex.Message}");
                    return result;
                }
            }

            return result;
        }

        public static async Task<Dictionary<long, string>> GetAvatarsAsync(IEnumerable<long> userIds, CancellationToken ct = default)
        {
            var map = new Dictionary<long, string>();
            var ids = userIds.Distinct().ToList();

            try
            {
                for (int i = 0; i < ids.Count; i += 100)
                {
                    List<long> chunk = ids.GetRange(i, Math.Min(100, ids.Count - i));
                    string url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={string.Join(",", chunk)}&size=150x150&format=Png&isCircular=false";

                    using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url, ct).ConfigureAwait(false));

                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement el in data.EnumerateArray())
                        {
                            long id = el.TryGetProperty("targetId", out var t) && t.TryGetInt64(out long l) ? l : 0;
                            string img = el.TryGetProperty("imageUrl", out var iu) ? (iu.GetString() ?? "") : "";

                            if (id != 0 && !string.IsNullOrEmpty(img))
                                map[id] = img;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GetAvatarsAsync failed: {ex.Message}");
            }

            return map;
        }

        public static string GetJoinDeeplink(FriendPresence presence) =>
            PhasmaStrap.Utility.RobloxLaunch.DeepLink(presence.RootPlaceId, presence.GameId);

        private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string? cookie)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("User-Agent", $"{App.ProjectName}/{App.Version}");

            string c = cookie ?? RobloxCookie.Get() ?? "";
            if (!string.IsNullOrEmpty(c))
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={c}");

            return req;
        }
    }
}
