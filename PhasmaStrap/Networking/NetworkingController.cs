namespace PhasmaStrap.Networking
{
    public static class NetworkingController
    {
        private const string LOG_IDENT = "NetworkingController";

        private static System.Threading.Timer? _keeper;

        private static int _keeperBusy;

        public static bool IsActive => ProxyHealth.IsHostedAnywhere() && HostsFileManager.IsBlockPresent();

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

            HostsElevation.ReconcileOnStartup();
        }

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

            if (!AssetProxyServer.InterceptedHosts.ContainsKey(JoinPickerPolicy.Host))
                AssetProxyServer.InterceptedHosts[JoinPickerPolicy.Host] = (null, null, null);
            AssetProxyServer.AsyncHandlers[JoinPickerPolicy.Host] = JoinPickerPolicy.HandleAsync;
        }

        private static void RegisterAssetWarpHosts()
        {
            AssetProxyServer.InterceptedHosts.TryGetValue(AssetWarpPolicy.Host, out var existingDelivery);

            AssetProxyServer.InterceptedHosts[AssetWarpPolicy.Host] = (
                AssetWarpPolicy.TransformRequest,
                CombineResponseTransforms(CombineResponseTransforms(existingDelivery.ResponseTransform, AssetPreloadCache.CacheResponse), AssetContentService.RewriteBatch),
                ServeBatchFromPreloadCache);

            AssetProxyServer.AsyncHandlers[AssetWarpPolicy.Host] = AssetContentService.HandleAsync;

            AssetProxyServer.InterceptedHosts.TryGetValue(AssetWarpThumbnailPolicy.Host, out var existingThumbnails);
            AssetProxyServer.InterceptedHosts[AssetWarpThumbnailPolicy.Host] = (
                existingThumbnails.RequestTransform,
                CombineResponseTransforms(existingThumbnails.ResponseTransform, AssetWarpThumbnailPolicy.ProcessResponse),
                existingThumbnails.TryServeFromCache);
        }

        private static ProxiedResponse? ServeBatchFromPreloadCache(ProxiedRequest request)
        {
            ProxiedResponse? cached = AssetPreloadCache.TryServeFromCache(request);
            if (cached is null)
                return null;

            byte[]? rewritten = AssetContentService.RewriteBatch(request, cached);
            return rewritten is null ? cached : cached with { Body = rewritten };
        }

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
