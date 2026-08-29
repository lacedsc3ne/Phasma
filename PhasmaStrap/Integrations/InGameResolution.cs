using PhasmaStrap.Utility;

namespace PhasmaStrap.Integrations
{
    // Forces the chosen monitor to a specific resolution/refresh rate while a Roblox session is
    // active, and restores whatever mode was active before the session started once it ends.
    // Ported from Voidstrap's InGameResolutionApplier (which just wraps DisplaySystem.ApplyMode),
    // extended here with the "remember and restore the previous mode on game leave" half - hooked
    // from Watcher.cs the same way FakeExclusiveFullscreen/AudioDucker are.
    public static class ForcedResolution
    {
        private const string LOG_IDENT = "ForcedResolution";

        private static readonly object _sync = new();
        private static DisplayMode? _savedMode;
        private static string? _savedDevice;
        private static bool _applied;

        public static void OnGameJoin()
        {
            lock (_sync)
            {
                if (_applied)
                    return;

                string? device = string.IsNullOrWhiteSpace(App.Settings.Prop.InGameResolutionMonitor)
                    ? null
                    : App.Settings.Prop.InGameResolutionMonitor;

                DisplayMode? current = DisplaySystem.GetCurrentMode(device);
                int width = App.Settings.Prop.InGameResolutionWidth;
                int height = App.Settings.Prop.InGameResolutionHeight;
                int refreshRate = App.Settings.Prop.InGameResolutionRefreshRate;

                if (current != null && current.Width == width && current.Height == height && current.RefreshRate == refreshRate)
                {
                    // already at the target mode, nothing to restore later
                    return;
                }

                int code = DisplaySystem.ApplyMode(device, width, height, refreshRate);
                if (code != DisplaySystem.Success)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to apply resolution {width}x{height}@{refreshRate} on '{device ?? "primary"}': {DisplaySystem.DescribeError(code)}");
                    return;
                }

                _savedMode = current;
                _savedDevice = device;
                _applied = true;
                App.Logger.WriteLine(LOG_IDENT, $"Applied forced resolution {width}x{height}@{refreshRate} on '{device ?? "primary"}'");
            }
        }

        public static void OnGameLeave() => Restore();

        public static void Shutdown() => Restore();

        public static void Restore()
        {
            lock (_sync)
            {
                if (!_applied || _savedMode == null)
                {
                    _applied = false;
                    return;
                }

                int code = DisplaySystem.ApplyMode(_savedDevice, _savedMode.Width, _savedMode.Height, _savedMode.RefreshRate);
                if (code != DisplaySystem.Success)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to restore original resolution: {DisplaySystem.DescribeError(code)}");
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "Restored the original display resolution");
                }

                _applied = false;
                _savedMode = null;
                _savedDevice = null;
            }
        }
    }
}
