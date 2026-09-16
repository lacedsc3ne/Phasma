namespace PhasmaStrap.Enums
{
    // Overlays tab (ModsPage) - how the Roblox homepage background preview/overlay is drawn.
    // Not to be confused with the in-game crosshair HUD overlay on OverlaysPage.
    public enum HomepageBackgroundMode
    {
        [EnumSort(Order = 1)]
        [EnumName(StaticName = "Off")]
        None,

        [EnumSort(Order = 2)]
        [EnumName(StaticName = "Solid color")]
        Solid,

        [EnumSort(Order = 3)]
        [EnumName(StaticName = "Gradient")]
        Gradient
    }
}
