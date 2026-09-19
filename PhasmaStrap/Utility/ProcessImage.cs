using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    // The full exe path of another process. Process.MainModule needs rights a game process
    // usually refuses; QueryFullProcessImageName only needs the limited query right.
    internal static class ProcessImage
    {
        public static string? PathOf(int pid)
        {
            IntPtr handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (handle == IntPtr.Zero)
                return null;

            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        // every running Roblox player: its version folder and when it started (UTC)
        public static List<(string Folder, DateTime StartedUtc)> RunningRoblox()
        {
            var list = new List<(string, DateTime)>();

            foreach (Process process in Process.GetProcessesByName(App.RobloxPlayerAppName))
            {
                using (process)
                {
                    try
                    {
                        string? exe = PathOf(process.Id);
                        if (exe is not null)
                            list.Add((Path.GetDirectoryName(exe)!, process.StartTime.ToUniversalTime()));
                    }
                    catch (Exception)
                    {
                        // exited meanwhile
                    }
                }
            }

            return list;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
