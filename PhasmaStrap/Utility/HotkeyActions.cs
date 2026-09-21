namespace PhasmaStrap.Utility
{
    public static class HotkeyActions
    {
        public const string CleanRamNow = "CleanRamNow";
        public const string ToggleHeadsetAudio = "ToggleHeadsetAudio";
        public const string TakeScreenshot = "TakeScreenshot";
        public const string SaveInstantReplay = "SaveInstantReplay";
        public const string ToggleOverlayFocusMode = "ToggleOverlayFocusMode";

        public static readonly (string Id, string DisplayName, string Description)[] All =
        {
            (CleanRamNow, "Clean RAM Now", "Trims process working sets and purges the standby list - the same action as the Performance page's Clean RAM button."),
            (ToggleHeadsetAudio, "Toggle Headset Audio Boost", "Turns the headset loudness boost on or off without switching to Settings."),
            (TakeScreenshot, "Take Screenshot", "Captures the Roblox window and saves it to the Capture page's gallery."),
            (SaveInstantReplay, "Save Instant Replay", "Saves the last several seconds of gameplay as an MP4 clip - only does anything if Instant Replay is enabled on the Capture page."),
            (ToggleOverlayFocusMode, "Toggle Overlay Focus Mode", "Instantly hides the stats HUD and crosshair - useful right before a screenshot or clip so they don't end up in it."),
        };
    }
}
