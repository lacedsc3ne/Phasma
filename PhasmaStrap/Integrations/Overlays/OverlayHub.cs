using System;
using System.Threading;

namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>
    /// Owns the background thread that supervises OverlayCompositor sessions: starts one
    /// once Roblox's game window shows up and overlays are enabled, restarts it if it
    /// exits unexpectedly, and stops it on game leave / shutdown. Wired into
    /// PhasmaStrap.Watcher via ActivityWatcher.OnGameJoin/OnGameLeave, matching the
    /// Start()/Stop() integration pattern used elsewhere in PhasmaStrap.Integrations.
    ///
    /// Simplified from Voidstrap's OverlayHub: the standalone WPF crosshair-window
    /// fallback (used there so the crosshair could still show while the GPU compositor
    /// wasn't running) and the RiShade-panel hotkey plumbing were dropped since neither
    /// exists in this port - the crosshair here is only ever drawn by the compositor
    /// itself, and there is no RiShade panel.
    /// </summary>
    public static class OverlayHub
    {
        private const string LOG_IDENT = "Overlays";

        private static Thread? _thread;
        private static CancellationTokenSource? _cts;
        private static readonly object _lock = new object();
        private static volatile bool _shutdown;
        private static volatile bool _inGame;
        private static volatile bool _compositorLive;

        public static bool InGame => _inGame;

        internal static void SetCompositorLive(bool live) => _compositorLive = live;

        public static bool CompositorLive => _compositorLive;

        public static bool Refresh()
        {
            if (OverlaySettings.AnyEnabled)
                return Start();
            Stop();
            return true;
        }

        public static void Restart()
        {
            Stop();
            Refresh();
        }

        public static void OnGameJoin()
        {
            _inGame = true;
            Refresh();
        }

        public static void OnGameLeave()
        {
            _inGame = false;
            Refresh();
        }

        public static void Shutdown()
        {
            _shutdown = true;
            Stop();
        }

        private static bool Start()
        {
            if (_shutdown)
                return false;
            lock (_lock)
            {
                if (_thread != null)
                    return true;
                var cts = new CancellationTokenSource();
                Thread thread = new Thread(() => Supervise(cts))
                {
                    IsBackground = true,
                    Name = "Overlays",
                    Priority = ThreadPriority.BelowNormal,
                };
                _cts = cts;
                _thread = thread;
                try
                {
                    thread.Start();
                    return true;
                }
                catch (Exception ex)
                {
                    _thread = null;
                    _cts = null;
                    cts.Dispose();
                    App.Logger.WriteException("OverlayHub::Start", ex);
                    return false;
                }
            }
        }

        private static void Stop()
        {
            CancellationTokenSource? cts;
            lock (_lock)
            {
                cts = _cts;
            }
            if (cts == null)
                return;
            try
            {
                cts.Cancel();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayHub::Stop", ex);
            }
        }

        private static void Supervise(CancellationTokenSource owner)
        {
            CancellationToken token = owner.Token;
            Mutex? mutex = null;
            bool held = false;
            try
            {
                mutex = new Mutex(false, "PhasmaStrapOverlayCompositorActive");
                bool loggedWaitOwner = false;
                bool loggedWaitRoblox = false;
                int fastFailures = 0;
                while (!token.IsCancellationRequested)
                {
                    if (!OverlaySettings.AnyEnabled)
                        break;
                    if (!held)
                    {
                        try
                        {
                            held = mutex.WaitOne(0);
                        }
                        catch (AbandonedMutexException)
                        {
                            held = true;
                        }
                        if (!held)
                        {
                            if (!loggedWaitOwner)
                            {
                                loggedWaitOwner = true;
                                App.Logger.WriteLine(LOG_IDENT, "Another PhasmaStrap process is running the compositor, standing by");
                            }
                            if (token.WaitHandle.WaitOne(2000))
                                break;
                            continue;
                        }
                        App.Logger.WriteLine(LOG_IDENT, "This process now owns the compositor");
                    }

                    IntPtr sessionHwnd = RobloxWindowTracker.Current.Hwnd;
                    if (sessionHwnd == IntPtr.Zero)
                    {
                        if (!loggedWaitRoblox)
                        {
                            loggedWaitRoblox = true;
                            App.Logger.WriteLine(LOG_IDENT, "Waiting for the Roblox window");
                        }
                        if (token.WaitHandle.WaitOne(1000))
                            break;
                        continue;
                    }
                    loggedWaitRoblox = false;

                    long sessionStartedMs = Environment.TickCount64;
                    using (var runCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        var runToken = runCts.Token;
                        var watcher = new Thread(() => WatchRoblox(runCts))
                        {
                            IsBackground = true,
                            Name = "OverlaysWatch",
                            Priority = ThreadPriority.BelowNormal,
                        };
                        watcher.Start();
                        try
                        {
                            var compositor = new OverlayCompositor();
                            compositor.Run(runToken);
                        }
                        finally
                        {
                            runCts.Cancel();
                            watcher.Join(1500);
                        }
                    }

                    if (!OverlaySettings.AnyEnabled)
                        break;
                    IntPtr currentHwnd = RobloxWindowTracker.Current.Hwnd;
                    if (Environment.TickCount64 - sessionStartedMs < 3000 && sessionHwnd != IntPtr.Zero && currentHwnd == sessionHwnd)
                        fastFailures++;
                    else
                        fastFailures = 0;
                    if (fastFailures == 3)
                        App.Logger.WriteLine(LOG_IDENT, "Compositor keeps ending quickly, retrying every 10 seconds");
                    if (!token.IsCancellationRequested)
                        App.Logger.WriteLine(LOG_IDENT, "Compositor session ended, waiting for Roblox again");
                    if (token.WaitHandle.WaitOne(fastFailures >= 3 ? 10000 : 1000))
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayHub::Supervise", ex);
            }
            finally
            {
                if (held && mutex != null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }
                mutex?.Dispose();
                CompleteThread(owner);
            }
        }

        private static void CompleteThread(CancellationTokenSource owner)
        {
            bool dispose = false;
            bool restart = false;
            lock (_lock)
            {
                if (ReferenceEquals(_thread, Thread.CurrentThread) && ReferenceEquals(_cts, owner))
                {
                    _thread = null;
                    _cts = null;
                    dispose = true;
                    restart = !_shutdown && OverlaySettings.AnyEnabled;
                }
            }
            if (dispose)
                owner.Dispose();
            if (restart)
                ThreadPool.QueueUserWorkItem(_ => Start());
        }

        private static void WatchRoblox(CancellationTokenSource runCts)
        {
            try
            {
                while (!runCts.IsCancellationRequested)
                {
                    if (RobloxWindowTracker.Current.Hwnd == IntPtr.Zero)
                    {
                        runCts.Cancel();
                        break;
                    }
                    if (runCts.Token.WaitHandle.WaitOne(750))
                        break;
                }
            }
            catch
            {
            }
        }
    }
}
