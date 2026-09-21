using System.IO.Compression;

namespace PhasmaStrap.Utility
{
    public static class CrashBundle
    {
        private const string LOG_IDENT = "CrashBundle";
        private const int NewestLogs = 6;

        public static string SuggestedName => $"PhasmaStrap diagnostics {DateTime.Now:yyyy-MM-dd HH-mm}.zip";

        public static int Write(string destination)
        {
            int added = 0;

            if (File.Exists(destination))
                File.Delete(destination);

            using FileStream stream = File.Create(destination);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            added += AddNewest(archive, Paths.Logs, "*.log", "PhasmaStrap logs", NewestLogs);
            added += AddNewest(archive, Paths.RobloxLogs, "*.log", "Roblox logs", 2);
            added += AddNewest(archive, Path.Combine(Paths.Base, "CrashReports"), "*.json", "Crash reports", 20);

            added += AddFile(archive, Path.Combine(Paths.Modifications, "ClientSettings", "ClientAppSettings.json"), "Your flags/ClientAppSettings.json");
            added += AddFile(archive, App.Settings.FileLocation, "Your settings/Settings.json");
            added += AddFile(archive, App.State.FileLocation, "Your settings/State.json");

            AddText(archive, "about.txt", About());

            App.Logger.WriteLine(LOG_IDENT, $"Wrote {added} file(s) to {destination}");
            return added;
        }

        private static string About()
        {
            var lines = new List<string>
            {
                $"PhasmaStrap {App.Version}",
                $"Bundled {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Windows {Environment.OSVersion.Version}",
                $"Machine has {Environment.ProcessorCount} processor thread(s)",
                "",
                "This holds your recent PhasmaStrap and Roblox logs, any crash reports, your FastFlags",
                "and your settings. Read it before sending it anywhere: the settings file records the",
                "games you have played and your account name.",
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static int AddNewest(ZipArchive archive, string folder, string pattern, string prefix, int take)
        {
            int added = 0;

            try
            {
                if (!Directory.Exists(folder))
                    return 0;

                foreach (string file in Directory.GetFiles(folder, pattern).OrderByDescending(File.GetLastWriteTimeUtc).Take(take))
                    added += AddFile(archive, file, $"{prefix}/{Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read {folder}: {ex.Message}");
            }

            return added;
        }

        private static int AddFile(ZipArchive archive, string path, string entryName)
        {
            try
            {
                if (!File.Exists(path))
                    return 0;

                using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using Stream target = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                source.CopyTo(target);

                return 1;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not add {path}: {ex.Message}");
                return 0;
            }
        }

        private static void AddText(ZipArchive archive, string entryName, string text)
        {
            try
            {
                using Stream target = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                using var writer = new StreamWriter(target);
                writer.Write(text);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write {entryName}: {ex.Message}");
            }
        }
    }
}
