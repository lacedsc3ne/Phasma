using PhasmaStrap.Models.Entities;
using PhasmaStrap.UI;

namespace PhasmaStrap.Utility
{
    // Makes per-place FastFlag presets apply no matter how the game was joined.
    //
    // Roblox reads ClientAppSettings.json once, when its process starts. The Bootstrapper writes a
    // place's preset into that file at launch - but that only helps when (a) the launch link names
    // the place and (b) a new Roblox process actually starts. It does neither when:
    //   - the game is joined from inside the Roblox app (home screen, friends list, in-app server
    //     list): no PhasmaStrap launch happens at all;
    //   - a friend is joined from their profile: the link carries a user ID, not a place ID;
    //   - a browser "Play" is clicked while Roblox is already open: the running client takes the
    //     join over, so the freshly written flags are never read.
    // In all three the running client simply keeps the flags it started with.
    //
    // So this keeps a small marker of which preset the RUNNING client was started with, and:
    //   - the Bootstrapper closes a running client first when the launch needs a different preset;
    //   - the Watcher, on a confirmed join to a place whose preset doesn't match the marker,
    //     restarts Roblox straight back into the same server through a normal PhasmaStrap launch,
    //     which then applies the right flags.
    // The same check removes a preset's flags again when a different game is joined.
    internal static class FastFlagPresetSession
    {
        private const string LOG_IDENT = "FastFlagPresetSession";

        private sealed class Marker
        {
            public string Preset { get; set; } = "";
            public DateTime RestartUtc { get; set; } = DateTime.MinValue;
        }

        private static string MarkerPath => Path.Combine(Paths.Base, "AppliedFastFlagPreset.json");

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
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the preset marker: {ex.Message}");
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
                App.Logger.WriteLine(LOG_IDENT, $"Could not write the preset marker: {ex.Message}");
            }
        }

        public static bool Enabled => App.Settings.Prop.UseFastFlagManager && App.Settings.Prop.FastFlagPresetRestartRoblox;

        // true for a short while after this class restarted Roblox - the Watcher uses it so its
        // "Roblox vanished mid-game, must have crashed" auto-rejoin doesn't fire on top
        public static bool RestartedRecently => (DateTime.UtcNow - Read().RestartUtc).TotalSeconds < 45;

        // the preset a place should run with, or "" for the plain global flags
        public static string DesiredFor(long placeId)
        {
            if (placeId <= 0 || !App.Settings.Prop.UseFastFlagManager)
                return "";

            if (!App.Settings.Prop.FastFlagPlacePresets.TryGetValue(placeId.ToString(), out string? name) || string.IsNullOrEmpty(name))
                return "";

            return FastFlagSnapshotManager.List().Any(s => s.Name == name) ? name : "";
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
        public static async Task<bool> PrepareLaunchAsync(long? launchPlaceId)
        {
            if (!IsRobloxRunning())
                return true;

            // a link without a place ID (follow a friend, open the app): nothing to compare yet -
            // the Watcher sorts it out once the join shows which place it was
            if (launchPlaceId is null || !Enabled)
                return false;

            string desired = DesiredFor(launchPlaceId.Value);
            string current = Read().Preset;

            if (string.Equals(desired, current, StringComparison.Ordinal))
                return false;

            App.Logger.WriteLine(LOG_IDENT, $"Roblox is already running with {Describe(current)}, place {launchPlaceId} needs {Describe(desired)} - closing it so the new flags are read");

            Marker marker = Read();
            marker.RestartUtc = DateTime.UtcNow;
            Write(marker);

            await CloseRobloxAsync();
            return true;
        }

        public static void RecordLaunch(string appliedPreset)
        {
            Marker marker = Read();
            marker.Preset = appliedPreset ?? "";
            Write(marker);
        }

        // ------------------------------------------------------------------ Watcher side

        public static void OnGameJoined(ActivityData data)
        {
            if (!Enabled || data.PlaceId <= 0)
                return;

            // a teleport inside one experience (lobby -> match, sub-places): leave it alone, a
            // restart would drop the teleport data and the party
            if (data.RootActivity is not null)
                return;

            Marker marker = Read();
            string desired = DesiredFor(data.PlaceId);

            if (string.Equals(desired, marker.Preset, StringComparison.Ordinal))
                return;

            if ((DateTime.UtcNow - marker.RestartUtc).TotalSeconds < 45)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place {data.PlaceId} wants {Describe(desired)} but Roblox was only just restarted - not restarting again");
                return;
            }

            if (data.ServerType != ServerType.Public || string.IsNullOrEmpty(data.JobId))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place {data.PlaceId} wants {Describe(desired)}, but a {data.ServerType} server can't be rejoined by ID - leaving it running with {Describe(marker.Preset)}");

                if (desired.Length > 0)
                    NotificationCenter.Notify("FastFlag preset not applied", $"\"{desired}\" couldn't be applied: private and reserved servers can't be rejoined automatically. Close Roblox and join from the link to get it.");

                return;
            }

            if (Interlocked.Exchange(ref _restarting, 1) != 0)
                return;

            long placeId = data.PlaceId;
            string jobId = data.JobId;

            _ = Task.Run(async () =>
            {
                try
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Joined place {placeId} with {Describe(marker.Preset)}, it needs {Describe(desired)} - restarting Roblox into the same server");

                    NotificationCenter.Notify(
                        desired.Length > 0 ? $"Applying \"{desired}\"" : "Removing FastFlag preset",
                        desired.Length > 0
                            ? "Restarting Roblox into the same server so this game's FastFlag preset takes effect."
                            : "Restarting Roblox into the same server so the previous game's preset no longer applies.");

                    marker.RestartUtc = DateTime.UtcNow;
                    Write(marker);

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

        private static string Describe(string preset) => preset.Length > 0 ? $"preset \"{preset}\"" : "the global flags";
    }
}
