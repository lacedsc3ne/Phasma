namespace PhasmaStrap.Integrations.Nvidia
{
    public sealed class NvidiaHistoryEntry
    {
        public DateTime Timestamp { get; }

        public string Message { get; }

        public NvidiaHistoryEntry(string message)
        {
            Message = message;
            Timestamp = DateTime.Now;
        }
    }

    public static class NvidiaFlagHistory
    {
        private const int MaxEntries = 50;

        private static readonly object s_lock = new();
        private static readonly LinkedList<NvidiaHistoryEntry> s_entries = new();

        public static event EventHandler? Changed;

        public static IReadOnlyList<NvidiaHistoryEntry> Entries
        {
            get
            {
                lock (s_lock)
                    return s_entries.ToArray();
            }
        }

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            NvidiaHistoryEntry entry = new NvidiaHistoryEntry(message);

            lock (s_lock)
            {
                s_entries.AddFirst(entry);

                while (s_entries.Count > MaxEntries)
                    s_entries.RemoveLast();
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Clear()
        {
            lock (s_lock)
                s_entries.Clear();

            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
