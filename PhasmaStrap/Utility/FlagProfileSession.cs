using PhasmaStrap.Models.Entities;
using PhasmaStrap.UI;

namespace PhasmaStrap.Utility
{
    internal static class FlagProfileSession
    {
        private const string LOG_IDENT = "FlagProfileSession";

        public sealed record Wanted(string ProfileName, string Signature)
        {
            public static readonly Wanted None = new("", "");

            public bool IsNone => Signature.Length == 0;

            public static Wanted Of(FlagProfile? profile) =>
                profile is null || profile.ChangeCount == 0 ? None : new Wanted(profile.Name, FlagLayers.Signature(profile));
        }

        private sealed class Marker
        {
            public string Profile { get; set; } = "";
            public string Signature { get; set; } = "";
            public DateTime RestartUtc { get; set; } = DateTime.MinValue;
        }

        private static string MarkerPath => Path.Combine(Paths.Base, "AppliedFlagProfile.json");

        private static int _restarting;

        private static Marker Read()
        {
            try
            {
                if (File.Exists(MarkerPath))
                    return JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerPath)) ?? new Marker();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the profile marker: {ex.Message}");
            }

            return new Marker();
        }

        private static void Write(Marker marker)
        {
            try
            {
                File.WriteAllText(MarkerPath, JsonSerializer.Serialize(marker));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write the profile marker: {ex.Message}");
            }
        }

        private static bool CloseRunningOnLaunch => App.Settings.Prop.UseFastFlagManager && App.Settings.Prop.FastFlagPresetCloseRunningRoblox;

        private static readonly HashSet<long> _notifiedPlaces = new();

        public static bool RestartedRecently => (DateTime.UtcNow - Read().RestartUtc).TotalSeconds < 45;

        public static void MarkIntentionalRestart()
        {
            Marker marker = Read();
            marker.RestartUtc = DateTime.UtcNow;
            Write(marker);
        }

        private static bool IsRobloxRunning()
        {
            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private static async Task CloseRobloxAsync()
        {
            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);

            foreach (Process process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                    {
                        if (process.WaitForExit(3000))
                            continue;
                    }

                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not close Roblox ({process.Id}): {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            for (int i = 0; i < 40 && IsRobloxRunning(); i++)
                await Task.Delay(250);
        }

        public static async Task<bool> PrepareLaunchAsync(Wanted wanted, bool gameKnown)
        {
            if (!IsRobloxRunning())
                return true;

            Marker running = Read();

            if (!CloseRunningOnLaunch)
            {
                if (gameKnown && !wanted.IsNone && !string.Equals(wanted.Signature, running.Signature, StringComparison.Ordinal))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox is already running with {Describe(running.Profile, running.Signature)}; this launch wants {Describe(wanted.ProfileName, wanted.Signature)}, which will not apply until Roblox restarts");

                    NotificationCenter.Notify(
                        $"FastFlag profile \"{wanted.ProfileName}\" will not apply",
                        "Roblox is already open, and it only reads FastFlags when it starts. Close Roblox and launch this game again, or turn on closing Roblox on launch in FastFlag settings.",
                        NotificationCategory.General,
                        durationSeconds: 10,
                        kind: NotificationKindId.FastFlagProfile);
                }

                return false;
            }

            if (!gameKnown)
                return false;

            Marker marker = running;
            if (string.Equals(wanted.Signature, marker.Signature, StringComparison.Ordinal))
                return false;

            App.Logger.WriteLine(LOG_IDENT, $"Roblox is already running with {Describe(marker.Profile, marker.Signature)}, this launch needs {Describe(wanted.ProfileName, wanted.Signature)} - closing it so the new flags are read");

            marker.RestartUtc = DateTime.UtcNow;
            Write(marker);

            await CloseRobloxAsync();
            return true;
        }

        public static void NoteLaunchWithoutGame(FlagProfileData profiles)
        {
            if (profiles.Rules.Count == 0)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Roblox is opening without a game, so none of your {profiles.Rules.Count} per-game flag profiles can be picked for this session");

            NotificationCenter.Notify(
                "Per-game FastFlags need a game to launch into",
                "Roblox is opening on its own, so PhasmaStrap cannot tell which game you will pick. Launch the game from the Games page to get its profile.",
                NotificationCategory.General,
                durationSeconds: 10,
                kind: NotificationKindId.FastFlagProfile);
        }

        public static void RecordLaunch(Wanted applied)
        {
            Marker marker = Read();
            marker.Profile = applied.ProfileName;
            marker.Signature = applied.Signature;
            Write(marker);
        }

        public static void OnGameJoined(ActivityData data)
        {
            if (data.PlaceId <= 0)
                return;

            if (data.UniverseId > 0)
                GameLookup.Remember(data.PlaceId, data.UniverseId);

            if (!App.Settings.Prop.UseFastFlagManager)
                return;

            FlagProfileData profiles = FlagProfileManager.ReadFromDisk();
            FlagProfile? profile = FlagLayers.ProfileFor(profiles, data.PlaceId, data.UniverseId);
            Wanted wanted = Wanted.Of(profile);

            if (wanted.IsNone)
                return;

            bool? inRunning = ProfileInRunningRoblox(profile!);
            if (inRunning == true)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Joined place {data.PlaceId}: profile \"{wanted.ProfileName}\" is in the flags Roblox started with");
                RecordLaunch(wanted);
                return;
            }

            Marker marker = Read();
            if (inRunning is null && string.Equals(wanted.Signature, marker.Signature, StringComparison.Ordinal))
                return;

            lock (_notifiedPlaces)
            {
                if (!_notifiedPlaces.Add(data.PlaceId))
                    return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Joined place {data.PlaceId} with {Describe(marker.Profile, marker.Signature)}; its profile \"{wanted.ProfileName}\" is not active");

            bool canRejoin = data.ServerType == ServerType.Public && !string.IsNullOrEmpty(data.JobId);
            long placeId = data.PlaceId;
            string jobId = data.JobId;

            NotificationCenter.Notify(
                $"FastFlag profile \"{wanted.ProfileName}\" is not active",
                canRejoin
                    ? "Roblox only reads FastFlags when it starts, and it was started with other flags. Click here to restart it into this same server with this game's profile."
                    : "Roblox only reads FastFlags when it starts, and it was started with other flags. Close Roblox and launch this game again to get its profile.",
                NotificationCategory.General,
                durationSeconds: 10,
                onClick: canRejoin ? () => RestartInto(placeId, jobId) : null,
                kind: NotificationKindId.FastFlagProfile);
        }

        private static void RestartInto(long placeId, string jobId)
        {
            if (Interlocked.Exchange(ref _restarting, 1) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Restarting Roblox into place {placeId} / {jobId} on request");

                    MarkIntentionalRestart();
                    await CloseRobloxAsync();

                    RobloxLaunch.Join(placeId, jobId);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Restart failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _restarting, 0);
                }
            });
        }

        private static bool? ProfileInRunningRoblox(FlagProfile profile)
        {
            bool? answer = null;

            foreach (var (folder, started) in ProcessImage.RunningRoblox())
            {
                bool? has = ProfileInFlagsFile(profile, Path.Combine(folder, "ClientSettings", "ClientAppSettings.json"), started);

                if (has == true)
                    return true;
                answer ??= has;
            }

            return answer;
        }

        private static bool? ProfileInFlagsFile(FlagProfile profile, string file, DateTime startedUtc)
        {
            try
            {
                if (!File.Exists(file))
                    return profile.Flags.Count > 0 ? false : null;

                if (File.GetLastWriteTimeUtc(file) > startedUtc.AddSeconds(1))
                    return null;

                var flags = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file)) ?? new();
                var values = flags.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);

                return profile.Flags.All(kv => values.TryGetValue(kv.Key, out string? v) && string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase))
                    && profile.Remove.All(name => !values.ContainsKey(name));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the running Roblox's flags: {ex.Message}");
                return null;
            }
        }

        private static string Describe(string profile, string signature) => signature.Length > 0 ? $"profile \"{profile}\"" : "just your own flags";
    }
}
