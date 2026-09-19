namespace PhasmaStrap.Networking
{
    // single on/off switch for the whole local-proxy feature: registers the intercepted
    // hosts and their transforms, starts the proxy server, and requests the elevated
    // hosts-file write. Disabling reverses all three, in the opposite order, so nothing
    // is left half-configured.
    public static class NetworkingController
    {
        private const string LOG_IDENT = "NetworkingController";

        private static System.Threading.Timer? _keeper;

        private static int _keeperBusy;

        public static bool IsActive => AssetProxyServer.IsRunning && HostsFileManager.IsBlockPresent();

        // Two trust boundaries have to be satisfied for Roblox to accept the proxy's leaf certs:
        //  1. the Windows CurrentUser store - used by PhasmaStrap's own HttpClient and any other
        //     app on the machine that hits an intercepted host (browsers, Studio's web views);
        //  2. Roblox's OWN bundled CA list (<version>\ssl\cacert.pem) - the client is libcurl
        //     built against that file and never consults the Windows store at all. Skipping this
        //     step is exactly why "none of the spoof settings do anything": every handshake from
        //     the game fails before a single request is parsed, and the traffic log stays empty.
        public static bool EnsureCertificateInstalled()
        {
            bool storeOk = AssetProxyCA.IsInstalledInTrustStore();

            if (!storeOk)
            {
                storeOk = AssetProxyCA.InstallToTrustStore();
                App.Logger.WriteLine(LOG_IDENT, storeOk ? "Installed the proxy root certificate" : "Failed to install the proxy root certificate");
            }

            try
            {
                AssetProxyCA.PatchRobloxTrustBundles();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not patch Roblox's trust bundle: {ex.Message}");
            }

            return storeOk;
        }

        public static bool Enable()
        {
            RegisterHosts();
            AssetProxyServer.Start();

            if (!AssetProxyServer.IsRunning)
            {
                App.Logger.WriteLine(LOG_IDENT, "Proxy server failed to start, aborting enable");
                return false;
            }

            if (!EnsureCertificateInstalled())
            {
                App.Logger.WriteLine(LOG_IDENT, "Certificate install failed, rolling back");
                AssetProxyServer.Stop();
                return false;
            }

            // one elevated run syncs the telemetry block too, so enabling both never costs two prompts
            bool hostsOk = HostsFileManager.IsBlockCurrent() || HostsElevation.Apply(true, App.Settings.Prop.BlockRobloxTelemetry);
            if (!hostsOk)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hosts file install was declined or failed, rolling back");
                AssetProxyServer.Stop();
                return false;
            }

            App.Settings.Prop.NetworkingProxyEnabled = true;
            App.Settings.Save();

            UsernameSpoofer.WarmUpIdentity();
            StartKeeper();
            return true;
        }

        public static void Disable()
        {
            StopKeeper();

            // remove the hosts entries FIRST, so Roblox stops routing through us before we
            // stop listening - otherwise there's a window where those hostnames resolve to
            // a dead local port
            HostsElevation.Apply(false, App.Settings.Prop.BlockRobloxTelemetry);
            AssetProxyServer.Stop();

            try
            {
                AssetProxyCA.UnpatchRobloxTrustBundles();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not restore Roblox's trust bundle: {ex.Message}");
            }

            App.Settings.Prop.NetworkingProxyEnabled = false;
            App.Settings.Save();
        }

        // called at startup to catch a previous session that crashed or was killed while
        // this was enabled - a dead proxy with hosts entries still pointing at it would
        // otherwise leave Roblox unable to reach the real servers at all
        public static void ReconcileOnStartup()
        {
            if (App.Settings.Prop.NetworkingProxyEnabled)
            {
                RegisterHosts();
                AssetProxyServer.Start();
                EnsureCertificateInstalled();
                UsernameSpoofer.WarmUpIdentity();
                StartKeeper();
            }

            // hosts-file state (stale/missing/leftover blocks for BOTH the proxy and the telemetry
            // blocker) is reconciled in one elevated run by HostsElevation.ReconcileOnStartup
            HostsElevation.ReconcileOnStartup();
        }

        // The proxy is hosted by whichever PhasmaStrap process managed to bind port 443 first -
        // typically the settings window, or the game-session watcher when the window isn't open.
        // When that process exits (closing the settings window mid-game, say) the hosts entries
        // would point at a dead port and every intercepted API call from Roblox would fail. This
        // keeper runs in every process: it re-attempts the bind every few seconds so another
        // process takes over within moments, and in non-settings processes it also re-reads
        // Settings.json when it changes on disk so a toggle flipped in the window applies to the
        // live game session instead of waiting for the next launch.
        private static void StartKeeper()
        {
            lock (typeof(NetworkingController))
            {
                _keeper ??= new System.Threading.Timer(_ => KeeperTick(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
            }
        }

        private static void StopKeeper()
        {
            lock (typeof(NetworkingController))
            {
                _keeper?.Dispose();
                _keeper = null;
            }
        }

        private static void KeeperTick()
        {
            if (Interlocked.CompareExchange(ref _keeperBusy, 1, 0) != 0)
                return;

            try
            {
                // (settings hot-reload for non-settings processes lives in Utility.SettingsHotReload,
                // started from App startup - it isn't tied to the proxy being on)

                if (!App.Settings.Prop.NetworkingProxyEnabled)
                    return;

                if (!AssetProxyServer.IsRunning)
                {
                    RegisterHosts();
                    AssetProxyServer.Start();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Keeper tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _keeperBusy, 0);
            }
        }

        private static void RegisterHosts()
        {
            AssetProxyServer.InterceptedHosts[PresenceSpoofPolicy.Host] = (PresenceSpoofPolicy.TransformRequest, null, PresenceSpoofPolicy.TryServeFromCache);

            AssetProxyServer.InterceptedHosts.TryGetValue(RobuxSpoofer.Host, out var existingEconomy);
            AssetProxyServer.InterceptedHosts[RobuxSpoofer.Host] = (existingEconomy.RequestTransform, RobuxSpoofer.ProcessResponse, existingEconomy.TryServeFromCache);

            AssetProxyServer.InterceptedHosts.TryGetValue(UsernameSpoofer.Host, out var existingApis);
            AssetProxyServer.InterceptedHosts[UsernameSpoofer.Host] = (existingApis.RequestTransform, CombineResponseTransforms(existingApis.ResponseTransform, UsernameSpoofer.ProcessResponse), existingApis.TryServeFromCache);

            AssetProxyServer.InterceptedHosts.TryGetValue(GameCreatorSpoofer.Host, out var existingGames);
            AssetProxyServer.InterceptedHosts[GameCreatorSpoofer.Host] = (existingGames.RequestTransform, CombineResponseTransforms(existingGames.ResponseTransform, GameCreatorSpoofer.ProcessResponse), existingGames.TryServeFromCache);

            RegisterAssetWarpHosts();
        }

        private static void RegisterAssetWarpHosts()
        {
            AssetProxyServer.InterceptedHosts.TryGetValue(AssetWarpPolicy.Host, out var existingDelivery);
            // order matters: the preload cache stores the batch answer as ROBLOX gave it (its signed
            // URLs are what a later session needs refreshed, not PhasmaStrap's rewritten ones), and
            // only then are the locations pointed at the proxy
            AssetProxyServer.InterceptedHosts[AssetWarpPolicy.Host] = (
                AssetWarpPolicy.TransformRequest,
                CombineResponseTransforms(CombineResponseTransforms(existingDelivery.ResponseTransform, AssetPreloadCache.CacheResponse), AssetContentService.RewriteBatch),
                ServeBatchFromPreloadCache);

            // asset content that was routed through the proxy (cache, texture shrinker, swap packs)
            AssetProxyServer.AsyncHandlers[AssetWarpPolicy.Host] = AssetContentService.HandleAsync;

            AssetProxyServer.InterceptedHosts.TryGetValue(AssetWarpThumbnailPolicy.Host, out var existingThumbnails);
            AssetProxyServer.InterceptedHosts[AssetWarpThumbnailPolicy.Host] = (
                existingThumbnails.RequestTransform,
                CombineResponseTransforms(existingThumbnails.ResponseTransform, AssetWarpThumbnailPolicy.ProcessResponse),
                existingThumbnails.TryServeFromCache);
        }

        // the proxy skips response transforms for answers that came out of a cache, so a preloaded
        // batch answer has its locations rewritten here instead
        private static ProxiedResponse? ServeBatchFromPreloadCache(ProxiedRequest request)
        {
            ProxiedResponse? cached = AssetPreloadCache.TryServeFromCache(request);
            if (cached is null)
                return null;

            byte[]? rewritten = AssetContentService.RewriteBatch(request, cached);
            return rewritten is null ? cached : cached with { Body = rewritten };
        }

        // both transforms get a chance: the second sees the first's output, so e.g. a username
        // rewrite and a thumbnail rewrite on the same host compose instead of one winning
        private static Func<ProxiedRequest, ProxiedResponse, byte[]?> CombineResponseTransforms(
            Func<ProxiedRequest, ProxiedResponse, byte[]?>? first,
            Func<ProxiedRequest, ProxiedResponse, byte[]?> second)
        {
            if (first is null)
                return second;

            return (request, response) =>
            {
                byte[]? firstResult = first(request, response);
                ProxiedResponse intermediate = firstResult is null ? response : response with { Body = firstResult };
                return second(request, intermediate) ?? firstResult;
            };
        }
    }
}
