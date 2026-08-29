using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Interop = PhasmaStrap.Integrations.Overlays.OverlayInterop;

namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>
    /// Tracks which WPF windows are "overlay" windows (so the compositor's foreground
    /// detection doesn't hide itself behind its own UI) and produces a human-readable
    /// explanation of why overlays might not be visible. Ported from Voidstrap's
    /// Overlays subsystem; RiShade/Anti Aliasing/Frame Generation specific causes were
    /// dropped since those subsystems aren't part of this port.
    /// </summary>
    public static class OverlayDiagnostics
    {
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly object _handleLock = new object();
        private static readonly System.Collections.Generic.HashSet<IntPtr> _registeredHandles = new System.Collections.Generic.HashSet<IntPtr>();
        private static readonly System.Collections.Generic.HashSet<IntPtr> _discoveredHandles = new System.Collections.Generic.HashSet<IntPtr>();
        private static IntPtr[] _overlayHandles = Array.Empty<IntPtr>();
        private static int _raisePending;

        public static void RegisterOverlayHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            lock (_handleLock)
            {
                _registeredHandles.Add(handle);
                PublishHandlesLocked();
            }
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        public static void UnregisterOverlayHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            lock (_handleLock)
            {
                _registeredHandles.Remove(handle);
                PublishHandlesLocked();
            }
        }

        public static void RaiseOverlayWindows()
        {
            var app = Application.Current;
            if (app?.Dispatcher == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                return;

            if (!app.Dispatcher.CheckAccess())
            {
                if (Interlocked.Exchange(ref _raisePending, 1) != 0)
                    return;
                try
                {
                    app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushRaiseOverlayWindows));
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _raisePending, 0);
                }
                return;
            }

            RaiseOverlayWindowsCore(app);
        }

        private static void FlushRaiseOverlayWindows()
        {
            Interlocked.Exchange(ref _raisePending, 0);
            var app = Application.Current;
            if (app?.Dispatcher == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                return;
            RaiseOverlayWindowsCore(app);
        }

        private static void RaiseOverlayWindowsCore(Application app)
        {
            var handles = new System.Collections.Generic.List<IntPtr>();
            foreach (Window window in app.Windows)
            {
                if (window == null || !IsOverlayWindow(window))
                    continue;
                try
                {
                    IntPtr handle = new WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        handles.Add(handle);
                        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                    }
                }
                catch
                {
                }
            }
            lock (_handleLock)
            {
                _discoveredHandles.Clear();
                foreach (IntPtr handle in handles)
                    _discoveredHandles.Add(handle);
                PublishHandlesLocked();
            }
        }

        private static void PublishHandlesLocked()
        {
            _registeredHandles.RemoveWhere(handle => handle == IntPtr.Zero || !IsWindow(handle));
            _discoveredHandles.RemoveWhere(handle => handle == IntPtr.Zero || !IsWindow(handle));
            var combined = new System.Collections.Generic.HashSet<IntPtr>(_registeredHandles);
            foreach (IntPtr handle in _discoveredHandles)
                combined.Add(handle);
            Volatile.Write(ref _overlayHandles, combined.ToArray());
        }

        public static bool IsOverlayHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return false;
            foreach (IntPtr overlay in Volatile.Read(ref _overlayHandles))
                if (overlay == handle)
                    return true;
            return false;
        }

        private static bool IsOverlayWindow(Window window)
        {
            string ns = window.GetType().Namespace ?? "";
            string name = window.GetType().Name;
            return ns.Contains("Overlay")
                || name.Contains("Overlay")
                || name.Contains("Crosshair");
        }

        public static string BuildReport()
        {
            using IDisposable? lease = TryAcquireTracker();

            var sb = new StringBuilder();
            var prop = App.Settings.Prop;

            bool hud = prop.OverlayHudEnabled;
            bool crosshair = prop.Crosshair;
            bool gpuCompositor = OverlaySettings.AnyEnabled;

            RobloxWindowRect roblox = ResolveRobloxRect();
            bool inGame = roblox.Valid;

            sb.AppendLine("PhasmaStrap overlay diagnostics");
            sb.AppendLine();

            if (!hud && !crosshair)
            {
                sb.AppendLine("CAUSE: Every overlay is turned off.");
                sb.AppendLine();
                sb.AppendLine("Nothing is set to show. Turn on what you want in Settings > Overlays.");
                return sb.ToString();
            }

            sb.AppendLine("What is turned on:");
            sb.AppendLine("  Stats overlay (FPS): " + OnOff(hud));
            sb.AppendLine("  Crosshair: " + OnOff(crosshair));
            sb.AppendLine("  GPU compositor running: " + OnOff(gpuCompositor));
            sb.AppendLine("  Roblox detected in a game: " + OnOff(inGame));
            sb.AppendLine();

            if (!inGame)
            {
                sb.AppendLine("CAUSE: Roblox is not in a game right now.");
                sb.AppendLine("Overlays only draw while a Roblox game window is open and focused. Join a game, then check again.");
                return sb.ToString();
            }

            bool likelyRobloxFullscreen = IsRobloxOwnFullscreen(roblox);

            sb.AppendLine("Most likely cause, in order:");
            sb.AppendLine();

            int n = 1;

            if (likelyRobloxFullscreen)
            {
                sb.AppendLine(n++ + ". Roblox is in its own Fullscreen display mode (exclusive fullscreen).");
                sb.AppendLine("   Windows gives an exclusive fullscreen game the whole screen and will not draw ANY external overlay on top of it, including PhasmaStrap's. This is the number one reason overlays vanish once you are in first person.");
                sb.AppendLine("   FIX: In Roblox, press Esc, open Settings, set Display Mode to Windowed.");
                sb.AppendLine();
            }

            if (n == 1)
            {
                sb.AppendLine("Everything looks correctly set up and the overlay window is open.");
                sb.AppendLine("If you still cannot see it, Roblox is almost certainly running in its own exclusive Fullscreen display mode. Press Esc in Roblox, open Settings and set Display Mode to Windowed.");
            }

            return sb.ToString();
        }

        private static IDisposable? TryAcquireTracker()
        {
            try
            {
                return RobloxWindowTracker.Acquire();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("OverlayDiagnostics", "Window tracker unavailable: " + ex.Message);
                return null;
            }
        }

        private static RobloxWindowRect ResolveRobloxRect() => RobloxWindowTracker.Current;

        private static bool IsRobloxOwnFullscreen(RobloxWindowRect roblox)
        {
            if (roblox.Hwnd == IntPtr.Zero)
                return false;
            try
            {
                if (!TryGetMonitorBounds(roblox.Hwnd, out int mLeft, out int mTop, out int mRight, out int mBottom))
                    return false;

                bool coversMonitor = roblox.Left <= mLeft + 1 && roblox.Top <= mTop + 1
                    && roblox.Left + roblox.Width >= mRight - 1
                    && roblox.Top + roblox.Height >= mBottom - 1;
                if (!coversMonitor)
                    return false;

                int style = GetWindowLong(roblox.Hwnd, GWL_STYLE);
                bool borderless = (style & (WS_CAPTION | WS_THICKFRAME)) == 0;
                return borderless;
            }
            catch
            {
                return false;
            }
        }

        private static string OnOff(bool value) => value ? "ON" : "off";

        private static bool TryGetMonitorBounds(IntPtr hwnd, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            IntPtr monitor = Interop.MonitorFromWindow(hwnd, Interop.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;
            var info = new Interop.MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<Interop.MONITORINFOEXW>() };
            if (!Interop.GetMonitorInfoW(monitor, ref info))
                return false;
            left = info.rcMonitor.Left;
            top = info.rcMonitor.Top;
            right = info.rcMonitor.Right;
            bottom = info.rcMonitor.Bottom;
            return true;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);
    }
}
