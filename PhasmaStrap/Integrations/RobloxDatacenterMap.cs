using System.Net.Sockets;
using PhasmaStrap.Models;

namespace PhasmaStrap.Integrations
{
    // maps a Roblox game-server IP to the physical datacenter it's likely in, via a static
    // table of known Roblox CIDR ranges (all publicly observable, not proprietary data),
    // plus whatever gets learned at runtime through ServerFetchStore. Ported from Voidstrap.
    public static class RobloxDatacenterMap
    {
        private const int MaxCidrEntries = 8192;

        private sealed class CidrEntry
        {
            public uint Network;
            public uint Mask;
            public int PrefixLength;
            public string Cidr = "";
            public RobloxDatacenter Datacenter = null!;
        }

        private static readonly List<SeedCidrEntry> _seedEntries = BuildSeedEntries();

        private static readonly HashSet<string> _builtInCidrs = new(_seedEntries.Select(seed => seed.Cidr), StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> _builtInCityCountry = BuildCityCountryIndex();

        private static readonly List<CidrEntry> _entries = BuildCidrEntries();

        private static Dictionary<string, string> BuildCityCountryIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SeedCidrEntry seed in _seedEntries)
            {
                if (!string.IsNullOrWhiteSpace(seed.City) && !string.IsNullOrWhiteSpace(seed.Country))
                    index.TryAdd(seed.City.Trim(), seed.Country.Trim());
            }
            return index;
        }

        public static string ResolveCountry(string? city, string? country)
        {
            if (!string.IsNullOrWhiteSpace(city) && _builtInCityCountry.TryGetValue(city.Trim(), out string? known))
                return known;
            return CountryFlag.Canonical(country);
        }

        public static IReadOnlyList<SeedCidrEntry> AllSeedEntries()
        {
            lock (_entries)
                return _seedEntries.ToArray();
        }

        public static void AddCidrEntries(IEnumerable<SeedCidrEntry> entries)
        {
            lock (_entries)
            {
                Dictionary<string, CidrEntry> byCidr = _entries.ToDictionary(e => e.Cidr, StringComparer.OrdinalIgnoreCase);

                foreach (SeedCidrEntry entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Cidr) || _builtInCidrs.Contains(entry.Cidr.Trim()))
                        continue;

                    try
                    {
                        if (string.IsNullOrWhiteSpace(entry.City) || !double.IsFinite(entry.Lat) || !double.IsFinite(entry.Lon)
                            || entry.Lat is < -90.0 or > 90.0 || entry.Lon is < -180.0 or > 180.0 || (entry.Lat == 0.0 && entry.Lon == 0.0))
                            continue;

                        string[] parts = entry.Cidr.Split('/');
                        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork
                            || !int.TryParse(parts[1], out int prefix) || prefix is < 0 or > 32)
                            continue;

                        byte[] bytes = address.GetAddressBytes();
                        uint network = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
                        uint mask = prefix == 0 ? 0u : (uint)(-1 << (32 - prefix));
                        network &= mask;

                        var datacenter = new RobloxDatacenter
                        {
                            City = entry.City,
                            Region = entry.Region,
                            Country = ResolveCountry(entry.City, entry.Country),
                            Lat = entry.Lat,
                            Lon = entry.Lon
                        };

                        if (byCidr.TryGetValue(entry.Cidr, out CidrEntry? existing))
                        {
                            existing.Network = network;
                            existing.Mask = mask;
                            existing.PrefixLength = prefix;
                            existing.Datacenter = datacenter;
                            continue;
                        }

                        if (_entries.Count >= MaxCidrEntries)
                            break;

                        var added = new CidrEntry { Network = network, Mask = mask, PrefixLength = prefix, Cidr = entry.Cidr, Datacenter = datacenter };
                        _entries.Add(added);
                        byCidr.Add(entry.Cidr, added);
                    }
                    catch { }
                }

                _entries.Sort((left, right) => right.PrefixLength.CompareTo(left.PrefixLength));
            }
        }

        public static RobloxDatacenter? Map(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
                return null;

            byte[] bytes = address.GetAddressBytes();
            uint value = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);

            lock (_entries)
            {
                foreach (CidrEntry entry in _entries)
                {
                    if ((value & entry.Mask) == entry.Network)
                        return entry.Datacenter;
                }
            }

            return null;
        }

        public static IReadOnlyCollection<RobloxDatacenter> AllDatacenters()
        {
            var seen = new HashSet<string>();
            var list = new List<RobloxDatacenter>();

            lock (_entries)
            {
                foreach (CidrEntry entry in _entries)
                {
                    if (seen.Add($"{entry.Datacenter.City}|{entry.Datacenter.Country}"))
                        list.Add(entry.Datacenter);
                }
            }

            return list;
        }

        private static List<CidrEntry> BuildCidrEntries()
        {
            var list = new List<CidrEntry>();

            foreach (SeedCidrEntry seed in _seedEntries)
            {
                string[] parts = seed.Cidr.Split('/');
                IPAddress address = IPAddress.Parse(parts[0]);
                int prefix = int.Parse(parts[1]);
                byte[] bytes = address.GetAddressBytes();
                uint network = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
                uint mask = prefix != 0 ? (uint)(-1 << (32 - prefix)) : 0u;
                network &= mask;

                list.Add(new CidrEntry
                {
                    Network = network,
                    Mask = mask,
                    PrefixLength = prefix,
                    Cidr = seed.Cidr,
                    Datacenter = new RobloxDatacenter { City = seed.City, Region = seed.Region, Country = seed.Country, Lat = seed.Lat, Lon = seed.Lon }
                });
            }

            return list;
        }

        // known public Roblox datacenter CIDR ranges - publicly observable, not proprietary
        private static List<SeedCidrEntry> BuildSeedEntries() => new()
        {
            new SeedCidrEntry { Cidr = "128.116.115.0/24", City = "Seattle", Region = "Washington", Country = "USA", Lat = 47.6062, Lon = -122.3321 },
            new SeedCidrEntry { Cidr = "128.116.116.0/24", City = "Los Angeles", Region = "California", Country = "USA", Lat = 34.0522, Lon = -118.2437 },
            new SeedCidrEntry { Cidr = "128.116.1.0/24", City = "Los Angeles", Region = "California", Country = "USA", Lat = 34.0522, Lon = -118.2437 },
            new SeedCidrEntry { Cidr = "128.116.63.0/24", City = "Los Angeles", Region = "California", Country = "USA", Lat = 34.0522, Lon = -118.2437 },
            new SeedCidrEntry { Cidr = "128.116.95.0/24", City = "Dallas", Region = "Texas", Country = "USA", Lat = 32.7767, Lon = -96.797 },
            new SeedCidrEntry { Cidr = "128.116.101.0/24", City = "Chicago", Region = "Illinois", Country = "USA", Lat = 41.8781, Lon = -87.6298 },
            new SeedCidrEntry { Cidr = "128.116.48.0/24", City = "Chicago", Region = "Illinois", Country = "USA", Lat = 41.8781, Lon = -87.6298 },
            new SeedCidrEntry { Cidr = "128.116.22.0/24", City = "Atlanta", Region = "Georgia", Country = "USA", Lat = 33.749, Lon = -84.388 },
            new SeedCidrEntry { Cidr = "128.116.99.0/24", City = "Atlanta", Region = "Georgia", Country = "USA", Lat = 33.749, Lon = -84.388 },
            new SeedCidrEntry { Cidr = "128.116.45.0/24", City = "Miami", Region = "Florida", Country = "USA", Lat = 25.7617, Lon = -80.1918 },
            new SeedCidrEntry { Cidr = "128.116.127.0/24", City = "Miami", Region = "Florida", Country = "USA", Lat = 25.7617, Lon = -80.1918 },
            new SeedCidrEntry { Cidr = "128.116.102.0/24", City = "Ashburn", Region = "Virginia", Country = "USA", Lat = 39.0438, Lon = -77.4874 },
            new SeedCidrEntry { Cidr = "128.116.53.0/24", City = "Ashburn", Region = "Virginia", Country = "USA", Lat = 39.0438, Lon = -77.4874 },
            new SeedCidrEntry { Cidr = "128.116.32.0/24", City = "New York", Region = "New York", Country = "USA", Lat = 40.7128, Lon = -74.006 },
            new SeedCidrEntry { Cidr = "128.116.33.0/24", City = "London", Region = "England", Country = "UK", Lat = 51.5074, Lon = -0.1278 },
            new SeedCidrEntry { Cidr = "128.116.119.0/24", City = "London", Region = "England", Country = "UK", Lat = 51.5074, Lon = -0.1278 },
            new SeedCidrEntry { Cidr = "128.116.21.0/24", City = "Amsterdam", Region = "North Holland", Country = "Netherlands", Lat = 52.3676, Lon = 4.9041 },
            new SeedCidrEntry { Cidr = "128.116.4.0/24", City = "Paris", Region = "Ile-de-France", Country = "France", Lat = 48.8566, Lon = 2.3522 },
            new SeedCidrEntry { Cidr = "128.116.122.0/24", City = "Paris", Region = "Ile-de-France", Country = "France", Lat = 48.8566, Lon = 2.3522 },
            new SeedCidrEntry { Cidr = "128.116.5.0/24", City = "Frankfurt", Region = "Hesse", Country = "Germany", Lat = 50.1109, Lon = 8.6821 },
            new SeedCidrEntry { Cidr = "128.116.44.0/24", City = "Frankfurt", Region = "Hesse", Country = "Germany", Lat = 50.1109, Lon = 8.6821 },
            new SeedCidrEntry { Cidr = "128.116.123.0/24", City = "Frankfurt", Region = "Hesse", Country = "Germany", Lat = 50.1109, Lon = 8.6821 },
            new SeedCidrEntry { Cidr = "128.116.31.0/24", City = "Warsaw", Region = "Mazovia", Country = "Poland", Lat = 52.2297, Lon = 21.0122 },
            new SeedCidrEntry { Cidr = "128.116.124.0/24", City = "Warsaw", Region = "Mazovia", Country = "Poland", Lat = 52.2297, Lon = 21.0122 },
            new SeedCidrEntry { Cidr = "128.116.104.0/24", City = "Mumbai", Region = "Maharashtra", Country = "India", Lat = 19.076, Lon = 72.8777 },
            new SeedCidrEntry { Cidr = "128.116.55.0/24", City = "Tokyo", Region = "Kanto", Country = "Japan", Lat = 35.6762, Lon = 139.6503 },
            new SeedCidrEntry { Cidr = "128.116.120.0/24", City = "Tokyo", Region = "Kanto", Country = "Japan", Lat = 35.6762, Lon = 139.6503 },
            new SeedCidrEntry { Cidr = "128.116.50.0/24", City = "Singapore", Region = "Singapore", Country = "Singapore", Lat = 1.3521, Lon = 103.8198 },
            new SeedCidrEntry { Cidr = "128.116.97.0/24", City = "Singapore", Region = "Singapore", Country = "Singapore", Lat = 1.3521, Lon = 103.8198 },
            new SeedCidrEntry { Cidr = "128.116.51.0/24", City = "Sydney", Region = "New South Wales", Country = "Australia", Lat = -33.8688, Lon = 151.2093 },
            new SeedCidrEntry { Cidr = "128.116.117.0/24", City = "San Jose", Region = "California", Country = "USA", Lat = 37.3382, Lon = -121.8863 },
            new SeedCidrEntry { Cidr = "209.206.42.0/24", City = "San Jose", Region = "California", Country = "USA", Lat = 37.3382, Lon = -121.8863 },
            new SeedCidrEntry { Cidr = "209.206.43.0/24", City = "San Jose", Region = "California", Country = "USA", Lat = 37.3382, Lon = -121.8863 },
            new SeedCidrEntry { Cidr = "128.116.30.0/24", City = "Hong Kong", Region = "Hong Kong", Country = "China", Lat = 22.3193, Lon = 114.1694 },
            new SeedCidrEntry { Cidr = "128.116.118.0/24", City = "Hong Kong", Region = "Hong Kong", Country = "China", Lat = 22.3193, Lon = 114.1694 },
        };
    }
}
