namespace PhasmaStrap.Integrations
{
    // fetches a Roblox experience's public "created"/"updated" timestamps plus its badge-award
    // dates, then buckets those events by day of week to show which days it typically ships
    // updates on. Ported from Voidstrap's RobloxUpdateHeatmapService, which returned a full
    // per-calendar-day heatmap (keyed by "yyyy-MM-dd") meant to be rendered inside Voidstrap's
    // embedded in-game browser overlay. PhasmaStrap has no equivalent embedded browser surface,
    // so this is simplified down to a day-of-week frequency summary - the part of the data that's
    // actually useful as a standalone settings-page widget - and dropped the bounded-stream
    // reader / retry-with-backoff plumbing in favor of App.HttpClient, which already applies
    // sane timeouts app-wide.
    public sealed record RobloxUpdateHeatmapResult(
        bool Success,
        string? ErrorMessage,
        string? Created,
        string? Updated,
        IReadOnlyDictionary<DayOfWeek, int> DayCounts,
        int TotalEvents);

    public static class RobloxUpdateHeatmapService
    {
        private const string LOG_IDENT = "RobloxUpdateHeatmapService";
        private const int MaxBadgePages = 6;

        public static async Task<long?> ResolveUniverseIdAsync(long placeId, CancellationToken token = default)
        {
            try
            {
                string body = await GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", token);
                using JsonDocument doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("universeId", out JsonElement idEl) && idEl.TryGetInt64(out long universeId))
                    return universeId;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to resolve universe for place {placeId}: {ex.Message}");
            }

            return null;
        }

        public static async Task<RobloxUpdateHeatmapResult> GetAsync(long universeId, CancellationToken token = default)
        {
            Dictionary<DayOfWeek, int> dayCounts = new();
            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
                dayCounts[day] = 0;

            if (universeId <= 0)
                return new RobloxUpdateHeatmapResult(false, "A valid Roblox universe ID is required", null, null, dayCounts, 0);

            string? created = null;
            string? updated = null;
            int total = 0;
            bool fetched = false;
            string? lastError = null;

            try
            {
                string body = await GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", token);
                using JsonDocument doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                {
                    JsonElement game = data[0];
                    created = GetString(game, "created");
                    updated = GetString(game, "updated");
                    fetched = true;

                    if (TryParseDay(updated, out DayOfWeek dow))
                    {
                        dayCounts[dow]++;
                        total++;
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                App.Logger.WriteLine(LOG_IDENT, $"Game info request failed for universe {universeId}: {ex.Message}");
            }

            try
            {
                string cursor = "";
                for (int page = 0; page < MaxBadgePages; page++)
                {
                    string url = $"https://badges.roblox.com/v1/universes/{universeId}/badges?limit=100&sortOrder=Asc";
                    if (!string.IsNullOrWhiteSpace(cursor))
                        url += "&cursor=" + Uri.EscapeDataString(cursor);

                    string body = await GetStringAsync(url, token);
                    using JsonDocument doc = JsonDocument.Parse(body);

                    if (doc.RootElement.TryGetProperty("data", out JsonElement badges) && badges.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement badge in badges.EnumerateArray())
                        {
                            if (TryParseDay(GetString(badge, "created"), out DayOfWeek dow))
                            {
                                dayCounts[dow]++;
                                total++;
                            }
                        }
                        fetched = true;
                    }

                    cursor = GetString(doc.RootElement, "nextPageCursor");
                    if (string.IsNullOrWhiteSpace(cursor))
                        break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                App.Logger.WriteLine(LOG_IDENT, $"Badge list request failed for universe {universeId}: {ex.Message}");
            }

            if (!fetched)
                return new RobloxUpdateHeatmapResult(false, lastError ?? "Roblox update data is unavailable", created, updated, dayCounts, 0);

            App.Logger.WriteLine(LOG_IDENT, $"Collected {total} update event(s) for universe {universeId}");
            return new RobloxUpdateHeatmapResult(true, null, created, updated, dayCounts, total);
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            using HttpResponseMessage response = await App.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }

        private static bool TryParseDay(string? isoDate, out DayOfWeek day)
        {
            if (!string.IsNullOrWhiteSpace(isoDate) &&
                DateTime.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime date))
            {
                day = date.DayOfWeek;
                return true;
            }

            day = default;
            return false;
        }

        private static string GetString(JsonElement element, string name, string fallback = "")
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
        }
    }
}
