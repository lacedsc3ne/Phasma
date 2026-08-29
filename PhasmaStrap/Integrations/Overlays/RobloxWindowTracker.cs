using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Interop = PhasmaStrap.Integrations.Overlays.OverlayInterop;

namespace PhasmaStrap.Integrations.Overlays
{
    public readonly struct RobloxWindowRect
    {
        public readonly IntPtr Hwnd;
        public readonly int Left;
        public readonly int Top;
        public readonly int Width;
        public readonly int Height;
        public readonly bool Valid;
        public readonly bool Foreground;

        public RobloxWindowRect(IntPtr hwnd, int left, int top, int width, int height, bool valid, bool foreground)
        {
            Hwnd = hwnd;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Valid = valid;
            Foreground = foreground;
        }

        public bool Matches(in RobloxWindowRect other)
        {
            return Hwnd == other.Hwnd
                && Left == other.Left
                && Top == other.Top
                && Width == other.Width
                && Height == other.Height
                && Valid == other.Valid
                && Foreground == other.Foreground;
        }
    }

    /// <summary>
    /// Tracks the live Roblox game client window (position, size, foreground state) via
    /// WinEvent hooks with a slow discovery-poll fallback. Ported from Voidstrap's
    /// Overlays subsystem; the Linux-specific measurement path was dropped since
    /// PhasmaStrap is Windows-only.
    /// </summary>
    public static class RobloxWindowTracker
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_DESTROY = 0x8001;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int OBJID_WINDOW = 0;

        private static readonly object _sync = new object();

        private static WinEventProc? _locationProc;
        private static WinEventProc? _foregroundProc;
        private static IntPtr _locationHook;
        private static IntPtr _destroyHook;
        private static IntPtr _foregroundHook;

        private static DispatcherTimer? _discoveryTimer;
        private static IntPtr _hwnd;
        private static uint _pid;
        private static RobloxWindowRect _current;
        private static volatile bool _started;
        private static int _publishPending;
        private static int _destroyPending;
        private static IntPtr _destroyedHwnd;
        private static int _ensurePending;
        private static int _consumerCount;

        public static event EventHandler<RobloxWindowRect>? Changed;

        public static IDisposable Acquire()
        {
            lock (_sync)
                _consumerCount++;
            Ensure();
            return new TrackerLease();
        }

        public static RobloxWindowRect Current
        {
            get
            {
                lock (_sync)
                    return _current;
            }
        }

        public static void Ensure()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            if (!dispatcher.CheckAccess())
            {
                if (Interlocked.Exchange(ref _ensurePending, 1) != 0)
                    return;
                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushEnsure));
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _ensurePending, 0);
                }
                return;
            }

            lock (_sync)
            {
                if (_started)
                    return;
                _started = true;
            }

            _foregroundProc = OnForegroundEvent;
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            _discoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _discoveryTimer.Tick += OnDiscoveryTick;
            _discoveryTimer.Start();

            OnDiscoveryTick(null, EventArgs.Empty);
        }

        private static void FlushEnsure()
        {
            Interlocked.Exchange(ref _ensurePending, 0);
            Ensure();
        }

        public static void Shutdown()
        {
            lock (_sync)
                _consumerCount = 0;
            StopTracking(true);
        }

        private static void ReleaseConsumer()
        {
            bool stop;
            lock (_sync)
            {
                if (_consumerCount > 0)
                    _consumerCount--;
                stop = _consumerCount == 0;
            }
            if (!stop)
                return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;
            if (dispatcher.CheckAccess())
            {
                StopIfUnused();
                return;
            }
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(StopIfUnused));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static void StopIfUnused()
        {
            lock (_sync)
            {
                if (_consumerCount != 0)
                    return;
            }
            StopTracking(false);
        }

        private static void StopTracking(bool clearHandlers)
        {
            lock (_sync)
            {
                if (!_started)
                {
                    if (clearHandlers)
                        Changed = null;
                    return;
                }
                _started = false;
            }

            var timer = _discoveryTimer;
            _discoveryTimer = null;
            if (timer != null)
            {
                try
                {
                    timer.Stop();
                    timer.Tick -= OnDiscoveryTick;
                }
                catch
                {
                }
            }

            ReleaseLocationHook();

            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _foregroundProc = null;

            _hwnd = IntPtr.Zero;
            _pid = 0;
            lock (_sync)
                _current = default;
            Interlocked.Exchange(ref _publishPending, 0);
            Interlocked.Exchange(ref _destroyPending, 0);
            Interlocked.Exchange(ref _ensurePending, 0);
            _destroyedHwnd = IntPtr.Zero;
            if (clearHandlers)
                Changed = null;
        }

        private sealed class TrackerLease : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                    ReleaseConsumer();
            }
        }

        private static void ReleaseLocationHook()
        {
            if (_locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(_locationHook);
                _locationHook = IntPtr.Zero;
            }
            if (_destroyHook != IntPtr.Zero)
            {
                UnhookWinEvent(_destroyHook);
                _destroyHook = IntPtr.Zero;
            }
            _locationProc = null;
        }

        private static void OnDiscoveryTick(object? sender, EventArgs e)
        {
            if (!_started)
                return;

            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd) && IsWindowVisible(_hwnd))
            {
                SetDiscoveryInterval(5000);
                Publish();
                return;
            }

            SetDiscoveryInterval(1000);

            if (_hwnd != IntPtr.Zero)
            {
                ReleaseLocationHook();
                _hwnd = IntPtr.Zero;
                _pid = 0;
            }

            IntPtr found = FindRobloxWindow(out uint pid);

            if (found == IntPtr.Zero)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    ReleaseLocationHook();
                    _hwnd = IntPtr.Zero;
                    _pid = 0;
                }
                Publish();
                return;
            }

            if (found != _hwnd || pid != _pid)
            {
                ReleaseLocationHook();
                _hwnd = found;
                _pid = pid;
                _locationProc = OnLocationEvent;
                _destroyHook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, IntPtr.Zero, _locationProc, pid, 0, WINEVENT_OUTOFCONTEXT);
                _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _locationProc, pid, 0, WINEVENT_OUTOFCONTEXT);
            }

            Publish();
        }

        private static void SetDiscoveryInterval(int milliseconds)
        {
            if (_discoveryTimer == null)
                return;
            TimeSpan interval = TimeSpan.FromMilliseconds(milliseconds);
            if (_discoveryTimer.Interval != interval)
                _discoveryTimer.Interval = interval;
        }

        private static void OnLocationEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (!_started || hwnd != _hwnd || idObject != OBJID_WINDOW)
                return;

            if (eventType == EVENT_OBJECT_DESTROY)
            {
                QueuePublish(hwnd, true);
                return;
            }

            if (eventType == EVENT_OBJECT_LOCATIONCHANGE)
                QueuePublish(hwnd, false);
        }

        private static void OnForegroundEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (_started)
                QueuePublish(IntPtr.Zero, false);
        }

        private static void QueuePublish(IntPtr hwnd, bool destroyed)
        {
            if (!_started)
                return;
            if (destroyed)
            {
                _destroyedHwnd = hwnd;
                Interlocked.Exchange(ref _destroyPending, 1);
            }
            if (Interlocked.Exchange(ref _publishPending, 1) != 0)
                return;

            Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                Interlocked.Exchange(ref _publishPending, 0);
                return;
            }
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushPublish));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _publishPending, 0);
            }
        }

        private static void FlushPublish()
        {
            Interlocked.Exchange(ref _publishPending, 0);
            if (!_started)
                return;
            if (Interlocked.Exchange(ref _destroyPending, 0) != 0 && _destroyedHwnd == _hwnd)
            {
                ReleaseLocationHook();
                _hwnd = IntPtr.Zero;
                _pid = 0;
            }
            Publish();
        }

        private static void Publish()
        {
            if (!_started)
                return;

            RobloxWindowRect next = Measure();
            EventHandler<RobloxWindowRect>? handlers;
            lock (_sync)
            {
                if (next.Matches(_current))
                    return;
                _current = next;
                handlers = Changed;
            }
            if (handlers == null)
                return;
            foreach (EventHandler<RobloxWindowRect> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(null, next);
                }
                catch (Exception ex)
                {
                    App.Logger?.WriteLine("RobloxWindowTracker::Publish", "Error: " + ex.Message);
                }
            }
        }

        private static RobloxWindowRect Measure()
        {
            IntPtr hwnd = _hwnd;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                return new RobloxWindowRect(hwnd, 0, 0, 0, 0, false, false);

            if (!GetClientRect(hwnd, out Interop.RECT client))
                return new RobloxWindowRect(hwnd, 0, 0, 0, 0, false, false);

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0)
                return new RobloxWindowRect(hwnd, 0, 0, 0, 0, false, false);

            Interop.POINT origin = new Interop.POINT { X = client.Left, Y = client.Top };
            if (!ClientToScreen(hwnd, ref origin))
                return new RobloxWindowRect(hwnd, 0, 0, 0, 0, false, false);

            return new RobloxWindowRect(hwnd, origin.X, origin.Y, width, height, true, IsRobloxForeground());
        }

        public static bool IsRobloxForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;
            if (foreground == _hwnd)
                return true;
            if (OverlayDiagnostics.IsOverlayHandle(foreground))
                return true;

            GetWindowThreadProcessId(foreground, out uint pid);
            return pid != 0 && pid == _pid;
        }

        private static readonly HashSet<uint> _findPids = new HashSet<uint>();
        private static EnumWindowsProc? _enumProc;
        private static IntPtr _findBest;
        private static uint _findBestPid;
        private static long _findBestArea;

        private static IntPtr FindRobloxWindow(out uint pid)
        {
            pid = 0;

            _findPids.Clear();
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName("RobloxPlayerBeta");
            }
            catch
            {
                return IntPtr.Zero;
            }
            try
            {
                foreach (Process process in processes)
                {
                    try { _findPids.Add((uint)process.Id); } catch { }
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    try { process.Dispose(); } catch { }
                }
            }
            if (_findPids.Count == 0)
                return IntPtr.Zero;

            _findBest = IntPtr.Zero;
            _findBestPid = 0;
            _findBestArea = 0;

            if (_enumProc == null)
                _enumProc = EnumWindowCallback;
            try
            {
                EnumWindows(_enumProc, IntPtr.Zero);
            }
            catch
            {
            }

            pid = _findBestPid;
            return _findBest;
        }

        private static bool EnumWindowCallback(IntPtr hwnd, IntPtr lparam)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out uint wpid);
            if (wpid == 0 || !_findPids.Contains(wpid))
                return true;

            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            if (!sb.ToString().Equals("WINDOWSCLIENT", StringComparison.Ordinal))
                return true;

            if (!GetClientRect(hwnd, out Interop.RECT rc))
                return true;
            long area = (long)(rc.Right - rc.Left) * (rc.Bottom - rc.Top);
            if (area > _findBestArea)
            {
                _findBestArea = area;
                _findBest = hwnd;
                _findBestPid = wpid;
            }
            return true;
        }

        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lparam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint pid, uint thread, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        private static bool GetClientRect(IntPtr hwnd, out Interop.RECT rect) => Interop.GetClientRect(hwnd, out rect);

        private static bool ClientToScreen(IntPtr hwnd, ref Interop.POINT point) => Interop.ClientToScreen(hwnd, ref point);

        private static bool IsWindow(IntPtr hwnd) => Interop.IsWindow(hwnd);

        private static bool IsWindowVisible(IntPtr hwnd) => Interop.IsWindowVisible(hwnd);

        private static bool IsIconic(IntPtr hwnd) => Interop.IsIconic(hwnd);

        private static IntPtr GetForegroundWindow() => Interop.GetForegroundWindow();

        private static void GetWindowThreadProcessId(IntPtr hwnd, out uint pid) => Interop.GetWindowThreadProcessId(hwnd, out pid);
    }
}
