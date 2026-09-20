namespace PhasmaStrap.Utility
{
    public static class AccountQuickSwitch
    {
        public static Action<string>? Log;

        public sealed class Account
        {
            public long UserId { get; set; }
            public string Username { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Note { get; set; } = "";
            public string DatFile { get; set; } = "";
            public DateTime AddedUtc { get; set; }
            public DateTime LastUsedUtc { get; set; }

            [System.Text.Json.Serialization.JsonIgnore]
            public string Title => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Username ? Username : $"{DisplayName} (@{Username})";
        }

        private static string MetaPath(string folder) => Path.Combine(folder, "accounts.json");

        private static List<Account> ReadAll(string folder)
        {
            string path = MetaPath(folder);
            if (!File.Exists(path))
                return new List<Account>();

            return System.Text.Json.JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(path)) ?? new List<Account>();
        }

        public static List<Account> List(string folder)
        {
            try
            {
                return ReadAll(folder)
                    .Where(a => !string.IsNullOrEmpty(a.DatFile) && File.Exists(Path.Combine(folder, a.DatFile)))
                    .OrderByDescending(a => a.LastUsedUtc)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read the account library: {ex.Message}");
                return new List<Account>();
            }
        }

        public static async Task SwitchAsync(string folder, string liveCookiePath, long userId)
        {
            List<Account> all = ReadAll(folder);
            Account account = all.FirstOrDefault(a => a.UserId == userId)
                ?? throw new InvalidOperationException("That account is no longer in the library.");

            string saved = Path.Combine(folder, account.DatFile);
            if (string.IsNullOrEmpty(account.DatFile) || !File.Exists(saved))
                throw new InvalidOperationException("The saved login for this account is missing. Remove it on the Accounts page and add it again.");

            string safety = Path.Combine(folder, "_previous_session.dat");
            bool backedUp = false;

            try
            {
                if (File.Exists(liveCookiePath))
                {
                    await CopyWithRetryAsync(liveCookiePath, safety);
                    backedUp = true;
                }

                await ReplaceAsync(saved, liveCookiePath);
            }
            catch
            {
                if (backedUp && !File.Exists(liveCookiePath))
                {
                    try { await ReplaceAsync(safety, liveCookiePath); } catch { }
                }

                throw;
            }

            account.LastUsedUtc = DateTime.UtcNow;
            File.WriteAllText(MetaPath(folder), System.Text.Json.JsonSerializer.Serialize(all, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log?.Invoke($"Switched the Roblox login to {account.Username} ({account.UserId})");
        }

        private static async Task CopyWithRetryAsync(string source, string destination)
        {
            AccountGuard.MarkOwnAccess();

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                    await input.CopyToAsync(output);
                    return;
                }
                catch (IOException) when (attempt < 12)
                {
                    await Task.Delay(250);
                }
                catch (IOException)
                {
                    throw new IOException("Could not access the Roblox login file - Roblox may still be closing. Try again in a moment.");
                }
            }
        }

        private static async Task ReplaceAsync(string source, string liveCookiePath)
        {
            string temp = liveCookiePath + ".tmp";
            AccountGuard.MarkOwnAccess();

            try
            {
                await CopyWithRetryAsync(source, temp);

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(liveCookiePath))
                            File.Replace(temp, liveCookiePath, null);
                        else
                            File.Move(temp, liveCookiePath);
                        return;
                    }
                    catch (IOException) when (attempt < 12)
                    {
                        await Task.Delay(250);
                    }
                    catch (IOException)
                    {
                        throw new IOException("Could not write the Roblox login file - Roblox may still be closing. Try again in a moment.");
                    }
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}
