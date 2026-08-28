using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
    // Feeds PlayTimeStore off ActivityWatcher's existing OnGameLeave event, for the History page.
    // Simplified from Voidstrap's HistoryPersister/PlayTimeWatcher pair: there's no live in-session
    // ticking and no separate server-history file to maintain here - by the time OnGameLeave fires,
    // ActivityWatcher.History already holds the just-completed session with both its join and leave
    // timestamps, so there's nothing left to poll for. This just records that one completed session.
    public sealed class PlayTimeWatcher : IDisposable
    {
        private const string LOG_IDENT = "PlayTimeWatcher";

        private readonly ActivityWatcher _activityWatcher;

        private bool _disposed;

        public PlayTimeWatcher(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnGameLeave += OnGameLeave;
        }

        private void OnGameLeave(object? sender, EventArgs e)
        {
            ActivityData? activity;

            lock (_activityWatcher.History)
                activity = _activityWatcher.History.FirstOrDefault();

            if (activity is null || activity.PlaceId <= 0 || !activity.TimeLeft.HasValue)
                return;

            _ = RecordAsync(activity);
        }

        private async Task RecordAsync(ActivityData activity)
        {
            try
            {
                if (activity.UniverseDetails is null && activity.UniverseId > 0)
                {
                    try
                    {
                        await UniverseDetails.FetchSingle(activity.UniverseId);
                        activity.UniverseDetails = UniverseDetails.LoadFromCache(activity.UniverseId);
                    }
                    catch (Exception ex)
                    {
                        // best-effort - we can still record the time played without a name/icon
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to fetch universe details for {activity.UniverseId}: {ex.Message}");
                    }
                }

                PlayTimeStore.RecordSession(activity);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _activityWatcher.OnGameLeave -= OnGameLeave;

            GC.SuppressFinalize(this);
        }
    }
}
