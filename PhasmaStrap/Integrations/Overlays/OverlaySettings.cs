using PhasmaStrap.Models.Persistable;

namespace PhasmaStrap.Integrations.Overlays
{
    public static class OverlaySettings
    {
        public static bool GameEffectsEnabled => OverlayWindowNeeded || App.Settings.Prop.StreamSafeEnabled;

        public static bool OverlayWindowNeeded =>
            HudEnabled ||
            CrosshairEnabled ||
            App.Settings.Prop.RiShadeEnabled ||
            (App.Settings.Prop.AntiAliasingEnabled && App.Settings.Prop.AntiAliasingMethodIndex > 0) ||
            FrameGeneration.FrameGenSettings.ModeIndex > 0;

        public static bool AnyEnabled => OverlayHub.InGame && GameEffectsEnabled;

        public static bool NeedsCapture =>
            App.Settings.Prop.RiShadeEnabled ||
            (App.Settings.Prop.AntiAliasingEnabled && App.Settings.Prop.AntiAliasingMethodIndex > 0) ||
            FrameGeneration.FrameGenSettings.ModeIndex > 0 ||
            App.Settings.Prop.StreamSafeEnabled;

        public static bool HudEnabled
        {
            get
            {
                if (App.Settings.Prop.OverlayFocusModeEnabled)
                    return false;

                return TryGetPlaceProfile(out var profile) ? profile.HudEnabled : App.Settings.Prop.OverlayHudEnabled;
            }
        }

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
