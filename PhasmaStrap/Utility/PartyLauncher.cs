namespace PhasmaStrap.Utility
{
    public static class PartyLauncher
    {
        private const string LOG_IDENT = "PartyLauncher";

        private static long _lastPlace;
        private static string _lastJob = "";
        private static DateTime _lastAt = DateTime.MinValue;

        public static bool RobloxRunning()
        {
            try
            {
                return Process.GetProcessesByName(App.RobloxPlayerAppName).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void Follow(PartyJoin join)
        {
            if (!long.TryParse(join.PlaceId, out long placeId) || placeId <= 0)
                return;

            if (placeId == _lastPlace && join.JobId == _lastJob && DateTime.UtcNow - _lastAt < TimeSpan.FromSeconds(30))
                return;

            _lastPlace = placeId;
            _lastJob = join.JobId;
            _lastAt = DateTime.UtcNow;

            string mode = App.Settings.Prop.PartyJoinMode ?? "AskWhenInGame";
            bool inGame = RobloxRunning();
            bool withParty = inGame && AlreadyFollowing();

            if (mode == "Always" || !inGame || withParty)
            {
                Join(placeId, join.JobId, inGame);
                return;
            }

            NotificationCenter.Notify(
                "Your party started a game",
                inGame
                    ? "You are in a game right now. Click here to leave it and join your party."
                    : "Click here to join them.",
                NotificationCategory.General,
                durationSeconds: 20,
                onClick: () => Join(placeId, join.JobId, inGame),
                kind: NotificationKindId.Party);
        }

        private static long _followedPlace;
        private static string _followedJob = "";

        private static bool AlreadyFollowing()
        {
            if (_followedPlace <= 0)
                return false;

            (long place, string job) = PartyService.WhereYouAre();

            if (place != _followedPlace)
                return false;

            return string.IsNullOrEmpty(_followedJob) || string.IsNullOrEmpty(job) || job == _followedJob;
        }

        private static void Join(long placeId, string jobId, bool closeFirst)
        {
            App.Logger.WriteLine(LOG_IDENT, $"Following the party into {placeId}/{jobId}");

            _followedPlace = placeId;
            _followedJob = jobId ?? "";

            if (closeFirst)
            {
                Task.Run(async () =>
                {
                    await CloseRobloxAsync();
                    Launch(placeId, jobId);
                });

                return;
            }

            Launch(placeId, jobId);
        }

        private static void Launch(long placeId, string jobId)
        {
            try
            {
                RobloxLaunch.Join(placeId, jobId);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not launch: {ex.Message}");

                NotificationCenter.Notify(
                    "Could not join the party's game",
                    ex.Message,
                    NotificationCategory.General,
                    kind: NotificationKindId.Party);
            }
        }

        private static async Task CloseRobloxAsync()
        {
            FlagProfileSession.MarkIntentionalRestart();

            foreach (Process process in Process.GetProcessesByName(App.RobloxPlayerAppName))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(4000);
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

            for (int i = 0; i < 40 && RobloxRunning(); i++)
                await Task.Delay(250);
        }
    }
}
