namespace PhasmaStrap.Models.Persistable
{
    // A per-game override for Settings.OverlayPlaceProfiles - having an entry for a place means
    // "use these instead of the global HudEnabled/Crosshair toggles while in this place", not
    // "these are additional settings". Both fields are always explicit (no nullable bools) since
    // an entry existing at all already means "override".
    public sealed class OverlayPlaceProfile
    {
        public bool HudEnabled { get; set; }
        public bool CrosshairEnabled { get; set; }
    }
}
