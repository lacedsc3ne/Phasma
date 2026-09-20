using System.Threading.Tasks;

namespace PhasmaStrap.Integrations.FrameGeneration
{
    public static class FrameGenManager
    {
        private const string LOG_IDENT = "FrameGen";

        public static bool SetMode(int modeIndex, bool confirmed)
        {
            int nextMode = modeIndex > 0 ? 1 : 0;
            if (!confirmed && FrameGenSettings.ModeIndex == 0 && nextMode > 0)
                return false;

            App.Settings.Prop.FrameGenModeIndex = nextMode;
            App.Settings.Save();

            if (nextMode > 0)
                Task.Run(FrameGenPipeline.Prepare);

            App.Logger.WriteLine(LOG_IDENT, "Mode set to " + FrameGenSettings.ModeNames[FrameGenSettings.ModeIndex]);
            Overlays.OverlayHub.Refresh();

            return true;
        }

        public static void SetQuality(int quality)
        {
            App.Settings.Prop.FrameGenQuality = System.Math.Clamp(quality, 0, 2);
            App.Settings.Save();
        }
    }
}
