using System;
using System.Diagnostics;
using System.Threading;

namespace PhasmaStrap.Integrations.RiShade
{
    /// <summary>
    /// Drives the lifecycle of <see cref="RiShadeOverlay"/> for one Roblox process, tied to
    /// <see cref="ActivityWatcher"/>'s game join/leave events (mirrors the Start()/Stop() pattern
    /// used elsewhere in the Watcher process). The overlay only runs while
    /// <c>App.Settings.Prop.RiShadeEnabled</c> is true and the player is actually in a game -
    /// there's no point capturing/compositing over the Roblox app shell.
    /// <para/>
    /// Unlike Voidstrap's static RiShadeManager (which watched a shared RiShadeSettings.json file
    /// for live cross-process edits), this port reads settings straight off <c>App.Settings.Prop</c>
    /// every frame (see RiShadeOverlay.UpdateParamsIfNeeded) - simpler, at the cost of only picking
    /// up a change to the master enabled toggle on the next game join rather than mid-session.
    /// </summary>
    public sealed class RiShadeManager : IDisposable
    {
        private const string LOG_IDENT = "RiShadeManager";

        private readonly int _robloxPid;
        private Thread? _thread;
        private CancellationTokenSource? _cts;

        public RiShadeManager(int robloxPid)
        {
            _robloxPid = robloxPid;
        }

        public void OnGameJoin()
        {
            if (!App.Settings.Prop.RiShadeEnabled)
                return;
            Start();
        }

        public void OnGameLeave() => Stop();

        private void Start()
        {
            if (_thread != null)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Starting RiShade overlay session");
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _thread = new Thread(() => RunLoop(token))
            {
                IsBackground = true,
                Name = "RiShadeOverlay",
            };
            _thread.Start();
        }

        private void RunLoop(CancellationToken token)
        {
            try
            {
                IntPtr hwnd = ResolveHwnd(token);
                while (!token.IsCancellationRequested && hwnd != IntPtr.Zero && App.Settings.Prop.RiShadeEnabled)
                {
                    var overlay = new RiShadeOverlay();
                    overlay.Run(hwnd, token);

                    if (token.IsCancellationRequested || !App.Settings.Prop.RiShadeEnabled)
                        break;

                    // the overlay only returns early on its own if the device/capture was lost or
                    // the roblox window disappeared before it could start - back off, then retry
                    token.WaitHandle.WaitOne(1000);
                    if (token.IsCancellationRequested)
                        break;
                    hwnd = ResolveHwnd(token);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private IntPtr ResolveHwnd(CancellationToken token)
        {
            for (int i = 0; i < 50 && !token.IsCancellationRequested; i++)
            {
                try
                {
                    using var process = Process.GetProcessById(_robloxPid);
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.MainWindowHandle;
                }
                catch
                {
                    return IntPtr.Zero;
                }
                token.WaitHandle.WaitOne(200);
            }
            return IntPtr.Zero;
        }

        private void Stop()
        {
            if (_thread == null)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Stopping RiShade overlay session");
            _cts?.Cancel();
            _thread.Join(3000);
            _cts?.Dispose();
            _cts = null;
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
