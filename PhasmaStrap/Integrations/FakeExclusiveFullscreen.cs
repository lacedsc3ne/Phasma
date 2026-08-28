using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PhasmaStrap.Integrations
{
    // strips the Roblox game window's border/titlebar and resizes it to exactly cover its
    // monitor while a game is running, for lower input latency than Roblox's own windowed
    // fullscreen mode. Simplified from Voidstrap's version: that one cooperates with its
    // Overlays compositor (a backdrop window, DWM thumbnail mirroring, live z-order
    // tracking) that PhasmaStrap doesn't have; this just resizes the real window directly,
    // triggered on game join/leave like IntegrationWatcher and StudioRichPresence are.
    public static class FakeExclusiveFullscreen
    {
        private const string LOG_IDENT = "FakeExclusiveFullscreen";

        // WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_BORDER | WS_DLGFRAME
        private const int StyleMask = 0x00C00000 | 0x00040000 | 0x00020000 | 0x00010000 | 0x00080000 | 0x00800000 | 0x00400000;

        // WS_POPUP
        private const int WS_POPUP = unchecked((int)0x80000000);

        private static readonly object _sync = new();
        private static HWND _robloxHwnd;
        private static RECT _savedRect;
        private static int _savedStyle;
        private static int _savedExStyle;
        private static bool _applied;

        public static bool Enabled => App.Settings.Prop.FakeExclusiveFullscreen;

        public static void OnGameJoin()
        {
            if (Enabled)
                Apply();
            else
                Restore();
        }

        public static void OnGameLeave() => Restore();

        public static void Shutdown() => Restore();

        public static bool Apply()
        {
            lock (_sync)
            {
                HWND hwnd = FindRobloxWindow();
                if (hwnd == HWND.Null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No Roblox window found, cannot apply");
                    return false;
                }

                if (!TryGetMonitorBounds(hwnd, out RECT monitor))
                    return false;

                if (_applied && _robloxHwnd != hwnd)
                    RestoreLocked();

                int width = monitor.right - monitor.left;
                int height = monitor.bottom - monitor.top;
                if (width <= 1 || height <= 1)
                    return false;

                if (!_applied)
                {
                    if (!PInvoke.GetWindowRect(hwnd, out _savedRect))
                        return false;

                    _savedStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
                    _savedExStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
                    _robloxHwnd = hwnd;
                }

                int style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
                style &= ~StyleMask;
                style |= WS_POPUP;
                PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style);

                PInvoke.SetWindowPos(hwnd, HWND.Null, monitor.left, monitor.top, width, height,
                    SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                    | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);

                if (PInvoke.GetForegroundWindow() != hwnd)
                    PInvoke.SetForegroundWindow(hwnd);

                _applied = true;
                App.Logger.WriteLine(LOG_IDENT, $"Roblox window set to {width}x{height} borderless fullscreen");
                return true;
            }
        }

        public static void Restore()
        {
            lock (_sync)
                RestoreLocked();
        }

        private static void RestoreLocked()
        {
            if (!_applied || _robloxHwnd == HWND.Null)
                return;

            if (PInvoke.IsWindow(_robloxHwnd))
            {
                PInvoke.SetWindowLong(_robloxHwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, _savedStyle);
                PInvoke.SetWindowLong(_robloxHwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, _savedExStyle);
                PInvoke.SetWindowPos(_robloxHwnd, HWND.Null, _savedRect.left, _savedRect.top,
                    _savedRect.right - _savedRect.left, _savedRect.bottom - _savedRect.top,
                    SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                    | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
                App.Logger.WriteLine(LOG_IDENT, "Restored the Roblox window to its original size and style");
            }

            _applied = false;
            _robloxHwnd = HWND.Null;
        }

        private static HWND FindRobloxWindow()
        {
            Process[] processes = Process.GetProcessesByName("RobloxPlayerBeta");
            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return (HWND)process.MainWindowHandle;
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }

            return HWND.Null;
        }

        private static bool TryGetMonitorBounds(HWND hwnd, out RECT bounds)
        {
            bounds = default;

            HMONITOR monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            if (monitor.IsNull)
                return false;

            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!PInvoke.GetMonitorInfo(monitor, ref info))
                return false;

            bounds = info.rcMonitor;
            return true;
        }
    }
}
