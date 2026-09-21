namespace PhasmaStrap.Networking
{
    public sealed class ProxyTrafficEntry
    {
        public DateTime Timestamp { get; init; }
        public string Host { get; init; } = "";
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public int StatusCode { get; init; }
        public bool ServedFromCache { get; init; }
        public int ResponseBytes { get; init; }

        public string Summary => $"{Method} {Host}{Path}";
        public string OutcomeDisplay => ServedFromCache ? $"{StatusCode} (cache)" : StatusCode.ToString();
    }

    public static class ProxyTrafficLog
    {
        private const int MaxEntries = 200;

        private static readonly object Sync = new();
        private static readonly LinkedList<ProxyTrafficEntry> Entries = new();

        private static string FilePath => Path.Combine(Paths.LocalAppData, "PhasmaStrap", "AssetProxy", "traffic.json");

        private static DateTime _lastWrite = DateTime.MinValue;

        public static event EventHandler? Changed;

        public static IReadOnlyList<ProxyTrafficEntry> Recent
        {
            get
            {
                lock (Sync)
                    return Entries.ToArray();
            }
        }

        public static void Record(string host, string method, string path, int statusCode, bool servedFromCache, int responseBytes)
        {
            var entry = new ProxyTrafficEntry
            {
                Timestamp = DateTime.Now,
                Host = host,
                Method = method,
                Path = path,
                StatusCode = statusCode,
                ServedFromCache = servedFromCache,
                ResponseBytes = responseBytes,
            };

            lock (Sync)
            {
                Entries.AddFirst(entry);
                while (Entries.Count > MaxEntries)
                    Entries.RemoveLast();
            }

            Share();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static void Share()
        {
            if (DateTime.UtcNow - _lastWrite < TimeSpan.FromMilliseconds(500))
                return;

            _lastWrite = DateTime.UtcNow;

            try
            {
                ProxyTrafficEntry[] snapshot;

                lock (Sync)
                    snapshot = Entries.ToArray();

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                string temporary = FilePath + "." + Environment.ProcessId + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
                File.Move(temporary, FilePath, true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ProxyTrafficLog", $"Could not share the traffic log: {ex.Message}");
            }
        }

        public static IReadOnlyList<ProxyTrafficEntry> FromHostingProcess()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return Array.Empty<ProxyTrafficEntry>();

                return JsonSerializer.Deserialize<ProxyTrafficEntry[]>(File.ReadAllText(FilePath)) ?? Array.Empty<ProxyTrafficEntry>();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ProxyTrafficLog", $"Could not read the shared traffic log: {ex.Message}");
                return Array.Empty<ProxyTrafficEntry>();
            }
        }

        public static void Clear()
        {
            lock (Sync)
                Entries.Clear();

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ProxyTrafficLog", $"Could not clear the shared traffic log: {ex.Message}");
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
