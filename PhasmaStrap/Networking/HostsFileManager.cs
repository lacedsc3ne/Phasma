using System.ComponentModel;

namespace PhasmaStrap.Networking
{
    public static class HostsFileManager
    {
        private const string LOG_IDENT = "HostsFileManager";

        private const string BlockStart = "# PhasmaStrap proxy - do not edit this block by hand";
        private const string BlockEnd = "# PhasmaStrap proxy end";

        public static string[] InterceptedHostnames => new[]
            {
                PresenceSpoofPolicy.Host,
                RobuxSpoofer.Host,
                UsernameSpoofer.Host,
                GameCreatorSpoofer.Host,
                AssetWarpPolicy.Host,
                AssetWarpThumbnailPolicy.Host,
            }
            .Concat(App.Settings.Prop.JoinServerPickerEnabled ? new[] { JoinPickerPolicy.Host } : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static string HostsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        public static bool IsBlockPresent()
        {
            try
            {
                if (!File.Exists(HostsFilePath))
                    return false;

                return File.ReadAllText(HostsFilePath).Contains(BlockStart, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsBlockCurrent()
        {
            try
            {
                if (!File.Exists(HostsFilePath))
                    return false;

                var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool inBlock = false, found = false;

                foreach (string raw in File.ReadAllLines(HostsFilePath))
                {
                    string line = raw.Trim();

                    if (line.Equals(BlockStart, StringComparison.Ordinal)) { inBlock = true; found = true; continue; }
                    if (line.Equals(BlockEnd, StringComparison.Ordinal)) { inBlock = false; continue; }

                    if (!inBlock)
                        continue;

                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        listed.Add(parts[1]);
                }

                return found && listed.SetEquals(InterceptedHostnames);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool RequestInstall()
        {
            return RunElevated("-writeproxyhosts");
        }

        public static bool RequestRemoval()
        {
            return RunElevated("-removeproxyhosts");
        }

        private static bool RunElevated(string flag)
        {
            const string LOG_IDENT = "HostsFileManager::RunElevated";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Paths.Process,
                    Arguments = flag,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using Process? process = Process.Start(startInfo);
                if (process is null)
                    return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                App.Logger.WriteLine(LOG_IDENT, "User declined the elevation prompt");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static bool WriteBlockElevated()
        {
            const string LOG_IDENT = "HostsFileManager::WriteBlockElevated";

            try
            {
                List<string> lines = File.Exists(HostsFilePath) ? File.ReadAllLines(HostsFilePath).ToList() : new List<string>();
                lines = StripExistingBlock(lines);

                lines.Add(BlockStart);
                foreach (string hostname in InterceptedHostnames)
                    lines.Add($"127.0.0.1 {hostname}");
                lines.Add(BlockEnd);

                File.WriteAllLines(HostsFilePath, lines);
                App.Logger.WriteLine(LOG_IDENT, "Hosts file block written");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static bool RemoveBlockElevated()
        {
            const string LOG_IDENT = "HostsFileManager::RemoveBlockElevated";

            try
            {
                if (!File.Exists(HostsFilePath))
                    return true;

                List<string> lines = StripExistingBlock(File.ReadAllLines(HostsFilePath).ToList());
                File.WriteAllLines(HostsFilePath, lines);
                App.Logger.WriteLine(LOG_IDENT, "Hosts file block removed");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        private static List<string> StripExistingBlock(List<string> lines)
        {
            var result = new List<string>();
            bool inBlock = false;

            foreach (string line in lines)
            {
                if (line.Trim().Equals(BlockStart, StringComparison.Ordinal))
                {
                    inBlock = true;
                    continue;
                }

                if (line.Trim().Equals(BlockEnd, StringComparison.Ordinal))
                {
                    inBlock = false;
                    continue;
                }

                if (!inBlock)
                    result.Add(line);
            }

            return result;
        }
    }
}
