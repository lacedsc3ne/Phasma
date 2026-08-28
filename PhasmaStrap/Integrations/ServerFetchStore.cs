using System.Net.Sockets;
using PhasmaStrap.Models;

namespace PhasmaStrap.Integrations
{
    // persists what the matchmaker learns about servers it has probed (IP -> datacenter,
    // ping samples), so future searches can weight a real observed ping alongside the
    // geographic estimate. Simplified from Voidstrap's version: drops its remote
    // community-preset fetching (GitHub-hosted shared datacenter data), keeping only local
    // learning from your own probes.
    public static class ServerFetchStore
    {
        private const string LOG_IDENT = "ServerFetchStore";
        private const int MaxPingSamples = 25;
        private const int MaxIpsPerEntry = 100;
        private const int MaxServerEntries = 4096;

        private static readonly object _lock = new();
        private static ServerFetchData _data = new();
        private static bool _loaded;
        private static Timer? _saveTimer;

        private static readonly JsonSerializerOptions StoreJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string FolderPath => Paths.ServerFetch;
        private static string FilePath => Path.Combine(FolderPath, "Data.json");

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (_lock)
            {
                if (_loaded)
                    return;

                Load();
                PrunePrivateCidrs();
                RepairCountries();

                RobloxDatacenterMap.AddCidrEntries(_data.Servers.Select(item => new SeedCidrEntry
                {
                    Cidr = string.IsNullOrWhiteSpace(item.Value.Cidr) ? item.Key : item.Value.Cidr,
                    City = item.Value.City,
                    Region = item.Value.Region,
                    Country = item.Value.Country,
                    Lat = item.Value.Lat,
                    Lon = item.Value.Lon
                }));

                _loaded = true;
            }
        }

        private static void RepairCountries()
        {
            int repaired = 0;
            foreach (var server in _data.Servers)
            {
                string corrected = RobloxDatacenterMap.ResolveCountry(server.Value.City, server.Value.Country);
                if (string.Equals(corrected, server.Value.Country ?? "", StringComparison.Ordinal))
                    continue;

                server.Value.Country = corrected;
                repaired++;
            }

            if (repaired > 0)
                SaveThrottled();
        }

        private static void PrunePrivateCidrs()
        {
            try
            {
                var stale = _data.Servers.Keys.Where(key => Matchmaker.IsPrivateIp(key.Split('/')[0])).ToList();
                if (stale.Count == 0)
                    return;

                foreach (string key in stale)
                    _data.Servers.Remove(key);

                App.Logger.WriteLine(LOG_IDENT, $"Pruned {stale.Count} private/internal CIDR(s) from learned datacenters");
                SaveThrottled();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PrunePrivateCidrs failed: {ex.Message}");
            }
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(FolderPath);
                    _data = new ServerFetchData();
                    return;
                }

                ServerFetchData? loaded = JsonSerializer.Deserialize<ServerFetchData>(File.ReadAllText(FilePath), StoreJsonOptions);
                _data = NormalizeData(loaded ?? new ServerFetchData());
                App.Logger.WriteLine(LOG_IDENT, $"Loaded {_data.Servers.Count} learned datacenter(s)");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Load failed, starting fresh: {ex.Message}");
                _data = new ServerFetchData();
            }
        }

        private static ServerFetchData NormalizeData(ServerFetchData data)
        {
            var normalized = new Dictionary<string, LearnedServerEntry>(StringComparer.OrdinalIgnoreCase);

            var entries = data.Servers
                .OrderByDescending(item => item.Value.LastSeenUtc)
                .Take(MaxServerEntries);

            foreach (var item in entries)
            {
                LearnedServerEntry entry = item.Value;

                if (entry.IPs != null)
                    entry.IPs = entry.IPs.Where(ip => !string.IsNullOrWhiteSpace(ip)).Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(MaxIpsPerEntry).ToList();

                if (entry.PingSamplesMs != null)
                    entry.PingSamplesMs = entry.PingSamplesMs.TakeLast(MaxPingSamples).ToList();

                normalized[item.Key] = entry;
            }

            data.Servers = normalized;
            return data;
        }

        public static LearnedServerEntry? Lookup(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return null;

            EnsureLoaded();
            string? cidr = GetSlash24Cidr(ip);
            if (cidr is null)
                return null;

            lock (_lock)
            {
                _data.Servers.TryGetValue(cidr, out LearnedServerEntry? value);
                return value;
            }
        }

        public static void RecordSighting(string? ip, string? city = null, string? region = null, string? country = null, double? lat = null, double? lon = null)
        {
            if (string.IsNullOrWhiteSpace(ip) || Matchmaker.IsPrivateIp(ip))
                return;

            EnsureLoaded();
            string? cidr = GetSlash24Cidr(ip);
            if (cidr is null)
                return;

            lock (_lock)
            {
                if (!_data.Servers.TryGetValue(cidr, out LearnedServerEntry? entry))
                {
                    entry = new LearnedServerEntry
                    {
                        Cidr = cidr,
                        City = city ?? "",
                        Region = region ?? "",
                        Country = RobloxDatacenterMap.ResolveCountry(city, country),
                        Lat = lat.GetValueOrDefault(),
                        Lon = lon.GetValueOrDefault(),
                        FirstSeenUtc = DateTime.UtcNow
                    };
                    _data.Servers[cidr] = entry;
                }
                else
                {
                    if (string.IsNullOrEmpty(entry.City) && !string.IsNullOrEmpty(city))
                        entry.City = city;
                    if (string.IsNullOrEmpty(entry.Region) && !string.IsNullOrEmpty(region))
                        entry.Region = region;
                    if (string.IsNullOrEmpty(entry.Country) && !string.IsNullOrEmpty(country))
                        entry.Country = RobloxDatacenterMap.ResolveCountry(city ?? entry.City, country);
                    if (entry.Lat == 0.0 && lat.HasValue)
                        entry.Lat = lat.Value;
                    if (entry.Lon == 0.0 && lon.HasValue)
                        entry.Lon = lon.Value;
                }

                entry.SeenCount++;
                entry.LastSeenUtc = DateTime.UtcNow;
                entry.IPs ??= new List<string>();

                if (!entry.IPs.Contains(ip))
                {
                    entry.IPs.Add(ip);
                    if (entry.IPs.Count > MaxIpsPerEntry)
                        entry.IPs.RemoveRange(0, entry.IPs.Count - MaxIpsPerEntry);
                }
            }

            SaveThrottled();
        }

        public static double GetMedianPing(string? ip, out int sampleCount)
        {
            sampleCount = 0;
            if (string.IsNullOrWhiteSpace(ip))
                return -1.0;

            EnsureLoaded();
            string? cidr = GetSlash24Cidr(ip);
            if (cidr is null)
                return -1.0;

            lock (_lock)
            {
                if (!_data.Servers.TryGetValue(cidr, out LearnedServerEntry? entry) || entry.PingSamplesMs is null || entry.PingSamplesMs.Count == 0)
                    return -1.0;

                int[] samples = entry.PingSamplesMs.OrderBy(v => v).ToArray();
                sampleCount = samples.Length;
                int middle = samples.Length / 2;
                return samples.Length % 2 == 0 ? (samples[middle - 1] + samples[middle]) / 2.0 : samples[middle];
            }
        }

        public static (int Datacenters, int Servers, int TotalSightings, int PingedDatacenters) GetStats()
        {
            EnsureLoaded();
            lock (_lock)
            {
                int datacenters = _data.Servers.Count;
                int servers = _data.Servers.Values.Sum(e => e.IPs?.Count ?? 0);
                int sightings = _data.Servers.Values.Sum(e => e.SeenCount);
                int pinged = _data.Servers.Values.Count(e => e.PingSamplesMs is { Count: > 0 });
                return (datacenters, servers, sightings, pinged);
            }
        }

        public static void SaveThrottled()
        {
            lock (_lock)
            {
                _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _saveTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            }
        }

        public static void SaveNow()
        {
            string contents;
            try
            {
                lock (_lock)
                {
                    _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _data = NormalizeData(_data);
                    _data.UpdatedUtc = DateTime.UtcNow;
                    contents = JsonSerializer.Serialize(_data, StoreJsonOptions);
                }

                Directory.CreateDirectory(FolderPath);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save failed: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            Timer? timer;
            lock (_lock)
            {
                timer = _saveTimer;
                _saveTimer = null;
            }
            timer?.Dispose();

            if (_loaded)
                SaveNow();
        }

        private static string? GetSlash24Cidr(string ip)
        {
            if (!IPAddress.TryParse(ip, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
                return null;

            byte[] bytes = address.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }
    }
}
