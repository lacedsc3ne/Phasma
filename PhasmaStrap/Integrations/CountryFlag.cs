namespace PhasmaStrap.Integrations
{
    // country-name/code normalization used by the datacenter map and matchmaker. Simplified
    // from Voidstrap's version: drops its flag-icon downloading, keeping just the name
    // canonicalization matchmaking needs.
    internal static class CountryFlag
    {
        private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "USA", "US" }, { "U.S.", "US" }, { "U.S.A.", "US" }, { "America", "US" },
            { "UK", "GB" }, { "Great Britain", "GB" }, { "England", "GB" }, { "Scotland", "GB" }, { "Wales", "GB" },
            { "Northern Ireland", "GB" }, { "Britain", "GB" },
            { "UAE", "AE" }, { "Holland", "NL" }, { "Korea", "KR" }, { "Czechia", "CZ" }, { "Czech Republic", "CZ" },
            { "Russia", "RU" }, { "Russian Federation", "RU" }, { "Vietnam", "VN" }, { "Viet Nam", "VN" },
            { "Turkey", "TR" }, { "Turkiye", "TR" }, { "Ivory Coast", "CI" }, { "Cape Verde", "CV" },
            { "Macedonia", "MK" }, { "Swaziland", "SZ" }, { "Burma", "MM" }, { "East Timor", "TL" },
            { "Congo Kinshasa", "CD" }, { "Congo Brazzaville", "CG" }, { "Palestine", "PS" },
            { "Hong Kong SAR", "HK" }, { "Macau", "MO" }, { "Bolivia", "BO" }, { "Laos", "LA" },
            { "Syria", "SY" }, { "Iran", "IR" }, { "Tanzania", "TZ" }, { "Moldova", "MD" },
            { "Brunei", "BN" }, { "Venezuela", "VE" }, { "South Korea", "KR" }, { "North Korea", "KP" },
        };

        private static readonly Lazy<Dictionary<string, string>> _nameToIso = new(BuildNameIndex);

        private static Dictionary<string, string> BuildNameIndex()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    string iso = region.TwoLetterISORegionName.ToUpperInvariant();
                    if (iso.Length != 2)
                        continue;

                    map.TryAdd(region.EnglishName, iso);
                    map.TryAdd(region.DisplayName, iso);
                    map.TryAdd(region.NativeName, iso);
                    map.TryAdd(region.ThreeLetterISORegionName, iso);
                    map.TryAdd(iso, iso);
                }
                catch (ArgumentException) { }
            }

            foreach (var alias in _aliases)
                map[alias.Key] = alias.Value;

            return map;
        }

        public static string ToIso2(string? country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return "";

            string trimmed = country.Trim();
            if (_nameToIso.Value.TryGetValue(trimmed, out string? mapped))
                return mapped;

            if (trimmed.Length != 2 || !char.IsLetter(trimmed[0]) || !char.IsLetter(trimmed[1]))
                return "";

            string upper = trimmed.ToUpperInvariant();
            try
            {
                return new RegionInfo(upper).TwoLetterISORegionName.Equals(upper, StringComparison.OrdinalIgnoreCase) ? upper : "";
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        public static string Canonical(string? country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return "";

            string trimmed = country.Trim();
            string iso = ToIso2(trimmed);
            if (iso.Length == 2)
                return ToDisplayName(iso);

            return trimmed.Length > 2 ? trimmed : "";
        }

        public static string ToDisplayName(string? country)
        {
            string iso = ToIso2(country);
            if (iso.Length != 2)
                return country?.Trim() ?? "";

            try
            {
                return new RegionInfo(iso).EnglishName;
            }
            catch (ArgumentException)
            {
                return country?.Trim() ?? "";
            }
        }
    }
}
