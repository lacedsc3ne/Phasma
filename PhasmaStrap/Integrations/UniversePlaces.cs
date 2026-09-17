namespace PhasmaStrap.Integrations
{
    public sealed record UniversePlace(long PlaceId, string Name, bool IsRootPlace);

    public sealed record UniverseSummary(long UniverseId, long RootPlaceId, string Name, string Creator);

    /// <summary>
    /// Looks up the experience a place belongs to and every place inside it (the "subplaces" -
    /// lobbies, maps, VIP areas) so Home's link launcher can offer them. Uses the signed-in
    /// account's cookie when there is one, which is what makes private/unlisted places show up.
    /// </summary>
    public static class UniversePlaces
    {
        private const string LOG_IDENT = "UniversePlaces";

        public static async Task<long?> GetUniverseIdAsync(long placeId, CancellationToken ct = default)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct));
                if (doc.RootElement.TryGetProperty("universeId", out JsonElement id) && id.TryGetInt64(out long universeId) && universeId > 0)
                    return universeId;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Universe lookup for place {placeId} failed: {ex.Message}");
            }

            return null;
        }

        public static async Task<UniverseSummary?> GetSummaryAsync(long universeId, CancellationToken ct = default)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", ct));
                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                {
                    JsonElement game = data[0];
                    long root = game.TryGetProperty("rootPlaceId", out JsonElement r) && r.TryGetInt64(out long rp) ? rp : 0;
                    string name = game.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                    string creator = game.TryGetProperty("creator", out JsonElement c) && c.TryGetProperty("name", out JsonElement cn) ? cn.GetString() ?? "" : "";
                    return new UniverseSummary(universeId, root, name, creator);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Summary lookup for universe {universeId} failed: {ex.Message}");
            }

            return null;
        }

        public static async Task<List<UniversePlace>> GetPlacesAsync(long universeId, long rootPlaceId, CancellationToken ct = default)
        {
            var places = new List<UniversePlace>();
            string? cursor = null;
            string? cookie = null;

            try { cookie = RobloxCookie.Get(); } catch (Exception) { }

            try
            {
                do
                {
                    string url = $"https://develop.roblox.com/v1/universes/{universeId}/places?limit=100&sortOrder=Asc" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrEmpty(cookie))
                        request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");

                    using HttpResponseMessage response = await App.HttpClient.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();

                    using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

                    if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in data.EnumerateArray())
                        {
                            long id = item.TryGetProperty("id", out JsonElement i) && i.TryGetInt64(out long pid) ? pid : 0;
                            string name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                            if (id > 0)
                                places.Add(new UniversePlace(id, name, id == rootPlaceId));
                        }
                    }

                    cursor = doc.RootElement.TryGetProperty("nextPageCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                }
                while (!string.IsNullOrEmpty(cursor) && places.Count < 500);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place listing for universe {universeId} failed: {ex.Message}");
            }

            // root place first, then the rest alphabetically
            return places.OrderByDescending(p => p.IsRootPlace).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
