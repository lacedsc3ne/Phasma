using PhasmaStrap.Enums;
using PhasmaStrap.Models;

namespace PhasmaStrap.Integrations
{
    // One-time upgrade path from the old flat Settings.FastFlagPlaceProfiles map (place ID -> profile
    // name, at most one profile per place) to the newer per-profile Settings.FastFlagProfileScopes
    // (each profile owns its own "Applies to" mode + place list, same shape as Engine Settings scope).
    // Runs on every startup but is a no-op once the legacy map is empty, so it only does real work once
    // per install, right after upgrading past the version that introduced per-profile scoping.
    internal static class FastFlagProfileMigration
    {
        private const string LOG_IDENT = "FastFlagProfileMigration";

        public static void MigrateLegacyPlaceProfiles()
        {
            var legacy = App.Settings.Prop.FastFlagPlaceProfiles;
            if (legacy.Count == 0)
                return;

            var scopes = App.Settings.Prop.FastFlagProfileScopes;
            int migrated = 0;

            foreach (var (placeId, profileName) in legacy)
            {
                if (string.IsNullOrEmpty(profileName) || !App.Settings.Prop.FastFlagProfiles.ContainsKey(profileName))
                    continue;

                if (!scopes.TryGetValue(profileName, out FastFlagProfileScope? scope))
                {
                    scope = new FastFlagProfileScope { Mode = EngineSettingsScopeMode.OnlyListedPlaces };
                    scopes[profileName] = scope;
                }

                if (!scope.Places.Contains(placeId))
                {
                    scope.Places.Add(placeId);
                    migrated++;
                }
            }

            legacy.Clear();
            App.Settings.Save();

            App.Logger.WriteLine(LOG_IDENT, $"Migrated {migrated} legacy FastFlag Profile place assignment(s) to per-profile scopes");
        }
    }
}
