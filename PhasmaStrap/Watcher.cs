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

        private readonly PerformanceMeasurer? _performanceMeasurer;

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
                NowPlaying.Follow(ActivityWatcher);

                ActivityWatcher.OnGameJoin += (sender, _) => OverlayHub.OnGameJoin((sender as ActivityWatcher)?.Data.PlaceId ?? 0);
                ActivityWatcher.OnGameLeave += delegate { OverlayHub.OnGameLeave(); };

                ActivityWatcher.OnGameJoin += (sender, _) =>
                {
                    if (App.Settings.Prop.OverlayHudShowPing || App.Settings.Prop.OverlayHudShowRegion)
                        ServerPingMonitor.Start((sender as ActivityWatcher)?.Data.MachineAddress);
                };
                ActivityWatcher.OnGameLeave += (_, _) => ServerPingMonitor.Stop();

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

                if (App.Settings.Prop.GameChatEnabled)
                    GameChat = new(ActivityWatcher, _watcherData.ProcessId);

                if (App.Settings.Prop.CustomIntegrations.Count > 0)
                    IntegrationWatcher = new(ActivityWatcher);

                PlayTimeWatcher = new(ActivityWatcher);

                if (App.Settings.Prop.SessionHistoryEnabled)
                    SessionTracker = new(ActivityWatcher);

                _performanceMeasurer = new(ActivityWatcher);

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

                ActivityWatcher.OnGameJoin += (_, _) =>
                {
                    if (App.Settings.Prop.InstantReplayEnabled)
                        _instantReplay.Start();
                };
                ActivityWatcher.OnGameLeave += (_, _) => _instantReplay.Stop();

                _ = Task.Run(() =>
                {
                    try { Utility.RobloxVersions.ShowPendingFlagNotice(); }
                    catch (Exception ex) { App.Logger.WriteLine("Watcher", $"Flag notice failed: {ex.Message}"); }

                    Utility.AccountGuard.ScanAndNotify();
                });

                ActivityWatcher.OnGameJoin += (sender, _) =>
                {
                    if (sender is ActivityWatcher watcher)
                        Utility.FlagProfileSession.OnGameJoined(watcher.Data);
                };

                Utility.SettingsHotReload.Reloaded += (_, _) =>
                {
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
                        OverlayHub.Refresh();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("Watcher::SettingsReloaded", $"Overlay refresh failed: {ex.Message}");
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

                ActivityWatcher.OnGameJoin += (sender, _) => ForcedResolution.OnGameJoin((sender as ActivityWatcher)?.Data.PlaceId ?? 0);
                ActivityWatcher.OnGameLeave += (_, _) => ForcedResolution.OnGameLeave();

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

            _hotkeys = new GlobalHotkeyManager();
            _hotkeys.RegisterAction(HotkeyActions.CleanRamNow, CleanRamNow);
            _hotkeys.RegisterAction(HotkeyActions.ToggleHeadsetAudio, ToggleHeadsetAudio);
            _hotkeys.RegisterAction(HotkeyActions.TakeScreenshot, TakeScreenshot);
            _hotkeys.RegisterAction(HotkeyActions.SaveInstantReplay, SaveInstantReplay);
            _hotkeys.RegisterAction(HotkeyActions.ToggleOverlayFocusMode, ToggleOverlayFocusMode);
            _hotkeys.RegisterAction(HotkeyActions.RaiseFrameLimit, RaiseFrameLimit);
            _hotkeys.RegisterAction(HotkeyActions.LowerFrameLimit, LowerFrameLimit);
            _hotkeys.ApplyBindings();

            _notifyIcon = new(this);
        }

        private void StartProcessOptimizer()
        {
            if (_watcherData is null)
                return;

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
            SystemMemoryCleaner.TrimResult result = SystemMemoryCleaner.TrimAllProcessWorkingSets();
            NotificationCenter.Notify(
                "RAM cleaned",
                $"Trimmed {result.ProcessesTrimmed} processes (~{result.BytesFreed / 1048576.0:0.#} MB).",
                NotificationCategory.General,
                kind: NotificationKindId.RamCleaned);
        }

        public void RaiseFrameLimit() => StepFrameLimit(true);

        public void LowerFrameLimit() => StepFrameLimit(false);

        private void StepFrameLimit(bool up)
        {
            if (!Integrations.Nvidia.FrameLimiter.Available)
            {
                NotificationCenter.Notify(
                    "Frame rate limit needs an NVIDIA card",
                    Integrations.Nvidia.FrameLimiter.UnavailableReason,
                    NotificationCategory.General,
                    kind: NotificationKindId.FrameRateLimit);
                return;
            }

            int current = Integrations.Nvidia.FrameLimiter.Current();
            int wanted = up
                ? Integrations.Nvidia.FrameLimiter.Raise(current)
                : Integrations.Nvidia.FrameLimiter.Lower(current);

            if (wanted == current)
            {
                NotificationCenter.Notify(
                    $"Frame rate limit: {Integrations.Nvidia.FrameLimiter.Describe(current)}",
                    up ? "Already at the top of your list." : "Already at the bottom of your list.",
                    NotificationCategory.General,
                    kind: NotificationKindId.FrameRateLimit);
                return;
            }

            if (!Integrations.Nvidia.FrameLimiter.Set(wanted))
            {
                NotificationCenter.Notify(
                    "Frame rate limit did not change",
                    "The NVIDIA driver would not take the new limit. The log has the reason.",
                    NotificationCategory.General,
                    kind: NotificationKindId.FrameRateLimit);
                return;
            }

            NotificationCenter.Notify(
                $"Frame rate limit: {Integrations.Nvidia.FrameLimiter.Describe(wanted)}",
                "This is the NVIDIA driver's limiter for Roblox. If the change does not show up straight away, it takes effect the next time Roblox starts.",
                NotificationCategory.General,
                kind: NotificationKindId.FrameRateLimit);
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

        private bool _pickingArea;

        public void TakeScreenshot()
        {
            App.SendStat("capture", "screenshot");

            if (!App.Settings.Prop.ScreenshotPickArea)
            {
                ScreenshotTaken(ScreenshotCapture.Capture());
                return;
            }

            if (_pickingArea)
                return;

            System.Drawing.Bitmap? shot = ScreenshotCapture.Grab(out System.Drawing.Rectangle where);
            if (shot is null)
            {
                ScreenshotTaken(null);
                return;
            }

            _pickingArea = true;
            try
            {
                UI.Elements.Dialogs.ScreenshotAreaWindow.Pick(shot, where, picked =>
                {
                    _pickingArea = false;
                    if (picked is null)
                        return;

                    using (picked)
                        ScreenshotTaken(ScreenshotCapture.Save(picked));
                });
            }
            catch (Exception ex)
            {
                _pickingArea = false;
                App.Logger.WriteException("Watcher::TakeScreenshot", ex);
                shot.Dispose();
            }
        }

        private void ScreenshotTaken(string? path)
        {
            bool copied = path is not null && App.Settings.Prop.CaptureCopyScreenshotToClipboard && CopyCapture(path, image: true);

            if (path is not null)
                TidyCaptures();

            NotificationCenter.Notify(
                path is null ? "Screenshot failed" : copied ? "Screenshot saved and copied" : "Screenshot saved",
                path is not null ? Path.GetFileName(path) : "Could not find the Roblox window.",
                NotificationCategory.General,
                onClick: path is not null ? NotificationCenter.RevealFile(path) : null,
                actionText: path is not null ? "Edit" : null,
                action: path is not null ? () => Utility.CaptureLibrary.OpenEditor(path) : null,
                kind: NotificationKindId.Screenshot);
        }

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
            App.SendStat("capture", "instantReplay");

            const string LOG_IDENT = "Watcher::SaveInstantReplay";

            if (!App.Settings.Prop.InstantReplayEnabled)
            {
                NotificationCenter.Notify("Instant Replay is off", "Turn it on under Capture > Instant Replay - it starts buffering as soon as you're in a game.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                return;
            }

            if (!_instantReplay.IsRunning)
            {
                if (ActivityWatcher?.InGame == true)
                {
                    _instantReplay.Start();
                    NotificationCenter.Notify("Instant Replay just started", "It's buffering now - press the hotkey again in a few seconds to save a clip.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                }
                else
                {
                    NotificationCenter.Notify("Not in a game yet", "Instant Replay only buffers while you're in a Roblox game.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                }
                return;
            }

            if (Interlocked.CompareExchange(ref _replaySaving, 1, 0) != 0)
            {
                NotificationCenter.Notify("Still saving the last clip", "Give it a moment before pressing the hotkey again.", NotificationCategory.General, kind: NotificationKindId.ReplayNotReady);
                return;
            }

            int seconds = App.Settings.Prop.InstantReplayClipSeconds;
            if (!_instantReplay.OnGpu)
                NotificationCenter.Notify("Saving replay...", $"Encoding the last {seconds}s to MP4 - this takes a few seconds.", NotificationCategory.General, 4, kind: NotificationKindId.Replay);
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
                        onClick: path is not null ? NotificationCenter.RevealFile(path) : null,
                        actionText: path is not null ? "Edit" : null,
                        action: path is not null ? () => Utility.CaptureLibrary.OpenEditor(path) : null,
                        kind: NotificationKindId.Replay);
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
                NotificationCategory.General,
                kind: NotificationKindId.OverlayFocusMode);
        }

        public void KillRobloxProcess()
        {
            _killedOnPurpose = true;
            CloseProcess(_watcherData!.ProcessId, true);
        }

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

        private async Task WaitForRobloxExitAsync()
        {
            int pid = _watcherData!.ProcessId;

            try
            {
                using Process roblox = Process.GetProcessById(pid);

                if (string.Equals(roblox.ProcessName, App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger.WriteLine("Watcher::Run", $"Holding a handle on Roblox ({pid}), waiting for it to exit");
                    await roblox.WaitForExitAsync();
                    return;
                }

                App.Logger.WriteLine("Watcher::Run", $"Process {pid} is '{roblox.ProcessName}', not Roblox, so there is nothing to watch");
                return;
            }
            catch (ArgumentException)
            {
                App.Logger.WriteLine("Watcher::Run", $"Roblox ({pid}) had already exited");
                return;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::Run", $"Could not hold a handle on Roblox ({pid}), falling back to polling: {ex.Message}");
            }

            while (Utilities.GetProcessesSafe().Any(x => x.Id == pid && string.Equals(x.ProcessName, App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)))
                await Task.Delay(1000);
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();

            await WaitForRobloxExitAsync();

            bool possibleCrash = ActivityWatcher is not null && ActivityWatcher.InGame;

            bool intentional = Utility.FlagProfileSession.RestartedRecently || _killedOnPurpose;
            if (possibleCrash && intentional)
                possibleCrash = false;

            bool crashToast = false;

            if (App.Settings.Prop.CrashAnalyzerEnabled && !intentional)
            {
                CrashReport? crash = await AnalyzeExitAsync();

                if (crash is not null)
                {
                    if (crash.CleanExit && crash.Confidence.Length == 0)
                    {
                        possibleCrash = false;
                    }
                    else
                    {
                        CrashReports.Save(crash);

                        if (possibleCrash || crash.Confidence != "Unclear")
                        {
                            NotificationCenter.Notify("Roblox closed unexpectedly", crash.Cause, NotificationCategory.General, 12,
                                onClick: () => { try { Process.Start(Paths.Process, "-settings"); } catch { } },
                                kind: NotificationKindId.RobloxClosed);
                            crashToast = true;
                        }
                    }
                }
            }

            if (possibleCrash && App.Settings.Prop.AutoRejoinOnCrash)
            {
                await TryAutoRejoinAsync();

                await Task.Delay(TimeSpan.FromSeconds(6));
            }
            else if (crashToast)
            {
                await Task.Delay(TimeSpan.FromSeconds(12));
            }

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        private bool _killedOnPurpose;

        private async Task<CrashReport?> AnalyzeExitAsync()
        {
            const string LOG_IDENT = "Watcher::AnalyzeExit";

            try
            {
                string? log = ActivityWatcher?.LogLocation ?? _watcherData?.LogFile;
                if (string.IsNullOrEmpty(log) || !File.Exists(log))
                    return null;

                await Task.Delay(2500);

                CrashReport report = await Task.Run(() => CrashReports.Analyze(log));
                App.Logger.WriteLine(LOG_IDENT, $"clean exit: {report.CleanExit}, confidence: '{report.Confidence}', cause: {report.Cause}");
                return report;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Analysis failed: {ex.Message}");
                return null;
            }
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
                    NotificationCategory.General,
                    kind: NotificationKindId.AutoRejoin);

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

                await Task.Delay(TimeSpan.FromSeconds(3));

                if (Utilities.GetProcessesSafe().Any(x => x.ProcessName.Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)))
                {
                    NotificationCenter.Notify("Rejoin successful", "Roblox relaunched.", NotificationCategory.General, kind: NotificationKindId.AutoRejoin);
                    return;
                }
            }

            NotificationCenter.Notify("Rejoin failed", $"Could not relaunch Roblox after {maxAttempts} attempt(s).", NotificationCategory.General, kind: NotificationKindId.AutoRejoin);
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            Step("now playing", NowPlaying.Forget);
            Step("overlays", OverlayHub.Shutdown);
            Step("ping monitor", ServerPingMonitor.Stop);
            Step("tray icon", () => _notifyIcon?.Dispose());
            Step("discord", () => RichPresence?.Dispose());
            Step("game chat", () => GameChat?.Dispose());
            Step("integrations", () => IntegrationWatcher?.Dispose());
            Step("playtime", () => PlayTimeWatcher?.Dispose());
            Step("sessions", () => SessionTracker?.Dispose());
            Step("performance", () => _performanceMeasurer?.Dispose());
            Step("playtime store", PlayTimeStore.Shutdown);
            Step("fullscreen", FakeExclusiveFullscreen.Shutdown);
            Step("audio ducking", AudioDucker.Shutdown);
            Step("headset audio", HeadsetAudio.Shutdown);
            Step("resolution", ForcedResolution.Shutdown);
            Step("window customizer", RobloxWindowCustomizer.Shutdown);
            Step("process optimizer", StopProcessOptimizer);
            Step("memory manager", MemoryManager.Shutdown);
            Step("hotkeys", () => _hotkeys?.Dispose());
            Step("instant replay", _instantReplay.Dispose);
            Step("activity watcher", () => ActivityWatcher?.Dispose());
            Step("watcher lock", _lock.Dispose);

            GC.SuppressFinalize(this);
        }

        private static void Step(string name, Action action)
        {
            var clock = Stopwatch.StartNew();

            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Watcher::Dispose", $"{name} threw: {ex.Message}");
            }

            if (clock.ElapsedMilliseconds > 750)
                App.Logger.WriteLine("Watcher::Dispose", $"{name} took {clock.ElapsedMilliseconds} ms");
        }
    }
}
