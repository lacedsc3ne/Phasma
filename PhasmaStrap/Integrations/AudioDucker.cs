using NAudio.CoreAudioApi;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PhasmaStrap.Integrations
{
    // Ducks (lowers) Roblox's own audio session while its window is in the background during
    // a game session, and fades it back to full volume the moment Roblox is refocused; any
    // ducked session is restored to its original volume when the session ends. Runs only
    // between OnGameJoin and OnGameLeave/Dispose, gated behind DuckRobloxAudioOnUnfocus.
    // Simplified from Voidstrap's version: that one also supports a standalone always-on
    // "duck on unfocus" mode independent of game sessions, a "reset Roblox audio on next
    // launch" one-shot setting, and cooperates with Voidstrap's HeadsetAudio/Overlays
    // integrations; this only runs for the duration of a tracked game session and talks to
    // the Windows Core Audio session APIs directly. Ported from Voidstrap.
    public static class AudioDucker
    {
        private sealed class SessionSnapshot
        {
            public uint ProcessId { get; init; }
            public float Volume { get; init; }
            public bool Muted { get; init; }
        }

        private const string LOG_IDENT = "AudioDucker";
        private const string RobloxProcess = "RobloxPlayerBeta";
        private const float DuckLevel = 0.2f;
        private const float FadeStep = 0.25f;
        private const int FadeIntervalMs = 150;
        private const int FocusPollIntervalMs = 500;
        private const int SessionRefreshIntervalMs = 5000;
        private const int PidCacheIntervalMs = 5000;

        private static readonly object _gate = new();
        private static readonly Dictionary<string, SessionSnapshot> _snapshots = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, uint> _normalizedSessions = new(StringComparer.Ordinal);
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static CancellationTokenSource? _restoreCts;
        private static Task? _restoreTask;
        private static int _generation;

        private static HashSet<uint> _cachedRobloxPids = new();
        private static long _cachedRobloxPidsMs;

        public static bool IsRunning { get; private set; }

        public static bool Start()
        {
            if (!App.Settings.Prop.DuckRobloxAudioOnUnfocus)
                return false;

            CancellationTokenSource? owner = null;
            try
            {
                CancelRestoreRetries();
                RestoreAllSnapshots();
                lock (_gate)
                {
                    _normalizedSessions.Clear();
                    if (IsRunning)
                        return true;

                    owner = new CancellationTokenSource();
                    int generation = ++_generation;
                    _cts = owner;
                    IsRunning = true;
                    _loopTask = Task.Run(() => LoopAsync(generation, owner));
                }
                App.Logger.WriteLine(LOG_IDENT, "Audio ducking started");
                return true;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_cts, owner))
                    {
                        _cts = null;
                        _loopTask = null;
                        IsRunning = false;
                    }
                }
                owner?.Dispose();
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;
            lock (_gate)
            {
                cts = _cts;
                if (cts == null && _loopTask == null)
                {
                    IsRunning = false;
                }
                else
                {
                    _generation++;
                    IsRunning = false;
                    _cts = null;
                    _loopTask = null;
                }
            }

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            RestoreAllSnapshots();
            StartRestoreRetries();
            App.Logger.WriteLine(LOG_IDENT, "Audio ducking stopped");
        }

        public static void Shutdown()
        {
            Stop();
            CancelRestoreRetries();
            RestoreAllSnapshots();
        }

        private static async Task LoopAsync(int generation, CancellationTokenSource owner)
        {
            CancellationToken token = owner.Token;
            bool wasFocused = true;
            bool fading = false;
            long nextSessionRefreshMs = 0;
            try
            {
                while (!token.IsCancellationRequested && generation == Volatile.Read(ref _generation))
                {
                    HashSet<uint> pids = GetRobloxPidsCached();
                    if (pids.Count == 0)
                    {
                        RestoreAllSnapshots();
                        ClearNormalizedSessions();
                        wasFocused = true;
                        fading = false;
                        nextSessionRefreshMs = 0;
                        await Task.Delay(FocusPollIntervalMs, token).ConfigureAwait(false);
                        continue;
                    }

                    bool focused = IsForegroundRoblox(pids);
                    long now = Environment.TickCount64;
                    if (!fading && focused == wasFocused && now < nextSessionRefreshMs)
                    {
                        await Task.Delay(FocusPollIntervalMs, token).ConfigureAwait(false);
                        continue;
                    }
                    bool complete = ProcessSessions(pids, focused, generation);
                    fading = !complete;
                    nextSessionRefreshMs = now + (fading ? FadeIntervalMs : SessionRefreshIntervalMs);
                    if (focused && complete && HasSnapshots())
                        ClearSnapshots();
                    wasFocused = focused;
                    await Task.Delay(fading ? FadeIntervalMs : FocusPollIntervalMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Audio loop error: " + ex.Message);
            }
            finally
            {
                bool retryRestore;
                lock (_gate)
                {
                    retryRestore = !IsRunning;
                    if (ReferenceEquals(_cts, owner))
                    {
                        _cts = null;
                        _loopTask = null;
                        IsRunning = false;
                    }
                }
                owner.Dispose();
                if (retryRestore)
                {
                    RestoreAllSnapshots();
                    StartRestoreRetries();
                }
            }
        }

        private static bool ProcessSessions(HashSet<uint> pids, bool focused, int generation)
        {
            lock (_gate)
            {
                foreach (KeyValuePair<string, uint> item in _normalizedSessions.ToArray())
                {
                    if (!pids.Contains(item.Value))
                        _normalizedSessions.Remove(item.Key);
                }
            }

            Dictionary<string, SessionSnapshot> snapshots = CopySnapshots();
            bool complete = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ForEachRobloxSession(pids, (key, session) =>
            {
                bool normalized;
                lock (_gate)
                {
                    if (generation != _generation || !IsRunning)
                        return;
                    normalized = _normalizedSessions.ContainsKey(key);
                    if (!normalized)
                    {
                        if (!focused)
                        {
                            session.SimpleAudioVolume.Volume = DuckLevel;
                            SessionSnapshot seeded = new()
                            {
                                ProcessId = session.GetProcessID,
                                Volume = 1.0f,
                                Muted = session.SimpleAudioVolume.Mute
                            };
                            _snapshots[key] = seeded;
                            snapshots[key] = seeded;
                        }
                        else
                        {
                            session.SimpleAudioVolume.Volume = 1.0f;
                            _snapshots.Remove(key);
                            snapshots.Remove(key);
                        }
                        _normalizedSessions[key] = session.GetProcessID;
                    }
                    if (!focused && !_snapshots.ContainsKey(key))
                    {
                        SessionSnapshot captured = new()
                        {
                            ProcessId = session.GetProcessID,
                            Volume = session.SimpleAudioVolume.Volume,
                            Muted = session.SimpleAudioVolume.Mute
                        };
                        _snapshots[key] = captured;
                        snapshots[key] = captured;
                    }
                    if (snapshots.TryGetValue(key, out SessionSnapshot? snapshot))
                    {
                        seen.Add(key);
                        float target = focused ? snapshot.Volume : snapshot.Volume * DuckLevel;
                        float current = session.SimpleAudioVolume.Volume;
                        float next = StepToward(current, target, FadeStep);
                        session.SimpleAudioVolume.Volume = next;
                        if (focused)
                            session.SimpleAudioVolume.Mute = snapshot.Muted;
                        if (Math.Abs(next - target) > 0.005f)
                            complete = false;
                        else
                            session.SimpleAudioVolume.Volume = target;
                    }
                }
            });

            if (focused)
            {
                foreach (KeyValuePair<string, SessionSnapshot> item in snapshots)
                {
                    if (!seen.Contains(item.Key) && pids.Contains(item.Value.ProcessId))
                        complete = false;
                }
            }
            return complete;
        }

        private static HashSet<uint> GetRobloxPidsCached()
        {
            long now = Environment.TickCount64;
            if (_cachedRobloxPidsMs != 0 && now - _cachedRobloxPidsMs < PidCacheIntervalMs)
                return _cachedRobloxPids;
            _cachedRobloxPids = GetRobloxPids();
            _cachedRobloxPidsMs = now;
            return _cachedRobloxPids;
        }

        private static HashSet<uint> GetRobloxPids()
        {
            var result = new HashSet<uint>();
            try
            {
                Process[] processes = Process.GetProcessesByName(RobloxProcess);
                foreach (Process process in processes)
                {
                    try
                    {
                        result.Add((uint)process.Id);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private static unsafe bool IsForegroundRoblox(HashSet<uint> pids)
        {
            try
            {
                HWND foreground = PInvoke.GetForegroundWindow();
                if (foreground == HWND.Null)
                    return false;
                uint pid;
                PInvoke.GetWindowThreadProcessId(foreground, &pid);
                return pids.Contains(pid);
            }
            catch
            {
                return false;
            }
        }

        private static bool RestoreAllSnapshots()
        {
            Dictionary<string, SessionSnapshot> snapshots = CopySnapshots();
            if (snapshots.Count == 0)
                return true;

            HashSet<uint> pids = GetRobloxPids();
            var restored = new HashSet<string>(StringComparer.Ordinal);
            bool enumerated = ForEachRobloxSession(pids, (key, session) =>
            {
                if (!snapshots.TryGetValue(key, out SessionSnapshot? snapshot))
                    return;
                lock (_gate)
                {
                    session.SimpleAudioVolume.Volume = snapshot.Volume;
                    session.SimpleAudioVolume.Mute = snapshot.Muted;
                    restored.Add(key);
                }
            });
            if (!enumerated)
                return false;
            lock (_gate)
            {
                foreach (KeyValuePair<string, SessionSnapshot> item in snapshots)
                {
                    if (restored.Contains(item.Key) || !pids.Contains(item.Value.ProcessId))
                        _snapshots.Remove(item.Key);
                }
                return _snapshots.Count == 0;
            }
        }

        private static void StartRestoreRetries()
        {
            lock (_gate)
            {
                if (_snapshots.Count == 0 || IsRunning || _restoreTask is { IsCompleted: false })
                    return;
                var cts = new CancellationTokenSource();
                _restoreCts = cts;
                _restoreTask = Task.Run(() => RestoreUntilCompleteAsync(cts));
            }
        }

        private static async Task RestoreUntilCompleteAsync(CancellationTokenSource owner)
        {
            try
            {
                for (int attempt = 0; attempt < 20 && !owner.IsCancellationRequested; attempt++)
                {
                    if (RestoreAllSnapshots())
                        break;
                    await Task.Delay(500, owner.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_restoreCts, owner))
                    {
                        _restoreCts = null;
                        _restoreTask = null;
                    }
                }
                owner.Dispose();
            }
        }

        private static void CancelRestoreRetries()
        {
            CancellationTokenSource? cts;
            lock (_gate)
            {
                cts = _restoreCts;
                _restoreCts = null;
                _restoreTask = null;
            }
            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static bool ForEachRobloxSession(HashSet<uint> pids, Action<string, AudioSessionControl> action)
        {
            if (pids.Count == 0)
                return true;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    try
                    {
                        SessionCollection sessions = device.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            AudioSessionControl session = sessions[i];
                            try
                            {
                                if (!pids.Contains(session.GetProcessID))
                                    continue;
                                string instance = session.GetSessionInstanceIdentifier;
                                if (string.IsNullOrEmpty(instance))
                                    instance = session.GetSessionIdentifier + ":" + session.GetProcessID;
                                action(device.ID + "|" + instance, session);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        device.Dispose();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Audio device access failed: " + ex.Message);
                return false;
            }
        }

        private static Dictionary<string, SessionSnapshot> CopySnapshots()
        {
            lock (_gate)
                return new Dictionary<string, SessionSnapshot>(_snapshots, StringComparer.Ordinal);
        }

        private static bool HasSnapshots()
        {
            lock (_gate)
                return _snapshots.Count > 0;
        }

        private static void ClearSnapshots()
        {
            lock (_gate)
                _snapshots.Clear();
        }

        private static void ClearNormalizedSessions()
        {
            lock (_gate)
                _normalizedSessions.Clear();
        }

        private static float StepToward(float current, float target, float step)
        {
            if (current < target)
                return Math.Min(current + step, target);
            if (current > target)
                return Math.Max(current - step, target);
            return target;
        }
    }
}
