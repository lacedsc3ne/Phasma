using PhasmaStrap.Models.Entities;
using PhasmaStrap.UI;

namespace PhasmaStrap.Utility
{
    // Keeps track of which FastFlag profile the RUNNING Roblox was started with, so a game's
    // profile applies no matter how the game was joined.
    //
    // Roblox reads its flags once, when its process starts. A PhasmaStrap launch writes the right
    // flags for the game being launched - but a game joined from inside the Roblox app, or a
    // browser "Play" while Roblox is already open, keeps whatever flags that Roblox started with.
    // Nothing is ever restarted automatically:
    //   - the Watcher, on a join to a game whose profile isn't the one Roblox started with, shows
    //     a toast; clicking it restarts Roblox straight back into the same server;
    //   - optionally (off by default) a launch closes an already running Roblox first when it
    //     needs a different profile, so a browser "Play" picks the new flags up.
    //
    // Profiles are compared by content (FlagLayers.Signature), so editing a profile while Roblox
    // runs counts as "not active" too.
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

        // games already mentioned this session - one toast per game, not one per server hop
        private static readonly HashSet<long> _notifiedPlaces = new();

        // true for a short while after Roblox was closed on purpose - the Watcher uses it so its
        // "Roblox vanished mid-game, must have crashed" handling doesn't fire on top
        public static bool RestartedRecently => (DateTime.UtcNow - Read().RestartUtc).TotalSeconds < 45;

        // Roblox is about to be closed on purpose by something else (the tray's account switch)
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

        // ------------------------------------------------------------------ Bootstrapper side

        // Called before the flags for this launch are written. Returns true when a new Roblox
        // process is going to start (so the marker should be updated afterwards).
        // gameKnown: false when the launch link didn't say which game (open the app, a share link).
        public static async Task<bool> PrepareLaunchAsync(Wanted wanted, bool gameKnown)
        {
            if (!IsRobloxRunning())
                return true;

            // nothing to compare yet - the Watcher sorts it out once the join shows the game
            if (!gameKnown || !CloseRunningOnLaunch)
                return false;

            Marker marker = Read();
            if (string.Equals(wanted.Signature, marker.Signature, StringComparison.Ordinal))
                return false;

            App.Logger.WriteLine(LOG_IDENT, $"Roblox is already running with {Describe(marker.Profile, marker.Signature)}, this launch needs {Describe(wanted.ProfileName, wanted.Signature)} - closing it so the new flags are read");

            marker.RestartUtc = DateTime.UtcNow;
            Write(marker);

            await CloseRobloxAsync();
            return true;
        }

        public static void RecordLaunch(Wanted applied)
        {
            Marker marker = Read();
            marker.Profile = applied.ProfileName;
            marker.Signature = applied.Signature;
            Write(marker);
        }

        // ------------------------------------------------------------------ Watcher side

        public static void OnGameJoined(ActivityData data)
        {
            if (data.PlaceId <= 0)
                return;

            // this is the one moment the place -> game pairing is known for certain
            if (data.UniverseId > 0)
                GameLookup.Remember(data.PlaceId, data.UniverseId);

            if (!App.Settings.Prop.UseFastFlagManager)
                return;

            FlagProfileData profiles = FlagProfileManager.ReadFromDisk();
            FlagProfile? profile = FlagLayers.ProfileFor(profiles, data.PlaceId, data.UniverseId);
            Wanted wanted = Wanted.Of(profile);

            // only worth a mention when this game HAS a profile that isn't running; a profile left
            // over from the previous game is not something to nag about
            if (wanted.IsNone)
                return;

            // The flags file Roblox actually read decides it, when it can be known: the running
            // Roblox's own ClientAppSettings.json, unchanged since that Roblox started. (The marker
            // below was only updated when a launch looked like it would start a new Roblox, so a
            // launch while an old one was still closing left it behind - a warning with the
            // profile's flags plainly working.)
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
                onClick: canRejoin ? () => RestartInto(placeId, jobId) : null);
        }

        // only ever runs because the toast was clicked
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

                    Process.Start(Paths.Process, $"-player \"roblox://experiences/start?placeId={placeId}&gameInstanceId={jobId}\"");
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

        // true / false when the running Roblox's flags file shows it; null when that can't be
        // known (no Roblox, or the file was rewritten after that Roblox started)
        private static bool? ProfileInRunningRoblox(FlagProfile profile)
        {
            bool? answer = null;

            foreach (var (folder, started) in ProcessImage.RunningRoblox())
            {
                bool? has = ProfileInFlagsFile(profile, Path.Combine(folder, "ClientSettings", "ClientAppSettings.json"), started);

                // any running Roblox with the profile counts (a second window, say)
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

                // written after this Roblox started: not what it read
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
