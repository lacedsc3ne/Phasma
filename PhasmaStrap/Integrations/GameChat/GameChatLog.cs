namespace PhasmaStrap.Integrations.GameChat
{
    /// <summary>
    /// A single captured chat message, as shown by the standalone Chat Logs viewer window
    /// (UI/Elements/ContextMenu/ChatLogs.xaml).
    /// </summary>
    public sealed class GameChatLogEntry
    {
        public DateTime Time { get; init; }
        public string Channel { get; init; } = "";
        public string Sender { get; init; } = "";
        public string Message { get; init; } = "";
    }

    /// <summary>
    /// Capped in-memory log of messages received through PhasmaStrap's own GameChat overlay
    /// (see <see cref="GameChatOverlay"/>). Independent of any single overlay window's lifetime,
    /// so the standalone Chat Logs viewer can show/export the same message stream the overlay
    /// renders, even across overlay open/close cycles within a session.
    /// </summary>
    public static class GameChatLog
    {
        private const int MaxEntries = 2000;

        private static readonly object _sync = new();
        private static readonly List<GameChatLogEntry> _entries = new();

        public static event EventHandler<GameChatLogEntry>? Added;

        public static void Record(string channel, string sender, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var entry = new GameChatLogEntry
            {
                Time = DateTime.Now,
                Channel = channel ?? "",
                Sender = sender ?? "",
                Message = message,
            };

            lock (_sync)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0);
            }

            Added?.Invoke(null, entry);
        }

        public static List<GameChatLogEntry> Snapshot()
        {
            lock (_sync)
                return new List<GameChatLogEntry>(_entries);
        }
    }
}
