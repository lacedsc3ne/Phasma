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

    // In-memory only, capped ring buffer of every request AssetProxyServer actually handles - see
    // its call site in HandleClientAsync. Exists purely for DeveloperToolsPage's live traffic
    // view; nothing else reads this, and it's never persisted to disk.
    public static class ProxyTrafficLog
    {
        private const int MaxEntries = 200;

        private static readonly object Sync = new();
        private static readonly LinkedList<ProxyTrafficEntry> Entries = new();

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

            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Clear()
        {
            lock (Sync)
                Entries.Clear();

            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
