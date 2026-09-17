using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Lets a second Roblox instance start alongside an already-running one, for the Instances
    /// page's multi-instance manager.
    ///
    /// Roblox creates a couple of named kernel objects at startup - ROBLOX_singletonEvent and
    /// ROBLOX_singletonMutex - and any later RobloxPlayerBeta.exe that finds them already existing
    /// quits immediately. This closes those two objects' handles from OUTSIDE the Roblox process
    /// using documented cross-process handle duplication (DuplicateHandle with
    /// DUPLICATE_CLOSE_SOURCE) - once nothing references an object any more, the kernel destroys
    /// it, so the next Roblox launch no longer sees it and starts normally. No code is injected
    /// into or runs inside Roblox's process; this only touches its handle table from the outside,
    /// the same mechanism Process Explorer's own "Close Handle" feature uses. This mirrors the
    /// technique several published open-source Roblox multi-instance launchers already use (e.g.
    /// MultiBlox, roblox-multi-instance-launcher) rather than the DLL-injection approach some other
    /// tools use instead, which is a meaningfully different (and much riskier, anti-cheat-adjacent)
    /// technique this deliberately avoids. Still unofficial and still not risk-free - Roblox could
    /// change how it enforces this at any time.
    ///
    /// There's no documented Win32 way to enumerate another process's handles by name, so this uses
    /// two long-stable but undocumented ntdll.dll APIs (NtQuerySystemInformation / NtQueryObject).
    /// Every NtQueryObject call runs on a disposable background thread with a timeout and is simply
    /// abandoned (never joined or killed) if it doesn't return quickly - a handful of handle types
    /// are documented to be able to hang that call forever, there's no safe way to cancel a thread
    /// stuck in a syscall, and not waiting on it is the standard mitigation every serious tool doing
    /// this (Process Hacker/System Informer included) actually uses. All buffer reads are bounds-
    /// checked against the managed array's own length regardless of how the OS reports the handle
    /// count, so a struct layout drift in some future Windows version can make this quietly find
    /// nothing rather than read out of bounds.
    /// </summary>
    internal static class SingletonMutexBypass
    {
        private const string LOG_IDENT = "SingletonMutexBypass";

        private static readonly string[] TargetObjectNames = { "ROBLOX_singletonEvent", "ROBLOX_singletonMutex" };

        private const int SystemExtendedHandleInformation = 64;
        private const int ObjectNameInformation = 1;
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        private const uint PROCESS_DUP_HANDLE = 0x0040;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
        private const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
        {
            public IntPtr Object;
            public IntPtr UniqueProcessId;
            public IntPtr HandleValue;
            public uint GrantedAccess;
            public ushort CreatorBackTraceIndex;
            public ushort ObjectTypeIndex;
            public uint HandleAttributes;
            public uint Reserved;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryObject(IntPtr Handle, int ObjectInformationClass, IntPtr ObjectInformation, int ObjectInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// Tries to close the named singleton handles inside the given Roblox process so a new
        /// instance can start. Returns how many were actually closed (0 if none were found - which
        /// can just mean this Windows version names/shapes the objects differently, not necessarily
        /// that anything went wrong).
        /// </summary>
        public static int TryFreeSingleton(int robloxProcessId)
        {
            int closed = 0;
            IntPtr hProcess = IntPtr.Zero;

            try
            {
                hProcess = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION, false, robloxProcessId);
                if (hProcess == IntPtr.Zero)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not open Roblox process {robloxProcessId} for handle inspection");
                    return 0;
                }

                byte[]? snapshot = QuerySystemHandles();
                if (snapshot is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "NtQuerySystemInformation failed");
                    return 0;
                }

                foreach (IntPtr sourceHandle in EnumerateHandlesForProcess(snapshot, robloxProcessId))
                {
                    string? name = TryGetHandleName(hProcess, sourceHandle);

                    if (name is null)
                        continue;

                    if (!TargetObjectNames.Any(t => name.EndsWith(t, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (DuplicateHandle(hProcess, sourceHandle, GetCurrentProcess(), out IntPtr closingHandle, 0, false, DUPLICATE_CLOSE_SOURCE))
                    {
                        CloseHandle(closingHandle);
                        closed++;
                        App.Logger.WriteLine(LOG_IDENT, $"Closed '{name}' in process {robloxProcessId}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            finally
            {
                if (hProcess != IntPtr.Zero)
                    CloseHandle(hProcess);
            }

            return closed;
        }

        private static byte[]? QuerySystemHandles()
        {
            int size = 1 << 20;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);

                try
                {
                    int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out int returnLength);

                    if (status == 0)
                    {
                        var result = new byte[returnLength];
                        Marshal.Copy(buffer, result, 0, returnLength);
                        return result;
                    }

                    if (status != STATUS_INFO_LENGTH_MISMATCH)
                        return null;

                    size = Math.Max(size * 2, returnLength + 65536);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return null;
        }

        private static IEnumerable<IntPtr> EnumerateHandlesForProcess(byte[] snapshot, int targetPid)
        {
            int headerSize = IntPtr.Size * 2;
            int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();

            GCHandle pinned = GCHandle.Alloc(snapshot, GCHandleType.Pinned);
            try
            {
                IntPtr basePtr = pinned.AddrOfPinnedObject();
                long numberOfHandles = IntPtr.Size == 8 ? Marshal.ReadInt64(basePtr) : Marshal.ReadInt32(basePtr);

                for (long i = 0; i < numberOfHandles; i++)
                {
                    long entryOffset = headerSize + i * entrySize;

                    // never trust the OS-reported count over the buffer we actually got back - a
                    // struct layout drift should make this find nothing, not read out of bounds
                    if (entryOffset < 0 || entryOffset + entrySize > snapshot.Length)
                        yield break;

                    var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(IntPtr.Add(basePtr, (int)entryOffset));

                    if (entry.UniqueProcessId.ToInt64() == targetPid)
                        yield return entry.HandleValue;
                }
            }
            finally
            {
                pinned.Free();
            }
        }

        private static string? TryGetHandleName(IntPtr sourceProcess, IntPtr sourceHandle)
        {
            if (!DuplicateHandle(sourceProcess, sourceHandle, GetCurrentProcess(), out IntPtr localHandle, DUPLICATE_SAME_ACCESS, false, 0))
                return null;

            try
            {
                string? name = null;
                var done = new ManualResetEventSlim(false);

                // NtQueryObject(ObjectNameInformation) can hang forever on a handful of handle
                // types (notably synchronous named pipes waiting on a peer) - run it on its own
                // thread and just stop waiting if it's slow, rather than ever blocking the caller
                var worker = new Thread(() =>
                {
                    try
                    {
                        name = QueryObjectName(localHandle);
                    }
                    catch
                    {
                        // best effort only
                    }
                    finally
                    {
                        done.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "SingletonHandleProbe",
                };

                worker.Start();

                if (!done.Wait(250))
                    return null; // abandon the thread; it will exit on its own if the syscall ever returns

                return name;
            }
            finally
            {
                CloseHandle(localHandle);
            }
        }

        private static string? QueryObjectName(IntPtr handle)
        {
            int size = 1024;
            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                int status = NtQueryObject(handle, ObjectNameInformation, buffer, size, out int returnLength);

                if (status == STATUS_INFO_LENGTH_MISMATCH && returnLength > size)
                {
                    Marshal.FreeHGlobal(buffer);
                    size = returnLength;
                    buffer = Marshal.AllocHGlobal(size);
                    status = NtQueryObject(handle, ObjectNameInformation, buffer, size, out returnLength);
                }

                if (status != 0)
                    return null;

                // UNICODE_STRING: ushort Length (bytes), ushort MaximumLength, then (on x64) 4 bytes
                // padding, then a pointer to the string data - here it points inside this same buffer
                ushort length = unchecked((ushort)Marshal.ReadInt16(buffer, 0));
                IntPtr stringPtr = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);

                if (length == 0 || stringPtr == IntPtr.Zero)
                    return null;

                return Marshal.PtrToStringUni(stringPtr, length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
