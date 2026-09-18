using PhasmaStrap.AppData;
using PhasmaStrap.Integrations;
using PhasmaStrap.Integrations.GameChat;
using PhasmaStrap.Integrations.Overlays;
using PhasmaStrap.Models;
using PhasmaStrap.UI;
using PhasmaStrap.Utility;

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

        public readonly SessionTracker? SessionTracker;

        public readonly GameChatIntegration? GameChat;

        private readonly GlobalHotkeyManager? _hotkeys;

        private readonly InstantReplayRecorder _instantReplay = new();

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
                ActivityWatcher.OnGameJoin += (sender, _) => OverlayHub.OnGameJoin((sender as ActivityWatcher)?.Data.PlaceId ?? 0);
                ActivityWatcher.OnGameLeave += delegate { OverlayHub.OnGameLeave(); };

                // the HUD's "Show ping" row - checked live (not gated at startup like most of the
                // handlers below) since it costs nothing to subscribe and this way toggling it on
                // the Overlays page takes effect on the very next game join, no relaunch needed
                ActivityWatcher.OnGameJoin += (sender, _) =>
                {
                    // the region badge shows the ping next to the region in the tray menu
                    if (App.Settings.Prop.OverlayHudShowPing || App.Settings.Prop.OverlayHudShowRegion)
                        ServerPingMonitor.Start((sender as ActivityWatcher)?.Data.MachineAddress);
                };
                ActivityWatcher.OnGameLeave += (_, _) => ServerPingMonitor.Stop();

                // where the server is - an offline lookup, always on (tray menu + optional HUD row)
                ActivityWatcher.OnGameJoin += (sender, _) => ServerRegion.OnGameJoin((sender as ActivityWatcher)?.Data.MachineAddress);
                ActivityWatcher.OnGameLeave += (_, _) => ServerRegion.OnGameLeave();

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

                if (App.Settings.Prop.SessionHistoryEnabled)
                    SessionTracker = new(ActivityWatcher);

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

                // checked live (not gated at startup like most handlers here) so turning Instant
                // Replay on from the Capture page takes effect on the very next game join - Roblox
                // is very often already running by the time someone finds this toggle, and gating
                // this at Watcher startup like the others would silently require a relaunch first
                ActivityWatcher.OnGameJoin += (_, _) =>
                {
                    if (App.Settings.Prop.InstantReplayEnabled)
                        _instantReplay.Start();
                };
                ActivityWatcher.OnGameLeave += (_, _) => _instantReplay.Stop();

                // a game joined from inside the Roblox app (or by following a friend) never went
                // through a launch that could apply its FastFlag preset - see FastFlagPresetSession
                ActivityWatcher.OnGameJoin += (sender, _) =>
                {
                    if (sender is ActivityWatcher watcher)
                        Utility.FastFlagPresetSession.OnGameJoined(watcher.Data);
                };

                // the Capture page saves the toggle immediately and this process reloads the file
                // (SettingsHotReload) - so flipping it mid-game starts/stops the buffer right away
                Utility.SettingsHotReload.Reloaded += (_, _) =>
                {
                    // hotkeys bound/changed while the game runs - RegisterHotKey must run on the
                    // thread that owns the message window, so marshal back
                    try
                    {
                        App.Current.Dispatcher.BeginInvoke(() => _hotkeys?.ApplyBindings());
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("Watcher::SettingsReloaded", $"Hotkey re-apply failed: {ex.Message}");
                    }

                    try
                    {
                        bool wanted = App.Settings.Prop.InstantReplayEnabled && ActivityWatcher.InGame;
                        if (wanted && !_instantReplay.IsRunning)
                            _instantReplay.Start();
                        else if (!App.Settings.Prop.InstantReplayEnabled && _instantReplay.IsRunning)
                            _instantReplay.Stop();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("Watcher::SettingsReloaded", $"Instant Replay state update failed: {ex.Message}");
                    }
                };

                if (App.Settings.Prop.ForceInGameResolution)
                {
                    ActivityWatcher.OnGameJoin += (_, _) => ForcedResolution.OnGameJoin();
                    ActivityWatcher.OnGameLeave += (_, _) => ForcedResolution.OnGameLeave();
                }

                // also wire up the optimizer if any per-game preset is assigned, even when the
                // global toggles are all off - a place-specific preset should still fire (see
                // EnginePresets.Resolve, invoked from StartProcessOptimizer once the join's place
                // ID is known)
                if (RobloxProcessOptimizer.ShouldRun(App.Settings.Prop) || App.Settings.Prop.EnginePlaceProfiles.Count > 0)
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

                if (App.Settings.Prop.BoostTimerResolution || App.Settings.Prop.UseHighPerformancePowerPlan)
                {
                    ActivityWatcher.OnGameJoin += (_, _) => SystemPerformanceBoost.OnGameJoin();
                    ActivityWatcher.OnGameLeave += (_, _) => SystemPerformanceBoost.OnGameLeave();
                }
            }

            if (RobloxWindowCustomizer.IsEnabled)
                RobloxWindowCustomizer.Start(ActivityWatcher);

            // the same actions are also exposed on the tray icon's context menu (MenuContainer),
            // so they live as methods below rather than inline lambdas
            _hotkeys = new GlobalHotkeyManager();
            _hotkeys.RegisterAction(HotkeyActions.CleanRamNow, CleanRamNow);
            _hotkeys.RegisterAction(HotkeyActions.ToggleHeadsetAudio, ToggleHeadsetAudio);
            _hotkeys.RegisterAction(HotkeyActions.TakeScreenshot, TakeScreenshot);
            _hotkeys.RegisterAction(HotkeyActions.SaveInstantReplay, SaveInstantReplay);
            _hotkeys.RegisterAction(HotkeyActions.ToggleOverlayFocusMode, ToggleOverlayFocusMode);
            _hotkeys.ApplyBindings();

            _notifyIcon = new(this);
        }

        private void StartProcessOptimizer()
        {
            if (_watcherData is null)
                return;

            // ActivityWatcher.Data.PlaceId is already resolved by the time OnGameJoin fires, so the
            // per-place preset/exclusion (EnginePresets.Resolve) can be looked up right here
            long placeId = ActivityWatcher?.Data.PlaceId ?? 0;
            EnginePresetValues effective = placeId != 0 ? EnginePresets.Resolve(placeId) : EnginePresets.FromSettings(App.Settings.Prop);

            _processOptimizer ??= new RobloxProcessOptimizer(_watcherData.ProcessId, effective);
            _processOptimizer.Start();
        }

        private void StopProcessOptimizer()
        {
            _processOptimizer?.Dispose();
            _processOptimizer = null;
        }

        public int RobloxProcessId => _watcherData?.ProcessId ?? 0;

        public bool InstantReplayRunning => _instantReplay.IsRunning;

        public void CleanRamNow()
        {
            // deliberately the unelevated trim only - the standby list purge needs an elevated
            // relaunch (a UAC prompt), which would interrupt whatever's in focus (a game) every
            // single time this fires. That part stays a manual, explicit action from the
            // Rendering page's Clean RAM button, same reasoning as AutoRamCleaner.
            SystemMemoryCleaner.TrimResult result = SystemMemoryCleaner.TrimAllProcessWorkingSets();
            NotificationCenter.Notify(
                "RAM cleaned",
                $"Trimmed {result.ProcessesTrimmed} processes (~{result.BytesFreed / 1048576.0:0.#} MB).",
                NotificationCategory.General);
        }

        public void ToggleHeadsetAudio()
        {
            bool enabled = !App.Settings.Prop.HeadsetAudioEnabled;
            App.Settings.Prop.HeadsetAudioEnabled = enabled;
            App.Settings.Save();

            if (enabled)
                HeadsetAudio.Start();
            else
                HeadsetAudio.Stop();
        }

        public void TakeScreenshot()
        {
            string? path = ScreenshotCapture.Capture();

            bool copied = path is not null && App.Settings.Prop.CaptureCopyScreenshotToClipboard && CopyCapture(path, image: true);

            if (path is not null)
                TidyCaptures();

            NotificationCenter.Notify(
                path is null ? "Screenshot failed" : copied ? "Screenshot saved and copied" : "Screenshot saved",
                path is not null ? Path.GetFileName(path) : "Could not find the Roblox window.",
                NotificationCategory.General,
                onClick: path is not null ? NotificationCenter.RevealFile(path) : null);
        }

        // runs after a capture has been saved - the only moment old captures are ever cleaned up
        private static void TidyCaptures()
        {
            int limitMb = App.Settings.Prop.CaptureStorageLimitMB;
            int maxAge = App.Settings.Prop.CaptureMaxAgeDays;
            if (limitMb <= 0 && maxAge <= 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    CaptureStorage.Log ??= message => App.Logger.WriteLine("CaptureStorage", message);
                    CaptureStorage.Result result = CaptureStorage.Enforce(
                        new[] { ScreenshotCapture.ScreenshotsDir, InstantReplayRecorder.ClipsDir },
                        limitMb * 1048576L, maxAge);

                    if (result.Removed > 0)
                        App.Logger.WriteLine("Watcher::TidyCaptures", $"Moved {result.Removed} old capture(s) ({result.FreedBytes / 1048576.0:0} MB) to the Recycle Bin");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("Watcher::TidyCaptures", $"Failed: {ex.Message}");
                }
            });
        }

        private static bool CopyCapture(string path, bool image)
        {
            ClipboardShare.Log ??= message => App.Logger.WriteLine("ClipboardShare", message);
            return image ? ClipboardShare.CopyImageFile(path) : ClipboardShare.CopyFile(path);
        }

        private int _replaySaving;

        public void SaveInstantReplay()
        {
            const string LOG_IDENT = "Watcher::SaveInstantReplay";

            if (!App.Settings.Prop.InstantReplayEnabled)
            {
                NotificationCenter.Notify("Instant Replay is off", "Turn it on under Capture > Instant Replay - it starts buffering as soon as you're in a game.", NotificationCategory.General);
                return;
            }

            if (!_instantReplay.IsRunning)
            {
                if (ActivityWatcher?.InGame == true)
                {
                    // enabled but never started (e.g. enabled before this build) - start now
                    _instantReplay.Start();
                    NotificationCenter.Notify("Instant Replay just started", "It's buffering now - press the hotkey again in a few seconds to save a clip.", NotificationCategory.General);
                }
                else
                {
                    NotificationCenter.Notify("Not in a game yet", "Instant Replay only buffers while you're in a Roblox game.", NotificationCategory.General);
                }
                return;
            }

            if (Interlocked.CompareExchange(ref _replaySaving, 1, 0) != 0)
            {
                NotificationCenter.Notify("Still saving the last clip", "Give it a few seconds before pressing the hotkey again.", NotificationCategory.General);
                return;
            }

            // encoding takes a few seconds - it must never run on this thread (the hotkey/message
            // thread), or every later hotkey press and tray interaction queues up behind it
            int seconds = App.Settings.Prop.InstantReplayClipSeconds;
            NotificationCenter.Notify("Saving replay...", $"Encoding the last {seconds}s to MP4 - this takes a few seconds.", NotificationCategory.General, 4);
            App.Logger.WriteLine(LOG_IDENT, "Encoding clip on a background thread");

            _ = Task.Run(() =>
            {
                string? path = null;
                string? error = null;

                try
                {
                    path = _instantReplay.SaveClip();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _replaySaving, 0);
                }

                bool copied = path is not null && App.Settings.Prop.CaptureCopyReplayToClipboard && CopyCapture(path, image: false);

                if (path is not null)
                    TidyCaptures();

                App.Current.Dispatcher.BeginInvoke(() =>
                {
                    NotificationCenter.Notify(
                        path is null ? "Replay failed" : copied ? "Replay saved and copied" : "Replay saved",
                        path is not null ? Path.GetFileName(path) : (error ?? "Nothing was buffered yet - check the log for details."),
                        NotificationCategory.General, 6,
                        onClick: path is not null ? NotificationCenter.RevealFile(path) : null);
                });
            });
        }

        public void ToggleOverlayFocusMode()
        {
            bool enabled = !App.Settings.Prop.OverlayFocusModeEnabled;
            App.Settings.Prop.OverlayFocusModeEnabled = enabled;
            App.Settings.Save();
            OverlayHub.Refresh();

            NotificationCenter.Notify(
                enabled ? "Overlay Focus Mode on" : "Overlay Focus Mode off",
                enabled ? "HUD and crosshair are hidden until you toggle this again." : "HUD and crosshair are back.",
                NotificationCategory.General);
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

            // ActivityWatcher.InGame only ever goes back to false via a clean disconnect/leave log
            // line (see ActivityWatcher's GameDisconnectedEntry/GameLeavingEntry handling) - if the
            // process is gone but that never happened, nothing ever told us the session ended
            // normally, so this is the closest honest signal for "Roblox crashed" available without
            // reading process exit codes (which Roblox's own client doesn't set meaningfully anyway).
            bool possibleCrash = ActivityWatcher is not null && ActivityWatcher.InGame;

            // not a crash if FastFlagPresetSession just closed Roblox on purpose to restart it
            if (possibleCrash && Utility.FastFlagPresetSession.RestartedRecently)
                possibleCrash = false;

            if (possibleCrash && App.Settings.Prop.AutoRejoinOnCrash)
            {
                await TryAutoRejoinAsync();

                // LaunchHandler.LaunchWatcher tears this whole process down (Dispose + App.Terminate)
                // the instant Run() returns - without this, the final "rejoin succeeded/failed" toast
                // never gets a chance to render, since NotificationCenter.Notify only queues it onto
                // the dispatcher and returns immediately rather than waiting for the animation
                await Task.Delay(TimeSpan.FromSeconds(6));
            }

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        private async Task TryAutoRejoinAsync()
        {
            const string LOG_IDENT = "Watcher::TryAutoRejoinAsync";

            long placeId = ActivityWatcher?.Data.PlaceId ?? 0;
            string jobId = ActivityWatcher?.Data.JobId ?? "";

            if (placeId == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "Possible crash detected but no place ID was captured, cannot rejoin");
                return;
            }

            int maxAttempts = Math.Max(1, App.Settings.Prop.AutoRejoinMaxAttempts);
            int delaySeconds = Math.Max(1, App.Settings.Prop.AutoRejoinDelaySeconds);

            string uri = string.IsNullOrEmpty(jobId)
                ? $"roblox://experiences/start?placeId={placeId}"
                : $"roblox://experiences/start?placeId={placeId}&gameInstanceId={jobId}";

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                NotificationCenter.Notify(
                    "Possible crash detected",
                    $"Attempting to rejoin (try {attempt} of {maxAttempts})...",
                    NotificationCategory.General);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                try
                {
                    Process.Start(Paths.Process, $"-player \"{uri}\"");
                    App.Logger.WriteLine(LOG_IDENT, $"Rejoin attempt {attempt}/{maxAttempts} launched for place {placeId}, job {jobId}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Rejoin attempt {attempt}/{maxAttempts} failed to launch: {ex.Message}");
                    continue;
                }

                // give the freshly-launched process a moment to actually start before deciding
                // whether this attempt "took" - if a Roblox player process is now running, stop
                // here rather than launching several overlapping instances
                await Task.Delay(TimeSpan.FromSeconds(3));

                if (Utilities.GetProcessesSafe().Any(x => x.ProcessName.Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)))
                {
                    NotificationCenter.Notify("Rejoin successful", "Roblox relaunched.", NotificationCategory.General);
                    return;
                }
            }

            NotificationCenter.Notify("Rejoin failed", $"Could not relaunch Roblox after {maxAttempts} attempt(s).", NotificationCategory.General);
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            OverlayHub.Shutdown();
            ServerPingMonitor.Stop();

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();
            GameChat?.Dispose();
            IntegrationWatcher?.Dispose();
            PlayTimeWatcher?.Dispose();
            SessionTracker?.Dispose();
            PlayTimeStore.Shutdown();
            FakeExclusiveFullscreen.Shutdown();
            AudioDucker.Shutdown();
            HeadsetAudio.Shutdown();
            ForcedResolution.Shutdown();
            RobloxWindowCustomizer.Shutdown();
            StopProcessOptimizer();
            MemoryManager.Shutdown();
            _hotkeys?.Dispose();
            _instantReplay.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
