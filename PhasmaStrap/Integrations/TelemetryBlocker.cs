using System.ComponentModel;

namespace PhasmaStrap.Integrations
{
    // blackholes Roblox's own telemetry/crash-upload domains via the hosts file - a
    // separate, disjoint block from the one Networking.HostsFileManager writes for the
    // local MITM proxy, so the two coexist safely on the same physical hosts file.
    // Ported from Voidstrap.
    public static class TelemetryBlocker
    {
        private const string LOG_IDENT = "TelemetryBlocker";
        private const string Marker = "# PHASMASTRAP-TELEMETRYBLOCK";

        public static readonly string[] Domains =
        {
            "client-telemetry.roblox.com",
            "ephemeralcounters.api.roblox.com",
            "metrics.roblox.com",
            "tracing.roblox.com",
            "lms.roblox.com",
            "ncs.roblox.com",
            "gold.roblox.com",
            "abtesting.roblox.com",
            "upload.crashes.roblox.com",
            "upload.crashes.rbxinfra.com",
            "roblox.qq.com",
        };

        private static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        public static bool IsApplied()
        {
            try
            {
                if (!File.Exists(HostsPath))
                    return false;

                return File.ReadAllLines(HostsPath).Any(l => l.Contains(Marker, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }

        public static void ReconcileOnStartup()
        {
            bool applied = IsApplied();

            if (App.Settings.Prop.BlockRobloxTelemetry && !applied)
                RequestApply();
            else if (!App.Settings.Prop.BlockRobloxTelemetry && applied)
                RequestRemove();
        }

        public static bool RequestApply() => RunElevated("-writetelemetryblock");

        public static bool RequestRemove() => RunElevated("-removetelemetryblock");

        private static bool RunElevated(string flag)
        {
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

        // called only from within the short-lived elevated relaunch, never from the normal
        // app process
        public static bool ApplyElevated()
        {
            try
            {
                List<string> lines = ReadHostsWithoutBlock();
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add("");

                foreach (string domain in Domains)
                {
                    lines.Add($"0.0.0.0 {domain} {Marker}");
                    lines.Add($":: {domain} {Marker}");
                }

                File.WriteAllLines(HostsPath, lines);
                FlushDns();
                App.Logger.WriteLine(LOG_IDENT, $"Blocked {Domains.Length} telemetry domains");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static bool RemoveElevated()
        {
            try
            {
                if (!File.Exists(HostsPath))
                    return true;

                string[] all = File.ReadAllLines(HostsPath);
                string[] kept = all.Where(l => !l.Contains(Marker, StringComparison.Ordinal)).ToArray();

                if (kept.Length != all.Length)
                {
                    File.WriteAllLines(HostsPath, kept);
                    FlushDns();
                    App.Logger.WriteLine(LOG_IDENT, "Removed telemetry block entries");
                }

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        private static List<string> ReadHostsWithoutBlock()
        {
            if (!File.Exists(HostsPath))
                return new List<string>();

            return File.ReadAllLines(HostsPath).Where(l => !l.Contains(Marker, StringComparison.Ordinal)).ToList();
        }

        private static void FlushDns()
        {
            try
            {
                using Process? process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                process?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
