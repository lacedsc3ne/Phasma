using System.Threading.Tasks;

namespace PhasmaStrap.Integrations.FrameGeneration
{
    // Frame Generation no longer owns a background thread/overlay of its own - its interpolation
    // pipeline now runs as a stage inside the shared Overlays.OverlayCompositor (see
    // OverlayCompositor.RenderFrame). This just updates settings and asks OverlayHub to
    // start/stop the compositor immediately if the change means it should now be running (or no
    // longer needs to be).
    public static class FrameGenManager
    {
        private const string LOG_IDENT = "FrameGen";

        /// <summary>
        /// Toggles the master mode. modeIndex is clamped to 0 (Off) or 1 (Auto).
        /// Returns false if the user declined the warning prompt for turning it on.
        /// </summary>
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
