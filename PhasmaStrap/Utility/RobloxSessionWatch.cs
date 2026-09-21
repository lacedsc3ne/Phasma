using PhasmaStrap.Models;

namespace PhasmaStrap.Utility
{
    public static class RobloxSessionWatch
    {
        private const string LOG_IDENT = "RobloxSessionWatch";
        private const string MutexName = @"Local\PhasmaStrapSessionWatch";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ClaimInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LogWait = TimeSpan.FromSeconds(20);

        private static readonly HashSet<int> _seen = new();

        private static CancellationTokenSource? _cts;
        private static Thread? _thread;

        public static void Start()
        {
            if (_thread is not null)
                return;

            _cts = new CancellationTokenSource();

            _thread = new Thread(() => Loop(_cts.Token))
            {
                IsBackground = true,
                Name = "Roblox session watch"
            };

            _thread.Start();
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _thread = null;
        }

        private static void Loop(CancellationToken token)
        {
            using var mutex = new Mutex(false, MutexName);
            bool watching = false;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!watching)
                    {
                        try
                        {
                            watching = mutex.WaitOne(0);
                        }
                        catch (AbandonedMutexException)
                        {
                            watching = true;
                        }

                        if (watching)
                            App.Logger.WriteLine(LOG_IDENT, "This process is looking for Roblox sessions PhasmaStrap did not start");
                    }

                    if (watching && App.Settings.Prop.WatchExternalLaunches)
                    {
                        try
                        {
                            Sweep(token);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Sweep failed: {ex.Message}");
                        }
                    }

                    if (token.WaitHandle.WaitOne(watching ? PollInterval : ClaimInterval))
                        return;
                }
            }
            finally
            {
                if (watching)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not hand over the session watch: {ex.Message}");
                    }
                }
            }
        }

        private static void Sweep(CancellationToken token)
        {
            Process[] players = Process.GetProcessesByName(App.RobloxPlayerAppName);

            try
            {
                _seen.RemoveWhere(pid => players.All(p => p.Id != pid));

                if (players.Length == 0)
                    return;

                Process player = players.OrderBy(StartedAt).First();

                if (!_seen.Add(player.Id))
                    return;

                if (WatcherAlreadyRunning())
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox {player.Id} is already being watched");
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Roblox {player.Id} started without PhasmaStrap, picking it up");
                Adopt(player, token);
            }
            finally
            {
                foreach (Process player in players)
                    player.Dispose();
            }
        }

        private static DateTime StartedAt(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static bool WatcherAlreadyRunning()
        {
            using var probe = new InterProcessLock("Watcher");
            return !probe.IsAcquired;
        }

        private static void Adopt(Process player, CancellationToken token)
        {
            DateTime startedAt = StartedAt(player);
            string? log = WaitForLog(startedAt, player, token);

            if (log is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No Roblox log turned up for {player.Id}, leaving it alone");
                return;
            }

            var data = new WatcherData
            {
                ProcessId = player.Id,
                LogFile = log,
                AutoclosePids = new List<int>()
            };

            try
            {
                string argument = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
                Process.Start(Paths.Process, $"-watcher \"{argument}\"");

                App.Logger.WriteLine(LOG_IDENT, $"Watching {player.Id} with {Path.GetFileName(log)}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not start a watcher for {player.Id}: {ex.Message}");
            }
        }

        private static string? WaitForLog(DateTime startedAt, Process player, CancellationToken token)
        {
            DateTime giveUp = DateTime.Now + LogWait;

            while (DateTime.Now < giveUp && !token.IsCancellationRequested)
            {
                if (player.HasExited)
                    return null;

                string? found = NewestPlayerLog(startedAt);

                if (found is not null)
                    return found;

                if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
                    return null;
            }

            return null;
        }

        private static string? NewestPlayerLog(DateTime startedAt)
        {
            try
            {
                if (!Directory.Exists(Paths.RobloxLogs))
                    return null;

                return new DirectoryInfo(Paths.RobloxLogs)
                    .EnumerateFiles("*_Player_*.log")
                    .Where(file => file.CreationTime >= startedAt.AddSeconds(-20))
                    .OrderByDescending(file => file.CreationTime)
                    .FirstOrDefault()?.FullName;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not look through the Roblox logs: {ex.Message}");
                return null;
            }
        }
    }
}
