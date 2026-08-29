using System;
using System.Threading.Tasks;

namespace PhasmaStrap.Integrations.FrameGeneration
{
    /// <summary>
    /// Lifecycle glue between settings/game-session events and <see cref="FrameGenOverlay"/>.
    /// </summary>
    public static class FrameGenManager
    {
        private const string LOG_IDENT = "FrameGen";

        private static bool _installed;
        private static bool _inGame;

        public static void Install()
        {
            if (_installed)
                return;
            _installed = true;

            if (FrameGenSettings.ModeIndex > 0)
                Task.Run(FrameGenPipeline.Prepare);

            App.Logger.WriteLine(LOG_IDENT, "Installed, mode is " + FrameGenSettings.ModeNames[FrameGenSettings.ModeIndex]);
        }

        /// <summary>
        /// Toggles the master mode. modeIndex is clamped to 0 (Off) or 1 (Auto).
        /// Returns false if the user declined the warning prompt for turning it on.
        /// </summary>
        public static bool SetMode(int modeIndex, bool confirmed)
        {
            Install();

            int nextMode = modeIndex > 0 ? 1 : 0;
            if (!confirmed && FrameGenSettings.ModeIndex == 0 && nextMode > 0)
                return false;

            App.Settings.Prop.FrameGenModeIndex = nextMode;
            App.Settings.Save();

            if (nextMode > 0)
                Task.Run(FrameGenPipeline.Prepare);

            App.Logger.WriteLine(LOG_IDENT, "Mode set to " + FrameGenSettings.ModeNames[FrameGenSettings.ModeIndex]);

            if (_inGame)
                FrameGenOverlay.Refresh();

            return true;
        }

        public static void SetQuality(int quality)
        {
            App.Settings.Prop.FrameGenQuality = Math.Clamp(quality, 0, 2);
            App.Settings.Save();
        }

        public static void OnGameJoin()
        {
            _inGame = true;
            if (FrameGenSettings.ModeIndex > 0)
            {
                Install();
                FrameGenOverlay.Start();
            }
        }

        public static void OnGameLeave()
        {
            _inGame = false;
            FrameGenOverlay.Stop();
        }

        public static void Shutdown()
        {
            _inGame = false;
            FrameGenOverlay.Stop();
            _installed = false;
        }
    }
}
