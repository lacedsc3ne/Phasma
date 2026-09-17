using System.ComponentModel;

namespace PhasmaStrap.Networking
{
    // manages the block of hosts-file entries that redirect specific Roblox API hostnames
    // to the local proxy. Writing to the hosts file needs administrator rights, which
    // PhasmaStrap does not run with by default (and should not, just for this one
    // feature) - so the actual write happens in a short-lived elevated relaunch of the
    // app itself (-writeproxyhosts / -removeproxyhosts), triggered here via a single UAC
    // prompt, rather than elevating the whole application.
    public static class HostsFileManager
    {
        private const string LOG_IDENT = "HostsFileManager";

        private const string BlockStart = "# PhasmaStrap proxy - do not edit this block by hand";
        private const string BlockEnd = "# PhasmaStrap proxy end";

        // NOTE: AssetWarpPolicy.Host/AssetWarpThumbnailPolicy.Host are always included here,
        // same as the other policies below - they're unconditionally registered with
        // AssetProxyServer too (see NetworkingController.RegisterAssetWarpHosts), and it's the
        // policies themselves (AssetWarpPolicy.IsEnabled / AssetWarpThumbnailPolicy.IsEnabled)
        // that gate whether anything actually gets stripped. Without an entry here, Windows
        // would resolve those hostnames normally and Roblox would talk to them directly,
        // bypassing the proxy (and therefore AssetWarp) entirely regardless of its toggles.
        public static readonly string[] InterceptedHostnames = new[]
            {
                PresenceSpoofPolicy.Host,
                RobuxSpoofer.Host,
                UsernameSpoofer.Host,
                GameCreatorSpoofer.Host,
                AssetWarpPolicy.Host,
                AssetWarpThumbnailPolicy.Host,
            }
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

        // true only when the block exists AND lists exactly the hostnames this build intercepts -
        // a block written by an older build (different host list) would leave new policies
        // bypassed, or worse, keep redirecting a host nothing listens for any more
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

        // runs the elevated write, prompting for UAC once. Returns true only if the
        // elevated process reports success.
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
                // the user declined the UAC prompt
                App.Logger.WriteLine(LOG_IDENT, "User declined the elevation prompt");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        // called only from within the short-lived elevated process (already running as
        // administrator at this point), never from the normal app process
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
