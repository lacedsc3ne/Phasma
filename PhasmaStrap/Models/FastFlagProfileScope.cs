using PhasmaStrap.Enums;

namespace PhasmaStrap.Models
{
    // A single FastFlag profile's own "Applies to" targeting - same shape/meaning as
    // EngineSettingsScopeMode/EngineSettingsScopedPlaces, just owned per-profile instead of being one
    // global scope. See Settings.FastFlagProfileScopes and Bootstrapper.TryApplyFastFlagProfileAsync.
    public class FastFlagProfileScope
    {
        public EngineSettingsScopeMode Mode { get; set; } = EngineSettingsScopeMode.OnlyListedPlaces;

        public List<string> Places { get; set; } = new();
    }
}
