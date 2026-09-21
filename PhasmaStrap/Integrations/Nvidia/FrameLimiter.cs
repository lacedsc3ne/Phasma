namespace PhasmaStrap.Integrations.Nvidia
{
    public static class FrameLimiter
    {
        private const string LOG_IDENT = "FrameLimiter";

        public const uint SettingId = 0x10835002;

        public static bool Available => NvidiaProfileInspector.IsAvailable;

        public static string UnavailableReason => NvidiaProfileInspector.UnavailableReason;

        public static int Current()
        {
            if (!Available)
                return 0;

            try
            {
                Dictionary<uint, uint> values = NvidiaProfileInspector.ReadValues(new[] { SettingId });
                return values.TryGetValue(SettingId, out uint found) ? (int)found : 0;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the driver frame limit: {ex.Message}");
                return 0;
            }
        }

        public static bool Set(int fps)
        {
            if (!Available)
                return false;

            try
            {
                NvidiaApplyResult result = NvidiaProfileInspector.Apply(new[] { new KeyValuePair<uint, uint>(SettingId, (uint)Math.Clamp(fps, 0, 1000)) });

                if (!result.Ok)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"The driver refused the frame limit: {result.Message}");

                    foreach (string failure in result.Failures)
                        App.Logger.WriteLine(LOG_IDENT, $"  {failure}");

                    return false;
                }

                App.Logger.WriteLine(LOG_IDENT, fps > 0 ? $"Driver frame limit set to {fps} fps" : "Driver frame limit turned off");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not set the driver frame limit: {ex.Message}");
                return false;
            }
        }

        public static string Describe(int fps) => fps > 0 ? $"{fps} fps" : "no limit";
    }
}
