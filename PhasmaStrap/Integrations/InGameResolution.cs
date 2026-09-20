using PhasmaStrap.Utility;

namespace PhasmaStrap.Integrations
{
    public static class ForcedResolution
    {
        private const string LOG_IDENT = "ForcedResolution";

        private static readonly object _sync = new();
        private static DisplayMode? _savedMode;
        private static string? _savedDevice;
        private static bool _applied;

        public static void OnGameJoin(long placeId = 0)
        {
            lock (_sync)
            {
                string monitor;
                int width, height, refreshRate;

                if (placeId > 0 && App.Settings.Prop.InGameResolutionPlaceProfiles.TryGetValue(placeId.ToString(), out InGameResolutionProfile? profile))
                {
                    monitor = profile.Monitor;
                    width = profile.Width;
                    height = profile.Height;
                    refreshRate = profile.RefreshRate;
                    App.Logger.WriteLine(LOG_IDENT, $"Place {placeId} has its own resolution: {width}x{height}@{refreshRate}");
                }
                else if (App.Settings.Prop.ForceInGameResolution)
                {
                    monitor = App.Settings.Prop.InGameResolutionMonitor;
                    width = App.Settings.Prop.InGameResolutionWidth;
                    height = App.Settings.Prop.InGameResolutionHeight;
                    refreshRate = App.Settings.Prop.InGameResolutionRefreshRate;
                }
                else
                {
                    RestoreLocked();
                    return;
                }

                if (_applied)
                    RestoreLocked();

                string? device = string.IsNullOrWhiteSpace(monitor) ? null : monitor;

                DisplayMode? current = DisplaySystem.GetCurrentMode(device);

                if (current != null && current.Width == width && current.Height == height && current.RefreshRate == refreshRate)
                {
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
                RestoreLocked();
        }

        private static void RestoreLocked()
        {
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
