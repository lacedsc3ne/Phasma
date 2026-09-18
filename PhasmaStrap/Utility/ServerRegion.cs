using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.Utility
{
    // "Where is this server?" for the tray menu and the overlay HUD's REGION row.
    //
    // The answer comes from the datacenter map the matchmaker already keeps (seed CIDR list plus
    // everything learned from earlier joins), so it is normally an offline lookup on the address
    // Roblox itself logged. Only when that knows nothing - and only if "query server location" is
    // on, which is the setting that already allows asking an outside service about a server
    // address - is the matchmaker's online lookup used.
    internal static class ServerRegion
    {
        private const string LOG_IDENT = "ServerRegion";

        private static volatile string _current = "";
        private static int _generation;

        /// <summary>"Frankfurt, DE" for the server being played on, or "" when unknown / not in a game.</summary>
        public static string Current => _current;

        public static event Action? Changed;

        public static string Describe(RobloxDatacenter? datacenter)
        {
            if (datacenter is null || string.IsNullOrWhiteSpace(datacenter.City))
                return "";

            string country = Matchmaker.NormalizeCountryCode(datacenter.Country);
            return string.IsNullOrWhiteSpace(country) ? datacenter.City : $"{datacenter.City}, {country}";
        }

        // fits the HUD's value column: "Amsterdam, Netherlands" is too wide, "Amsterdam" says enough
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

                    // a newer join (or a leave) has happened meanwhile
                    if (generation != Volatile.Read(ref _generation))
                        return;

                    string region = Describe(datacenter);
                    App.Logger.WriteLine(LOG_IDENT, region.Length > 0 ? $"{machineAddress} is in {region}" : $"No datacenter known for {machineAddress}");
                    Set(region);
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

        private static void Set(string region)
        {
            if (_current == region)
                return;

            _current = region;

            try { Changed?.Invoke(); } catch { }
        }
    }
}
