namespace PhasmaStrap.Utility
{
    internal static class CrashReports
    {
        private const string LOG_IDENT = "CrashReports";
        private const int Keep = 40;

        private static string Folder => Path.Combine(Paths.Base, "CrashReports");

        public static CrashAnalyzer.Context BuildContext()
        {
            int mods = 0;
            try
            {
                if (Directory.Exists(Paths.Modifications))
                    mods = Directory.EnumerateFiles(Paths.Modifications, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith("ClientAppSettings.json", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
            }

            string roblox = Path.Combine(Paths.LocalAppData, "Roblox");

            return new CrashAnalyzer.Context
            {
                CustomFastFlags = App.FastFlags.Prop.Count,
                ActiveMods = mods,
                DumpDirectories =
                {
                    Path.Combine(roblox, "logs", "crashes"),
                    Path.Combine(roblox, "crashes"),
                    Path.Combine(Paths.LocalAppData, "CrashDumps"),
                },
            };
        }

        public static CrashReport Analyze(string logFile)
        {
            CrashAnalyzer.Log ??= message => App.Logger.WriteLine("CrashAnalyzer", message);
            return CrashAnalyzer.Analyze(logFile, BuildContext());
        }

        public static void Save(CrashReport report)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, $"{report.WhenLocal:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

                foreach (FileInfo old in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(f => f.Name).Skip(Keep))
                    old.Delete();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not save the report: {ex.Message}");
            }
        }

        public static List<CrashReport> List()
        {
            var result = new List<CrashReport>();

            try
            {
                if (!Directory.Exists(Folder))
                    return result;

                foreach (FileInfo file in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(f => f.Name))
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<CrashReport>(File.ReadAllText(file.FullName)) is CrashReport report)
                            result.Add(report);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list reports: {ex.Message}");
            }

            return result;
        }

        public static string? NewestLog()
        {
            try
            {
                bool running = Process.GetProcessesByName(App.RobloxPlayerAppName).Length > 0;

                return new DirectoryInfo(Paths.RobloxLogs).GetFiles("*.log")
                    .Where(f => f.Name.Contains("_Player_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault(f => !running || (DateTime.Now - f.LastWriteTime).TotalSeconds > 90)?.FullName;
            }
            catch
            {
                return null;
            }
        }
    }
}
