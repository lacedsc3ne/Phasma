namespace PhasmaStrap.Integrations.Nvidia
{
    public static class FrameLimiter
    {
        private const string LOG_IDENT = "FrameLimiter";

        public const uint SettingId = 0x10835002;

        public static bool Available => NvidiaProfileInspector.IsAvailable;

        public static string UnavailableReason => NvidiaProfileInspector.UnavailableReason;

        public static IReadOnlyList<int> Steps
        {
            get
            {
                List<int> steps = App.Settings.Prop.FrameLimitSteps
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(part => int.TryParse(part, out int value) ? value : 0)
                    .Where(value => value > 0 && value <= 1000)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();

                return steps.Count > 0 ? steps : new List<int> { 60, 120, 144, 240 };
            }
        }

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

        public static int Raise(int current)
        {
            if (current == 0)
                return 0;

            foreach (int step in Steps)
            {
                if (step > current)
                    return step;
            }

            return 0;
        }

        public static int Lower(int current)
        {
            IReadOnlyList<int> steps = Steps;

            if (current == 0)
                return steps[^1];

            for (int i = steps.Count - 1; i >= 0; i--)
            {
                if (steps[i] < current)
                    return steps[i];
            }

            return steps[0];
        }

        public static bool Set(int fps)
        {
            if (!Available)
                return false;

            try
            {
                NvidiaApplyResult result = NvidiaProfileInspector.Apply(new[] { new KeyValuePair<uint, uint>(SettingId, (uint)Math.Max(0, fps)) });

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
