using System.Security.Cryptography;

namespace PhasmaStrap.Networking
{
    // AssetWarp "Preloading" - a disk cache for the batch asset-resolution/thumbnail-lookup
    // responses that flow through AssetWarpPolicy/AssetWarpThumbnailPolicy.
    //
    // Scope note (read before assuming this caches actual textures/meshes): assetdelivery.roblox.com
    // and thumbnails.roblox.com only ever return JSON resolving an asset ID to a CDN URL (or a
    // thumbnail's image URL) - the real asset bytes are then fetched by Roblox's client directly
    // from a separate, essentially unbounded set of CDN hostnames PhasmaStrap's proxy never sees
    // (it isn't in the hosts-file redirect or InterceptedHosts, and adding open-ended CDN
    // interception is a materially bigger, separate change). So what this actually caches and
    // replays is the *resolution step* - which CDN URL a given asset ID maps to right now - not
    // the asset content itself. That's still a real, measurable win on repeat launches of the same
    // game (skips a network round-trip per batch instead of per byte), just a smaller one than
    // "serves previously downloaded assets" might suggest at face value.
    internal static class AssetPreloadCache
    {
        private const string LOG_IDENT = "AssetPreloadCache";

        private static string CacheDir => Path.Combine(Paths.Base, "AssetPreloadCache");

        private static readonly object EvictionSync = new();

        private static long _lastEvictionTicks;

        private const int MinEvictionIntervalMs = 30000;

        public static ProxiedResponse? TryServeFromCache(ProxiedRequest request)
        {
            if (!App.Settings.Prop.AssetWarpPreloadEnabled)
                return null;

            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                string path = CacheFilePath(request);
                if (!File.Exists(path))
                    return null;

                byte[] cached = File.ReadAllBytes(path);
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);

                App.Logger.WriteLine(LOG_IDENT, $"Served {request.Host}{request.Path} from preload cache ({cached.Length} bytes)");

                return new ProxiedResponse(200, "OK", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = "application/json",
                }, cached);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return null;
            }
        }

        // registered as an additional response-transform so every real resolution response that
        // flows through also gets written to the cache for next time - returns null always (it
        // never modifies the response itself, it just observes it on the way through)
        public static byte[]? CacheResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!App.Settings.Prop.AssetWarpPreloadEnabled)
                return null;

            if (response.StatusCode != 200 || response.Body.Length == 0)
                return null;

            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllBytes(CacheFilePath(request), response.Body);
                MaybeEvict();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            return null;
        }

        private static string CacheFilePath(ProxiedRequest request)
        {
            // keyed on host+path+body: a batch request's body IS the list of asset IDs being
            // resolved, so two different batches never collide even on the same endpoint path
            using SHA256 sha = SHA256.Create();
            byte[] keyBytes = Encoding.UTF8.GetBytes($"{request.Host}{request.Path}").Concat(request.Body).ToArray();
            string hash = Convert.ToHexString(sha.ComputeHash(keyBytes));
            return Path.Combine(CacheDir, $"{hash}.json");
        }

        private static void MaybeEvict()
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastEvictionTicks) < MinEvictionIntervalMs)
                return;

            if (!Monitor.TryEnter(EvictionSync))
                return;

            try
            {
                Interlocked.Exchange(ref _lastEvictionTicks, now);

                if (!Directory.Exists(CacheDir))
                    return;

                var files = new DirectoryInfo(CacheDir).GetFiles("*.json");
                long limitBytes = (long)Math.Max(1, App.Settings.Prop.AssetWarpPreloadCacheMb) * 1024 * 1024;
                long totalBytes = files.Sum(f => f.Length);

                if (totalBytes <= limitBytes)
                    return;

                // delete least-recently-accessed first until back under the limit
                foreach (FileInfo file in files.OrderBy(f => f.LastAccessTimeUtc))
                {
                    if (totalBytes <= limitBytes)
                        break;

                    try
                    {
                        totalBytes -= file.Length;
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            finally
            {
                Monitor.Exit(EvictionSync);
            }
        }

        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDir))
                    Directory.Delete(CacheDir, recursive: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        // "Preload Avatar" / "Preload Recent Games" - proactively fire the same real, public
        // resolution requests Roblox's own client would make (bypassing the local proxy entirely,
        // straight to the real API), so by the time an actual game session asks, the answer is
        // either already warm in this cache or the OS/TLS connection to Roblox's API is already
        // established. Both are best-effort and swallow failures - this is a performance nicety,
        // never something a launch should fail over.
        public static async Task PreloadAvatarAsync(CancellationToken token = default)
        {
            if (!App.Settings.Prop.AssetWarpPreloadAvatar)
                return;

            try
            {
                Integrations.RobloxCookie.RobloxAccount? account = await Integrations.RobloxCookie.GetAccountAsync(token).ConfigureAwait(false);
                if (account is null)
                    return;

                string url = $"https://thumbnails.roblox.com/v1/users/avatar?userIds={account.UserId}&size=420x420&format=Png&isCircular=false";
                await App.HttpClient.GetStringAsync(url, token).ConfigureAwait(false);

                App.Logger.WriteLine(LOG_IDENT, $"Preloaded avatar thumbnail for user {account.UserId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public static async Task PreloadRecentGamesAsync(IEnumerable<long> recentUniverseIds, CancellationToken token = default)
        {
            if (!App.Settings.Prop.AssetWarpPreloadCrossGame)
                return;

            List<long> ids = recentUniverseIds.Take(10).ToList();
            if (ids.Count == 0)
                return;

            try
            {
                string joined = string.Join(',', ids);
                string url = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={joined}&size=512x512&format=Png&isCircular=false";
                await App.HttpClient.GetStringAsync(url, token).ConfigureAwait(false);

                App.Logger.WriteLine(LOG_IDENT, $"Preloaded icons for {ids.Count} recent game(s)");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }
    }
}
