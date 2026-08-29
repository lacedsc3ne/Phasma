namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>
    /// Simplified from Voidstrap's OverlaySettings: the original gated the compositor
    /// on RiShade/Anti Aliasing/Frame Generation being enabled (GameEffectsEnabled) and
    /// also drove a homepage-background compositor mode. Neither RiShade/AA/FrameGen nor
    /// the homepage background subsystem are part of this port, so the compositor here
    /// only needs to run while in a Roblox game and something it draws itself (the stats
    /// HUD or the crosshair) is turned on.
    /// </summary>
    public static class OverlaySettings
    {
        public static bool GameEffectsEnabled =>
            App.Settings.Prop.OverlayHudEnabled || App.Settings.Prop.Crosshair;

        public static bool AnyEnabled => OverlayHub.InGame && GameEffectsEnabled;
    }
}
