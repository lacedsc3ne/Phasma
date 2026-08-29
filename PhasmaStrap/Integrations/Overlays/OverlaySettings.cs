namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>
    /// Gates whether the compositor should be running at all. RiShade, Anti-Aliasing, and Frame
    /// Generation each run as a stage inside the compositor's own render loop (see
    /// OverlayCompositor.RenderFrame) rather than owning a separate pipeline, so the compositor
    /// needs to start even if the HUD and crosshair are both off but one of those three is on.
    /// </summary>
    public static class OverlaySettings
    {
        public static bool GameEffectsEnabled =>
            App.Settings.Prop.OverlayHudEnabled ||
            App.Settings.Prop.Crosshair ||
            App.Settings.Prop.RiShadeEnabled ||
            (App.Settings.Prop.AntiAliasingEnabled && App.Settings.Prop.AntiAliasingMethodIndex > 0) ||
            FrameGeneration.FrameGenSettings.ModeIndex > 0;

        public static bool AnyEnabled => OverlayHub.InGame && GameEffectsEnabled;
    }
}
