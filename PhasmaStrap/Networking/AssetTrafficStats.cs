namespace PhasmaStrap.Networking
{
    public sealed class TrafficBucket
    {
        public int Requests { get; set; }
        public int CacheHits { get; set; }
        public long DownloadedBytes { get; set; }       // really fetched from Roblox
        public long ServedFromCacheBytes { get; set; }  // handed over without touching the network
        public long ShrinkSavedBytes { get; set; }      // what the texture shrinker took off
        public int Shrunk { get; set; }
        public int Swapped { get; set; }
        public int Failed { get; set; }
    }

    public sealed class HostTiming
    {
        public int Requests { get; set; }
        public long TotalMs { get; set; }
        public long SlowestMs { get; set; }
        public long Bytes { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public double AverageMs => Requests > 0 ? (double)TotalMs / Requests : 0;
    }

    public sealed class PlaceTraffic
    {
        public long PlaceId { get; set; }
        public DateTime FirstUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastUtc { get; set; } = DateTime.UtcNow;
        public TrafficBucket Total { get; set; } = new();
        public Dictionary<string, TrafficBucket> ByType { get; set; } = new();
        public Dictionary<string, HostTiming> Hosts { get; set; } = new();
    }

    public sealed class TrafficData
    {
        public Dictionary<long, PlaceTraffic> Places { get; set; } = new();
    }

    // The traffic report: per game, how much was downloaded, how much the cache and the shrinker
    // saved, what kinds of assets it was, and which of Roblox's servers were slow to answer.
    //
    // Only covers what passes through PhasmaStrap (routed asset downloads); the game's own
    // real-time connection to the server does not. Counted in memory, written out every half
    // minute. The file path is a parameter, so it can be exercised from a console harness.
    public sealed class AssetTrafficStats
    {
        public static Action<string>? Log;

        private const int MaxPlaces = 200;

        private readonly string _path;
        private readonly object _lock = new();
        private TrafficData? _data;
        private bool _dirty;
        private System.Threading.Timer? _timer;

        public AssetTrafficStats(string path)
        {
            _path = path;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        }

        public static string TypeName(int typeId) => typeId switch
        {
            1 => "Images",
            2 or 8 or 11 or 12 or 17 or 18 or 19 or >= 41 and <= 47 or >= 64 and <= 72 => "Clothing and accessories",
            3 => "Sounds",
            4 or 40 => "Meshes",
            5 or 38 => "Scripts and plugins",
            9 or 10 => "Models and places",
            13 => "Decals",
            24 or >= 48 and <= 56 or 61 or 78 => "Animations",
            39 => "Solid models",
            62 => "Video",
            63 => "Texture packs",
            73 or 74 or 75 => "Fonts",
            0 => "Unknown",
            _ => $"Other (type {typeId})",
        };

        private TrafficData Data()
        {
            if (_data is not null)
                return _data;

            try
            {
                if (File.Exists(_path))
                    _data = JsonSerializer.Deserialize<TrafficData>(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read {_path}: {ex.Message}");
            }

            return _data ??= new TrafficData();
        }

        public sealed class Event
        {
            public long PlaceId;
            public int TypeId;
            public long BytesToGame;
            public long BytesDownloaded;
            public long ShrinkSaved;
            public bool CacheHit, Shrunk, Swapped, Failed;
            public string UpstreamHost = "";
            public long UpstreamMs;
        }

        public void Record(Event e)
        {
            lock (_lock)
            {
                TrafficData data = Data();

                if (!data.Places.TryGetValue(e.PlaceId, out PlaceTraffic? place))
                    data.Places[e.PlaceId] = place = new PlaceTraffic { PlaceId = e.PlaceId };

                place.LastUtc = DateTime.UtcNow;

                string type = TypeName(e.TypeId);
                if (!place.ByType.TryGetValue(type, out TrafficBucket? byType))
                    place.ByType[type] = byType = new TrafficBucket();

                foreach (TrafficBucket bucket in new[] { place.Total, byType })
                {
                    bucket.Requests++;
                    bucket.DownloadedBytes += e.BytesDownloaded;
                    bucket.ShrinkSavedBytes += e.ShrinkSaved;
                    if (e.CacheHit) { bucket.CacheHits++; bucket.ServedFromCacheBytes += e.BytesToGame; }
                    if (e.Shrunk) bucket.Shrunk++;
                    if (e.Swapped) bucket.Swapped++;
                    if (e.Failed) bucket.Failed++;
                }

                if (e.UpstreamHost.Length > 0)
                {
                    if (!place.Hosts.TryGetValue(e.UpstreamHost, out HostTiming? host))
                        place.Hosts[e.UpstreamHost] = host = new HostTiming();

                    host.Requests++;
                    host.TotalMs += e.UpstreamMs;
                    host.SlowestMs = Math.Max(host.SlowestMs, e.UpstreamMs);
                    host.Bytes += e.BytesDownloaded;
                }

                _dirty = true;
                _timer ??= new System.Threading.Timer(_ => Flush(), null, 30_000, 30_000);
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (!_dirty || _data is null)
                    return;

                try
                {
                    // (only the one process that owns the proxy's port ever counts, so there is
                    // nobody to merge with - the file is simply this process's totals)
                    TrafficData merged = _data;

                    if (_data.Places.Count > MaxPlaces)
                    {
                        foreach (long old in _data.Places.Values.OrderBy(p => p.LastUtc).Take(_data.Places.Count - MaxPlaces).Select(p => p.PlaceId).ToList())
                            _data.Places.Remove(old);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    string temp = _path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(merged));
                    File.Move(temp, _path, true);
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not write {_path}: {ex.Message}");
                }
            }
        }

        // a fresh read from disk, for the page
        public TrafficData Load()
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<TrafficData>(File.ReadAllText(_path)) ?? new TrafficData() : new TrafficData();
            }
            catch
            {
                return new TrafficData();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _data = new TrafficData();
                _dirty = false;
                try { File.Delete(_path); } catch { }
            }
        }
    }
}
