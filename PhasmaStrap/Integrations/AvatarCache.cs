namespace PhasmaStrap.Integrations
{
    /// <summary>
    /// Keeps avatar headshots on disk (LocalAppData\PhasmaStrap\Avatars\{userId}.png) so the
    /// Friends/Accounts lists don't re-download every picture on every visit. Entries are
    /// refreshed after a few days so avatar changes still show up.
    /// </summary>
    public static class AvatarCache
    {
        private const string LOG_IDENT = "AvatarCache";

        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

        private static readonly SemaphoreSlim Throttle = new(6);

        public static string Folder => Path.Combine(Paths.LocalAppData, "PhasmaStrap", "Avatars");

        /// <summary>Returns the cached file for a user if it exists and is fresh, else null.</summary>
        public static string? TryGetFresh(long userId)
        {
            try
            {
                string path = Path.Combine(Folder, $"{userId}.png");
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < MaxAge && new FileInfo(path).Length > 0)
                    return path;
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>Downloads a headshot into the cache and returns the local path (null on failure).</summary>
        public static async Task<string?> DownloadAsync(long userId, string url, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            await Throttle.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"{userId}.png");
                string temp = path + ".part";

                byte[] bytes = await App.HttpClient.GetByteArrayAsync(url, ct);
                if (bytes.Length == 0)
                    return null;

                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, true);
                return path;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not cache avatar for {userId}: {ex.Message}");
                return null;
            }
            finally
            {
                Throttle.Release();
            }
        }

        public static void Prune(TimeSpan olderThan)
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return;

                foreach (FileInfo file in new DirectoryInfo(Folder).GetFiles())
                {
                    if (DateTime.UtcNow - file.LastWriteTimeUtc > olderThan)
                        file.Delete();
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
