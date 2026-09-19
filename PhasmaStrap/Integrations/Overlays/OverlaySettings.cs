using PhasmaStrap.Models.Persistable;

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
        public static bool GameEffectsEnabled => OverlayWindowNeeded || App.Settings.Prop.StreamSafeEnabled;

        // something is drawn on the overlay window itself (stream-safe mode only needs its own
        // stream view window, so on its own it leaves the overlay hidden)
        public static bool OverlayWindowNeeded =>
            HudEnabled ||
            CrosshairEnabled ||
            App.Settings.Prop.RiShadeEnabled ||
            (App.Settings.Prop.AntiAliasingEnabled && App.Settings.Prop.AntiAliasingMethodIndex > 0) ||
            FrameGeneration.FrameGenSettings.ModeIndex > 0;

        public static bool AnyEnabled => OverlayHub.InGame && GameEffectsEnabled;

        /// <summary>
        /// Whether the stats HUD should actually be drawn right now, folding in Overlay Focus Mode
        /// (a manual, hotkey/toggle-driven "hide overlays for a moment" switch - not automatic
        /// capture-app detection, which Windows has no reliable general API for) and any per-game
        /// overlay profile override for the currently joined place.
        /// </summary>
        public static bool HudEnabled
        {
            get
            {
                if (App.Settings.Prop.OverlayFocusModeEnabled)
                    return false;

                return TryGetPlaceProfile(out var profile) ? profile.HudEnabled : App.Settings.Prop.OverlayHudEnabled;
            }
        }

        /// <summary>Same as <see cref="HudEnabled"/> but for the crosshair.</summary>
        public static bool CrosshairEnabled
        {
            get
            {
                if (App.Settings.Prop.OverlayFocusModeEnabled)
                    return false;

                return TryGetPlaceProfile(out var profile) ? profile.CrosshairEnabled : App.Settings.Prop.Crosshair;
            }
        }

        private static bool TryGetPlaceProfile(out OverlayPlaceProfile profile)
        {
            long placeId = OverlayHub.CurrentPlaceId;

            if (placeId != 0 && App.Settings.Prop.OverlayPlaceProfiles.TryGetValue(placeId.ToString(), out var found))
            {
                profile = found;
                return true;
            }

            profile = null!;
            return false;
        }
    }
}
