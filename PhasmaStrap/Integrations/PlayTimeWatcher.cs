using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
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
