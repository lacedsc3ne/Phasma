using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    // Lightweight system CPU/RAM readouts for the overlay HUD's extra rows (OverlaysPage's
    // "Show CPU"/"Show RAM" toggles). Deliberately avoids System.Diagnostics.PerformanceCounter -
    // its first NextValue() call always returns 0 and needs a warm-up sample pair, which doesn't
    // suit a "call once a second, get a number" caller. GetSystemTimes gives the same delta-based
    // total-CPU number with none of that ceremony, following the same raw P/Invoke style already
    // used by AutoRamCleaner/MemoryManager for GlobalMemoryStatusEx/EmptyWorkingSet.
    internal static class SystemStatsSampler
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

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
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private static ulong _lastIdle, _lastKernel, _lastUser;
        private static bool _haveBaseline;

        private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

        /// <summary>
        /// Total system CPU usage (all processes, all cores) as a 0-100 percentage, based on the
        /// delta since the previous call. Returns 0 on the very first call, since there's no prior
        /// sample to diff against yet - callers polling once a second (like the HUD) will get a
        /// real number from the second call onward.
        /// </summary>
        public static double SampleCpuPercent()
        {
            if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
                return 0;

            ulong idleTicks = ToUInt64(idle);
            ulong kernelTicks = ToUInt64(kernel);
            ulong userTicks = ToUInt64(user);

            if (!_haveBaseline)
            {
                _lastIdle = idleTicks;
                _lastKernel = kernelTicks;
                _lastUser = userTicks;
                _haveBaseline = true;
                return 0;
            }

            ulong idleDelta = idleTicks - _lastIdle;
            ulong totalDelta = (kernelTicks - _lastKernel) + (userTicks - _lastUser);

            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;

            if (totalDelta == 0)
                return 0;

            double busy = 1.0 - (double)idleDelta / totalDelta;
            return Math.Clamp(busy * 100.0, 0, 100);
        }

        /// <summary>Physical RAM currently in use, as a 0-100 percentage.</summary>
        public static double SampleRamPercent()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

            if (!GlobalMemoryStatusEx(ref status))
                return 0;

            return status.dwMemoryLoad;
        }
    }
}
