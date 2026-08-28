namespace PhasmaStrap.Networking
{
    // resolves hostnames via DNS-over-HTTPS rather than the OS resolver. This is necessary
    // because the whole point of intercepting these specific hosts is that the OS hosts
    // file points them at 127.0.0.1 - the proxy still needs the REAL address to actually
    // forward requests to Roblox's servers.
    public static class DohResolver
    {
        private const string LOG_IDENT = "DohResolver";

        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static readonly Dictionary<string, (string Ip, DateTime Expiry)> Cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new();

        public static async Task<string?> ResolveAsync(string hostname, CancellationToken ct = default)
        {
            lock (Sync)
            {
                if (Cache.TryGetValue(hostname, out var cached) && cached.Expiry > DateTime.UtcNow)
                    return cached.Ip;
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(hostname)}&type=A");
                request.Headers.Add("Accept", "application/dns-json");

                using HttpResponseMessage response = await Client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                string body = await response.Content.ReadAsStringAsync(ct);
                using JsonDocument document = JsonDocument.Parse(body);

                if (document.RootElement.TryGetProperty("Answer", out JsonElement answers))
                {
                    foreach (JsonElement answer in answers.EnumerateArray())
                    {
                        // type 1 == A record
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

                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Resolution failed for {hostname}: {ex.Message}");
                return null;
            }
        }
    }
}
