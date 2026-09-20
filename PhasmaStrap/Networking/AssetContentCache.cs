namespace PhasmaStrap.Networking
{
    public sealed class CachedAsset
    {
        public byte[] Body = Array.Empty<byte>();
        public string ContentType = "";
        public int TypeId;
        public long AssetId;
        public long OriginalBytes;
    }

    public sealed class AssetContentCache
    {
        public static Action<string>? Log;

        private static readonly byte[] Magic = { (byte)'P', (byte)'H', (byte)'A', (byte)'C' };

        private readonly string _root;
        private readonly object _evictionLock = new();
        private long _approximateBytes = -1;
        private long _lastEvictionTick;

        public AssetContentCache(string root)
        {
            _root = root;
        }

        private sealed class Header
        {
            public string ContentType { get; set; } = "";
            public int TypeId { get; set; }
            public long AssetId { get; set; }
            public long OriginalBytes { get; set; }
        }

        private string PathOf(string key, string variant)
        {
            string name = variant.Length > 0 ? $"{key}.{variant}" : key;
            return Path.Combine(_root, key.Length >= 2 ? key[..2] : "__", name);
        }

        public bool Contains(string key, string variant = "") => File.Exists(PathOf(key, variant));

        public CachedAsset? TryGet(string key, string variant = "")
        {
            string path = PathOf(key, variant);

            try
            {
                if (!File.Exists(path))
                    return null;

                byte[] file = File.ReadAllBytes(path);
                if (file.Length < 8 || !file.AsSpan(0, 4).SequenceEqual(Magic))
                    return null;

                int headerLength = BitConverter.ToInt32(file, 4);
                if (headerLength <= 0 || 8 + headerLength > file.Length)
                    return null;

                Header header = JsonSerializer.Deserialize<Header>(file.AsSpan(8, headerLength)) ?? new Header();

                try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }

                return new CachedAsset
                {
                    Body = file[(8 + headerLength)..],
                    ContentType = header.ContentType,
                    TypeId = header.TypeId,
                    AssetId = header.AssetId,
                    OriginalBytes = header.OriginalBytes,
                };
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read cache entry {key}: {ex.Message}");
                return null;
            }
        }

        public void Put(string key, string variant, CachedAsset asset, long limitBytes)
        {
            if (asset.Body.Length == 0)
                return;

            string path = PathOf(key, variant);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                byte[] header = JsonSerializer.SerializeToUtf8Bytes(new Header { ContentType = asset.ContentType, TypeId = asset.TypeId, AssetId = asset.AssetId, OriginalBytes = asset.OriginalBytes });

                string temp = $"{path}.{Guid.NewGuid():N}.tmp";
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(Magic);
                    stream.Write(BitConverter.GetBytes(header.Length));
                    stream.Write(header);
                    stream.Write(asset.Body);
                }

                File.Move(temp, path, true);

                if (_approximateBytes >= 0)
                    Interlocked.Add(ref _approximateBytes, 8 + header.Length + asset.Body.Length);

                MaybeEvict(limitBytes);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not write cache entry {key}: {ex.Message}");
            }
        }

        private IEnumerable<FileInfo> Entries() =>
            Directory.Exists(_root)
                ? new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories).Where(f => !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && f.Directory?.Name.Length == 2)
                : Enumerable.Empty<FileInfo>();

        public (int Count, long Bytes) Size()
        {
            int count = 0;
            long bytes = 0;

            try
            {
                foreach (FileInfo file in Entries())
                {
                    count++;
                    bytes += file.Length;
                }
            }
            catch
            {
            }

            Interlocked.Exchange(ref _approximateBytes, bytes);
            return (count, bytes);
        }

        private void MaybeEvict(long limitBytes)
        {
            if (limitBytes <= 0)
                return;

            long known = Interlocked.Read(ref _approximateBytes);
            long now = Environment.TickCount64;

            if (known >= 0 && known <= limitBytes)
                return;

            if (now - Interlocked.Read(ref _lastEvictionTick) < 20_000 || !Monitor.TryEnter(_evictionLock))
                return;

            try
            {
                Interlocked.Exchange(ref _lastEvictionTick, now);
                Evict(limitBytes);
            }
            finally
            {
                Monitor.Exit(_evictionLock);
            }
        }

        public int Evict(long limitBytes)
        {
            int removed = 0;

            try
            {
                List<FileInfo> files = Entries().ToList();
                long total = files.Sum(f => f.Length);

                if (total > limitBytes)
                {
                    long target = (long)(limitBytes * 0.9);

                    foreach (FileInfo file in files.OrderBy(f => f.LastAccessTimeUtc))
                    {
                        if (total <= target)
                            break;

                        try
                        {
                            total -= file.Length;
                            file.Delete();
                            removed++;
                        }
                        catch
                        {
                        }
                    }

                    Log?.Invoke($"Evicted {removed} least recently used asset(s); the cache is now {total / 1048576.0:0} MB");
                }

                Interlocked.Exchange(ref _approximateBytes, total);

                foreach (FileInfo temp in new DirectoryInfo(_root).EnumerateFiles("*.tmp", SearchOption.AllDirectories).Where(f => (DateTime.UtcNow - f.LastWriteTimeUtc).TotalMinutes > 10))
                {
                    try { temp.Delete(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Eviction failed: {ex.Message}");
            }

            return removed;
        }

        public void Clear()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    foreach (DirectoryInfo shard in new DirectoryInfo(_root).GetDirectories().Where(d => d.Name.Length == 2))
                        shard.Delete(true);
                }

                Interlocked.Exchange(ref _approximateBytes, 0);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not clear the cache: {ex.Message}");
            }
        }
    }
}
