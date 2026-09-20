namespace PhasmaStrap.Networking
{
    public static class ProxyHealth
    {
        public sealed class State
        {
            public int HostPid { get; set; }
            public DateTime LastAcceptedUtc { get; set; }
            public DateTime LastRejectedUtc { get; set; }
            public string LastRejectedHost { get; set; } = "";
        }

        private static string FilePath => Path.Combine(Paths.LocalAppData, "PhasmaStrap", "AssetProxy", "health.json");

        private static readonly object Sync = new();
        private static State _mine = new();
        private static DateTime _lastWriteUtc;

        private static State? _read;
        private static DateTime _readAtUtc;

        public static void Hosting() => Update(s => { s.HostPid = Environment.ProcessId; }, force: true);

        public static void Accepted() => Update(s => s.LastAcceptedUtc = DateTime.UtcNow);

        public static void Rejected(string? host) => Update(s => { s.LastRejectedUtc = DateTime.UtcNow; s.LastRejectedHost = host ?? ""; }, force: true);

        private static void Update(Action<State> change, bool force = false)
        {
            lock (Sync)
            {
                change(_mine);
                _mine.HostPid = Environment.ProcessId;

                if (!force && (DateTime.UtcNow - _lastWriteUtc).TotalSeconds < 5)
                    return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(_mine));
                    _lastWriteUtc = DateTime.UtcNow;
                    _read = null;
                }
                catch (Exception)
                {
                }
            }
        }

        public static State Read()
        {
            lock (Sync)
            {
                if (_read is not null && (DateTime.UtcNow - _readAtUtc).TotalSeconds < 2)
                    return _read;

                try
                {
                    _read = File.Exists(FilePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new() : new();
                }
                catch (Exception)
                {
                    _read = new();
                }

                _readAtUtc = DateTime.UtcNow;
                return _read;
            }
        }

        public static bool IsHostedAnywhere()
        {
            if (AssetProxyServer.IsRunning)
                return true;

            int pid = Read().HostPid;
            if (pid <= 0)
                return false;

            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.HasExited || !process.ProcessName.StartsWith("PhasmaStrap", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch (Exception)
            {
                return false;
            }

            return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(e => e.Port == AssetProxyServer.Port && System.Net.IPAddress.IsLoopback(e.Address));
        }

        public static string? RobloxVerdict()
        {
            bool? trusts = AssetProxyCA.RunningRobloxTrustsProxy();
            if (trusts == false)
                return "Roblox was opened before its certificate bundle had the proxy's certificate - restart Roblox for the proxy to work";

            State state = Read();
            DateTime newest = state.LastAcceptedUtc > state.LastRejectedUtc ? state.LastAcceptedUtc : state.LastRejectedUtc;
            if (trusts is null || newest == default || (DateTime.UtcNow - newest).TotalMinutes > 30)
                return null;

            string at = newest.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

            return state.LastRejectedUtc > state.LastAcceptedUtc
                ? $"Roblox refused the proxy's certificate at {at} - the proxy isn't working this session. Restart Roblox; if it keeps happening, check the log"
                : $"Working - Roblox last connected through the proxy at {at}";
        }
    }
}
