namespace PhasmaStrap.Utility
{
    public sealed class RobloxVersionRecord
    {
        public string Guid { get; set; } = "";
        public string FileVersion { get; set; } = "";
        public DateTime InstalledUtc { get; set; }
    }

    public sealed class RobloxVersionData
    {
        public List<RobloxVersionRecord> History { get; set; } = new();

        public string LatestGuid { get; set; } = "";
        public string LatestFileVersion { get; set; } = "";

        public string CheckedGuid { get; set; } = "";
        public List<string> MissingFlags { get; set; } = new();
        public bool MissingFlagsShown { get; set; } = true;
    }

    public static class RobloxVersions
    {
        private const string LOG_IDENT = "RobloxVersions";

        public const string ModeLatest = "Latest";
        public const string ModeHold = "Hold";
        public const string ModePin = "Pin";

        public const string ManifestCopyName = "PhasmaStrap-rbxPkgManifest.txt";

        private static string DataPath => Path.Combine(Paths.Base, "RobloxVersions.json");

        private static readonly object _lock = new();

        public static RobloxVersionData Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(DataPath))
                        return JsonSerializer.Deserialize<RobloxVersionData>(File.ReadAllText(DataPath)) ?? new RobloxVersionData();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not read the version list: {ex.Message}");
                }

                return new RobloxVersionData();
            }
        }

        public static void Save(RobloxVersionData data)
        {
            lock (_lock)
            {
                try
                {
                    File.WriteAllText(DataPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not save the version list: {ex.Message}");
                }
            }
        }

        public static string FolderOf(string guid) => Path.Combine(Paths.Versions, guid);

        public static bool IsUsable(string? guid) =>
            !string.IsNullOrEmpty(guid)
            && File.Exists(Path.Combine(FolderOf(guid), "RobloxPlayerBeta.exe"))
            && File.Exists(Path.Combine(FolderOf(guid), ManifestCopyName));

        public static string? ReadManifestCopy(string guid)
        {
            try
            {
                string path = Path.Combine(FolderOf(guid), ManifestCopyName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        public static void SaveManifestCopy(string guid, string manifestText)
        {
            try
            {
                string folder = FolderOf(guid);
                string path = Path.Combine(folder, ManifestCopyName);
                if (Directory.Exists(folder) && !File.Exists(path))
                    File.WriteAllText(path, manifestText);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not keep the package list of {guid}: {ex.Message}");
            }
        }

        public static string FileVersionOf(string guid)
        {
            try
            {
                string exe = Path.Combine(FolderOf(guid), "RobloxPlayerBeta.exe");
                return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).FileVersion?.Replace(", ", ".") ?? "" : "";
            }
            catch
            {
                return "";
            }
        }

        public static void RecordLatest(string guid, string fileVersion)
        {
            RobloxVersionData data = Load();
            if (data.LatestGuid == guid)
                return;

            data.LatestGuid = guid;
            data.LatestFileVersion = fileVersion;
            Save(data);
        }

        public static string? Choose(string latestGuid, string installedGuid, out string? why)
        {
            why = null;

            switch (App.Settings.Prop.RobloxVersionMode)
            {
                case ModeHold:
                    if (string.IsNullOrEmpty(installedGuid) || installedGuid == latestGuid)
                        return null;

                    if (IsUsable(installedGuid))
                        return installedGuid;

                    why = "the held version is missing files, so the latest is used";
                    return null;

                case ModePin:
                    string pinned = App.Settings.Prop.RobloxPinnedVersion;
                    if (string.IsNullOrEmpty(pinned) || pinned == latestGuid)
                        return null;

                    if (IsUsable(pinned))
                        return pinned;

                    why = $"the pinned version {pinned} isn't on this PC any more, so the latest is used";
                    return null;

                default:
                    return null;
            }
        }

        public static void RecordInstall(string guid, string manifestText, string previousGuid)
        {
            SaveManifestCopy(guid, manifestText);

            RobloxVersionData data = Load();

            if (!string.IsNullOrEmpty(previousGuid) && previousGuid != guid && !data.History.Any(r => r.Guid == previousGuid))
                data.History.Add(new RobloxVersionRecord { Guid = previousGuid, FileVersion = FileVersionOf(previousGuid), InstalledUtc = Directory.Exists(FolderOf(previousGuid)) ? Directory.GetCreationTimeUtc(FolderOf(previousGuid)) : DateTime.UtcNow.AddMinutes(-1) });

            data.History.RemoveAll(r => r.Guid == guid);
            data.History.Add(new RobloxVersionRecord { Guid = guid, FileVersion = FileVersionOf(guid), InstalledUtc = DateTime.UtcNow });

            if (data.History.Count > 10)
                data.History.RemoveRange(0, data.History.Count - 10);

            Save(data);
        }

        public static HashSet<string> FoldersToKeep(string currentGuid)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (App.Settings.Prop.RobloxVersionMode == ModePin && !string.IsNullOrEmpty(App.Settings.Prop.RobloxPinnedVersion))
                keep.Add(App.Settings.Prop.RobloxPinnedVersion);

            if (App.Settings.Prop.RobloxKeepPreviousVersion)
            {
                RobloxVersionRecord? previous = Load().History
                    .Where(r => !string.Equals(r.Guid, currentGuid, StringComparison.OrdinalIgnoreCase) && IsUsable(r.Guid))
                    .OrderByDescending(r => r.InstalledUtc)
                    .FirstOrDefault();

                if (previous is not null)
                    keep.Add(previous.Guid);
            }

            return keep;
        }

        public static List<RobloxVersionRecord> OnDisk()
        {
            var known = Load().History.ToDictionary(r => r.Guid, StringComparer.OrdinalIgnoreCase);
            var result = new List<RobloxVersionRecord>();

            if (!Directory.Exists(Paths.Versions))
                return result;

            foreach (string dir in Directory.GetDirectories(Paths.Versions))
            {
                string guid = Path.GetFileName(dir);
                if (!IsUsable(guid))
                    continue;

                result.Add(known.TryGetValue(guid, out RobloxVersionRecord? record)
                    ? record
                    : new RobloxVersionRecord { Guid = guid, FileVersion = FileVersionOf(guid), InstalledUtc = Directory.GetCreationTimeUtc(dir) });
            }

            return result.OrderByDescending(r => r.InstalledUtc).ToList();
        }

        private static readonly string[] FlagPrefixes = { "DFFlag", "SFFlag", "FFlag", "DFInt", "FInt", "DFString", "FString", "DFLog", "FLog" };

        public static string BaseName(string flag)
        {
            string name = flag;

            foreach (string prefix in FlagPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                {
                    name = name[prefix.Length..];
                    break;
                }
            }

            foreach (string suffix in new[] { "_PlaceFilter", "_DataCenterFilter" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                    name = name[..^suffix.Length];
            }

            return name;
        }

        public static List<string> MissingFlags(string exePath, IEnumerable<string> flags)
        {
            var byBase = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string flag in flags.Distinct())
            {
                string baseName = BaseName(flag);
                if (baseName.Length < 4 || !baseName.All(c => char.IsAscii(c) && (char.IsLetterOrDigit(c) || c == '_')))
                    continue;

                if (!byBase.TryGetValue(baseName, out List<string>? list))
                    byBase[baseName] = list = new List<string>();
                list.Add(flag);
            }

            if (byBase.Count == 0)
                return new List<string>();

            var candidates = new Dictionary<uint, List<byte[]>>();
            foreach (string baseName in byBase.Keys)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(baseName);
                uint key = BitConverter.ToUInt32(bytes, 0);
                if (!candidates.TryGetValue(key, out List<byte[]>? list))
                    candidates[key] = list = new List<byte[]>();
                list.Add(bytes);
            }

            var found = new HashSet<string>(StringComparer.Ordinal);
            byte[] data = File.ReadAllBytes(exePath);

            static bool IsWordByte(byte b) => (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') || b == '_';

            for (int i = 1; i + 4 <= data.Length; i++)
            {
                if (!candidates.TryGetValue(BitConverter.ToUInt32(data, i), out List<byte[]>? list))
                    continue;

                foreach (byte[] name in list)
                {
                    int end = i + name.Length;
                    if (end > data.Length || (end < data.Length && IsWordByte(data[end])))
                        continue;

                    if (data.AsSpan(i, name.Length).SequenceEqual(name))
                        found.Add(Encoding.ASCII.GetString(name));
                }
            }

            return byBase.Where(kv => !found.Contains(kv.Key)).SelectMany(kv => kv.Value).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void ShowPendingFlagNotice()
        {
            RobloxVersionData data = Load();
            if (data.MissingFlagsShown || data.MissingFlags.Count == 0)
                return;

            data.MissingFlagsShown = true;
            Save(data);

            string names = string.Join(", ", data.MissingFlags.Take(3)) + (data.MissingFlags.Count > 3 ? $" and {data.MissingFlags.Count - 3} more" : "");

            UI.NotificationCenter.Notify(
                data.MissingFlags.Count == 1 ? "Roblox's update removed one of your flags" : $"Roblox's update removed {data.MissingFlags.Count} of your flags",
                $"{names} no longer exist, so they do nothing now. The full list is under Behaviour > Roblox version.",
                UI.NotificationCategory.General,
                durationSeconds: 12,
                kind: UI.NotificationKindId.FlagsRemoved);
        }

        public static List<string> YourFlagNames()
        {
            var names = new HashSet<string>(App.FastFlags.Prop.Keys, StringComparer.Ordinal);
            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles)
                names.UnionWith(profile.Flags.Keys);
            return names.ToList();
        }

        public static List<string> DroppedFlags(string oldExe, string newExe, IEnumerable<string> flags)
        {
            List<string> names = flags.Distinct().ToList();

            Task<List<string>> goneBefore = Task.Run(() => MissingFlags(oldExe, names));
            Task<List<string>> goneNow = Task.Run(() => MissingFlags(newExe, names));
            Task.WaitAll(goneBefore, goneNow);

            var before = new HashSet<string>(goneBefore.Result, StringComparer.Ordinal);
            return goneNow.Result.Where(f => !before.Contains(f)).ToList();
        }

        public static void CheckFlagsAfterInstall(string guid, string previousGuid)
        {
            try
            {
                string exe = Path.Combine(FolderOf(guid), "RobloxPlayerBeta.exe");
                string oldExe = Path.Combine(FolderOf(previousGuid), "RobloxPlayerBeta.exe");
                if (!File.Exists(exe) || !File.Exists(oldExe))
                    return;

                var watch = Stopwatch.StartNew();
                List<string> missing = DroppedFlags(oldExe, exe, YourFlagNames());

                RobloxVersionData data = Load();
                data.CheckedGuid = guid;
                data.MissingFlags = missing;
                data.MissingFlagsShown = missing.Count == 0;
                Save(data);

                App.Logger.WriteLine(LOG_IDENT, $"Flag check of {guid} against {previousGuid}: {missing.Count} of your flags dropped ({watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Flag check failed: {ex.Message}");
            }
        }
    }
}
