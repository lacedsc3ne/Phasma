namespace PhasmaStrap.Networking
{
    public static class DohResolver
    {
        private const string LOG_IDENT = "DohResolver";

        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static readonly string[] Resolvers =
        {
            "https://cloudflare-dns.com/dns-query",
            "https://dns.google/resolve",
            "https://1.1.1.1/dns-query",
        };

        private static readonly Dictionary<string, (string Ip, DateTime Expiry)> Cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new();

        public static async Task<string?> ResolveAsync(string hostname, CancellationToken ct = default)
        {
            lock (Sync)
            {
                if (Cache.TryGetValue(hostname, out var cached) && cached.Expiry > DateTime.UtcNow)
                    return cached.Ip;
            }

            string? lastError = null;

            foreach (string resolver in Resolvers)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{resolver}?name={Uri.EscapeDataString(hostname)}&type=A");
                    request.Headers.Add("Accept", "application/dns-json");

                    using HttpResponseMessage response = await Client.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();

                    string body = await response.Content.ReadAsStringAsync(ct);
                    using JsonDocument document = JsonDocument.Parse(body);

                    if (!document.RootElement.TryGetProperty("Answer", out JsonElement answers))
                        continue;

                    foreach (JsonElement answer in answers.EnumerateArray())
                    {
                        if (answer.TryGetProperty("type", out JsonElement type) && type.GetInt32() == 1
                            && answer.TryGetProperty("data", out JsonElement data))
                        {
                            string? ip = data.GetString();
                            if (!string.IsNullOrEmpty(ip))
                            {
                                lock (Sync)
                                    Cache[hostname] = (ip, DateTime.UtcNow.AddMinutes(5));

                                return ip;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            lock (Sync)
            {
                if (Cache.TryGetValue(hostname, out var stale))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"All resolvers failed for {hostname} ({lastError}), using the cached address");
                    return stale.Ip;
                }
            }

            App.Logger.WriteLine(LOG_IDENT, $"Resolution failed for {hostname}: {lastError ?? "no A record"}");
            return null;
        }
    }
}
