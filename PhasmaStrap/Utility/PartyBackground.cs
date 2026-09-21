using Microsoft.Win32;

namespace PhasmaStrap.Utility
{
    public static class PartyBackground
    {
        private const string LOG_IDENT = "PartyBackground";
        private const string RunValueName = "PhasmaStrapParty";
        private const string MutexName = @"Local\PhasmaStrapParty";

        public static bool Wanted => App.Settings.Prop.PartyEnabled && App.Settings.Prop.PartyBackgroundEnabled;

        public static void ApplyStartup()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");

                if (Wanted)
                    key.SetValue(RunValueName, $"\"{Paths.Application}\" -party");
                else if (key.GetValue(RunValueName) is not null)
                    key.DeleteValue(RunValueName, false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not update the start-up entry: {ex.Message}");
            }
        }

        public static void StartIfWanted()
        {
            if (!Wanted)
                return;

            try
            {
                using var running = new Mutex(false, MutexName, out bool createdNew);

                if (createdNew)
                    Process.Start(Paths.Application, "-party");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not start the party watcher: {ex.Message}");
            }
        }

        private static Mutex? _mutex;

        public static void RunBackground()
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                App.Logger.WriteLine(LOG_IDENT, "The party watcher is already running");
                App.Terminate();
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Background party watcher started");

            PartyService.JoinRequested += (_, join) => PartyLauncher.Follow(join);
            PartyService.Start();

            Task.Run(async () =>
            {
                while (Wanted)
                    await Task.Delay(TimeSpan.FromSeconds(20));

                App.Logger.WriteLine(LOG_IDENT, "Parties turned off, exiting");
                PartyService.Stop();
                App.Current.Dispatcher.Invoke(() => App.Terminate());
            });
        }
    }
}
