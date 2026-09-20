using PhasmaStrap.Models.Persistable;

namespace PhasmaStrap.Integrations
{
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
