using System.Net;
using System.Net.Http.Headers;

namespace PhasmaStrap.Integrations
{
    public sealed class PrivateServerInfo
    {
        public long Id { get; init; }
        public long UniverseId { get; init; }
        public long PlaceId { get; init; }
        public string Name { get; init; } = "";
        public string GameName { get; init; } = "";
        public string OwnerName { get; init; } = "";
        public long OwnerId { get; init; }
        public bool Owned { get; init; }
        public bool Active { get; init; }
        public DateTime? Expires { get; init; }
        public bool WillRenew { get; init; }
    }

    public static class PrivateServers
    {
        private const string LOG_IDENT = "PrivateServers";

        private static readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };

        private static string? _csrfToken;

        public sealed class NotSignedInException : Exception
        {
            public NotSignedInException() : base("Roblox isn't signed in on this PC.") { }
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? json, CancellationToken ct)
        {
            string cookie = RobloxCookie.Get() ?? "";
            if (cookie.Length == 0)
                throw new NotSignedInException();

            for (int attempt = 0; ; attempt++)
            {
                var request = new HttpRequestMessage(method, url);
                request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
                request.Headers.UserAgent.ParseAdd($"{App.ProjectName}/{App.Version}");

                if (json is not null)
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (method != HttpMethod.Get && _csrfToken is not null)
                    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);

                HttpResponseMessage response = await _client.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.Forbidden && attempt == 0 && response.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? tokens))
                {
                    _csrfToken = tokens.FirstOrDefault();
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    throw new NotSignedInException();
                }

                return response;
            }
        }

        private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, url, null, ct);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }

        private static long Long(JsonElement item, params string[] names)
        {
            foreach (string name in names)
            {
                if (item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result))
                    return result;
            }
            return 0;
        }

        private static string Text(JsonElement item, params string[] names)
        {
            foreach (string name in names)
            {
                if (item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";
            }
            return "";
        }

        private static bool Bool(JsonElement item, string name) =>
            item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

        public static async Task<List<PrivateServerInfo>> ListAsync(CancellationToken ct = default)
        {
            var result = new List<PrivateServerInfo>();
            bool loggedShape = false;

            foreach ((string tab, bool owned) in new[] { ("MyPrivateServers", true), ("OtherPrivateServers", false) })
            {
                string? cursor = null;

                do
                {
                    string url = $"https://games.roblox.com/v1/private-servers/my-private-servers?privateServersTab={tab}&itemsPerPage=100"
                        + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

                    using JsonDocument doc = await GetJsonAsync(url, ct);

                    if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in data.EnumerateArray())
                        {
                            if (!loggedShape)
                            {
                                App.Logger.WriteLine(LOG_IDENT, "Fields: " + string.Join(", ", item.EnumerateObject().Select(p => p.Name)));
                                loggedShape = true;
                            }

                            DateTime? expires = DateTime.TryParse(Text(item, "expirationDate"), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime e) ? e : null;

                            result.Add(new PrivateServerInfo
                            {
                                Id = Long(item, "privateServerId", "vipServerId", "id"),
                                UniverseId = Long(item, "universeId"),
                                PlaceId = Long(item, "placeId", "rootPlaceId"),
                                Name = Text(item, "name"),
                                GameName = Text(item, "universeName", "gameName"),
                                OwnerName = Text(item, "ownerName"),
                                OwnerId = Long(item, "ownerId"),
                                Owned = owned,
                                Active = !item.TryGetProperty("active", out _) || Bool(item, "active"),
                                Expires = expires,
                                WillRenew = Bool(item, "willRenew"),
                            });
                        }
                    }

                    cursor = doc.RootElement.TryGetProperty("nextPageCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                }
                while (!string.IsNullOrEmpty(cursor) && result.Count < 500);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Found {result.Count(s => s.Owned)} owned and {result.Count(s => !s.Owned)} shared private server(s)");
            return result;
        }

        public static async Task<string?> GetAccessCodeAsync(PrivateServerInfo server, CancellationToken ct = default)
        {
            string? cursor = null;

            for (int page = 0; page < 10; page++)
            {
                string url = $"https://games.roblox.com/v1/games/{server.PlaceId}/private-servers?limit=100"
                    + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

                using JsonDocument doc = await GetJsonAsync(url, ct);

                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in data.EnumerateArray())
                    {
                        if (Long(item, "vipServerId", "privateServerId", "id") == server.Id)
                        {
                            string code = Text(item, "accessCode");
                            return code.Length > 0 ? code : null;
                        }
                    }
                }

                cursor = doc.RootElement.TryGetProperty("nextPageCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                if (string.IsNullOrEmpty(cursor))
                    break;
            }

            return null;
        }

        public static async Task<bool> JoinAsync(PrivateServerInfo server, CancellationToken ct = default)
        {
            string? accessCode = await GetAccessCodeAsync(server, ct);
            if (accessCode is null)
                return false;

            App.Logger.WriteLine(LOG_IDENT, $"Joining private server {server.Id} in place {server.PlaceId}");
            Process.Start(Paths.Application, $"-player \"roblox://experiences/start?placeId={server.PlaceId}&accessCode={Uri.EscapeDataString(accessCode)}\"");
            return true;
        }

        private static string? ReadLink(JsonElement root)
        {
            string link = Text(root, "link");
            if (link.Length > 0)
                return link;

            string joinCode = Text(root, "joinCode");
            long placeId = root.TryGetProperty("game", out JsonElement game) && game.TryGetProperty("rootPlace", out JsonElement place) ? Long(place, "id") : 0;
            return joinCode.Length > 0 && placeId > 0 ? $"https://www.roblox.com/games/{placeId}?privateServerLinkCode={joinCode}" : null;
        }

        public static async Task<string?> GetLinkAsync(PrivateServerInfo server, CancellationToken ct = default)
        {
            using JsonDocument doc = await GetJsonAsync($"https://games.roblox.com/v1/vip-servers/{server.Id}", ct);
            return ReadLink(doc.RootElement);
        }

        public static async Task<string?> NewLinkAsync(PrivateServerInfo server, CancellationToken ct = default)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Patch, $"https://games.roblox.com/v1/vip-servers/{server.Id}", "{\"newJoinCode\":true}", ct);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            App.Logger.WriteLine(LOG_IDENT, $"New invite link made for private server {server.Id}");
            return ReadLink(doc.RootElement);
        }
    }
}
