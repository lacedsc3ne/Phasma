namespace PhasmaStrap.Integrations.AntiAliasing
{
    // Owns the lifetime of the standalone AntiAliasingOverlay worker thread.
    //
    // Upstream Voidstrap delegates this to a shared OverlayHub (part of its Overlays
    // subsystem) that PhasmaStrap doesn't have yet, so this manager runs the overlay on its
    // own dedicated background thread instead - started when a Roblox session begins and the
    // feature is enabled, stopped when the session ends, the feature is disabled, or the app
    // shuts down.
    public static class AntiAliasingManager
    {
        private const string LOG_IDENT = "AntiAliasing";

        private static readonly object _lock = new();
        private static Thread? _thread;
        private static CancellationTokenSource? _cts;
        private static bool _inGame;

        public static void SetEnabled(bool enabled)
        {
            App.Settings.Prop.AntiAliasingEnabled = enabled;
            App.Settings.Save();
            App.Logger.WriteLine(LOG_IDENT, $"Enabled set to {enabled}");
            Refresh();
        }

        public static void SetMethod(int methodIndex)
        {
            App.Settings.Prop.AntiAliasingMethodIndex = Math.Clamp(methodIndex, 0, AntiAliasingSettings.MethodNames.Length - 1);
            App.Settings.Save();
            App.Logger.WriteLine(LOG_IDENT, "Method set to " + AntiAliasingSettings.MethodNames[AntiAliasingSettings.MethodIndex]);
            Refresh();
        }

        public static void OnGameJoin()
        {
            lock (_lock)
            {
                _inGame = true;
            }
            Refresh();
        }

        public static void OnGameLeave()
        {
            lock (_lock)
            {
                _inGame = false;
            }
            Refresh();
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                _inGame = false;
                StopLocked();
            }
        }

        private static void Refresh()
        {
            lock (_lock)
            {
                bool shouldRun = _inGame && App.Settings.Prop.AntiAliasingEnabled && AntiAliasingSettings.MethodIndex > 0;

                if (shouldRun && _thread is null)
                    StartLocked();
                else if (!shouldRun && _thread is not null)
                    StopLocked();
            }
        }

        private static void StartLocked()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _thread = new Thread(() =>
            {
                try
                {
                    var overlay = new AntiAliasingOverlay();
                    overlay.Run(token);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            })
            {
                IsBackground = true,
                Name = "AntiAliasingOverlay",
            };

            App.Logger.WriteLine(LOG_IDENT, "Starting overlay thread");
            _thread.Start();
        }

        private static void StopLocked()
        {
            if (_thread is null)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Stopping overlay thread");
            _cts?.Cancel();
            _thread.Join(3000);
            _thread = null;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
