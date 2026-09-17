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
    // So this keeps a small marker of which preset the RUNNING client was started with. Nothing is
    // ever restarted automatically - being thrown out of a game you just joined is worse than
    // missing a few flags. Instead:
    //   - the Watcher, on a confirmed join to a place whose preset isn't the one Roblox started
    //     with, shows a toast saying so; clicking it restarts Roblox straight back into the same
    //     server through a normal PhasmaStrap launch, which then applies the right flags;
    //   - optionally (off by default) the Bootstrapper closes an already running Roblox when a
    //     launch needs a different preset, so a browser "Play" picks the new flags up.
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

        private static bool CloseRunningOnLaunch => App.Settings.Prop.UseFastFlagManager && App.Settings.Prop.FastFlagPresetCloseRunningRoblox;

        // places already mentioned this session - one toast per game, not one per server hop
        private static readonly HashSet<long> _notifiedPlaces = new();

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
            if (launchPlaceId is null || !CloseRunningOnLaunch)
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
            if (!App.Settings.Prop.UseFastFlagManager || data.PlaceId <= 0)
                return;

            string desired = DesiredFor(data.PlaceId);

            // only worth a mention when this game HAS a preset that isn't running; a preset left
            // over from the previous game is harmless enough not to nag about
            if (desired.Length == 0)
                return;

            Marker marker = Read();
            if (string.Equals(desired, marker.Preset, StringComparison.Ordinal))
                return;

            lock (_notifiedPlaces)
            {
                if (!_notifiedPlaces.Add(data.PlaceId))
                    return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Joined place {data.PlaceId} with {Describe(marker.Preset)}; its preset \"{desired}\" is not active because Roblox was already running");

            bool canRejoin = data.ServerType == ServerType.Public && !string.IsNullOrEmpty(data.JobId);
            long placeId = data.PlaceId;
            string jobId = data.JobId;

            NotificationCenter.Notify(
                $"Preset \"{desired}\" is not active",
                canRejoin
                    ? "Roblox was already running, and it only reads FastFlags when it starts. Click here to restart it into this same server with the preset."
                    : "Roblox was already running, and it only reads FastFlags when it starts. Close Roblox and join this game again to get the preset.",
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

                    Marker marker = Read();
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
