namespace PhasmaStrap.Utility
{
    // Your own notes and favourites for Roblox friends. Purely local (<base>\FriendNotes.json) -
    // Roblox has no such thing, and nothing here is ever sent anywhere.
    //
    // Read by the Friends page and by FriendActivityMonitor ("alerts for favourites only"), which
    // live in different processes, so every read goes back to the file when it has changed.
    //
    // The file path is a parameter, so it can be exercised from a console harness.
    public sealed class FriendNotesStore
    {
        public sealed class Entry
        {
            public bool Favourite { get; set; }
            public string Note { get; set; } = "";

            [System.Text.Json.Serialization.JsonIgnore]
            public bool IsEmpty => !Favourite && string.IsNullOrWhiteSpace(Note);
        }

        public static Action<string>? Log;

        private static FriendNotesStore? _shared;
        public static FriendNotesStore Shared => _shared ??= new FriendNotesStore(Path.Combine(Paths.Base, "FriendNotes.json"));

        private readonly string _path;
        private readonly object _lock = new();
        private bool _dirty;
        private Dictionary<long, Entry> _entries = new();
        private DateTime _loadedStamp = DateTime.MinValue;
        private System.Threading.Timer? _saveTimer;

        public FriendNotesStore(string path)
        {
            _path = path;

            // a note typed just before the window is closed is still inside the save delay
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (_dirty)
                    Flush();
            };
        }

        private void Reload()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    if (_loadedStamp != DateTime.MinValue)
                        _entries = new();
                    _loadedStamp = DateTime.MinValue;
                    return;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(_path);
                if (stamp == _loadedStamp)
                    return;

                _entries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<long, Entry>>(File.ReadAllText(_path)) ?? new();
                _loadedStamp = stamp;
            }
            catch (Exception ex)
            {
                // keep what is in memory - a half-written file must not wipe the notes
                Log?.Invoke($"Could not read {_path}: {ex.Message}");
            }
        }

        public Entry Get(long userId)
        {
            lock (_lock)
            {
                Reload();
                return _entries.TryGetValue(userId, out Entry? entry)
                    ? new Entry { Favourite = entry.Favourite, Note = entry.Note }
                    : new Entry();
            }
        }

        public bool IsFavourite(long userId) => Get(userId).Favourite;

        public HashSet<long> Favourites()
        {
            lock (_lock)
            {
                Reload();
                return _entries.Where(pair => pair.Value.Favourite).Select(pair => pair.Key).ToHashSet();
            }
        }

        public void Set(long userId, bool favourite, string? note)
        {
            lock (_lock)
            {
                Reload();

                var entry = new Entry { Favourite = favourite, Note = (note ?? "").Trim() };
                if (entry.IsEmpty)
                    _entries.Remove(userId);
                else
                    _entries[userId] = entry;

                _dirty = true;

                // typing a note calls this once per pause - one write shortly after the last change
                _saveTimer ??= new System.Threading.Timer(_ => Flush(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                _saveTimer.Change(400, System.Threading.Timeout.Infinite);
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                    string temp = _path + ".tmp";
                    File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(_entries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temp, _path, true);
                    _dirty = false;

                    _loadedStamp = File.GetLastWriteTimeUtc(_path);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not write {_path}: {ex.Message}");
                }
            }
        }
    }
}
