namespace PhasmaStrap.Integrations.GameChat
{
    public sealed class GameChatLogEntry
    {
        public DateTime Time { get; init; }
        public string Channel { get; init; } = "";
        public string Sender { get; init; } = "";
        public string Message { get; init; } = "";
    }

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
