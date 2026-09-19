using PhasmaStrap.Integrations;
using PhasmaStrap.UI;

namespace PhasmaStrap.Utility
{
    public sealed class MeasureRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; } = "";
        public int Seconds { get; set; } = 60;
        public int DelayAfterJoinSeconds { get; set; }      // let the world finish loading first
        public string Experiment { get; set; } = "";
        public string Variant { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class MeasureStatus
    {
        public string RequestId { get; set; } = "";
        public string State { get; set; } = "";             // waiting / measuring / done / failed
        public double Progress { get; set; }
        public string Message { get; set; } = "";
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    // Performance measurements have to run where the game session is (the Watcher), but are asked
    // for and looked at in the settings window - a different process. They talk through three
    // things under <base>\Diagnostics: request.json (one pending order), status.json (what the
    // Watcher is doing about it) and Runs\*.json (finished reports).
    internal static class PerformanceRuns
    {
        private const string LOG_IDENT = "PerformanceRuns";
        private const int Keep = 60;

        private static string Folder => Path.Combine(Paths.Base, "Diagnostics");
        private static string RequestPath => Path.Combine(Folder, "request.json");
        private static string StatusPath => Path.Combine(Folder, "status.json");
        private static string RunsFolder => Path.Combine(Folder, "Runs");

        private static T? Read<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void Write<T>(string path, T value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(value));
                File.Move(temp, path, true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write {path}: {ex.Message}");
            }
        }

        public static MeasureRequest? ReadRequest() => Read<MeasureRequest>(RequestPath);

        public static void WriteRequest(MeasureRequest request)
        {
            Write(RequestPath, request);
            WriteStatus(new MeasureStatus { RequestId = request.Id, State = "waiting", Message = "Waiting for a game session..." });
        }

        public static void ClearRequest()
        {
            try { File.Delete(RequestPath); } catch { }
        }

        public static MeasureStatus? ReadStatus() => Read<MeasureStatus>(StatusPath);

        public static void WriteStatus(MeasureStatus status)
        {
            status.UpdatedUtc = DateTime.UtcNow;
            Write(StatusPath, status);
        }

        public static void Save(PerformanceReport report)
        {
            Write(Path.Combine(RunsFolder, $"{report.WhenLocal:yyyyMMdd_HHmmss}_{report.Id[..6]}.json"), report);

            try
            {
                foreach (FileInfo old in new DirectoryInfo(RunsFolder).GetFiles("*.json").OrderByDescending(f => f.Name).Skip(Keep))
                    old.Delete();
            }
            catch
            {
            }
        }

        public static List<PerformanceReport> List()
        {
            var result = new List<PerformanceReport>();

            try
            {
                if (!Directory.Exists(RunsFolder))
                    return result;

                foreach (FileInfo file in new DirectoryInfo(RunsFolder).GetFiles("*.json").OrderByDescending(f => f.Name))
                {
                    if (Read<PerformanceReport>(file.FullName) is PerformanceReport report)
                        result.Add(report);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not list runs: {ex.Message}");
            }

            return result;
        }

        public static void Delete(string id)
        {
            try
            {
                foreach (FileInfo file in new DirectoryInfo(RunsFolder).GetFiles($"*_{id[..6]}.json"))
                    file.Delete();
            }
            catch
            {
            }
        }
    }

    // The Watcher's half: picks up a request once a game is being played, measures, files the report.
    internal sealed class PerformanceMeasurer : IDisposable
    {
        private const string LOG_IDENT = "PerformanceMeasurer";

        private readonly ActivityWatcher _activityWatcher;
        private readonly System.Threading.Timer _timer;
        private readonly CancellationTokenSource _cancel = new();
        private int _busy;

        public PerformanceMeasurer(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _timer = new System.Threading.Timer(_ => Poll(), null, 3000, 2000);
        }

        private void Poll()
        {
            if (Volatile.Read(ref _busy) != 0)
                return;

            try
            {
                MeasureRequest? request = PerformanceRuns.ReadRequest();
                if (request is null)
                    return;

                if ((DateTime.UtcNow - request.CreatedUtc).TotalHours > 8)
                {
                    PerformanceRuns.ClearRequest();
                    return;
                }

                if (!_activityWatcher.InGame)
                {
                    PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "waiting", Message = "Waiting for you to join a game..." });
                    return;
                }

                double inGame = (DateTime.Now - _activityWatcher.Data.TimeJoined).TotalSeconds;
                if (inGame < request.DelayAfterJoinSeconds)
                {
                    PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "waiting", Message = $"In a game - letting it finish loading ({request.DelayAfterJoinSeconds - inGame:0} s)..." });
                    return;
                }

                if (Interlocked.Exchange(ref _busy, 1) != 0)
                    return;

                // taken: a second Watcher (multi-instance) must not measure the same order
                PerformanceRuns.ClearRequest();

                var thread = new Thread(() => Run(request)) { IsBackground = true, Name = "PerformanceMeasurer" };
                thread.Start();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Poll failed: {ex.Message}");
            }
        }

        private void Run(MeasureRequest request)
        {
            try
            {
                FrameTimeProbe.Log ??= message => App.Logger.WriteLine("FrameTimeProbe", message);

                long placeId = _activityWatcher.Data.PlaceId;
                long universeId = _activityWatcher.Data.UniverseId;

                NotificationCenter.Notify("Measuring performance", $"Keep playing normally for {request.Seconds} seconds. Only time with Roblox in front counts.", NotificationCategory.General, 5);
                App.Logger.WriteLine(LOG_IDENT, $"Measuring {request.Seconds}s (label '{request.Label}', experiment '{request.Experiment}', variant '{request.Variant}')");

                var probe = new FrameTimeProbe(App.RobloxPlayerAppName);

                using var progress = new System.Threading.Timer(_ =>
                    PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "measuring", Progress = probe.Progress, Message = "Measuring - keep playing..." }), null, 0, 1000);

                PerformanceReport? report = probe.Measure(request.Seconds, _cancel.Token);

                progress.Change(Timeout.Infinite, Timeout.Infinite);

                if (report is null)
                {
                    PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "failed", Message = "Roblox was not the window in front long enough to measure anything." });
                    NotificationCenter.Notify("Nothing measured", "Roblox has to be the window in front while measuring.", NotificationCategory.General);
                    return;
                }

                report.Label = request.Label;
                report.Experiment = request.Experiment;
                report.Variant = request.Variant;
                report.PlaceId = placeId;

                try
                {
                    report.Game = Models.Entities.UniverseDetails.LoadFromCache(universeId)?.Data?.Name ?? "";
                }
                catch
                {
                }

                PerformanceRuns.Save(report);
                PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "done", Progress = 1, Message = report.Verdict });

                NotificationCenter.Notify("Performance measured", report.Verdict, NotificationCategory.General, 10,
                    onClick: () => { try { Process.Start(Paths.Process, "-settings"); } catch { } });
            }
            catch (OperationCanceledException)
            {
                PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "failed", Message = "Roblox closed before the measurement finished." });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                PerformanceRuns.WriteStatus(new MeasureStatus { RequestId = request.Id, State = "failed", Message = $"The measurement failed: {ex.Message}" });
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            _cancel.Cancel();
        }
    }
}
