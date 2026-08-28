namespace PhasmaStrap
{
    // deletes old files from PhasmaStrap's own logs/downloads and Roblox's logs/cache, on a
    // schedule the user picks, running after Roblox closes and before PhasmaStrap exits.
    // Ported from Voidstrap.
    public static class Cleaner
    {
        private const string LOG_IDENT = "Cleaner";

        public static readonly Dictionary<string, string> Directories = new()
        {
            { "PhasmaStrapLogs", Paths.Logs },
            { "PhasmaStrapCache", Paths.Downloads },
            { "RobloxLogs", Paths.RobloxLogs },
            { "RobloxCache", Paths.RobloxCache },
        };

        public static void DoCleaning()
        {
            App.Logger.WriteLine(LOG_IDENT, "Cleaner has started");

            if (App.Settings.Prop.CleanerOptions == Enums.CleanerOptions.Never)
            {
                App.Logger.WriteLine(LOG_IDENT, "Cleaner is set to Never, nothing to do");
                return;
            }

            int days = App.Settings.Prop.CleanerOptions switch
            {
                Enums.CleanerOptions.AfterLaunch => 0,
                Enums.CleanerOptions.OneDay => 1,
                Enums.CleanerOptions.OneWeek => 7,
                Enums.CleanerOptions.TwoWeeks => 14,
                Enums.CleanerOptions.ThreeWeeks => 21,
                Enums.CleanerOptions.OneMonth => 30,
                Enums.CleanerOptions.TwoMonths => 60,
                Enums.CleanerOptions.ThreeMonths => 90,
                _ => int.MaxValue,
            };

            DateTime threshold = days == int.MaxValue ? DateTime.MinValue : DateTime.Now.AddDays(-days);

            foreach ((string key, string directory) in Directories)
            {
                if (!App.Settings.Prop.CleanerDirectories.Contains(key))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Skipping {key}");
                    continue;
                }

                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    continue;

                try
                {
                    string[] files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
                    App.Logger.WriteLine(LOG_IDENT, $"Running cleaner in {key}, {files.Length} file(s) found");

                    string activeLog = App.Logger.FileLocation ?? "";

                    foreach (string file in files)
                    {
                        if (activeLog.Length > 0 && string.Equals(Path.GetFullPath(file), Path.GetFullPath(activeLog), StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!ShouldDelete(file, threshold, directory))
                            continue;

                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Unable to delete {file}");
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to clean up {directory}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Cleaner finished");
        }

        // only ever deletes files under a directory this class was explicitly told to clean,
        // so this is a defensive invariant check rather than a meaningful access boundary
        private static bool ShouldDelete(string file, DateTime threshold, string containingDirectory)
        {
            if (!File.Exists(file))
                return false;

            DateTime lastWrite, created;
            try
            {
                lastWrite = File.GetLastWriteTime(file);
                created = File.GetCreationTime(file);
            }
            catch
            {
                return false;
            }

            DateTime age = lastWrite > created ? created : lastWrite;
            if (age > threshold)
                return false;

            string fullFile = Path.GetFullPath(file);
            string fullContaining = Path.GetFullPath(containingDirectory);
            if (!fullFile.StartsWith(fullContaining, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{file} was outside its expected directory");

            return true;
        }
    }
}
