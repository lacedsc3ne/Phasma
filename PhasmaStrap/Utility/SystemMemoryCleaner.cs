using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    // A real system-wide "clean RAM" action, not the per-process trimming RobloxProcessOptimizer/
    // MemoryManager already do for just Roblox/PhasmaStrap. Two parts:
    //  1. EmptyWorkingSet on every process we can open (best effort - processes owned by another
    //     user/SYSTEM will fail to open and are silently skipped). This only moves pages out of each
    //     process's private working set into the standby list, it doesn't free physical memory by
    //     itself, but it's the part that needs no elevation.
    //  2. Purging the standby list itself (NtSetSystemInformation + MemoryPurgeStandbyList), which is
    //     what actually returns that memory to "free" - the same mechanism tools like RAMMap's
    //     "Empty > Standby List" or the classic EmptyStandbyList.exe use. This requires
    //     SeProfileSingleProcessPrivilege, which is only assignable on an elevated token, so it always
    //     runs as a short-lived elevated relaunch of PhasmaStrap itself (mirrors
    //     Integrations.ClassicHostRedirect's elevation pattern).
    internal static class SystemMemoryCleaner
    {
        private const string LOG_IDENT = "SystemMemoryCleaner";

        private const int SystemMemoryListInformation = 0x50;
        private const int MemoryPurgeStandbyList = 4;

        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;
        private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        public readonly record struct TrimResult(int ProcessesTrimmed, long BytesFreed);

        /// <summary>
        /// Trims the working set of every process we're able to open - no elevation required for
        /// processes owned by the current user, which is the vast majority of what's actually using
        /// meaningful memory on a typical desktop.
        /// </summary>
        public static TrimResult TrimAllProcessWorkingSets()
        {
            int trimmed = 0;
            long freed = 0;

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    IntPtr handle = IntPtr.Zero;

                    try
                    {
                        long before = process.WorkingSet64;

                        handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, process.Id);
                        if (handle == IntPtr.Zero)
                            continue;

                        if (!EmptyWorkingSet(handle))
                            continue;

                        process.Refresh();
                        long after = process.WorkingSet64;

                        trimmed++;
                        if (after < before)
                            freed += before - after;
                    }
                    catch
                    {
                        // protected/inaccessible/already-exited process - skip and move on
                    }
                    finally
                    {
                        if (handle != IntPtr.Zero)
                            CloseHandle(handle);
                    }
                }
            }

            return new TrimResult(trimmed, freed);
        }

        /// <summary>
        /// Relaunches PhasmaStrap elevated (one UAC prompt) to purge the system standby list, the part
        /// that actually returns memory to "free" rather than just moving it between lists. Returns
        /// false (not an exception) if elevation is declined or fails, so callers can report that
        /// distinctly from "the trim itself failed".
        /// </summary>
        public static bool PurgeStandbyListElevated()
        {
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
                return false;

            try
            {
                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = "-purgestandby",
                    UseShellExecute = true,
                    Verb = "runas"
                });

                if (process is null)
                    return false;

                process.WaitForExit(15000);
                return process.HasExited && process.ExitCode == 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                App.Logger.WriteLine(LOG_IDENT, "Standby list purge declined (UAC prompt cancelled)");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        /// <summary>
        /// The actual privileged purge - only ever called from the short-lived elevated relaunch
        /// started by <see cref="PurgeStandbyListElevated"/>, never from the normal app flow.
        /// </summary>
        public static bool PurgeStandbyListNow()
        {
            if (!TryEnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME))
                return false;

            IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buffer, MemoryPurgeStandbyList);
                int status = NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int));
                return status == 0;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryEnablePrivilege(string privilegeName)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    return false;

                var privileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                // AdjustTokenPrivileges returns true even when it silently skipped a privilege the
                // token doesn't hold (e.g. not actually elevated) - GetLastError distinguishes that
                // case (ERROR_NOT_ALL_ASSIGNED, 1300) from a genuine success.
                bool adjusted = AdjustTokenPrivileges(tokenHandle, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
                return adjusted && Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
    }
}
