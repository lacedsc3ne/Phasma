namespace PhasmaStrap.Utility
{
    public static class SettingsHotReload
    {
        private const string LOG_IDENT = "SettingsHotReload";

        private static System.Threading.Timer? _timer;
        private static DateTime _lastWriteUtc = DateTime.MinValue;
        private static int _busy;

        public static event EventHandler? Reloaded;

        public static void Start()
        {
            if (App.LaunchSettings.MenuFlag.Active)
                return;

            lock (typeof(SettingsHotReload))
            {
                if (_timer is not null)
                    return;

                try
                {
                    _lastWriteUtc = File.Exists(App.Settings.FileLocation) ? File.GetLastWriteTimeUtc(App.Settings.FileLocation) : DateTime.MinValue;
                }
                catch (Exception)
                {
                    _lastWriteUtc = DateTime.MinValue;
                }

                _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            }
        }

        public static void Stop()
        {
            lock (typeof(SettingsHotReload))
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private static void Tick()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;

            try
            {
                string path = App.Settings.FileLocation;
                if (!File.Exists(path))
                    return;

                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (writeTime == _lastWriteUtc)
                    return;

                _lastWriteUtc = writeTime;

                if (!App.Settings.HasFileOnDiskChanged())
                    return;

                App.Logger.WriteLine(LOG_IDENT, "Settings changed on disk, reloading so this session picks them up");
                App.Settings.Load(alertFailure: false);

                Reloaded?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Reload failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }
    }
}
