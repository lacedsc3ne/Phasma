using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PhasmaStrap.Integrations.GameChat
{
    /// <summary>
    /// Owns the lifetime of the game chat overlay and its keyboard hook for a single Roblox session.
    /// Wired up from <see cref="Watcher"/> only when <c>App.Settings.Prop.GameChatEnabled</c> is true
    /// (the feature defaults to off, since it installs a global keyboard hook).
    ///
    /// The overlay itself runs on the main WPF dispatcher; the keyboard hook runs on its own dedicated
    /// STA thread with its own dispatcher, so a slow/blocked UI never risks Windows silently unhooking
    /// the low-level keyboard hook (WH_KEYBOARD_LL hook procedures have a short timeout).
    /// </summary>
    public class GameChatIntegration : IDisposable
    {
        private const string Tag = "GameChatIntegration";

        private readonly ActivityWatcher _activityWatcher;
        private readonly uint _robloxPid;
        private GameChatOverlay? _overlay;
        private GameChatKeyboardHook? _hook;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly object _overlayGate = new();
        private Thread? _hookThread;
        private Dispatcher? _hookDispatcher;
        private Task? _monitorTask;
        private int _hangHandled;
        private volatile bool _disposed;

        private const int SW_HIDE = 0;

        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);

        public GameChatIntegration(ActivityWatcher activityWatcher, int robloxPid)
        {
            _activityWatcher = activityWatcher;
            _robloxPid = (uint)robloxPid;
            _activityWatcher.OnGameJoin += OnGameJoin;
            _activityWatcher.OnGameLeave += OnGameLeave;

            StartOverlay();
            _monitorTask = Task.Run(() => MonitorOverlayAsync(_lifetimeCts.Token));
        }

        private void StartOverlay()
        {
            if (_disposed)
                return;
            Application? application = Application.Current;
            if (application == null)
            {
                App.Logger.WriteLine(Tag, "No application dispatcher is available, game chat will not start");
                return;
            }
            Dispatcher dispatcher = application.Dispatcher;
            if (dispatcher.CheckAccess())
                CreateOverlay();
            else
                dispatcher.BeginInvoke(new Action(CreateOverlay));
        }

        private void CreateOverlay()
        {
            if (_disposed)
                return;
            GameChatOverlay overlay;
            try
            {
                overlay = new GameChatOverlay(_activityWatcher, _robloxPid);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(Tag + "::CreateOverlay", ex);
                return;
            }
            lock (_overlayGate)
            {
                if (_disposed)
                {
                    try { overlay.Close(); } catch { }
                    return;
                }
                _overlay = overlay;
            }
            overlay.Closed += OnOverlayClosed;
            if (_activityWatcher.InGame)
                overlay.EnterGame(_activityWatcher.Data?.JobId ?? "");
            StartHookThread(overlay);
            App.Logger.WriteLine(Tag, "Game chat overlay created");
        }

        private void StartHookThread(GameChatOverlay overlay)
        {
            var thread = new Thread(() => HookThreadMain(overlay))
            {
                IsBackground = true,
                Name = "PhasmaStrap Game Chat Input"
            };
            thread.SetApartmentState(ApartmentState.STA);
            lock (_overlayGate)
                _hookThread = thread;
            thread.Start();
        }

        private void HookThreadMain(GameChatOverlay overlay)
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            GameChatKeyboardHook? hook = null;
            try
            {
                dispatcher.UnhandledException += OnHookDispatcherException;
                lock (_overlayGate)
                {
                    if (_disposed || !ReferenceEquals(_overlay, overlay))
                        return;
                    _hookDispatcher = dispatcher;
                }
                hook = new GameChatKeyboardHook(overlay, _robloxPid);
                hook.SetEnabled(_activityWatcher.InGame);
                lock (_overlayGate)
                {
                    if (_disposed)
                        return;
                    _hook = hook;
                }
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(Tag + "::HookThread", ex);
            }
            finally
            {
                dispatcher.UnhandledException -= OnHookDispatcherException;
                try
                {
                    hook?.Dispose();
                }
                catch
                {
                }
                lock (_overlayGate)
                {
                    if (ReferenceEquals(_hook, hook))
                        _hook = null;
                    if (ReferenceEquals(_hookDispatcher, dispatcher))
                        _hookDispatcher = null;
                    if (ReferenceEquals(_hookThread, Thread.CurrentThread))
                        _hookThread = null;
                }
            }
        }

        private static void OnHookDispatcherException(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            App.Logger.WriteException(Tag + "::HookDispatcher", e.Exception);
        }

        // Watches the overlay's heartbeat (ticked once a second on its own dispatcher) so that if the
        // overlay's UI thread ever hangs, we hide the (now unresponsive but still topmost/always-on-top)
        // window rather than leave an unkillable overlay stuck on top of the user's screen, and we turn
        // the feature back off so it doesn't keep happening every session.
        private async Task MonitorOverlayAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(2000, token).ConfigureAwait(false);
                    GameChatOverlay? overlay;
                    lock (_overlayGate)
                        overlay = _overlay;
                    if (overlay == null)
                        continue;
                    IntPtr handle = overlay.WindowHandle;
                    long heartbeat = overlay.LastHeartbeatMs;
                    if (handle == IntPtr.Zero || !IsWindowVisible(handle) || heartbeat == 0 || Environment.TickCount64 - heartbeat < 6000)
                        continue;
                    if (Interlocked.Exchange(ref _hangHandled, 1) != 0)
                        continue;
                    HideOverlayWindows(handle);
                    App.Logger.WriteLine(Tag, "Game chat was hidden because the overlay stopped responding");
                    if (App.Settings.Prop.GameChatEnabled)
                    {
                        App.Settings.Prop.GameChatEnabled = false;
                        App.Settings.Save();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("GameChatIntegration::Monitor", ex);
            }
        }

        private static void HideOverlayWindows(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            ShowWindowAsync(handle, SW_HIDE);
        }

        private void OnGameJoin(object? sender, EventArgs e)
        {
            SetHookEnabled(true);
            GameChatOverlay? overlay;
            lock (_overlayGate)
                overlay = _overlay;
            overlay?.EnterGame(_activityWatcher.Data?.JobId ?? "");
        }

        private void OnGameLeave(object? sender, EventArgs e)
        {
            // PhasmaStrap's ActivityWatcher doesn't expose a live "currently teleporting" flag (see the
            // matching note in GameChatOverlay.LeaveGame), so we always disable the hook on leave rather
            // than keeping it armed through a teleport's brief join/leave gap.
            SetHookEnabled(false);
            GameChatOverlay? overlay;
            lock (_overlayGate)
                overlay = _overlay;
            overlay?.LeaveGame();
        }

        private void ResetHookChatMode()
        {
            GameChatKeyboardHook? hook;
            Dispatcher? dispatcher;
            lock (_overlayGate)
            {
                hook = _hook;
                dispatcher = _hookDispatcher;
            }
            if (_disposed || hook == null || dispatcher == null)
                return;
            if (dispatcher.CheckAccess())
            {
                hook.ResetChatMode();
                return;
            }
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_disposed && ReferenceEquals(_hook, hook))
                        hook.ResetChatMode();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void SetHookEnabled(bool enabled)
        {
            GameChatKeyboardHook? hook;
            Dispatcher? dispatcher;
            lock (_overlayGate)
            {
                hook = _hook;
                dispatcher = _hookDispatcher;
            }
            if (_disposed || hook == null || dispatcher == null)
                return;
            if (dispatcher.CheckAccess())
            {
                hook.SetEnabled(enabled);
                return;
            }
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_disposed && ReferenceEquals(_hook, hook))
                        hook.SetEnabled(enabled);
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnOverlayClosed(object? sender, EventArgs e)
        {
            if (sender is not GameChatOverlay overlay)
                return;
            overlay.Closed -= OnOverlayClosed;
            Dispatcher? hookDispatcher;
            lock (_overlayGate)
            {
                if (!ReferenceEquals(_overlay, overlay))
                    return;
                hookDispatcher = _hookDispatcher;
                _overlay = null;
            }
            ShutdownHookDispatcher(hookDispatcher);
        }

        private static void ShutdownHookDispatcher(Dispatcher? dispatcher)
        {
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;
            try
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _activityWatcher.OnGameJoin -= OnGameJoin;
            _activityWatcher.OnGameLeave -= OnGameLeave;

            GameChatOverlay? overlay;
            Dispatcher? hookDispatcher;
            lock (_overlayGate)
            {
                overlay = _overlay;
                hookDispatcher = _hookDispatcher;
            }

            if (overlay != null)
                HideOverlayWindows(overlay.WindowHandle);
            _lifetimeCts.Cancel();

            if (overlay != null)
            {
                try
                {
                    Dispatcher dispatcher = overlay.Dispatcher;
                    if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(overlay.Close));
                }
                catch (InvalidOperationException)
                {
                }
            }

            ShutdownHookDispatcher(hookDispatcher);

            _lifetimeCts.Dispose();
            _monitorTask = null;
            GC.SuppressFinalize(this);
        }
    }
}
