using System.Net.Http;

namespace PhasmaStrap.Utility
{
    public static class RobloxDefaultFlags
    {
        private const string LOG_IDENT = "RobloxDefaultFlags";
        private const string Endpoint = "https://clientsettingscdn.roblox.com/v2/settings/application/PCDesktopClient";

        private static Dictionary<string, string>? _cache;
        private static DateTime _fetchedUtc = DateTime.MinValue;

        public static async Task<Dictionary<string, string>?> GetAsync()
        {
            if (_cache is not null && DateTime.UtcNow - _fetchedUtc < TimeSpan.FromHours(1))
                return _cache;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox answered {(int)response.StatusCode}");
                    return null;
                }

                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                if (!document.RootElement.TryGetProperty("applicationSettings", out JsonElement settings))
                {
                    App.Logger.WriteLine(LOG_IDENT, "The answer had no applicationSettings");
                    return null;
                }

                var flags = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (JsonProperty entry in settings.EnumerateObject())
                    flags[entry.Name] = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? "" : entry.Value.ToString();

                _cache = flags;
                _fetchedUtc = DateTime.UtcNow;

                App.Logger.WriteLine(LOG_IDENT, $"Roblox is shipping {flags.Count} flag(s) right now");
                return flags;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read Roblox's flags: {ex.Message}");
                return null;
            }
        }

        public static List<FastFlagDiffEntry> CompareWithYours(Dictionary<string, string> defaults, Dictionary<string, object> yours)
        {
            var result = new List<FastFlagDiffEntry>();

            foreach (KeyValuePair<string, object> mine in yours.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                string value = mine.Value?.ToString() ?? "";

                if (!defaults.TryGetValue(mine.Key, out string? shipped))
                {
                    result.Add(new FastFlagDiffEntry { Key = mine.Key, OldValue = null, NewValue = value, ChangeType = "Added" });
                    continue;
                }

                if (!string.Equals(shipped, value, StringComparison.Ordinal))
                    result.Add(new FastFlagDiffEntry { Key = mine.Key, OldValue = shipped, NewValue = value, ChangeType = "Changed" });
            }

            return result;
        }
    }
}
