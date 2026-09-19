using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace PhasmaStrap.Networking
{
    // The proxy's asset desk: where the rewritten asset URLs (AssetRoute) land.
    //
    //   batch response  -> RewriteBatch:  locations point back at the proxy (all of them when
    //                                     routing is on, only swapped IDs when it is not)
    //   /phasma-asset/  -> HandleAsync:   swap pack file  |  disk cache  |  download, maybe shrink,
    //                                     cache  - and count it for the traffic report
    //
    // Whatever goes wrong in here, the game still gets its asset: the answer is then a redirect to
    // the original CDN address that travelled inside the rewritten URL.
    //
    // All of it is opt-in (AssetRouteEnabled / swap packs), on top of the proxy being on at all.
    internal static class AssetContentService
    {
        private const string LOG_IDENT = "AssetContentService";
        private const string BatchFragment = "/v1/assets/batch";

        public static readonly AssetContentCache Cache = new(Path.Combine(Paths.Base, "AssetCache"));
        public static readonly SwapPackStore Packs = new(Path.Combine(Paths.Base, "SwapPacks"));
        public static readonly AssetTrafficStats Traffic = new(Path.Combine(Paths.Base, "AssetCache", "traffic.json"));

        // pooled connections to the CDN (the proxy's own upstream code opens one per request,
        // which is fine for a handful of API calls and hopeless for two thousand assets)
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

        // images that came out no smaller: not worth trying again every time they are asked for
        private static readonly ConcurrentDictionary<string, byte> Unshrinkable = new();

        // one download per asset even when the game asks for it several times at once
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

        // ------------------------------------------------------------------ batch response

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

        // ------------------------------------------------------------------ asset requests

        private static ProxiedResponse Ok(byte[] body, string contentType) => new(200, "OK", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType.Length > 0 ? contentType : "application/octet-stream",
            ["Cache-Control"] = "public, max-age=31536000, immutable",
        }, body);

        private static ProxiedResponse Redirect(string url) => new(302, "Found", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Location"] = url }, Array.Empty<byte>());

        // null = this request is not an asset request (the proxy carries on as usual)
        public static async Task<ProxiedResponse?> HandleAsync(ProxiedRequest request, CancellationToken token)
        {
            AssetRouteInfo? info = AssetRoute.TryParse(request.Path);
            if (info is null)
                return null;

            HookLogs();

            var happened = new AssetTrafficStats.Event { PlaceId = info.PlaceId, TypeId = info.TypeId };

            try
            {
                // ---- a swap pack's replacement
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

                // ---- the shrunk copy, if that is what is wanted and it exists
                if (variant.Length > 0 && Cache.TryGet(info.Key, variant) is CachedAsset small)
                {
                    happened.CacheHit = true;
                    happened.Shrunk = true;
                    happened.BytesToGame = small.Body.Length;
                    return Ok(small.Body, small.ContentType);
                }

                // ---- the original: from disk, or from Roblox
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

                // ---- shrink on the way out
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
