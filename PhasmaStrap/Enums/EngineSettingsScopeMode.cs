namespace PhasmaStrap.Enums
{
    // scope for FastFlagsPage's curated toggle set ("Engine Settings") - see
    // Bootstrapper.TryApplyEngineSettingsScopeAsync and Settings.EngineSettingsScopedPlaces.
    public enum EngineSettingsScopeMode
    {
        All,
        OnlyListedPlaces,
        AllExceptListedPlaces,
    }
}
