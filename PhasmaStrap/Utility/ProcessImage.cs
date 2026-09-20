using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    internal static class ProcessImage
    {
        public static string? PathOf(int pid)
        {
            IntPtr handle = OpenProcess(0x1000 , false, pid);
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
