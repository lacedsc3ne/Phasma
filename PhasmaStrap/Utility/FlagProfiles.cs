namespace PhasmaStrap.Utility
{
    // A named set of FastFlag changes that only applies to the games it is given to. It sits on
    // top of "your flags" (the global list): it can add flags, give one of your flags a different
    // value, or turn one of your flags off for those games.
    public sealed class FlagProfile
    {
        // stable, so renaming a profile never breaks the games that use it
        public string Id { get; set; } = NewId();
        public string Name { get; set; } = "";

        // flags added or changed by this profile (values are stored as strings, like the global list)
        public Dictionary<string, string> Flags { get; set; } = new();

        // your flags that this profile turns off
        public List<string> Remove { get; set; } = new();

        public static string NewId() => Guid.NewGuid().ToString("N")[..12];

        public FlagProfile Copy() => new()
        {
            Id = Id,
            Name = Name,
            Flags = new Dictionary<string, string>(Flags),
            Remove = new List<string>(Remove),
        };

        [System.Text.Json.Serialization.JsonIgnore]
        public int ChangeCount => Flags.Count + Remove.Count;
    }

    // Which profile a game launches with. PlaceId 0 = the whole game (every place in the
    // universe); PlaceId > 0 = only that one place, which wins over a whole-game rule.
    public sealed class FlagGameRule
    {
        public long UniverseId { get; set; }
        public long PlaceId { get; set; }
        public long RootPlaceId { get; set; }
        public string GameName { get; set; } = "";
        public string PlaceName { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string ProfileId { get; set; } = "";

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsWholeGame => PlaceId <= 0;
    }

    public sealed class FlagProfileData
    {
        public int Version { get; set; } = 1;
        public List<FlagProfile> Profiles { get; set; } = new();
        public List<FlagGameRule> Rules { get; set; } = new();
    }

    // Pure functions over profiles - no App dependencies, so they can be exercised from a harness.
    public static class FlagLayers
    {
        // the rule a launch or join of this place uses: a rule for that exact place first, then
        // one for the whole game it belongs to
        public static FlagGameRule? RuleFor(FlagProfileData data, long placeId, long universeId)
        {
            if (placeId > 0)
            {
                FlagGameRule? place = data.Rules.FirstOrDefault(r => r.PlaceId == placeId);
                if (place is not null)
                    return place;
            }

            if (universeId > 0)
                return data.Rules.FirstOrDefault(r => r.IsWholeGame && r.UniverseId == universeId);

            return null;
        }

        public static FlagProfile? ProfileFor(FlagProfileData data, long placeId, long universeId)
        {
            FlagGameRule? rule = RuleFor(data, placeId, universeId);
            return rule is null ? null : data.Profiles.FirstOrDefault(p => p.Id == rule.ProfileId);
        }

        // true when a whole-game rule exists that the place can only be matched to once its
        // universe is known
        public static bool NeedsUniverse(FlagProfileData data, long placeId) =>
            !data.Rules.Any(r => r.PlaceId == placeId && placeId > 0) && data.Rules.Any(r => r.IsWholeGame);

        // what Roblox is given: your flags, minus the ones the profile turns off, plus its own
        public static Dictionary<string, string> Compose(IEnumerable<KeyValuePair<string, object>> global, FlagProfile? profile)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (key, value) in global)
            {
                if (value is not null)
                    result[key] = value.ToString() ?? "";
            }

            if (profile is null)
                return result;

            foreach (string key in profile.Remove)
                result.Remove(key);

            foreach (var (key, value) in profile.Flags)
                result[key] = value;

            return result;
        }

        public enum Source { Yours, Added, Changed, TurnedOff }

        public sealed record EffectiveFlag(string Name, string Value, Source Source, string? YourValue);

        // every flag a game ends up with, and where each comes from - for the preview
        public static List<EffectiveFlag> Explain(IEnumerable<KeyValuePair<string, object>> global, FlagProfile? profile)
        {
            var mine = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in global)
            {
                if (value is not null)
                    mine[key] = value.ToString() ?? "";
            }

            var result = new List<EffectiveFlag>();
            var removed = new HashSet<string>(profile?.Remove ?? new List<string>(), StringComparer.Ordinal);
            var changed = profile?.Flags ?? new Dictionary<string, string>();

            foreach (var (key, value) in mine)
            {
                if (changed.TryGetValue(key, out string? newValue))
                    result.Add(new EffectiveFlag(key, newValue, Source.Changed, value));
                else if (removed.Contains(key))
                    result.Add(new EffectiveFlag(key, value, Source.TurnedOff, value));
                else
                    result.Add(new EffectiveFlag(key, value, Source.Yours, value));
            }

            foreach (var (key, value) in changed)
            {
                if (!mine.ContainsKey(key))
                    result.Add(new EffectiveFlag(key, value, Source.Added, null));
            }

            return result.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // identifies a profile's CONTENT, so "Roblox is running with profile X" is still known to
        // be out of date after X was edited
        public static string Signature(FlagProfile? profile)
        {
            if (profile is null || profile.ChangeCount == 0)
                return "";

            var builder = new StringBuilder();
            foreach (var (key, value) in profile.Flags.OrderBy(f => f.Key, StringComparer.Ordinal))
                builder.Append(key).Append('=').Append(value).Append('\n');
            foreach (string key in profile.Remove.OrderBy(k => k, StringComparer.Ordinal))
                builder.Append('-').Append(key).Append('\n');

            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
        }

        public static string UniqueName(FlagProfileData data, string wanted, string? exceptId = null)
        {
            string baseName = string.IsNullOrWhiteSpace(wanted) ? "New profile" : wanted.Trim();
            string name = baseName;

            for (int i = 2; data.Profiles.Any(p => p.Id != exceptId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
                name = $"{baseName} ({i})";

            return name;
        }

        // ------------------------------------------------------------------ share codes

        private const string CodePrefix = "PHF1-";

        public static string ToShareCode(FlagProfile profile)
        {
            var payload = new FlagProfile { Id = "", Name = profile.Name, Flags = profile.Flags, Remove = profile.Remove };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);

            using var output = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
                deflate.Write(json);

            return CodePrefix + Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // Codes get pasted the way people share them: in Discord backticks or a code block, in
        // quotes, in the middle of a sentence, or broken over lines. Find the code in the text first.
        public static IEnumerable<string> FindCodes(string? pasted, string prefix)
        {
            string text = pasted ?? "";
            var pattern = new Regex(Regex.Escape(prefix) + "[A-Za-z0-9_-]+", RegexOptions.IgnoreCase);

            foreach (Match match in pattern.Matches(text))
                yield return match.Value;

            // a code wrapped over several lines
            string joined = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (joined != text)
            {
                foreach (Match match in pattern.Matches(joined))
                    yield return match.Value;
            }
        }

        public static FlagProfile? FromShareCode(string? pasted)
        {
            foreach (string code in FindCodes(pasted, CodePrefix))
            {
                if (Decode(code) is FlagProfile profile)
                    return profile;
            }

            return null;
        }

        private static FlagProfile? Decode(string text)
        {
            try
            {
                if (text.Length > 200_000)
                    return null;

                string body = text[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
                body += (body.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

                using var input = new MemoryStream(Convert.FromBase64String(body));
                using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var json = new MemoryStream();

                // a short code that inflates into megabytes is not a profile
                byte[] buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    json.Write(buffer, 0, read);
                    if (json.Length > 1_000_000)
                        return null;
                }

                FlagProfile? profile = JsonSerializer.Deserialize<FlagProfile>(json.ToArray());
                if (profile is null)
                    return null;

                profile.Id = FlagProfile.NewId();
                profile.Name = (profile.Name ?? "").Trim();
                profile.Flags = (profile.Flags ?? new()).Where(f => !string.IsNullOrWhiteSpace(f.Key)).ToDictionary(f => f.Key.Trim(), f => f.Value ?? "");
                profile.Remove = (profile.Remove ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
                return profile;
            }
            catch
            {
                return null;
            }
        }
    }

    // Checks one FastFlag name/value pair. Returns null when it is fine, otherwise what is wrong,
    // in words a player understands.
    public static class FlagValidation
    {
        private static readonly string[] Prefixes = { "FFlag", "DFFlag", "SFFlag", "FInt", "DFInt", "FString", "DFString", "FLog", "DFLog" };

        private static readonly Regex BoolFilter = new("^(?:true|false)(;[\\d]{1,})+$", RegexOptions.IgnoreCase);
        private static readonly Regex IntFilter = new("^([\\d]{1,})?(;[\\d]{1,})+$", RegexOptions.IgnoreCase);
        private static readonly Regex StringFilter = new("^[^;]*(;[\\d]{1,})+$", RegexOptions.IgnoreCase);

        public static string? NameProblem(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "The flag needs a name.";

            if (!Prefixes.Any(name.StartsWith))
                return "Flag names start with FFlag, DFFlag, FInt, DFInt, FString, DFString, FLog or DFLog.";

            if (!name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return "Flag names only contain letters, digits and _.";

            return null;
        }

        public static string? Problem(string name, string value)
        {
            string? nameProblem = NameProblem(name);
            if (nameProblem is not null)
                return nameProblem;

            value ??= "";
            string lower = value.ToLowerInvariant();

            if (name.EndsWith("_PlaceFilter") || name.EndsWith("_DataCenterFilter"))
            {
                bool ok = name.StartsWith("FFlag") || name.StartsWith("DFFlag") ? BoolFilter.IsMatch(value)
                    : name.StartsWith("FInt") || name.StartsWith("DFInt") ? IntFilter.IsMatch(value)
                    : StringFilter.IsMatch(value);

                return ok ? null : "A place filter looks like value;placeId;placeId.";
            }

            if ((name.StartsWith("FInt") || name.StartsWith("DFInt")) && !int.TryParse(value, out _))
                return "This flag needs a whole number.";

            if ((name.StartsWith("FFlag") || name.StartsWith("DFFlag") || name.StartsWith("SFFlag")) && lower != "true" && lower != "false")
                return "This flag needs True or False.";

            return null;
        }
    }

    // The profiles and game rules, kept in <base>\FastFlagProfiles.json. Like the global flags,
    // changes wait for the settings window's Save button.
    public sealed class FlagProfileManager : JsonManager<FlagProfileData>
    {
        public override string ClassName => nameof(FlagProfileManager);

        public override string LOG_IDENT_CLASS => ClassName;

        public override string FileName => "FastFlagProfiles.json";

        public override string FileLocation => Path.Combine(Paths.Base, FileName);

        private string _saved = "";

        // raised whenever a page changes the profiles or rules, so the other page can refresh
        public event EventHandler? Edited;

        public void NotifyEdited() => Edited?.Invoke(this, EventArgs.Empty);

        public bool Changed => Serialize(Prop) != _saved;

        private static string Serialize(FlagProfileData data) => JsonSerializer.Serialize(data);

        public override bool Load(bool alertFailure = true)
        {
            bool result = base.Load(alertFailure);
            Normalise(Prop);
            _saved = Serialize(Prop);
            return result;
        }

        public override void Save()
        {
            Normalise(Prop);
            base.Save();
            _saved = Serialize(Prop);
        }

        public void Revert()
        {
            try
            {
                Prop = JsonSerializer.Deserialize<FlagProfileData>(_saved) ?? new FlagProfileData();
            }
            catch
            {
                Prop = new FlagProfileData();
            }

            NotifyEdited();
        }

        private static void Normalise(FlagProfileData data)
        {
            data.Profiles ??= new();
            data.Rules ??= new();

            foreach (FlagProfile profile in data.Profiles)
            {
                profile.Flags ??= new();
                profile.Remove ??= new();
                if (string.IsNullOrEmpty(profile.Id))
                    profile.Id = FlagProfile.NewId();
            }

            // a rule pointing at a deleted profile does nothing - drop it
            data.Rules.RemoveAll(r => !data.Profiles.Any(p => p.Id == r.ProfileId));
        }

        public FlagProfile? Find(string? id) => id is null ? null : Prop.Profiles.FirstOrDefault(p => p.Id == id);

        public IEnumerable<FlagGameRule> RulesUsing(string profileId) => Prop.Rules.Where(r => r.ProfileId == profileId);

        // a fresh read of the file, for processes that do not own the settings window (the Watcher)
        public static FlagProfileData ReadFromDisk()
        {
            try
            {
                string path = Path.Combine(Paths.Base, "FastFlagProfiles.json");
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<FlagProfileData>(File.ReadAllText(path)) ?? new FlagProfileData();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("FlagProfileManager", $"Could not read the profiles file: {ex.Message}");
            }

            return new FlagProfileData();
        }

        // One-time move from the old system: presets were FastFlagSnapshots files assigned to
        // place IDs in Settings.FastFlagPlacePresets. Every assigned preset becomes a profile and
        // every assignment a single-place rule, so nothing launches differently afterwards.
        public void MigrateFromPlacePresets()
        {
            const string LOG_IDENT = "FlagProfileManager::Migrate";

            Dictionary<string, string> old = App.Settings.Prop.FastFlagPlacePresets;
            if (old is null || old.Count == 0)
                return;

            try
            {
                List<FastFlagSnapshot> snapshots = FastFlagSnapshotManager.List();
                var created = new Dictionary<string, FlagProfile>(StringComparer.Ordinal);

                foreach (var (placeText, presetName) in old)
                {
                    if (!long.TryParse(placeText, out long placeId) || placeId <= 0 || string.IsNullOrEmpty(presetName))
                        continue;

                    if (!created.TryGetValue(presetName, out FlagProfile? profile))
                    {
                        FastFlagSnapshot? snapshot = snapshots.FirstOrDefault(s => s.Name == presetName);
                        if (snapshot is null)
                            continue;

                        profile = new FlagProfile
                        {
                            Name = FlagLayers.UniqueName(Prop, presetName),
                            Flags = snapshot.Flags.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value.ToString() ?? ""),
                        };

                        Prop.Profiles.Add(profile);
                        created[presetName] = profile;
                    }

                    if (!Prop.Rules.Any(r => r.PlaceId == placeId))
                        Prop.Rules.Add(new FlagGameRule { PlaceId = placeId, ProfileId = profile.Id });
                }

                Save();

                // the snapshots now live on as profiles - keep them out of the snapshot list
                foreach (string name in created.Keys)
                    FastFlagSnapshotManager.Delete(name);

                App.Settings.Prop.FastFlagPlacePresets = new();
                App.Settings.Save();

                App.Logger.WriteLine(LOG_IDENT, $"Moved {created.Count} preset(s) and {Prop.Rules.Count} assignment(s) to profiles");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Migration failed, old assignments left as they were: {ex.Message}");
            }
        }
    }
}
