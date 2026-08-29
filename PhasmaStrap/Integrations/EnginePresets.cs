using PhasmaStrap.Models.Persistable;

namespace PhasmaStrap.Integrations
{
    // Named bundles of the Roblox process optimizer's 5 tunable properties (PerformancePage's
    // "Runtime optimizer" section), plus per-place resolution so a specific game can either use a
    // different bundle than the global default or be excluded from optimization entirely. Mirrors
    // FastFlagPlaceProfiles/FastFlagProfiles (named bundle assigned per place ID) and
    // MatchmakerExcludedPlaces (a flat exclusion list) - see Bootstrapper.TryApplyFastFlagProfileAsync
    // and TryApplyMatchmakingAsync for the equivalent patterns elsewhere in this codebase.
    public sealed record EnginePresetValues(
        bool OptimizeRoblox,
        bool RobloxEfficiencyMode,
        bool ReduceMemoryOutOfFocus,
        string SelectedCpuPriority,
        string RobloxPriorityLimit);

    public static class EnginePresets
    {
        public static readonly IReadOnlyDictionary<string, EnginePresetValues> Presets = new Dictionary<string, EnginePresetValues>
        {
            ["Default"] = new EnginePresetValues(
                OptimizeRoblox: false,
                RobloxEfficiencyMode: false,
                ReduceMemoryOutOfFocus: false,
                SelectedCpuPriority: "Automatic",
                RobloxPriorityLimit: "Normal"),

            ["Balanced"] = new EnginePresetValues(
                OptimizeRoblox: true,
                RobloxEfficiencyMode: false,
                ReduceMemoryOutOfFocus: true,
                SelectedCpuPriority: "Automatic",
                RobloxPriorityLimit: "Above Normal"),

            ["Performance"] = new EnginePresetValues(
                OptimizeRoblox: true,
                RobloxEfficiencyMode: false,
                ReduceMemoryOutOfFocus: false,
                SelectedCpuPriority: "Automatic",
                RobloxPriorityLimit: "High"),

            ["Power Saver"] = new EnginePresetValues(
                OptimizeRoblox: true,
                RobloxEfficiencyMode: true,
                ReduceMemoryOutOfFocus: true,
                SelectedCpuPriority: "Automatic",
                RobloxPriorityLimit: "Below Normal"),
        };

        // used for the excluded-places bundle - neutralizes every optimizer knob for that session
        public static readonly EnginePresetValues Off = Presets["Default"];

        public static string[] PresetNames => Presets.Keys.ToArray();

        public static EnginePresetValues FromSettings(Settings settings) => new(
            settings.OptimizeRoblox,
            settings.RobloxEfficiencyMode,
            settings.ReduceMemoryOutOfFocus,
            settings.SelectedCpuPriority,
            settings.RobloxPriorityLimit);

        public static void Apply(EnginePresetValues values, Settings settings)
        {
            settings.OptimizeRoblox = values.OptimizeRoblox;
            settings.RobloxEfficiencyMode = values.RobloxEfficiencyMode;
            settings.ReduceMemoryOutOfFocus = values.ReduceMemoryOutOfFocus;
            settings.SelectedCpuPriority = values.SelectedCpuPriority;
            settings.RobloxPriorityLimit = values.RobloxPriorityLimit;
        }

        // resolves the effective optimizer bundle for a specific place: excluded places always win
        // (never optimize that game), then a per-place preset assignment if one exists, else the
        // global settings currently configured on the Performance page (which may not match any
        // named preset if the user tweaked individual toggles by hand - that's fine, it's still a
        // valid bundle).
        public static EnginePresetValues Resolve(long placeId)
        {
            Settings settings = App.Settings.Prop;
            string placeKey = placeId.ToString();

            if (settings.EngineExcludedPlaces.Contains(placeKey))
                return Off;

            if (settings.EnginePlaceProfiles.TryGetValue(placeKey, out string? presetName)
                && Presets.TryGetValue(presetName, out EnginePresetValues? preset))
            {
                return preset;
            }

            return FromSettings(settings);
        }
    }
}
