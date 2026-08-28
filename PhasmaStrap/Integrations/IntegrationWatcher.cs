using System.Collections.Concurrent;
using PhasmaStrap.Models;

namespace PhasmaStrap.Integrations
{
    // launches/closes custom integrations as you join/leave games, rather than only once
    // when Roblox itself starts/exits: an integration can be scoped to a specific game (or
    // left unscoped to run for any game) and optionally closed again the moment you leave
    // that game (e.g. teleporting to a different place). Ported from Voidstrap.
    public sealed class IntegrationWatcher : IDisposable
    {
        private const string LOG_IDENT = "IntegrationWatcher";

        private sealed class ActiveIntegration
        {
            public CustomIntegration Integration { get; init; } = null!;
            public DateTime StartTimeUtc { get; init; }
            public string ExecutablePath { get; init; } = "";
        }

        private readonly ActivityWatcher _activityWatcher;
        private readonly ConcurrentDictionary<int, ActiveIntegration> _activeIntegrations = new();
        private readonly ConcurrentDictionary<string, long> _launchedSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private bool _disposed;
        private long _sessionGeneration;

        public IntegrationWatcher(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnGameJoin += OnGameJoin;
            _activityWatcher.OnGameLeave += OnGameLeave;
        }

        private void OnGameJoin(object? sender, EventArgs e)
        {
            if (_disposed || !_activityWatcher.InGame)
                return;

            PruneExitedProcesses();

            long placeId = _activityWatcher.Data.PlaceId;
            string jobId = _activityWatcher.Data.JobId;
            long sessionGeneration = Volatile.Read(ref _sessionGeneration);

            foreach (CustomIntegration integration in App.Settings.Prop.CustomIntegrations)
            {
                if ((!integration.SpecifyGame || integration.GameID == placeId.ToString()) && ReserveForSession(integration, sessionGeneration))
                    _ = LaunchIntegrationAsync(integration, placeId, jobId, sessionGeneration, _lifetimeCancellation.Token);
            }
        }

        private void OnGameLeave(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            Interlocked.Increment(ref _sessionGeneration);

            foreach (var item in _activeIntegrations.ToArray())
            {
                if (item.Value.Integration.AutoCloseOnGame)
                {
                    TerminateProcess(item.Key, item.Value);
                    _activeIntegrations.TryRemove(item.Key, out _);
                }
            }

            PruneExitedProcesses();
        }

        private bool ReserveForSession(CustomIntegration integration, long sessionGeneration)
        {
            string key = string.Join("\n", integration.Location, integration.LaunchArgs, integration.Name, integration.RunAsAdmin, integration.RunMinimized);

            while (true)
            {
                if (_launchedSessions.TryGetValue(key, out long existing))
                {
                    if (existing == sessionGeneration)
                        return false;

                    if (_launchedSessions.TryUpdate(key, sessionGeneration, existing))
                        return true;

                    continue;
                }

                if (_launchedSessions.TryAdd(key, sessionGeneration))
                    return true;
            }
        }

        private async Task LaunchIntegrationAsync(CustomIntegration integration, long placeId, string jobId, long sessionGeneration, CancellationToken token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(integration.Location) || !File.Exists(integration.Location))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Integration missing: {integration.Name}");
                    return;
                }

                if (integration.Delay > 0)
                    await Task.Delay(Math.Min(integration.Delay, 30000), token);

                if (!IsCurrentSession(placeId, jobId, sessionGeneration, token))
                    return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = integration.Location,
                    Arguments = integration.LaunchArgs.Replace("\r\n", " "),
                    WorkingDirectory = Path.GetDirectoryName(integration.Location),
                    UseShellExecute = true
                };

                if (integration.RunMinimized)
                    startInfo.WindowStyle = ProcessWindowStyle.Minimized;

                if (integration.RunAsAdmin)
                    startInfo.Verb = "runas";

                using Process? process = Process.Start(startInfo);
                if (process is null)
                    return;

                if (!IsCurrentSession(placeId, jobId, sessionGeneration, token))
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch { }
                    return;
                }

                DateTime startTimeUtc;
                try { startTimeUtc = process.StartTime.ToUniversalTime(); }
                catch { startTimeUtc = DateTime.MinValue; }

                string executablePath;
                try { executablePath = process.MainModule?.FileName ?? integration.Location; }
                catch { executablePath = integration.Location; }

                App.Logger.WriteLine(LOG_IDENT, $"Integration '{integration.Name}' launched for place {placeId} (PID {process.Id})");

                _activeIntegrations[process.Id] = new ActiveIntegration
                {
                    Integration = integration,
                    StartTimeUtc = startTimeUtc,
                    ExecutablePath = executablePath
                };
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to launch integration '{integration.Name}': {ex.Message}");
            }
        }

        private bool IsCurrentSession(long placeId, string jobId, long sessionGeneration, CancellationToken token) =>
            !_disposed
            && !token.IsCancellationRequested
            && Volatile.Read(ref _sessionGeneration) == sessionGeneration
            && _activityWatcher.InGame
            && _activityWatcher.Data.PlaceId == placeId
            && string.Equals(_activityWatcher.Data.JobId, jobId, StringComparison.OrdinalIgnoreCase);

        private void PruneExitedProcesses()
        {
            foreach (var item in _activeIntegrations.ToArray())
            {
                bool remove;
                try
                {
                    using Process process = Process.GetProcessById(item.Key);
                    remove = process.HasExited || !MatchesProcess(process, item.Value);
                }
                catch
                {
                    remove = true;
                }

                if (remove)
                    _activeIntegrations.TryRemove(item.Key, out _);
            }
        }

        private static bool MatchesProcess(Process process, ActiveIntegration expected)
        {
            if (expected.StartTimeUtc == DateTime.MinValue)
                return false;

            try
            {
                if (process.StartTime.ToUniversalTime() != expected.StartTimeUtc)
                    return false;
            }
            catch
            {
                return false;
            }

            if (!string.IsNullOrEmpty(expected.ExecutablePath))
            {
                try
                {
                    string currentPath = process.MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(currentPath) && !string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(expected.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                catch { }
            }

            return true;
        }

        private void TerminateProcess(int pid, ActiveIntegration expected)
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(pid);
                if (process.HasExited || !MatchesProcess(process, expected))
                    return;

                process.Kill();
                App.Logger.WriteLine(LOG_IDENT, $"Terminated integration process (PID {pid})");
            }
            catch (Exception)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to terminate process (PID {pid}), likely already exited");
            }
            finally
            {
                process?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Interlocked.Increment(ref _sessionGeneration);
            _lifetimeCancellation.Cancel();

            try
            {
                _activityWatcher.OnGameJoin -= OnGameJoin;
                _activityWatcher.OnGameLeave -= OnGameLeave;
            }
            catch { }

            foreach (var item in _activeIntegrations.ToArray())
            {
                if (item.Value.Integration.AutoClose || item.Value.Integration.AutoCloseOnGame)
                    TerminateProcess(item.Key, item.Value);
            }

            _activeIntegrations.Clear();
            _launchedSessions.Clear();
            _lifetimeCancellation.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
