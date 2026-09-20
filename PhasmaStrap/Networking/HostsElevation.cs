using System.ComponentModel;

namespace PhasmaStrap.Networking
{
    public static class HostsElevation
    {
        private const string LOG_IDENT = "HostsElevation";

        public static bool Apply(bool proxy, bool telemetry)
        {
            string data = $"proxy={(proxy ? "on" : "off")};telemetry={(telemetry ? "on" : "off")}";
            App.Logger.WriteLine(LOG_IDENT, $"Requesting elevated hosts sync ({data})");
            return RunElevated("-applyhosts", data);
        }

        public static void ReconcileOnStartup()
        {
            bool proxyWanted = App.Settings.Prop.NetworkingProxyEnabled;
            bool telemetryWanted = App.Settings.Prop.BlockRobloxTelemetry;

            bool proxyOk = proxyWanted ? HostsFileManager.IsBlockCurrent() : !HostsFileManager.IsBlockPresent();
            bool telemetryOk = telemetryWanted == Integrations.TelemetryBlocker.IsApplied();

            if (proxyOk && telemetryOk)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Hosts file out of sync (proxy ok: {proxyOk}, telemetry ok: {telemetryOk}) - syncing in one elevated run");
            Apply(proxyWanted, telemetryWanted);
        }

        public static bool ApplyElevated(string? data)
        {
            bool proxy = false, telemetry = false;

            foreach (string part in (data ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split('=', 2);
                if (kv.Length != 2)
                    continue;

                bool on = kv[1].Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
                switch (kv[0].Trim().ToLowerInvariant())
                {
                    case "proxy": proxy = on; break;
                    case "telemetry": telemetry = on; break;
                }
            }

            bool proxyOk = proxy ? HostsFileManager.WriteBlockElevated() : HostsFileManager.RemoveBlockElevated();
            bool telemetryOk = telemetry ? Integrations.TelemetryBlocker.ApplyElevated() : Integrations.TelemetryBlocker.RemoveElevated();

            return proxyOk && telemetryOk;
        }

        private static bool RunElevated(string flag, string data)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Paths.Process,
                    Arguments = $"{flag} \"{data}\"",
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
    }
}
