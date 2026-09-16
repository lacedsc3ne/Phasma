namespace PhasmaStrap.Integrations.Nvidia
{
    /// <summary>
    /// A single logged action in the NVIDIA advanced editor's "NVIDIA Flag History" section
    /// (see NvidiaPage's grid view). Just a timestamp and a human-readable description of what
    /// happened - there's nothing here that needs its own strong types, the driver/profile
    /// calls that actually did the work already returned their own results by the time this is
    /// logged.
    /// </summary>
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

    /// <summary>
    /// Session-only history of actions taken from the NVIDIA advanced editor (add/remove/delete
    /// custom settings, reset, apply) - shown in the collapsed "NVIDIA Flag History" CardExpander
    /// at the bottom of NvidiaPage's grid view.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="PhasmaStrap.UI.NotificationCenter"/>'s in-memory-only, capped history
    /// list: PhasmaStrap has no persistent store for this (no local database, no server backend)
    /// and building one just for a "recent actions" list would be a lot of new surface for very
    /// little value - a session-scoped list is honest about what it actually is and is enough to
    /// let the user see what the editor has done in this session.
    /// </remarks>
    public static class NvidiaFlagHistory
    {
        private const int MaxEntries = 50;

        private static readonly object s_lock = new();
        private static readonly LinkedList<NvidiaHistoryEntry> s_entries = new();

        /// <summary>
        /// Raised (off the UI thread - marshal before touching UI state) whenever <see cref="Entries"/>
        /// changes, so NvidiaViewModel can refresh the bound list.
        /// </summary>
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
