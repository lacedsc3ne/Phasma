using PhasmaStrap.AppData;
using PhasmaStrap.Integrations;
using PhasmaStrap.Integrations.GameChat;
using PhasmaStrap.Integrations.Overlays;
using PhasmaStrap.Models;

namespace PhasmaStrap
{
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly DiscordRichPresence? RichPresence;

        public readonly IntegrationWatcher? IntegrationWatcher;

        public readonly PlayTimeWatcher? PlayTimeWatcher;

        public readonly GameChatIntegration? GameChat;

        private RobloxProcessOptimizer? _processOptimizer;

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";

            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                _watcherData = new() { ProcessId = gameClientProcess.Id };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile);

                // OverlayHub is the single lifecycle owner for the whole GPU overlay compositor -
                // RiShade/Anti-Aliasing/Frame Generation all run as stages inside it (see
                // OverlayCompositor.RenderFrame) rather than having their own game-join/leave wiring.
                ActivityWatcher.OnGameJoin += delegate { OverlayHub.OnGameJoin(); };
                ActivityWatcher.OnGameLeave += delegate { OverlayHub.OnGameLeave(); };

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence)
                    RichPresence = new(ActivityWatcher);

                // opt-in only: this feature installs a global (system-wide) low-level keyboard hook
                // while a Roblox session is active, so it defaults to off and requires explicit consent
                if (App.Settings.Prop.GameChatEnabled)
                    GameChat = new(ActivityWatcher, _watcherData.ProcessId);

                if (App.Settings.Prop.CustomIntegrations.Count > 0)
                    IntegrationWatcher = new(ActivityWatcher);

                PlayTimeWatcher = new(ActivityWatcher);

                if (App.Settings.Prop.FakeExclusiveFullscreen)
                {
                    ActivityWatcher.OnGameJoin += (_, _) => FakeExclusiveFullscreen.OnGameJoin();
                    ActivityWatcher.OnGameLeave += (_, _) => FakeExclusiveFullscreen.OnGameLeave();
                }

                if (App.Settings.Prop.DuckRobloxAudioOnUnfocus)
                {
                    ActivityWatcher.OnGameJoin += (_, _) => AudioDucker.Start();
                    ActivityWatcher.OnGameLeave += (_, _) => AudioDucker.Stop();
                }

                if (App.Settings.Prop.HeadsetAudioEnabled)
                {
                    ActivityWatcher.OnGameJoin += (_, _) => HeadsetAudio.Start();
                    ActivityWatcher.OnGameLeave += (_, _) => HeadsetAudio.Stop();
                }

                if (App.Settings.Prop.ForceInGameResolution)
                {
                    ActivityWatcher.OnGameJoin += (_, _) => ForcedResolution.OnGameJoin();
                    ActivityWatcher.OnGameLeave += (_, _) => ForcedResolution.OnGameLeave();
                }

                if (RobloxProcessOptimizer.ShouldRun(App.Settings.Prop))
                {
                    ActivityWatcher.OnGameJoin += (_, _) => StartProcessOptimizer();
                    ActivityWatcher.OnGameLeave += (_, _) => StopProcessOptimizer();
                }

                if (App.Settings.Prop.LauncherMemoryManagerEnabled)
                {
                    MemoryManager.Start();
                    ActivityWatcher.OnGameJoin += (_, _) => MemoryManager.SetGameplayActive(true);
                    ActivityWatcher.OnGameLeave += (_, _) => MemoryManager.SetGameplayActive(false);
                }
            }

            _notifyIcon = new(this);
        }

        private void StartProcessOptimizer()
        {
            if (_watcherData is null)
                return;

            _processOptimizer ??= new RobloxProcessOptimizer(_watcherData.ProcessId);
            _processOptimizer.Start();
        }

        private void StopProcessOptimizer()
        {
            _processOptimizer?.Dispose();
            _processOptimizer = null;
        }

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();

            while (Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
                await Task.Delay(1000);

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            OverlayHub.Shutdown();

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();
            GameChat?.Dispose();
            IntegrationWatcher?.Dispose();
            PlayTimeWatcher?.Dispose();
            PlayTimeStore.Shutdown();
            FakeExclusiveFullscreen.Shutdown();
            AudioDucker.Shutdown();
            HeadsetAudio.Shutdown();
            ForcedResolution.Shutdown();
            StopProcessOptimizer();
            MemoryManager.Shutdown();

            GC.SuppressFinalize(this);
        }
    }
}
