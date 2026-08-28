using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Sockets;
using PhasmaStrap.Models;

namespace PhasmaStrap.Integrations
{
    // picks a better server to join than Roblox's own random assignment, by geolocating
    // yourself and a batch of candidate public servers (learning real observed ping over
    // time) and scoring them on estimated latency plus population. This is what actually
    // reads your live Roblox session cookie (via RobloxCookie) and probes Roblox's real
    // join-game-instance API - see PhasmaStrap.Integrations.RobloxCookie for why, and
    // Models/Persistable/Settings.cs for the knobs that bound how aggressive it is.
    // Ported from Voidstrap.
    public static class Matchmaker
    {
        private const string LOG_IDENT = "Matchmaker";

        public const int MinCandidateCount = 8;
        public const int MaxCandidateCount = 64;

        private const int FilteredCandidateCeiling = 80;
        private const int ProbeConcurrency = 8;
        private const int JoinTimeoutMs = 2500;
        private const int MaxJoinResponseBytes = 1048576;
        private const int MaxServerListPages = 5;
        private const int MaxIpLookupEntries = 1024;
        private const int MaxResolvedServerEntries = 2048;

        private const double EmptyPreferenceMs = 60.0;
        private const double FullnessTiebreakMs = 8.0;
        private const double ClosestDatacenterBandMs = 12.0;
        private const double HandoffPingMs = 120.0;
        private const double HandoffFloorMultiplier = 4.0;
        private const int EarlyExitMinResults = 12;
        private const int EarlyExitClosestMatches = 6;

        private static readonly TimeSpan OverallDeadline = TimeSpan.FromSeconds(25.0);
        private static readonly TimeSpan GeoCacheTtl = TimeSpan.FromHours(6.0);
        private static readonly TimeSpan IpLookupFailCooldown = TimeSpan.FromMinutes(10.0);
        private static readonly TimeSpan ResolvedServerCacheTtl = TimeSpan.FromMinutes(2.0);

        private static readonly HttpClient _geoClient = CreateClient("PhasmaStrap/1.0", TimeSpan.FromSeconds(8.0));
        private static readonly HttpClient _serverListClient = CreateClient("PhasmaStrap/1.0", TimeSpan.FromSeconds(10.0));
        private static readonly HttpClient _joinClient = CreateClient("Roblox/WinInet", TimeSpan.FromSeconds(8.0), allowAutoRedirect: false);

        private static readonly object _geoLock = new();
        private static readonly SemaphoreSlim _geoRefreshLock = new(1, 1);
        private static UserGeo? _cachedGeo;
        private static DateTime _cachedGeoUtc = DateTime.MinValue;

        private static readonly Dictionary<string, RobloxDatacenter> _ipLookupCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> _ipLookupOrder = new();
        private static readonly Dictionary<string, DateTime> _ipLookupFailUtc = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _ipLookupLock = new(6, 6);

        private static readonly Dictionary<string, (string Ip, int Port, DateTime ResolvedUtc)> _resolvedServerCache = new(StringComparer.OrdinalIgnoreCase);

        private static long _joinBackoffUntilTicks;
        private static string? _csrfToken;
        private static readonly SemaphoreSlim _csrfLock = new(1, 1);

        private static readonly Dictionary<string, string> _countryNormalizationMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "US", "USA" }, { "USA", "USA" }, { "United States", "USA" }, { "United States of America", "USA" },
            { "GB", "UK" }, { "UK", "UK" }, { "United Kingdom", "UK" }, { "Great Britain", "UK" }, { "England", "UK" },
            { "NL", "Netherlands" }, { "Netherlands", "Netherlands" }, { "Holland", "Netherlands" },
            { "FR", "France" }, { "France", "France" },
            { "DE", "Germany" }, { "Germany", "Germany" },
            { "PL", "Poland" }, { "Poland", "Poland" },
            { "IN", "India" }, { "India", "India" },
            { "JP", "Japan" }, { "Japan", "Japan" },
            { "SG", "Singapore" }, { "Singapore", "Singapore" },
            { "AU", "Australia" }, { "Australia", "Australia" },
            { "CN", "China" }, { "China", "China" }, { "HK", "China" }, { "Hong Kong", "China" },
            { "CA", "Canada" }, { "Canada", "Canada" },
            { "BR", "Brazil" }, { "Brazil", "Brazil" },
            { "KR", "South Korea" }, { "South Korea", "South Korea" }, { "Korea", "South Korea" },
            { "TW", "Taiwan" }, { "Taiwan", "Taiwan" },
            { "ZA", "South Africa" }, { "South Africa", "South Africa" },
            { "AE", "UAE" }, { "United Arab Emirates", "UAE" },
            { "RU", "Russia" }, { "Russia", "Russia" },
            { "MX", "Mexico" }, { "Mexico", "Mexico" },
            { "CL", "Chile" }, { "Chile", "Chile" },
            { "AR", "Argentina" }, { "Argentina", "Argentina" },
        };

        private static HttpClient CreateClient(string userAgent, TimeSpan timeout, bool allowAutoRedirect = true)
        {
            var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = allowAutoRedirect };
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public static int EstimatePingMs(double distanceKm)
        {
            if (double.IsNaN(distanceKm) || distanceKm < 0.0)
                return -1;
            return Math.Clamp((int)Math.Round(5.0 + distanceKm / 75.0), 1, 999);
        }

        private static double EstimateRttMs(double distanceKm)
        {
            if (double.IsNaN(distanceKm) || distanceKm < 0.0)
                return 999.0;
            return 5.0 + distanceKm / 75.0;
        }

        public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
            return 6371.0 * (2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a)));
        }

        private static double ToRad(double d) => d * Math.PI / 180.0;

        public static string NormalizeCountryCode(string? country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return country ?? "";
            return _countryNormalizationMap.TryGetValue(country.Trim(), out string? mapped) ? mapped : country;
        }

        public static string DatacenterKey(RobloxDatacenter? dc) => dc is null ? "" : $"{dc.City}|{NormalizeCountryCode(dc.Country)}";

        public static bool MatchesPreferredDc(RobloxDatacenter? dc, string? preferredKey)
        {
            if (dc is null || string.IsNullOrWhiteSpace(preferredKey))
                return false;

            int sep = preferredKey.IndexOf('|');
            string city = sep < 0 ? preferredKey : preferredKey[..sep];
            string country = sep < 0 ? "" : preferredKey[(sep + 1)..];

            if (!string.Equals(dc.City, city, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(dc.Country))
                return true;

            return string.Equals(NormalizeCountryCode(country), NormalizeCountryCode(dc.Country), StringComparison.OrdinalIgnoreCase);
        }

        public static HashSet<string> GetBlockedDatacenters()
        {
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in App.Settings.Prop.MatchmakerDisabledDatacenters)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    blocked.Add(key.Trim());
            }
            return blocked;
        }

        public static int ResolveEffectiveCandidateCount()
        {
            if (!App.Settings.Prop.MatchmakerAutoCandidates)
                return Math.Clamp(App.Settings.Prop.MatchmakerMaxCandidates, MinCandidateCount, MaxCandidateCount);

            int blocked = GetBlockedDatacenters().Count;
            return Math.Clamp(40 + blocked * 4, 40, MaxCandidateCount);
        }

        public static async Task<UserGeo?> GetUserGeoAsync(CancellationToken token = default)
        {
            lock (_geoLock)
            {
                if (_cachedGeo != null && DateTime.UtcNow - _cachedGeoUtc < GeoCacheTtl)
                    return _cachedGeo;
            }

            await _geoRefreshLock.WaitAsync(token);
            try
            {
                lock (_geoLock)
                {
                    if (_cachedGeo != null && DateTime.UtcNow - _cachedGeoUtc < GeoCacheTtl)
                        return _cachedGeo;
                }

                UserGeo? result = await TryGeoProviderAsync("https://ipinfo.io/json", ParseIpInfo, token)
                    ?? await TryGeoProviderAsync("https://ipwho.is/", ParseIpWhoIs, token)
                    ?? await TryGeoProviderAsync("https://ipapi.co/json/", ParseIpApiCo, token);

                if (result is null || !IsValidCoordinate(result.Lat, result.Lon))
                {
                    App.Logger.WriteLine(LOG_IDENT, "All geo providers failed, cannot match by location");
                    return null;
                }

                lock (_geoLock)
                {
                    _cachedGeo = result;
                    _cachedGeoUtc = DateTime.UtcNow;
                }

                App.Logger.WriteLine(LOG_IDENT, $"User geo: {result.City}, {result.Region}, {result.Country} ({result.Lat:F2}, {result.Lon:F2})");
                return result;
            }
            finally
            {
                _geoRefreshLock.Release();
            }
        }

        private static bool IsValidCoordinate(double lat, double lon) =>
            double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90.0 and <= 90.0 && lon is >= -180.0 and <= 180.0;

        private static async Task<string> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken token)
        {
            if (content.Headers.ContentLength is long length && length > maxBytes)
                throw new InvalidOperationException("Response too large");

            byte[] bytes = await content.ReadAsByteArrayAsync(token);
            if (bytes.Length > maxBytes)
                throw new InvalidOperationException("Response too large");

            return Encoding.UTF8.GetString(bytes);
        }

        private static async Task<UserGeo?> TryGeoProviderAsync(string url, Func<JsonElement, UserGeo?> parser, CancellationToken token)
        {
            try
            {
                using HttpResponseMessage response = await _geoClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                using JsonDocument doc = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 262144, token));
                return parser(doc.RootElement);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Geo provider failed ({url}): {ex.Message}");
                return null;
            }
        }

        private static UserGeo? ParseIpInfo(JsonElement root)
        {
            if (!root.TryGetProperty("loc", out JsonElement locEl))
                return null;

            string[] parts = (locEl.GetString() ?? "").Split(',');
            if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                return null;

            return new UserGeo { Lat = lat, Lon = lon, City = ReadString(root, "city"), Region = ReadString(root, "region"), Country = ReadString(root, "country") };
        }

        private static UserGeo? ParseIpWhoIs(JsonElement root)
        {
            if (root.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
                return null;
            if (!TryReadDouble(root, "latitude", out double lat) || !TryReadDouble(root, "longitude", out double lon))
                return null;

            return new UserGeo { Lat = lat, Lon = lon, City = ReadString(root, "city"), Region = ReadString(root, "region"), Country = ReadString(root, "country_code") };
        }

        private static UserGeo? ParseIpApiCo(JsonElement root)
        {
            if (root.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.True)
                return null;
            if (!TryReadDouble(root, "latitude", out double lat) || !TryReadDouble(root, "longitude", out double lon))
                return null;

            return new UserGeo { Lat = lat, Lon = lon, City = ReadString(root, "city"), Region = ReadString(root, "region"), Country = ReadString(root, "country") };
        }

        private static string ReadString(JsonElement root, string prop) =>
            root.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

        private static bool TryReadDouble(JsonElement root, string prop, out double value)
        {
            value = 0.0;
            return root.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value);
        }

        public static async Task<RobloxDatacenter?> LookupUnknownIpAsync(string? ip, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ip) || IsPrivateIp(ip))
                return null;

            RobloxDatacenter? mapped = RobloxDatacenterMap.Map(ip);
            if (mapped != null)
                return mapped;

            LearnedServerEntry? learned = ServerFetchStore.Lookup(ip);
            if (learned != null && (learned.Lat != 0.0 || learned.Lon != 0.0))
                return new RobloxDatacenter { City = learned.City, Region = learned.Region, Country = NormalizeCountryCode(learned.Country), Lat = learned.Lat, Lon = learned.Lon };

            lock (_ipLookupCache)
            {
                if (_ipLookupCache.TryGetValue(ip, out RobloxDatacenter? cached))
                    return cached;
                if (_ipLookupFailUtc.TryGetValue(ip, out DateTime failedUtc) && DateTime.UtcNow - failedUtc < IpLookupFailCooldown)
                    return null;
            }

            UserGeo? geo;
            await _ipLookupLock.WaitAsync(token);
            try
            {
                lock (_ipLookupCache)
                {
                    if (_ipLookupCache.TryGetValue(ip, out RobloxDatacenter? raced))
                        return raced;
                }

                geo = await TryGeoProviderAsync($"https://ipinfo.io/{ip}/json", ParseIpInfo, token)
                    ?? await TryGeoProviderAsync($"https://ipwho.is/{ip}", ParseIpWhoIs, token)
                    ?? await TryGeoProviderAsync($"https://ipapi.co/{ip}/json/", ParseIpApiCo, token);
            }
            finally
            {
                _ipLookupLock.Release();
            }

            if (geo is null)
            {
                lock (_ipLookupCache)
                    _ipLookupFailUtc[ip] = DateTime.UtcNow;
                return null;
            }

            var dc = new RobloxDatacenter { City = geo.City, Region = geo.Region, Country = NormalizeCountryCode(geo.Country), Lat = geo.Lat, Lon = geo.Lon };

            lock (_ipLookupCache)
            {
                if (!_ipLookupCache.ContainsKey(ip))
                    _ipLookupOrder.Enqueue(ip);
                _ipLookupCache[ip] = dc;
                while (_ipLookupCache.Count > MaxIpLookupEntries && _ipLookupOrder.TryDequeue(out string? oldest))
                    _ipLookupCache.Remove(oldest);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Resolved unknown IP {ip}: {dc.City}, {dc.Country} ({dc.Lat:F2}, {dc.Lon:F2})");
            return dc;
        }

        public static double NearestDatacenterKm(UserGeo geo)
        {
            double best = double.PositiveInfinity;
            foreach (RobloxDatacenter dc in RobloxDatacenterMap.AllDatacenters())
            {
                if (dc.Lat == 0.0 && dc.Lon == 0.0)
                    continue;
                double km = HaversineKm(geo.Lat, geo.Lon, dc.Lat, dc.Lon);
                if (km < best)
                    best = km;
            }
            return double.IsPositiveInfinity(best) ? 0.0 : best;
        }

        public static async Task<MatchmakerCandidate?> PickBestJobIdAsync(long placeId, IEnumerable<string>? exclude = null, int maxCandidates = 40, CancellationToken token = default)
        {
            try
            {
                return await PickBestCoreAsync(placeId, exclude, maxCandidates, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Matchmaking hit the {OverallDeadline.TotalSeconds:F0}s deadline, letting Roblox pick the server");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Matchmaking failed: {ex.Message}");
                return null;
            }
        }

        private static async Task<MatchmakerCandidate?> PickBestCoreAsync(long placeId, IEnumerable<string>? exclude, int maxCandidates, CancellationToken token)
        {
            var stageClock = System.Diagnostics.Stopwatch.StartNew();
            using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadlineCts.CancelAfter(OverallDeadline);
            CancellationToken outerToken = token;
            token = deadlineCts.Token;

            string? cookie = RobloxCookie.Get();
            if (string.IsNullOrEmpty(cookie))
            {
                App.Logger.WriteLine(LOG_IDENT, "No Roblox cookie found, sign in through Roblox/Studio once to use the matchmaker");
                return null;
            }

            string preferred = (App.Settings.Prop.MatchmakerPreferredDatacenter ?? "").Trim();
            HashSet<string> blocked = GetBlockedDatacenters();
            bool filtering = preferred.Length > 0 || blocked.Count > 0;
            int probeBudget = Math.Clamp(maxCandidates, MinCandidateCount, MaxCandidateCount);
            if (filtering)
                probeBudget = Math.Min(FilteredCandidateCeiling, probeBudget * 2);

            var excludeSet = exclude is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
            bool preferEmpty = App.Settings.Prop.MatchmakerPreferEmpty;

            Task<UserGeo?> geoTask = GetUserGeoAsync(token);
            Task<List<ServerListItem>> poolTask = ListPublicServersAsync(placeId, cookie, MaxServerListPages, preferEmpty, token);
            Task csrfTask = PrimeCsrfAsync(placeId, cookie, token);

            try
            {
                await Task.WhenAll(geoTask, poolTask, csrfTask);
            }
            catch (OperationCanceledException) when (outerToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Initial server search timed out");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Initial server search failed: {ex.Message}");
                return null;
            }

            UserGeo? geo = await geoTask;
            if (geo is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Cannot match without user location");
                return null;
            }

            List<ServerListItem> pool = (await poolTask).Where(x => !excludeSet.Contains(x.JobId)).ToList();
            if (pool.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "No untried public servers available for this place");
                return null;
            }

            List<ServerListItem> probeList = pool.Count > probeBudget ? Stratify(pool, probeBudget) : pool;
            App.Logger.WriteLine(LOG_IDENT, $"Server list ready in {stageClock.ElapsedMilliseconds}ms, probing {probeList.Count} of {pool.Count} servers for place {placeId}");
            long listReadyMs = stageClock.ElapsedMilliseconds;

            double nearestDcKm = NearestDatacenterKm(geo);
            double floorMs = EstimateRttMs(nearestDcKm);

            List<MatchmakerCandidate> probed;
            try
            {
                probed = await ProbeAsync(probeList, cookie, geo, preferred, blocked, preferEmpty, floorMs, token);
            }
            catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Matchmaking hit the {OverallDeadline.TotalSeconds:F0}s deadline while probing");
                return null;
            }

            if (probed.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "No probed server could be resolved to a datacenter");
                return null;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Probed {probed.Count} of {probeList.Count} servers in {stageClock.ElapsedMilliseconds - listReadyMs}ms, {stageClock.ElapsedMilliseconds}ms total");

            MatchmakerCandidate? closestOverall = probed.OrderBy(c => c.DistanceKm).FirstOrDefault();
            List<MatchmakerCandidate> allowed = probed.Where(c => !blocked.Contains(DatacenterKey(c.Datacenter))).ToList();
            if (allowed.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Every probed server was in a blocked datacenter, nothing to pick");
                return null;
            }

            string? blockedClosestCity = null;
            double blockedClosestKm = 0.0;
            if (closestOverall?.Datacenter != null && blocked.Contains(DatacenterKey(closestOverall.Datacenter)))
            {
                blockedClosestCity = closestOverall.Datacenter.City;
                blockedClosestKm = closestOverall.DistanceKm;
            }

            if (preferred.Length > 0)
            {
                List<MatchmakerCandidate> inPreferred = allowed.Where(c => MatchesPreferredDc(c.Datacenter, preferred)).ToList();
                if (inPreferred.Count > 0)
                    allowed = inPreferred;
            }
            else
            {
                double closestRtt = allowed.Min(c => (double)c.EstimatedPingMs);
                List<MatchmakerCandidate> close = allowed.Where(c => c.EstimatedPingMs <= closestRtt + ClosestDatacenterBandMs).ToList();
                if (close.Count > 0)
                    allowed = close;

                if (!preferEmpty)
                {
                    List<MatchmakerCandidate> active = allowed.Where(c => c.Playing >= 4).ToList();
                    if (active.Count > 0)
                        allowed = active;

                    List<MatchmakerCandidate> headroom = allowed.Where(HasSafeJoinHeadroom).ToList();
                    if (headroom.Count > 0)
                        allowed = headroom;
                }
            }

            MatchmakerCandidate winner = allowed.OrderBy(c => c.Score).First();
            winner = new MatchmakerCandidate
            {
                JobId = winner.JobId,
                MachineAddress = winner.MachineAddress,
                Port = winner.Port,
                Datacenter = winner.Datacenter,
                DistanceKm = winner.DistanceKm,
                Playing = winner.Playing,
                MaxPlayers = winner.MaxPlayers,
                Ping = winner.Ping,
                EstimatedPingMs = winner.EstimatedPingMs,
                Score = winner.Score,
                BlockedClosestCity = blockedClosestCity,
                BlockedClosestDistanceKm = blockedClosestKm
            };

            string players = winner.MaxPlayers > 0 ? $"{winner.Playing}/{winner.MaxPlayers} players" : "player count unknown";
            App.Logger.WriteLine(LOG_IDENT, $"Winner: {winner.DatacenterName}, about {winner.EstimatedPingMs}ms, {players}, JobId {winner.JobId}");

            bool winnerIsPreferred = preferred.Length > 0 && MatchesPreferredDc(winner.Datacenter, preferred);
            if (ShouldHandOff(winner.EstimatedPingMs, floorMs, winnerIsPreferred))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Every server found is far away (best is about {winner.EstimatedPingMs}ms), handing off to Roblox matchmaking");
                return null;
            }

            return winner;
        }

        internal static bool ShouldHandOff(double winnerPingMs, double floorMs, bool winnerIsPreferred)
        {
            if (winnerIsPreferred)
                return false;
            return winnerPingMs > HandoffPingMs && winnerPingMs > floorMs * HandoffFloorMultiplier;
        }

        private static bool HasSafeJoinHeadroom(MatchmakerCandidate candidate)
        {
            if (candidate.MaxPlayers <= 0)
                return true;
            int open = candidate.MaxPlayers - candidate.Playing;
            return open >= Math.Max(2, (int)Math.Ceiling(candidate.MaxPlayers * 0.08));
        }

        private static List<ServerListItem> Stratify(List<ServerListItem> items, int target)
        {
            var picked = new List<ServerListItem>(target);
            double step = (double)items.Count / target;
            for (int i = 0; i < target; i++)
                picked.Add(items[Math.Min(items.Count - 1, (int)Math.Floor(i * step))]);
            return picked;
        }

        private static double PopulationPenaltyMs(int playing, int maxPlayers, bool preferEmpty)
        {
            double fullness = maxPlayers > 0 ? Math.Clamp((double)playing / maxPlayers, 0.0, 1.0) : 0.5;

            if (preferEmpty)
                return fullness * EmptyPreferenceMs + JoinHeadroomPenaltyMs(playing, maxPlayers);

            double sparsePenalty = playing switch
            {
                <= 0 => 100.0,
                1 => 80.0,
                2 => 60.0,
                3 => 45.0,
                _ => fullness < 0.15 ? (0.15 - fullness) * 80.0 : 0.0
            };
            double crowdedPenalty = fullness > 0.85 ? (fullness - 0.85) * 120.0 : 0.0;
            double fullnessTiebreak = Math.Abs(fullness - 0.65) * FullnessTiebreakMs;
            return sparsePenalty + crowdedPenalty + fullnessTiebreak + JoinHeadroomPenaltyMs(playing, maxPlayers);
        }

        private static double JoinHeadroomPenaltyMs(int playing, int maxPlayers)
        {
            if (maxPlayers <= 0)
                return 0.0;
            int open = maxPlayers - playing;
            return open switch { <= 1 => 120.0, 2 => 35.0, _ => 0.0 };
        }

        private static async Task<List<MatchmakerCandidate>> ProbeAsync(List<ServerListItem> servers, string cookie, UserGeo geo, string preferred, HashSet<string> blocked, bool preferEmpty, double floorMs, CancellationToken token)
        {
            var results = new List<MatchmakerCandidate>();
            var resultsLock = new object();
            int goodEnough = 0;
            int resultCount = 0;

            using var earlyCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationToken localToken = earlyCts.Token;
            var queue = new ConcurrentQueue<ServerListItem>(servers);

            async Task WorkerAsync()
            {
                while (!localToken.IsCancellationRequested && queue.TryDequeue(out ServerListItem? sv))
                {
                    (string Ip, int Port)? resolved;
                    try
                    {
                        resolved = await ResolveServerAsync(sv, cookie, localToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (!resolved.HasValue)
                        continue;

                    RobloxDatacenter? dc;
                    try
                    {
                        dc = await LookupUnknownIpAsync(resolved.Value.Ip, localToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (dc is null)
                    {
                        ServerFetchStore.RecordSighting(resolved.Value.Ip);
                        continue;
                    }

                    string normalized = NormalizeCountryCode(dc.Country);
                    if (!string.Equals(normalized, dc.Country, StringComparison.Ordinal))
                        dc = new RobloxDatacenter { City = dc.City, Region = dc.Region, Country = normalized, Lat = dc.Lat, Lon = dc.Lon };

                    ServerFetchStore.RecordSighting(resolved.Value.Ip, dc.City, dc.Region, dc.Country, dc.Lat, dc.Lon);

                    double km = HaversineKm(geo.Lat, geo.Lon, dc.Lat, dc.Lon);
                    double geographicPing = EstimateRttMs(km);
                    double learnedPing = ServerFetchStore.GetMedianPing(resolved.Value.Ip, out int learnedSamples);
                    double learnedWeight = learnedSamples <= 0 || learnedPing < 1.0 || learnedPing > 999.0 ? 0.0 : Math.Min(0.75, 0.2 + learnedSamples * 0.05);
                    double effectivePing = learnedWeight > 0.0 ? geographicPing * (1.0 - learnedWeight) + learnedPing * learnedWeight : geographicPing;

                    var candidate = new MatchmakerCandidate
                    {
                        JobId = sv.JobId,
                        MachineAddress = resolved.Value.Ip,
                        Port = resolved.Value.Port,
                        Datacenter = dc,
                        DistanceKm = km,
                        Playing = sv.Playing,
                        MaxPlayers = sv.MaxPlayers,
                        Ping = sv.Ping,
                        EstimatedPingMs = Math.Clamp((int)Math.Round(effectivePing), 1, 999),
                        Score = effectivePing + PopulationPenaltyMs(sv.Playing, sv.MaxPlayers, preferEmpty)
                    };

                    lock (resultsLock)
                        results.Add(candidate);
                    Interlocked.Increment(ref resultCount);

                    bool usable = !blocked.Contains(DatacenterKey(dc));
                    bool onTarget = preferred.Length > 0 ? MatchesPreferredDc(dc, preferred) : EstimateRttMs(km) <= floorMs + ClosestDatacenterBandMs;
                    bool populated = preferEmpty || sv.Playing >= 4 || (sv.MaxPlayers > 0 && sv.Playing >= Math.Ceiling(sv.MaxPlayers * 0.15));
                    int requiredMatches = preferred.Length > 0 ? 3 : EarlyExitClosestMatches;

                    if (usable && onTarget && populated && Interlocked.Increment(ref goodEnough) >= requiredMatches && Volatile.Read(ref resultCount) >= EarlyExitMinResults)
                    {
                        earlyCts.Cancel();
                        return;
                    }
                }
            }

            Task[] workers = Enumerable.Range(0, Math.Min(ProbeConcurrency, Math.Max(1, servers.Count))).Select(_ => WorkerAsync()).ToArray();
            try
            {
                await Task.WhenAll(workers);
            }
            catch (OperationCanceledException) when (earlyCts.IsCancellationRequested && !token.IsCancellationRequested)
            {
            }

            return results;
        }

        private static async Task PrimeCsrfAsync(long placeId, string cookie, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(_csrfToken))
                return;

            await _csrfLock.WaitAsync(token);
            try
            {
                if (!string.IsNullOrEmpty(_csrfToken))
                    return;

                using HttpRequestMessage req = BuildJoinRequest(placeId, Guid.Empty.ToString(), cookie, null);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(JoinTimeoutMs);
                using HttpResponseMessage res = await _joinClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                if (res.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? values))
                    _csrfToken = values.FirstOrDefault();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"CSRF prime failed, probes will fetch it on demand: {ex.Message}");
            }
            finally
            {
                _csrfLock.Release();
            }
        }

        private static HttpRequestMessage BuildJoinRequest(long placeId, string jobId, string cookie, string? csrf)
        {
            const string url = "https://gamejoin.roblox.com/v1/join-game-instance";
            string body = JsonSerializer.Serialize(new { placeId, gameId = jobId, gameJoinAttemptId = Guid.NewGuid().ToString() });

            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            req.Headers.Referrer = new Uri("https://www.roblox.com/");
            if (!string.IsNullOrEmpty(csrf))
                req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return req;
        }

        private static async Task WaitForJoinBackoffAsync(CancellationToken token)
        {
            long until = Interlocked.Read(ref _joinBackoffUntilTicks);
            long now = DateTime.UtcNow.Ticks;
            if (until > now)
                await Task.Delay(TimeSpan.FromTicks(until - now) + TimeSpan.FromMilliseconds(Random.Shared.Next(25, 201)), token);
        }

        private static void SetJoinBackoff(TimeSpan duration)
        {
            if (duration > TimeSpan.FromSeconds(8.0))
                duration = TimeSpan.FromSeconds(8.0);

            long until = DateTime.UtcNow.Ticks + duration.Ticks;
            while (true)
            {
                long existing = Interlocked.Read(ref _joinBackoffUntilTicks);
                if (until <= existing || Interlocked.CompareExchange(ref _joinBackoffUntilTicks, until, existing) == existing)
                    return;
            }
        }

        private static async Task<(string Ip, int Port)?> ResolveServerAsync(ServerListItem server, string cookie, CancellationToken token)
        {
            string cacheKey = server.JobId;

            lock (_resolvedServerCache)
            {
                if (_resolvedServerCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.ResolvedUtc < ResolvedServerCacheTtl)
                    return (cached.Ip, cached.Port);
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await WaitForJoinBackoffAsync(token);
                    using HttpRequestMessage req = BuildJoinRequest(0, server.JobId, cookie, _csrfToken);
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeoutCts.CancelAfter(JoinTimeoutMs);
                    using HttpResponseMessage res = await _joinClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                    if (res.StatusCode == HttpStatusCode.Forbidden && res.Headers.TryGetValues("x-csrf-token", out IEnumerable<string>? values))
                    {
                        _csrfToken = values.FirstOrDefault();
                        continue;
                    }

                    if (res.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        SetJoinBackoff(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5));
                        return null;
                    }

                    if (!res.IsSuccessStatusCode)
                        return null;

                    string payload = await ReadBoundedAsync(res.Content, MaxJoinResponseBytes, timeoutCts.Token);
                    if (string.IsNullOrWhiteSpace(payload))
                        return null;

                    using JsonDocument doc = JsonDocument.Parse(payload);
                    if (!HasJoinScript(doc.RootElement))
                        return null;

                    (string ip, int port) = ParseJoinResponse(doc.RootElement);
                    if (string.IsNullOrEmpty(ip) || IsPrivateIp(ip))
                        return null;

                    lock (_resolvedServerCache)
                        _resolvedServerCache[cacheKey] = (ip, port, DateTime.UtcNow);

                    return (ip, port);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Probe of {server.JobId} failed: {ex.Message}");
                    return null;
                }
            }

            return null;
        }

        private static bool HasJoinScript(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (root.TryGetProperty("joinScript", out var js) && js.ValueKind == JsonValueKind.Object)
                return true;
            return (root.TryGetProperty("UdmuxEndpoints", out var ue) && ue.ValueKind == JsonValueKind.Array) || !string.IsNullOrEmpty(TryGetString(root, "MachineAddress"));
        }

        private static (string Ip, int Port) ParseJoinResponse(JsonElement root)
        {
            string? ip = null;
            int port = 0;

            JsonElement? joinScript = root.TryGetProperty("joinScript", out var js) && js.ValueKind == JsonValueKind.Object ? js : null;
            JsonElement? endpoints = root.TryGetProperty("UdmuxEndpoints", out var ue) && ue.ValueKind == JsonValueKind.Array ? ue
                : (joinScript.HasValue && joinScript.Value.TryGetProperty("UdmuxEndpoints", out var ue2) && ue2.ValueKind == JsonValueKind.Array ? ue2 : null);

            if (endpoints.HasValue && endpoints.Value.GetArrayLength() > 0)
            {
                JsonElement first = endpoints.Value[0];
                if (first.ValueKind == JsonValueKind.Object)
                {
                    ip = TryGetString(first, "Address");
                    int? p = TryGetInt(first, "Port");
                    if (p.HasValue && p.Value > 0)
                        port = p.Value;
                }
            }

            if (string.IsNullOrEmpty(ip))
            {
                ip = TryGetString(root, "MachineAddress");
                if (string.IsNullOrEmpty(ip) && joinScript.HasValue)
                    ip = TryGetString(joinScript.Value, "MachineAddress");
            }

            if (port == 0)
            {
                int? p = TryGetInt(root, "ServerPort") ?? (joinScript.HasValue ? TryGetInt(joinScript.Value, "ServerPort") : null);
                if (p.HasValue)
                    port = p.Value;
            }

            return (ip ?? "", port);
        }

        private static string? TryGetString(JsonElement el, string prop) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? TryGetInt(JsonElement el, string prop) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;

        private static async Task<List<ServerListItem>> ListPublicServersAsync(long placeId, string cookie, int maxPages, bool preferEmpty, CancellationToken token)
        {
            var items = new List<ServerListItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? cursor = null;
            int[] backoffMs = { 0, 750, 2000 };

            for (int page = 0; page < maxPages; page++)
            {
                string sortOrder = preferEmpty ? "Asc" : "Desc";
                string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?excludeFullGames=true&limit=100&sortOrder={sortOrder}";
                if (!string.IsNullOrEmpty(cursor))
                    url += "&cursor=" + Uri.EscapeDataString(cursor);

                string? nextCursor = null;
                bool pageOk = false;

                for (int attempt = 0; attempt < backoffMs.Length && !pageOk; attempt++)
                {
                    if (backoffMs[attempt] > 0)
                        await Task.Delay(backoffMs[attempt], token);

                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        if (!string.IsNullOrEmpty(cookie))
                            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");

                        using HttpResponseMessage res = await _serverListClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);

                        if (res.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            TimeSpan retryAfter = res.Headers.RetryAfter?.Delta ?? TimeSpan.Zero;
                            if (retryAfter > TimeSpan.Zero && retryAfter <= TimeSpan.FromSeconds(5.0))
                                await Task.Delay(retryAfter, token);
                            if (attempt == backoffMs.Length - 1)
                                return SortServers(items, preferEmpty);
                            continue;
                        }

                        if (!res.IsSuccessStatusCode)
                            return SortServers(items, preferEmpty);

                        using JsonDocument doc = JsonDocument.Parse(await ReadBoundedAsync(res.Content, 4 * 1024 * 1024, token));
                        if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement el in data.EnumerateArray())
                            {
                                string? jobId = TryGetString(el, "id");
                                if (string.IsNullOrEmpty(jobId) || !seen.Add(jobId))
                                    continue;

                                int playing = TryGetInt(el, "playing") ?? 0;
                                int maxPlayers = TryGetInt(el, "maxPlayers") ?? 0;
                                int ping = TryGetInt(el, "ping") ?? -1;
                                if (maxPlayers > 0 && playing >= maxPlayers)
                                    continue;

                                items.Add(new ServerListItem { JobId = jobId, Playing = playing, MaxPlayers = maxPlayers, Ping = ping });
                            }
                        }

                        nextCursor = TryGetString(doc.RootElement, "nextPageCursor");
                        pageOk = true;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        return SortServers(items, preferEmpty);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Server list page {page + 1} threw: {ex.Message}");
                        if (attempt == backoffMs.Length - 1)
                            return SortServers(items, preferEmpty);
                    }
                }

                if (string.IsNullOrEmpty(nextCursor))
                    break;
                cursor = nextCursor;
            }

            return SortServers(items, preferEmpty);
        }

        private static List<ServerListItem> SortServers(List<ServerListItem> items, bool preferEmpty) => preferEmpty
            ? items.OrderBy(x => x.Playing).ToList()
            : items.OrderByDescending(x => x.MaxPlayers > 0 ? (double)x.Playing / x.MaxPlayers : (x.Playing > 0 ? 0.5 : 0.0)).ThenByDescending(x => x.Playing).ToList();

        internal static bool IsPrivateIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return true;
            if (!IPAddress.TryParse(ip, out IPAddress? address))
                return false;
            if (IPAddress.IsLoopback(address))
                return true;

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] v6 = address.GetAddressBytes();
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (v6[0] & 0xFE) == 0xFC;
            }

            if (address.AddressFamily != AddressFamily.InterNetwork)
                return true;

            byte[] b = address.GetAddressBytes();
            if (b[0] == 0 || b[0] == 10 || b[0] == 127)
                return true;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                return true;
            if (b[0] == 192 && b[1] == 168)
                return true;
            if (b[0] == 169 && b[1] == 254)
                return true;
            if (b[0] >= 224)
                return true;

            return false;
        }
    }
}
