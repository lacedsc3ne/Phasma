namespace PhasmaStrap.Enums
{
    // what double-clicking the tray icon does while Roblox runs (NotifyIconWrapper). Stored as a
    // number in Settings.json - only ever add values at the end.
    public enum TrayDoubleClickAction
    {
        [EnumName(StaticName = "Do nothing")]
        Nothing = 0,

        [EnumName(StaticName = "Open settings")]
        OpenSettings = 1,

        [EnumName(StaticName = "Bring Roblox to the front")]
        ShowRoblox = 2,

        [EnumName(StaticName = "Open the tray menu")]
        OpenMenu = 3,

        [EnumName(StaticName = "Take a screenshot")]
        TakeScreenshot = 4,

        [EnumName(StaticName = "Save an instant replay")]
        SaveReplay = 5,

        [EnumName(StaticName = "Show server details")]
        ServerDetails = 6,

        [EnumName(StaticName = "Copy the invite link")]
        CopyInviteLink = 7,

        [EnumName(StaticName = "Clean RAM")]
        CleanRam = 8,
    }
}
