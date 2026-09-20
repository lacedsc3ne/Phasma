namespace PhasmaStrap.Utility
{
    public static class CaptureStorage
    {
        public static Action<string>? Log;

        private static readonly string[] Extensions = { ".png", ".jpg", ".mp4", ".gif" };

        public sealed class Entry
        {
            public string Path = "";
            public long Bytes;
            public DateTime Modified;
            public bool ReadOnly;
        }

        public sealed class Result
        {
            public int Removed;
            public long FreedBytes;
        }

        public static List<Entry> Scan(IEnumerable<string> directories)
        {
            var entries = new List<Entry>();

            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (FileInfo file in new DirectoryInfo(directory).GetFiles())
                {
                    if (!Extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (file.Name.EndsWith(".editing.mp4", StringComparison.OrdinalIgnoreCase))
                        continue;

                    entries.Add(new Entry { Path = file.FullName, Bytes = file.Length, Modified = file.LastWriteTime, ReadOnly = file.IsReadOnly });
                }
            }

            return entries;
        }

        public static List<Entry> Plan(List<Entry> entries, long limitBytes, int maxAgeDays, DateTime now)
        {
            var doomed = new List<Entry>();
            if (entries.Count == 0 || (limitBytes <= 0 && maxAgeDays <= 0))
                return doomed;

            List<Entry> ordered = entries.OrderBy(e => e.Modified).ToList();
            Entry newest = ordered[^1];
            long total = entries.Sum(e => e.Bytes);

            List<Entry> candidates = ordered.Where(e => !ReferenceEquals(e, newest) && !e.ReadOnly).ToList();

            if (maxAgeDays > 0)
            {
                foreach (Entry entry in candidates.Where(e => (now - e.Modified).TotalDays > maxAgeDays).ToList())
                {
                    doomed.Add(entry);
                    candidates.Remove(entry);
                    total -= entry.Bytes;
                }
            }

            if (limitBytes > 0)
            {
                foreach (Entry entry in candidates)
                {
                    if (total <= limitBytes)
                        break;

                    doomed.Add(entry);
                    total -= entry.Bytes;
                }
            }

            return doomed;
        }

        public static Result Enforce(IEnumerable<string> directories, long limitBytes, int maxAgeDays, Action<string>? remove = null)
        {
            var result = new Result();
            remove ??= Recycle;

            foreach (Entry entry in Plan(Scan(directories), limitBytes, maxAgeDays, DateTime.Now))
            {
                try
                {
                    remove(entry.Path);
                    result.Removed++;
                    result.FreedBytes += entry.Bytes;
                    Log?.Invoke($"Recycled {entry.Path} ({entry.Bytes / 1048576.0:0.0} MB, from {entry.Modified:g})");
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not recycle {entry.Path}: {ex.Message}");
                }
            }

            return result;
        }

        private static void Recycle(string path) =>
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}
