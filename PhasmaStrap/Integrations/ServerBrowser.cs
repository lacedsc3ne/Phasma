using PhasmaStrap.Models;

namespace PhasmaStrap.Integrations
{
    // lists a game's public servers via Roblox's own unauthenticated server-list endpoint -
    // deliberately does not touch the user's Roblox login session or probe/join servers to
    // measure real ping, unlike Voidstrap's version, which piggybacks on the same session
    // cookie its separate account-manager feature uses. Scoped down for that reason: this
    // only ever reads what Roblox's public API already hands out for free.
    public static class ServerBrowser
    {
        private const string LOG_IDENT = "ServerBrowser";
        private const int MaxPages = 3;
        private const int MaxResponseBytes = 4 * 1024 * 1024;

        public static async Task<List<ServerListItem>> ListPublicServersAsync(long placeId, CancellationToken token = default)
        {
            var items = new List<ServerListItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? cursor = null;

            for (int page = 0; page < MaxPages; page++)
            {
                string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?excludeFullGames=false&limit=100&sortOrder=Desc";
                if (!string.IsNullOrEmpty(cursor))
                    url += "&cursor=" + Uri.EscapeDataString(cursor);

                HttpResponseMessage response;
                try
                {
                    response = await App.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Server list request failed: {ex.Message}");
                    break;
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Server list page {page + 1} returned HTTP {(int)response.StatusCode}");
                        break;
                    }

                    if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
                        break;

                    string body = await response.Content.ReadAsStringAsync(token);

                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(body);
                    }
                    catch (JsonException)
                    {
                        break;
                    }

                    using (doc)
                    {
                        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in data.EnumerateArray())
                            {
                                string? jobId = el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
                                if (string.IsNullOrEmpty(jobId) || !seen.Add(jobId))
                                    continue;

                                items.Add(new ServerListItem
                                {
                                    JobId = jobId,
                                    Playing = el.TryGetProperty("playing", out var p) && p.TryGetInt32(out int playing) ? playing : 0,
                                    MaxPlayers = el.TryGetProperty("maxPlayers", out var m) && m.TryGetInt32(out int max) ? max : 0,
                                    Ping = el.TryGetProperty("ping", out var pg) && pg.TryGetInt32(out int ping) ? ping : -1,
                                });
                            }
                        }

                        cursor = doc.RootElement.TryGetProperty("nextPageCursor", out var nextEl) && nextEl.ValueKind == JsonValueKind.String ? nextEl.GetString() : null;
                    }
                }

                if (string.IsNullOrEmpty(cursor))
                    break;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Found {items.Count} public server(s) for place {placeId}");
            return items.OrderByDescending(x => x.Playing).ToList();
        }

        public static void JoinServer(long placeId, string jobId)
        {
            string uri = $"roblox://experiences/start?placeId={placeId}&gameInstanceId={jobId}";
            Process.Start(Paths.Process, $"-player \"{uri}\"");
        }
    }
}
