using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.Utility
{
    internal static class ServerRegion
    {
        private const string LOG_IDENT = "ServerRegion";

        private static volatile string _current = "";
        private static int _generation;

        public static string Current => _current;

        public static event Action? Changed;

        public static string Describe(RobloxDatacenter? datacenter)
        {
            if (datacenter is null || string.IsNullOrWhiteSpace(datacenter.City))
                return "";

            string country = Matchmaker.NormalizeCountryCode(datacenter.Country);
            return string.IsNullOrWhiteSpace(country) ? datacenter.City : $"{datacenter.City}, {country}";
        }

        public static string Shorten(string region, int maxChars)
        {
            if (region.Length <= maxChars)
                return region;

            int comma = region.LastIndexOf(", ", StringComparison.Ordinal);
            string city = comma > 0 ? region[..comma] : region;

            return city.Length <= maxChars ? city : city[..Math.Max(1, maxChars - 1)].TrimEnd() + ".";
        }

        public static void OnGameJoin(string? machineAddress)
        {
            int generation = Interlocked.Increment(ref _generation);
            Set("");

            if (string.IsNullOrWhiteSpace(machineAddress) || machineAddress.StartsWith("10."))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    ServerFetchStore.EnsureLoaded();

                    RobloxDatacenter? datacenter = RobloxDatacenterMap.Map(machineAddress);

                    if (datacenter is null && App.Settings.Prop.ShowServerDetails)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        datacenter = await Matchmaker.LookupUnknownIpAsync(machineAddress, timeout.Token).ConfigureAwait(false);
                    }

                    if (generation != Volatile.Read(ref _generation))
                        return;

                    string region = Describe(datacenter);
                    App.Logger.WriteLine(LOG_IDENT, region.Length > 0 ? $"{machineAddress} is in {region}" : $"No datacenter known for {machineAddress}");
                    Set(region);
                    Announce(region, datacenter);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Lookup failed: {ex.Message}");
                }
            });
        }

        public static void OnGameLeave()
        {
            Interlocked.Increment(ref _generation);
            Set("");
        }

        private static void Announce(string region, RobloxDatacenter? datacenter)
        {
            if (region.Length == 0)
                return;

            string detail = datacenter is not null && !string.IsNullOrWhiteSpace(datacenter.Region) && !string.Equals(datacenter.Region, datacenter.City, StringComparison.OrdinalIgnoreCase)
                ? $"{datacenter.City}, {datacenter.Region}"
                : region;

            int ping = ServerPingMonitor.LatestMs;

            NotificationCenter.Notify(
                "Server region",
                ping >= 0 ? $"{detail} - about {ping} ms" : detail,
                NotificationCategory.General,
                kind: NotificationKindId.ServerRegion);
        }

        private static void Set(string region)
        {
            if (_current == region)
                return;

            _current = region;

            try { Changed?.Invoke(); } catch { }
        }
    }
}
