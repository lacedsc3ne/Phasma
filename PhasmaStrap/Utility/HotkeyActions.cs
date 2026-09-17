namespace PhasmaStrap.Utility
{
    // The single source of truth for what hotkey-bindable actions exist. HotkeysPage (Settings
    // process) only needs the Id/DisplayName/Description to build its binding UI - it never has
    // a live GlobalHotkeyManager to query, since hotkeys only actually run inside an active
    // Watcher session (a separate process). Watcher.cs is the other consumer: it registers a
    // real callback against each of these same Ids. Keeping both sides keyed off these constants
    // (instead of duplicating string literals) is what keeps them from drifting apart.
    public static class HotkeyActions
    {
        public const string CleanRamNow = "CleanRamNow";
        public const string ToggleHeadsetAudio = "ToggleHeadsetAudio";
        public const string TakeScreenshot = "TakeScreenshot";

        public static readonly (string Id, string DisplayName, string Description)[] All =
        {
            (CleanRamNow, "Clean RAM Now", "Trims process working sets and purges the standby list - the same action as the Performance page's Clean RAM button."),
            (ToggleHeadsetAudio, "Toggle Headset Audio Boost", "Turns the headset loudness boost on or off without switching to Settings."),
            (TakeScreenshot, "Take Screenshot", "Captures the Roblox window and saves it to the Capture page's gallery."),
        };
    }
}
