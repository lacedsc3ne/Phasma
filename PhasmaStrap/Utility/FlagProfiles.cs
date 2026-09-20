namespace PhasmaStrap.Utility
{
    public sealed class FlagProfile
    {
        public string Id { get; set; } = NewId();
        public string Name { get; set; } = "";

        public Dictionary<string, string> Flags { get; set; } = new();

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

    public static class FlagLayers
    {
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

        public static bool NeedsUniverse(FlagProfileData data, long placeId) =>
            !data.Rules.Any(r => r.PlaceId == placeId && placeId > 0) && data.Rules.Any(r => r.IsWholeGame);

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

        private const string CodePrefix = "PHF1-";
        private const string ShortPrefix = "PHF2-";

        private static readonly string[] Prefixes = { ShortPrefix, CodePrefix };

        public static string ToShareCode(FlagProfile profile)
        {
            App.SendStat("shareCode", "flagProfileCreated");

            if (profile.Flags.Values.Any(v => v.IndexOfAny(new[] { '\r', '\n' }) >= 0))
            {
                var payload = new FlagProfile { Id = "", Name = profile.Name, Flags = profile.Flags, Remove = profile.Remove };
                return CodePrefix + Pack(JsonSerializer.SerializeToUtf8Bytes(payload));
            }

            var text = new StringBuilder((profile.Name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim());

            foreach (var (key, value) in profile.Flags)
                text.Append('\n').Append(Ref(key)).Append('=').Append(value);

            foreach (string key in profile.Remove)
                text.Append('\n').Append('-').Append(Ref(key));

            return ShortPrefix + Pack(Encoding.UTF8.GetBytes(text.ToString()));
        }

        private static string Ref(string key) =>
            FlagCodeNames.TryGetIndex(key, out int index) ? "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : key;

        private static string? Unref(string text)
        {
            if (!text.StartsWith('#'))
                return text;

            return int.TryParse(text[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                && index >= 0 && index < FlagCodeNames.Names.Length
                ? FlagCodeNames.Names[index]
                : null;
        }

        public static string Pack(byte[] data)
        {
            using var output = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
                deflate.Write(data);

            return Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[]? Unpack(string body, int maxBytes)
        {
            try
            {
                string base64 = body.Replace('-', '+').Replace('_', '/');
                base64 += (base64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

                using var input = new MemoryStream(Convert.FromBase64String(base64));
                using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();

                byte[] buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > maxBytes)
                        return null;
                }

                return output.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public static IEnumerable<string> FindCodes(string? pasted, params string[] prefixes)
        {
            string text = pasted ?? "";
            string joined = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

            foreach (string prefix in prefixes)
            {
                var pattern = new Regex(Regex.Escape(prefix) + "[A-Za-z0-9_-]+", RegexOptions.IgnoreCase);

                foreach (Match match in pattern.Matches(text))
                    yield return match.Value;

                if (joined != text)
                {
                    foreach (Match match in pattern.Matches(joined))
                        yield return match.Value;
                }
            }
        }

        public static string FindProfileCode(string? pasted) => FindCodes(pasted, Prefixes).FirstOrDefault() ?? "";

        public static FlagProfile? FromShareCode(string? pasted)
        {
            foreach (string code in FindCodes(pasted, Prefixes))
            {
                FlagProfile? profile = code.StartsWith(ShortPrefix, StringComparison.OrdinalIgnoreCase)
                    ? DecodeShort(code[ShortPrefix.Length..])
                    : DecodeJson(code[CodePrefix.Length..]);

                if (profile is not null)
                {
                    App.SendStat("shareCode", "flagProfileImported");
                    return Tidy(profile);
                }
            }

            return null;
        }

        private static FlagProfile? DecodeShort(string body)
        {
            if (body.Length > 200_000 || Unpack(body, 1_000_000) is not byte[] data)
                return null;

            string[] lines = Encoding.UTF8.GetString(data).Split('\n');
            var profile = new FlagProfile { Name = lines[0] };

            foreach (string line in lines.Skip(1))
            {
                if (line.Length == 0)
                    continue;

                if (line[0] == '-')
                {
                    if (Unref(line[1..]) is not string removed)
                        return null;

                    profile.Remove.Add(removed);
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0 || Unref(line[..equals]) is not string key)
                    return null;

                profile.Flags[key] = line[(equals + 1)..];
            }

            return profile;
        }

        private static FlagProfile? DecodeJson(string body)
        {
            if (body.Length > 200_000 || Unpack(body, 1_000_000) is not byte[] data)
                return null;

            try
            {
                return JsonSerializer.Deserialize<FlagProfile>(data);
            }
            catch
            {
                return null;
            }
        }

        private static FlagProfile Tidy(FlagProfile profile)
        {
            profile.Id = FlagProfile.NewId();
            profile.Name = (profile.Name ?? "").Trim();
            profile.Flags = (profile.Flags ?? new()).Where(f => !string.IsNullOrWhiteSpace(f.Key)).ToDictionary(f => f.Key.Trim(), f => f.Value ?? "");
            profile.Remove = (profile.Remove ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
            return profile;
        }
    }

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

    public sealed class FlagProfileManager : JsonManager<FlagProfileData>
    {
        public override string ClassName => nameof(FlagProfileManager);

        public override string LOG_IDENT_CLASS => ClassName;

        public override string FileName => "FastFlagProfiles.json";

        public override string FileLocation => Path.Combine(Paths.Base, FileName);

        private string _saved = "";

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

            data.Rules.RemoveAll(r => !data.Profiles.Any(p => p.Id == r.ProfileId));
        }

        public FlagProfile? Find(string? id) => id is null ? null : Prop.Profiles.FirstOrDefault(p => p.Id == id);

        public IEnumerable<FlagGameRule> RulesUsing(string profileId) => Prop.Rules.Where(r => r.ProfileId == profileId);

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
