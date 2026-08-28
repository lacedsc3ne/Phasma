namespace PhasmaStrap.Networking
{
    // single on/off switch for the whole local-proxy feature: registers the intercepted
    // hosts and their transforms, starts the proxy server, and requests the elevated
    // hosts-file write. Disabling reverses all three, in the opposite order, so nothing
    // is left half-configured.
    public static class NetworkingController
    {
        private const string LOG_IDENT = "NetworkingController";

        public static bool IsActive => AssetProxyServer.IsRunning && HostsFileManager.IsBlockPresent();

        public static bool Enable()
        {
            RegisterHosts();
            AssetProxyServer.Start();

            if (!AssetProxyServer.IsRunning)
            {
                App.Logger.WriteLine(LOG_IDENT, "Proxy server failed to start, aborting enable");
                return false;
            }

            bool hostsOk = HostsFileManager.RequestInstall();
            if (!hostsOk)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hosts file install was declined or failed, rolling back");
                AssetProxyServer.Stop();
                return false;
            }

            App.Settings.Prop.NetworkingProxyEnabled = true;
            App.Settings.Save();
            return true;
        }

        public static void Disable()
        {
            // remove the hosts entries FIRST, so Roblox stops routing through us before we
            // stop listening - otherwise there's a window where those hostnames resolve to
            // a dead local port
            HostsFileManager.RequestRemoval();
            AssetProxyServer.Stop();

            App.Settings.Prop.NetworkingProxyEnabled = false;
            App.Settings.Save();
        }

        // called at startup to catch a previous session that crashed or was killed while
        // this was enabled - a dead proxy with hosts entries still pointing at it would
        // otherwise leave Roblox unable to reach the real servers at all
        public static void ReconcileOnStartup()
        {
            bool blockPresent = HostsFileManager.IsBlockPresent();

            if (App.Settings.Prop.NetworkingProxyEnabled)
            {
                RegisterHosts();
                AssetProxyServer.Start();

                if (!blockPresent)
                    HostsFileManager.RequestInstall();
            }
            else if (blockPresent)
            {
                App.Logger.WriteLine(LOG_IDENT, "Found leftover proxy hosts entries from a previous session, removing");
                HostsFileManager.RequestRemoval();
            }
        }

        private static void RegisterHosts()
        {
            AssetProxyServer.InterceptedHosts[PresenceSpoofPolicy.Host] = (PresenceSpoofPolicy.TransformRequest, null);

            AssetProxyServer.InterceptedHosts.TryGetValue(RobuxSpoofer.Host, out var existingEconomy);
            AssetProxyServer.InterceptedHosts[RobuxSpoofer.Host] = (existingEconomy.RequestTransform, RobuxSpoofer.ProcessResponse);

            AssetProxyServer.InterceptedHosts.TryGetValue(UsernameSpoofer.Host, out var existingApis);
            AssetProxyServer.InterceptedHosts[UsernameSpoofer.Host] = (existingApis.RequestTransform, CombineResponseTransforms(existingApis.ResponseTransform, UsernameSpoofer.ProcessResponse));

            RegisterAssetWarpHosts();
        }

        private static void RegisterAssetWarpHosts()
        {
            AssetProxyServer.InterceptedHosts[AssetWarpPolicy.Host] = (AssetWarpPolicy.TransformRequest, null);

            AssetProxyServer.InterceptedHosts.TryGetValue(AssetWarpThumbnailPolicy.Host, out var existingThumbnails);
            AssetProxyServer.InterceptedHosts[AssetWarpThumbnailPolicy.Host] = (existingThumbnails.RequestTransform, CombineResponseTransforms(existingThumbnails.ResponseTransform, AssetWarpThumbnailPolicy.ProcessResponse));
        }

        private static Func<ProxiedRequest, ProxiedResponse, byte[]?> CombineResponseTransforms(
            Func<ProxiedRequest, ProxiedResponse, byte[]?>? first,
            Func<ProxiedRequest, ProxiedResponse, byte[]?> second)
        {
            return (request, response) => second(request, response) ?? first?.Invoke(request, response);
        }
    }
}
