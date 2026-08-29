using System;

namespace PhasmaStrap.Integrations.FrameGeneration
{
    public static class FrameGenSettings
    {
        public static readonly string[] ModeNames = new[] { "Off", "Auto" };

        public static int ModeIndex => Math.Clamp(App.Settings.Prop.FrameGenModeIndex, 0, ModeNames.Length - 1);

        public static bool IsAuto(int modeIndex) => modeIndex == 1;

        public static int QualityIndex => Math.Clamp(App.Settings.Prop.FrameGenQuality, 0, 2);
    }
}
