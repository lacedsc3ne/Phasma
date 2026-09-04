using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    // Periodically checks system memory pressure and runs the same unelevated trim
    // SystemMemoryCleaner.TrimAllProcessWorkingSets() does, but only when memory is actually under
    // pressure ("if not already [clean]") rather than on a fixed schedule regardless of need. Runs
    // for the lifetime of a watched Roblox session (see Watcher.cs), matching where
    // RobloxProcessOptimizer/SystemPerformanceBoost's other session-scoped tweaks live - the point
    // is to keep memory free for the game you're actually playing, not to run as a general background
    // service. Deliberately does NOT include the elevated standby-list purge from the manual "Clean
    // RAM" button - prompting UAC unattended on a timer would be both intrusive and indistinguishable
    // from malware behavior, so that part stays a manual, explicit action.
    internal static class AutoRamCleaner
    {
        private const string LOG_IDENT = "AutoRamCleaner";

        private const int PollIntervalMs = 5 * 60 * 1000;

        // MEMORYSTATUSEX.dwMemoryLoad is already "percent of physical RAM in use" - trim once usage
        // crosses this, and don't bother again until it's back above it (no point trimming an already-
        // clean system every 5 minutes)
        private const uint MemoryLoadTriggerPercent = 85;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;

        public static void Start()
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _loopTask = null;
        }

        private static async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsMemoryUnderPressure())
                    {
                        SystemMemoryCleaner.TrimResult result = SystemMemoryCleaner.TrimAllProcessWorkingSets();
                        App.Logger.WriteLine(LOG_IDENT, $"Memory usage was high - auto-trimmed {result.ProcessesTrimmed} processes (~{result.BytesFreed / 1048576.0:0.#} MB)");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }

                try
                {
                    await Task.Delay(PollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static bool IsMemoryUnderPressure()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

            if (!GlobalMemoryStatusEx(ref status))
                return false;

            return status.dwMemoryLoad >= MemoryLoadTriggerPercent;
        }
    }
}
