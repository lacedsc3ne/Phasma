using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
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

        public static double SampleRamPercent()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

            if (!GlobalMemoryStatusEx(ref status))
                return 0;

            return status.dwMemoryLoad;
        }
    }
}
