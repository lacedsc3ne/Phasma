using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace PhasmaStrap.Networking
{
    internal static class AssetContentService
    {
        private const string LOG_IDENT = "AssetContentService";
        private const string BatchFragment = "/v1/assets/batch";

        public static readonly AssetContentCache Cache = new(Path.Combine(Paths.Base, "AssetCache"));
        public static readonly SwapPackStore Packs = new(Path.Combine(Paths.Base, "SwapPacks"));
        public static readonly AssetTrafficStats Traffic = new(Path.Combine(Paths.Base, "AssetCache", "traffic.json"));

        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 24,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            UseCookies = false,
            AllowAutoRedirect = true,
        })
        { Timeout = TimeSpan.FromSeconds(60) };

        private static readonly ConcurrentDictionary<string, byte> Unshrinkable = new();

        private static readonly ConcurrentDictionary<string, Lazy<Task<CachedAsset?>>> InFlight = new();

        private static bool _logsHooked;

        private static void HookLogs()
        {
            if (_logsHooked)
                return;

            _logsHooked = true;
            AssetContentCache.Log = message => App.Logger.WriteLine("AssetContentCache", message);
            SwapPackStore.Log = message => App.Logger.WriteLine("SwapPacks", message);
            AssetTrafficStats.Log = message => App.Logger.WriteLine("AssetTrafficStats", message);
        }

        public static bool RoutingEnabled => App.Settings.Prop.AssetRouteEnabled;
        public static bool SwapsEnabled => App.Settings.Prop.SwapPacksEnabled && Packs.AnyEnabled();
        private static long CacheLimitBytes => Math.Max(256, App.Settings.Prop.AssetCacheLimitMb) * 1048576L;

        public sealed class ManifestEntry
        {
            public long AssetId { get; set; }
            public int TypeId { get; set; }
            public string Key { get; set; } = "";
        }

        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, ManifestEntry>> Manifests = new();
        private static readonly ConcurrentDictionary<long, byte> DirtyManifests = new();
        private static System.Threading.Timer? _manifestTimer;

        private static string ManifestFolder => Path.Combine(Paths.Base, "AssetCache", "manifests");
        private const int MaxManifestEntries = 20000;

        private static ConcurrentDictionary<long, ManifestEntry> ManifestOf(long placeId) => Manifests.GetOrAdd(placeId, id =>
        {
            var loaded = new ConcurrentDictionary<long, ManifestEntry>();
            try
            {
                string file = Path.Combine(ManifestFolder, $"{id}.json");
                if (File.Exists(file))
                {
                    foreach (ManifestEntry entry in JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(file)) ?? new())
                        loaded[entry.AssetId] = entry;
                }
            }
            catch
            {
            }
            return loaded;
        });

        private static void Remember(AssetRouteInfo info)
        {
            if (info.PlaceId <= 0 || info.AssetId <= 0 || info.OriginalUrl.Length == 0)
                return;

            ConcurrentDictionary<long, ManifestEntry> manifest = ManifestOf(info.PlaceId);
            if (manifest.Count >= MaxManifestEntries && !manifest.ContainsKey(info.AssetId))
                return;

            if (manifest.TryGetValue(info.AssetId, out ManifestEntry? known) && known.Key == info.Key)
                return;

            manifest[info.AssetId] = new ManifestEntry { AssetId = info.AssetId, TypeId = info.TypeId, Key = info.Key };
            DirtyManifests[info.PlaceId] = 0;

            _manifestTimer ??= new System.Threading.Timer(_ => FlushManifests(), null, 20_000, 20_000);
        }

        public static void FlushManifests()
        {
            foreach (long placeId in DirtyManifests.Keys.ToList())
            {
                DirtyManifests.TryRemove(placeId, out _);

                try
                {
                    Directory.CreateDirectory(ManifestFolder);
                    string file = Path.Combine(ManifestFolder, $"{placeId}.json");
                    string temp = file + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(ManifestOf(placeId).Values.ToList()));
                    File.Move(temp, file, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not write the manifest of place {placeId}: {ex.Message}");
                }
            }
        }

        public static List<(long PlaceId, int Known, int Cached)> ListManifests()
        {
            var result = new List<(long, int, int)>();

            try
            {
                if (!Directory.Exists(ManifestFolder))
                    return result;

                foreach (string file in Directory.GetFiles(ManifestFolder, "*.json"))
                {
                    if (!long.TryParse(Path.GetFileNameWithoutExtension(file), out long placeId))
                        continue;

                    List<ManifestEntry> entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(file)) ?? new();
                    result.Add((placeId, entries.Count, entries.Count(e => Cache.Contains(e.Key))));
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list manifests: {ex.Message}");
            }

            return result;
        }

        public static async Task<(int Fetched, int Failed, int AlreadyThere)> PrefetchAsync(long placeId, Action<double> progress, CancellationToken token)
        {
            HookLogs();

            List<ManifestEntry> missing = ManifestOf(placeId).Values.Where(e => !Cache.Contains(e.Key)).ToList();
            int already = ManifestOf(placeId).Count - missing.Count;
            int fetched = 0, failed = 0, done = 0;

            string? cookie = Integrations.RobloxCookie.Get();

            foreach (ManifestEntry[] chunk in missing.Chunk(200))
            {
                token.ThrowIfCancellationRequested();

                string body = JsonSerializer.Serialize(chunk.Select((e, i) => new { requestId = i.ToString(), assetId = e.AssetId }));
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Host"] = AssetWarpPolicy.Host,
                    ["User-Agent"] = "Roblox/WinInet",
                    ["Content-Type"] = "application/json",
                    ["Accept"] = "application/json",
                    ["Roblox-Place-Id"] = placeId.ToString(),
                };
                if (!string.IsNullOrEmpty(cookie))
                    headers["Cookie"] = $".ROBLOSECURITY={cookie}";

                ProxiedResponse? answer = await AssetProxyServer.ForwardToUpstreamAsync(new ProxiedRequest(AssetWarpPolicy.Host, "POST", BatchFragment, headers, Encoding.UTF8.GetBytes(body)), token);

                var locations = new List<(ManifestEntry Entry, string Url)>();
                if (answer is { StatusCode: 200 })
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(answer.Body);
                        foreach (JsonElement element in document.RootElement.EnumerateArray())
                        {
                            if (element.TryGetProperty("location", out JsonElement location) && element.TryGetProperty("requestId", out JsonElement requestId)
                                && int.TryParse(requestId.GetString(), out int index) && index >= 0 && index < chunk.Length
                                && location.GetString() is string url && AssetRoute.IsRobloxContentUrl(url))
                                locations.Add((chunk[index], url));
                        }
                    }
                    catch
                    {
                    }
                }

                failed += chunk.Length - locations.Count;
                done += chunk.Length - locations.Count;

                using var gate = new SemaphoreSlim(8);
                await Task.WhenAll(locations.Select(async item =>
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        var info = new AssetRouteInfo { OriginalUrl = item.Url, Key = AssetRoute.ContentKey(item.Url), AssetId = item.Entry.AssetId, TypeId = item.Entry.TypeId, PlaceId = placeId };

                        if (Cache.Contains(info.Key) || await DownloadAsync(info, token) is not null)
                        {
                            Interlocked.Increment(ref fetched);
                            Remember(info);
                        }
                        else
                        {
                            Interlocked.Increment(ref failed);
                        }
                    }
                    finally
                    {
                        gate.Release();
                        progress(Interlocked.Increment(ref done) / (double)Math.Max(1, missing.Count));
                    }
                }));
            }

            FlushManifests();
            App.Logger.WriteLine(LOG_IDENT, $"Prefetch of place {placeId}: {fetched} fetched, {failed} failed, {already} were already on disk");
            return (fetched, failed, already);
        }

        public static byte[]? RewriteBatch(ProxiedRequest request, ProxiedResponse response)
        {
            try
            {
                bool route = RoutingEnabled, swaps = SwapsEnabled;
                if (!route && !swaps)
                    return null;

                if (response.StatusCode != 200 || !request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) || !request.Path.Contains(BatchFragment, StringComparison.OrdinalIgnoreCase))
                    return null;

                HookLogs();

                long placeId = request.Headers.TryGetValue("Roblox-Place-Id", out string? header) && long.TryParse(header, out long parsed) ? parsed : 0;

                AssetRoute.RewriteResult? result = AssetRoute.RewriteBatch(request.Body, response.Body, AssetWarpPolicy.Host, placeId, route,
                    assetId => swaps ? Packs.PackFor(assetId, placeId) : "");

                if (result is null)
                    return null;

                if (result.Swapped > 0)
                    App.Logger.WriteLine(LOG_IDENT, $"Place {placeId}: {result.Swapped} asset(s) in this batch are replaced by a swap pack");

                return result.Body;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Batch rewrite failed, passing it through: {ex.Message}");
                return null;
            }
        }

        private static ProxiedResponse Ok(byte[] body, string contentType) => new(200, "OK", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType.Length > 0 ? contentType : "application/octet-stream",
            ["Cache-Control"] = "public, max-age=31536000, immutable",
        }, body);

        private static ProxiedResponse Redirect(string url) => new(302, "Found", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Location"] = url }, Array.Empty<byte>());

        public static async Task<ProxiedResponse?> HandleAsync(ProxiedRequest request, CancellationToken token)
        {
            AssetRouteInfo? info = AssetRoute.TryParse(request.Path);
            if (info is null)
                return null;

            HookLogs();
            Remember(info);

            var happened = new AssetTrafficStats.Event { PlaceId = info.PlaceId, TypeId = info.TypeId };

            try
            {
                if (info.SwapPack.Length > 0 && App.Settings.Prop.SwapPacksEnabled && Packs.Read(info.SwapPack, info.AssetId) is { } replacement)
                {
                    happened.Swapped = true;
                    happened.BytesToGame = replacement.Body.Length;
                    return Ok(replacement.Body, replacement.ContentType);
                }

                if (info.OriginalUrl.Length == 0)
                {
                    happened.Failed = true;
                    return new ProxiedResponse(404, "Not Found", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<byte>());
                }

                int shrinkTo = App.Settings.Prop.TextureShrinkEnabled ? Math.Clamp(App.Settings.Prop.TextureShrinkMaxSize, 64, 4096) : 0;
                string variant = shrinkTo > 0 ? $"s{shrinkTo}" : "";

                if (variant.Length > 0 && Cache.TryGet(info.Key, variant) is CachedAsset small)
                {
                    happened.CacheHit = true;
                    happened.Shrunk = true;
                    happened.BytesToGame = small.Body.Length;
                    return Ok(small.Body, small.ContentType);
                }

                CachedAsset? asset = Cache.TryGet(info.Key);
                bool fromCache = asset is not null;

                if (asset is null)
                {
                    var timer = Stopwatch.StartNew();
                    asset = await InFlight.GetOrAdd(info.Key, _ => new Lazy<Task<CachedAsset?>>(() => DownloadAsync(info, token))).Value;
                    InFlight.TryRemove(info.Key, out _);

                    if (asset is null)
                    {
                        happened.Failed = true;
                        return Redirect(info.OriginalUrl);
                    }

                    happened.BytesDownloaded = asset.OriginalBytes;
                    happened.UpstreamMs = timer.ElapsedMilliseconds;
                    happened.UpstreamHost = new Uri(info.OriginalUrl).Host;
                }

                if (variant.Length > 0 && !Unshrinkable.ContainsKey(info.Key))
                {
                    TextureShrinker.Outcome? outcome = TextureShrinker.Shrink(asset.Body, shrinkTo);

                    if (outcome is not null)
                    {
                        var shrunk = new CachedAsset { Body = outcome.Body, ContentType = outcome.Format == "jpeg" ? "image/jpeg" : "image/png", TypeId = asset.TypeId, AssetId = asset.AssetId, OriginalBytes = asset.Body.Length };
                        Cache.Put(info.Key, variant, shrunk, CacheLimitBytes);

                        happened.Shrunk = true;
                        happened.ShrinkSaved = asset.Body.Length - outcome.Body.Length;
                        happened.CacheHit = fromCache;
                        happened.BytesToGame = outcome.Body.Length;
                        return Ok(outcome.Body, shrunk.ContentType);
                    }

                    Unshrinkable.TryAdd(info.Key, 0);
                }

                happened.CacheHit = fromCache;
                happened.BytesToGame = asset.Body.Length;
                return Ok(asset.Body, asset.ContentType);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Asset {info.AssetId} ({info.Key}) failed, sending the game to the CDN itself: {ex.Message}");
                happened.Failed = true;
                return info.OriginalUrl.Length > 0 ? Redirect(info.OriginalUrl) : new ProxiedResponse(502, "Bad Gateway", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<byte>());
            }
            finally
            {
                if (App.Settings.Prop.TrafficReportEnabled)
                    Traffic.Record(happened);
            }
        }

        private static async Task<CachedAsset?> DownloadAsync(AssetRouteInfo info, CancellationToken token)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, info.OriginalUrl);
                message.Headers.TryAddWithoutValidation("User-Agent", "Roblox/WinInet");

                using HttpResponseMessage response = await Http.SendAsync(message, HttpCompletionOption.ResponseContentRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"CDN answered {(int)response.StatusCode} for asset {info.AssetId}");
                    return null;
                }

                byte[] body = await response.Content.ReadAsByteArrayAsync(token);
                if (body.Length == 0)
                    return null;

                var asset = new CachedAsset
                {
                    Body = body,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "",
                    TypeId = info.TypeId,
                    AssetId = info.AssetId,
                    OriginalBytes = body.Length,
                };

                if (App.Settings.Prop.AssetCacheEnabled)
                    Cache.Put(info.Key, "", asset, CacheLimitBytes);

                return asset;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Download of asset {info.AssetId} failed: {ex.Message}");
                return null;
            }
        }
    }
}
