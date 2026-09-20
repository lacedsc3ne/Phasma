using System.IO.Compression;

namespace PhasmaStrap.Networking
{
    public sealed class AssetSwap
    {
        public long AssetId { get; set; }
        public string File { get; set; } = "";
        public string Note { get; set; } = "";
    }

    public sealed class SwapPack
    {
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;

        public List<long> Places { get; set; } = new();
        public List<AssetSwap> Swaps { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public string Folder { get; set; } = "";
    }

    public sealed class SwapPackStore
    {
        public static Action<string>? Log;

        public const string Extension = ".phasmapack";
        private const long MaxImportBytes = 512L * 1024 * 1024;
        private const int MaxImportFiles = 2000;

        private readonly string _root;
        private readonly object _lock = new();
        private List<SwapPack>? _packs;
        private DateTime _stamp;

        public SwapPackStore(string root)
        {
            _root = root;
        }

        public string Root => _root;

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        private DateTime Stamp()
        {
            try
            {
                if (!Directory.Exists(_root))
                    return DateTime.MinValue;

                DateTime newest = Directory.GetLastWriteTimeUtc(_root);
                foreach (string manifest in Directory.GetFiles(_root, "pack.json", SearchOption.AllDirectories))
                {
                    DateTime time = File.GetLastWriteTimeUtc(manifest);
                    if (time > newest)
                        newest = time;
                }
                return newest;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public List<SwapPack> List()
        {
            lock (_lock)
            {
                DateTime stamp = Stamp();
                if (_packs is not null && stamp == _stamp)
                    return _packs;

                var packs = new List<SwapPack>();

                if (Directory.Exists(_root))
                {
                    foreach (string folder in Directory.GetDirectories(_root))
                    {
                        string manifest = Path.Combine(folder, "pack.json");
                        if (!File.Exists(manifest))
                            continue;

                        try
                        {
                            SwapPack? pack = JsonSerializer.Deserialize<SwapPack>(File.ReadAllText(manifest), Json);
                            if (pack is null)
                                continue;

                            pack.Folder = Path.GetFileName(folder);
                            pack.Swaps ??= new();
                            pack.Places ??= new();
                            if (pack.Name.Length == 0)
                                pack.Name = pack.Folder;

                            packs.Add(pack);
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke($"Could not read {manifest}: {ex.Message}");
                        }
                    }
                }

                _packs = packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                _stamp = stamp;
                return _packs;
            }
        }

        public void Save(SwapPack pack)
        {
            lock (_lock)
            {
                if (pack.Folder.Length == 0)
                    pack.Folder = UniqueFolder(pack.Name);

                string folder = Path.Combine(_root, pack.Folder);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "pack.json"), JsonSerializer.Serialize(pack, Json));
                _packs = null;
            }
        }

        public void Delete(SwapPack pack)
        {
            lock (_lock)
            {
                string folder = Path.Combine(_root, pack.Folder);
                if (pack.Folder.Length > 0 && Directory.Exists(folder))
                    Directory.Delete(folder, true);
                _packs = null;
            }
        }

        private string UniqueFolder(string name)
        {
            string safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
            if (safe.Length == 0) safe = "pack";
            if (safe.Length > 40) safe = safe[..40];

            string candidate = safe;
            for (int n = 2; Directory.Exists(Path.Combine(_root, candidate)); n++)
                candidate = $"{safe}-{n}";
            return candidate;
        }

        public void AddSwap(SwapPack pack, long assetId, string sourceFile, string note)
        {
            if (pack.Folder.Length == 0)
                Save(pack);

            string folder = Path.Combine(_root, pack.Folder);
            string name = $"{assetId}{Path.GetExtension(sourceFile).ToLowerInvariant()}";
            File.Copy(sourceFile, Path.Combine(folder, name), true);

            pack.Swaps.RemoveAll(s => s.AssetId == assetId);
            pack.Swaps.Add(new AssetSwap { AssetId = assetId, File = name, Note = note });
            Save(pack);
        }

        public void RemoveSwap(SwapPack pack, long assetId)
        {
            foreach (AssetSwap swap in pack.Swaps.Where(s => s.AssetId == assetId).ToList())
            {
                try { File.Delete(Path.Combine(_root, pack.Folder, swap.File)); } catch { }
                pack.Swaps.Remove(swap);
            }
            Save(pack);
        }

        public string PackFor(long assetId, long placeId)
        {
            string everywhere = "";

            foreach (SwapPack pack in List())
            {
                if (!pack.Enabled || !pack.Swaps.Any(s => s.AssetId == assetId))
                    continue;

                if (pack.Places.Count == 0)
                {
                    if (everywhere.Length == 0)
                        everywhere = pack.Folder;
                }
                else if (pack.Places.Contains(placeId))
                {
                    return pack.Folder;
                }
            }

            return everywhere;
        }

        public bool AnyEnabled() => List().Any(p => p.Enabled && p.Swaps.Count > 0);

        public (byte[] Body, string ContentType)? Read(string packFolder, long assetId)
        {
            try
            {
                SwapPack? pack = List().FirstOrDefault(p => p.Folder == packFolder && p.Enabled);
                AssetSwap? swap = pack?.Swaps.FirstOrDefault(s => s.AssetId == assetId);
                if (pack is null || swap is null)
                    return null;

                string folder = Path.GetFullPath(Path.Combine(_root, pack.Folder)) + Path.DirectorySeparatorChar;
                string file = Path.GetFullPath(Path.Combine(folder, swap.File));
                if (!file.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                    return null;

                string type = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".bmp" => "image/bmp",
                    ".ogg" => "audio/ogg",
                    ".mp3" => "audio/mpeg",
                    ".wav" => "audio/wav",
                    _ => "application/octet-stream",
                };

                return (File.ReadAllBytes(file), type);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read the replacement for {assetId} in '{packFolder}': {ex.Message}");
                return null;
            }
        }

        public void Export(SwapPack pack, string destination)
        {
            string folder = Path.Combine(_root, pack.Folder);
            if (File.Exists(destination))
                File.Delete(destination);

            ZipFile.CreateFromDirectory(folder, destination, CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        public SwapPack Import(string packFile)
        {
            using ZipArchive archive = ZipFile.OpenRead(packFile);

            ZipArchiveEntry manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("pack.json", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("This is not a PhasmaStrap swap pack (it has no pack.json).");

            if (archive.Entries.Count > MaxImportFiles || archive.Entries.Sum(e => e.Length) > MaxImportBytes)
                throw new InvalidDataException("The pack is too large to be a genuine swap pack.");

            SwapPack pack;
            using (var reader = new StreamReader(manifestEntry.Open()))
                pack = JsonSerializer.Deserialize<SwapPack>(reader.ReadToEnd(), Json) ?? throw new InvalidDataException("The pack's manifest is empty.");

            pack.Swaps ??= new();
            pack.Places ??= new();
            pack.Enabled = false;
            pack.Folder = "";
            if (string.IsNullOrWhiteSpace(pack.Name))
                pack.Name = Path.GetFileNameWithoutExtension(packFile);

            lock (_lock)
            {
                pack.Folder = UniqueFolder(pack.Name);
                string folder = Path.GetFullPath(Path.Combine(_root, pack.Folder)) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(folder);

                try
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith('/') || entry.FullName.Equals("pack.json", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string name = Path.GetFileName(entry.FullName);
                        string target = Path.GetFullPath(Path.Combine(folder, name));

                        if (name.Length == 0 || name != entry.FullName || !target.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"The pack contains a suspicious entry: {entry.FullName}");

                        string extension = Path.GetExtension(name).ToLowerInvariant();
                        if (extension is ".exe" or ".dll" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js" or ".lnk" or ".scr" or ".msi" or ".com")
                            throw new InvalidDataException($"The pack contains a program ({name}) - refusing to import it.");

                        entry.ExtractToFile(target, true);
                    }

                    pack.Swaps = pack.Swaps.Where(s => s.AssetId > 0 && s.File == Path.GetFileName(s.File) && File.Exists(Path.Combine(folder, s.File))).ToList();
                    File.WriteAllText(Path.Combine(folder, "pack.json"), JsonSerializer.Serialize(pack, Json));
                }
                catch
                {
                    try { Directory.Delete(folder, true); } catch { }
                    throw;
                }

                _packs = null;
            }

            return pack;
        }
    }
}
