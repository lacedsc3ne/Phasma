using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11 = Vortice.Direct3D11.D3D11;

namespace PhasmaStrap.Utility
{
    public sealed class PerformanceSample
    {
        public double Second { get; set; }
        public double GameCpuPercent { get; set; }
        public double GameMemoryMb { get; set; }
        public double PageFaultsPerSecond { get; set; }
        public double FreeMemoryMb { get; set; }
        public string BusiestOther { get; set; } = "";
        public double BusiestOtherPercent { get; set; }
    }

    public sealed class Stutter
    {
        public double Second { get; set; }
        public double Milliseconds { get; set; }
    }

    public sealed class PerformanceReport
    {
        public string Id { get; set; } = "";
        public DateTime WhenLocal { get; set; }
        public string Label { get; set; } = "";
        public string Game { get; set; } = "";
        public long PlaceId { get; set; }
        public string Experiment { get; set; } = "";
        public string Variant { get; set; } = "";

        public double Seconds { get; set; }
        public int Frames { get; set; }
        public double AverageFps { get; set; }
        public double MedianFrameMs { get; set; }
        public double Low1Fps { get; set; }
        public double Low01Fps { get; set; }
        public double WorstFrameMs { get; set; }
        public double StuttersPerMinute { get; set; }
        public int CapFps { get; set; }

        public List<Stutter> Stutters { get; set; } = new();
        public List<double> WorstPerSlice { get; set; } = new();
        public List<double> FpsPerSecond { get; set; } = new();
        public List<PerformanceSample> Samples { get; set; } = new();
        public List<string> Findings { get; set; } = new();
        public string Verdict { get; set; } = "";
    }

    public sealed class FrameTimeProbe
    {
        public static Action<string>? Log;

        private static readonly FeatureLevel[] FeatureLevels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

        private readonly string _processName;

        public FrameTimeProbe(string processName)
        {
            _processName = processName;
        }

        public double Progress { get; private set; }

        public PerformanceReport? Measure(int seconds, CancellationToken token)
        {
            var frameTimes = new List<(double At, double Ms)>(seconds * 250);
            var samples = new List<PerformanceSample>();

            ID3D11Device? device = null;
            ID3D11DeviceContext? context = null;
            IDXGIOutputDuplication? duplication = null;

            timeBeginPeriod(1);

            try
            {
                var wall = Stopwatch.StartNew();
                double measured = 0;
                long lastPresent = 0;
                double lastTick = 0;
                IntPtr hwnd = IntPtr.Zero;
                double hwndChecked = -10;

                double sharedMeasured = 0;
                bool inFront = false, sampling = true;
                var samplerThread = new Thread(() =>
                {
                    var sampler = new Sampler(_processName);
                    while (Volatile.Read(ref sampling))
                    {
                        Thread.Sleep(1000);
                        if (!Volatile.Read(ref inFront))
                            continue;

                        try
                        {
                            PerformanceSample? sample = sampler.Take(Volatile.Read(ref sharedMeasured));
                            if (sample is not null)
                            {
                                lock (samples)
                                    samples.Add(sample);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke($"Sampling failed: {ex.Message}");
                        }
                    }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "FrameTimeProbeSampler" };
                samplerThread.Start();

                try
                {
                while (measured < seconds && wall.Elapsed.TotalSeconds < seconds * 3 + 20)
                {
                    token.ThrowIfCancellationRequested();
                    Progress = Math.Min(1, measured / seconds);
                    Volatile.Write(ref sharedMeasured, measured);

                    if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || wall.Elapsed.TotalSeconds - hwndChecked > 3)
                    {
                        hwnd = FindWindow(_processName);
                        hwndChecked = wall.Elapsed.TotalSeconds;
                    }

                    bool front = hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd && !IsIconic(hwnd);
                    Volatile.Write(ref inFront, front);

                    if (!front)
                    {
                        lastPresent = 0;
                        lastTick = 0;
                        Thread.Sleep(150);
                        continue;
                    }

                    if (duplication is null)
                    {
                        if (device is null)
                            D3D11.D3D11CreateDevice((IDXGIAdapter)null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport, FeatureLevels, out device, out context).CheckError();

                        duplication = Duplicate(device!, hwnd);
                        if (duplication is null)
                        {
                            Log?.Invoke("Desktop duplication is not available for the monitor the game is on");
                            return null;
                        }
                    }

                    IDXGIResource? resource = null;
                    bool acquired = false;

                    try
                    {
                        duplication.AcquireNextFrame(100, out OutduplFrameInfo info, out resource);
                        acquired = true;

                        double now = wall.Elapsed.TotalSeconds;
                        if (lastTick > 0)
                            measured += Math.Min(0.25, now - lastTick);
                        lastTick = now;

                        if (info.LastPresentTime != 0)
                        {
                            if (lastPresent != 0 && info.AccumulatedFrames > 0)
                            {
                                double ms = (info.LastPresentTime - lastPresent) * 1000.0 / Stopwatch.Frequency / info.AccumulatedFrames;
                                if (ms > 0 && ms < 5000)
                                {
                                    for (int i = 0; i < info.AccumulatedFrames && i < 8; i++)
                                        frameTimes.Add((measured, ms));
                                }
                            }

                            lastPresent = info.LastPresentTime;
                        }
                    }
                    catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.WaitTimeout)
                    {
                        double now = wall.Elapsed.TotalSeconds;
                        if (lastTick > 0)
                            measured += Math.Min(0.25, now - lastTick);
                        lastTick = now;
                    }
                    catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
                    {
                        duplication.Dispose();
                        duplication = null;
                        lastPresent = 0;
                    }
                    finally
                    {
                        resource?.Dispose();
                        if (acquired)
                        {
                            try { duplication?.ReleaseFrame(); } catch { }
                        }
                    }
                }
                }
                finally
                {
                    Volatile.Write(ref sampling, false);
                    samplerThread.Join(2500);
                }

                Progress = 1;

                if (frameTimes.Count < 60)
                {
                    Log?.Invoke($"Only {frameTimes.Count} frames seen - the game was not the window in front");
                    return null;
                }

                lock (samples)
                    return Analyse(frameTimes, new List<PerformanceSample>(samples));
            }
            finally
            {
                timeEndPeriod(1);
                try { duplication?.Dispose(); } catch { }
                context?.Dispose();
                device?.Dispose();
            }
        }

        private static IDXGIOutputDuplication? Duplicate(ID3D11Device device, IntPtr hwnd)
        {
            GetWindowRect(hwnd, out RECT rect);
            int centerX = (rect.Left + rect.Right) / 2, centerY = (rect.Top + rect.Bottom) / 2;

            using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
            dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();

            try
            {
                for (int i = 0; ; i++)
                {
                    Result result = adapter.EnumOutputs(i, out IDXGIOutput output);
                    if (result.Failure || output is null)
                        return null;

                    try
                    {
                        var bounds = output.Description.DesktopCoordinates;
                        if (centerX < bounds.Left || centerX >= bounds.Right || centerY < bounds.Top || centerY >= bounds.Bottom)
                            continue;

                        using IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                        return output1.DuplicateOutput(device);
                    }
                    finally
                    {
                        output.Dispose();
                    }
                }
            }
            finally
            {
                adapter.Dispose();
            }
        }

        public static PerformanceReport Analyse(List<(double At, double Ms)> frames, List<PerformanceSample> samples)
        {
            var report = new PerformanceReport { Id = Guid.NewGuid().ToString("N"), WhenLocal = DateTime.Now, Samples = samples };

            List<double> times = frames.Select(f => f.Ms).ToList();
            List<double> sorted = times.OrderBy(t => t).ToList();
            double total = times.Sum();

            report.Frames = times.Count;
            report.Seconds = frames[^1].At;
            report.AverageFps = total > 0 ? times.Count * 1000.0 / total : 0;
            report.MedianFrameMs = sorted[sorted.Count / 2];
            report.WorstFrameMs = sorted[^1];
            report.Low1Fps = 1000.0 / sorted.Skip((int)(sorted.Count * 0.99)).DefaultIfEmpty(sorted[^1]).Average();
            report.Low01Fps = 1000.0 / sorted.Skip((int)(sorted.Count * 0.999)).DefaultIfEmpty(sorted[^1]).Average();

            var window = new Queue<double>();
            double windowSum = 0;

            for (int i = 0; i < frames.Count; i++)
            {
                double ms = frames[i].Ms;
                double usual = window.Count >= 20 ? windowSum / window.Count : report.MedianFrameMs;

                if (ms > usual * 2.5 && ms > usual + 12)
                {
                    if (report.Stutters.Count == 0 || frames[i].At - report.Stutters[^1].Second > 0.05)
                        report.Stutters.Add(new Stutter { Second = frames[i].At, Milliseconds = ms });
                }
                else
                {
                    window.Enqueue(ms);
                    windowSum += ms;
                    if (window.Count > 120)
                        windowSum -= window.Dequeue();
                }
            }

            report.StuttersPerMinute = report.Seconds > 0 ? report.Stutters.Count * 60.0 / report.Seconds : 0;

            int slices = Math.Max(1, (int)Math.Ceiling(report.Seconds / 0.25));
            double[] worst = new double[slices];
            double[] frameCount = new double[(int)Math.Ceiling(report.Seconds) + 1];
            foreach ((double at, double ms) in frames)
            {
                int slice = Math.Min(slices - 1, (int)(at / 0.25));
                worst[slice] = Math.Max(worst[slice], ms);
                frameCount[Math.Min(frameCount.Length - 1, (int)at)]++;
            }
            report.WorstPerSlice = worst.Select(w => Math.Round(w, 1)).ToList();
            report.FpsPerSecond = frameCount.Take(Math.Max(1, (int)report.Seconds)).ToList();

            foreach (int cap in new[] { 30, 60, 75, 90, 120, 144, 165, 240 })
            {
                double capMs = 1000.0 / cap;
                if (Math.Abs(report.MedianFrameMs - capMs) / capMs < 0.025 && sorted[(int)(sorted.Count * 0.1)] > capMs * 0.93)
                    report.CapFps = cap;
            }

            Explain(report);
            return report;
        }

        private static void Explain(PerformanceReport report)
        {
            List<string> findings = report.Findings;
            List<PerformanceSample> samples = report.Samples;
            List<Stutter> stutters = report.Stutters;

            double typicalStutter = stutters.Count > 0 ? stutters.Select(s => s.Milliseconds).OrderBy(ms => ms).ElementAt(stutters.Count / 2) : 0;

            report.Verdict = report.StuttersPerMinute < 1 && report.Low1Fps > report.AverageFps * 0.6
                ? $"Smooth: {report.AverageFps:0} fps on average, slowest 1 % of frames at {report.Low1Fps:0} fps, {stutters.Count} stutter(s) in {report.Seconds:0} s."
                : typicalStutter < 34 && report.WorstFrameMs < 60
                    ? $"Smooth with small hitches: {report.AverageFps:0} fps on average, slowest 1 % at {report.Low1Fps:0} fps. {stutters.Count} frame(s) in {report.Seconds:0} s took noticeably longer than their neighbours, but only around {typicalStutter:0} ms (worst {report.WorstFrameMs:0} ms) - hard to feel at this frame rate."
                : report.StuttersPerMinute < 6
                    ? $"Mostly smooth, with the odd hitch: {report.AverageFps:0} fps on average, slowest 1 % at {report.Low1Fps:0} fps, {stutters.Count} stutter(s) in {report.Seconds:0} s (worst {report.WorstFrameMs:0} ms)."
                    : $"It stutters: {report.AverageFps:0} fps on average but the slowest 1 % of frames run at {report.Low1Fps:0} fps, with {report.StuttersPerMinute:0} stutters a minute (worst {report.WorstFrameMs:0} ms).";

            if (report.CapFps > 0)
                findings.Add($"The frame rate sits at a cap of {report.CapFps} fps (Roblox's limiter, V-Sync or a driver limit). The average says nothing about headroom then - the lows and stutters are what count.");

            if (samples.Count == 0)
                return;

            var bad = new HashSet<int>(stutters.Select(s => (int)s.Second));
            List<PerformanceSample> during = samples.Where(s => bad.Contains((int)s.Second) || bad.Contains((int)s.Second - 1)).ToList();
            List<PerformanceSample> calm = samples.Except(during).ToList();

            double minFree = samples.Min(s => s.FreeMemoryMb);
            if (minFree > 0 && minFree < 700)
                findings.Add($"Windows was down to {minFree:0} MB of free memory. Below about 1 GB it starts pushing the game's data out to disk, and every time that data is needed again the game waits - close a browser or other programs before playing.");

            if (during.Count >= 3 && calm.Count >= 5)
            {
                double faultsDuring = during.Average(s => s.PageFaultsPerSecond), faultsCalm = calm.Average(s => s.PageFaultsPerSecond);
                if (faultsDuring > 4000 && faultsDuring > faultsCalm * 3)
                    findings.Add($"In the seconds with stutters the game took {faultsDuring:0} page faults a second, against {faultsCalm:0} otherwise. It was pulling data in (new assets streaming in, or memory that had been paged out) right when it hitched - typical while an area loads, and much worse when memory is short or the game sits on a hard disk.");

                foreach (var group in during.Where(s => s.BusiestOther.Length > 0 && s.BusiestOtherPercent >= 25).GroupBy(s => s.BusiestOther).OrderByDescending(g => g.Count()))
                {
                    double shareDuring = group.Count() / (double)during.Count;
                    double shareCalm = calm.Count(s => s.BusiestOther == group.Key && s.BusiestOtherPercent >= 25) / (double)calm.Count;

                    if (shareDuring >= 0.4 && shareDuring > shareCalm * 2)
                    {
                        findings.Add($"{group.Key} was busy ({group.Average(s => s.BusiestOtherPercent):0} % of a core) in {shareDuring:P0} of the seconds with a stutter, but only {shareCalm:P0} of the calm ones - it is competing with the game. Close it or stop whatever it does in the background while you play.");
                        break;
                    }
                }
            }

            double cpu = samples.Average(s => s.GameCpuPercent);
            if (cpu > 85)
                findings.Add($"Roblox used {cpu:0} % of the whole processor on average - the CPU is the limit here, and anything else that runs takes frames away.");

            if (stutters.Count >= 6)
            {
                List<double> gaps = stutters.Zip(stutters.Skip(1), (a, b) => b.Second - a.Second).ToList();
                double mean = gaps.Average();
                double deviation = Math.Sqrt(gaps.Average(g => (g - mean) * (g - mean)));

                if (mean > 0.8 && deviation / mean < 0.2)
                    findings.Add($"The stutters come like clockwork, every {mean:0.0} s. Games do not hitch that regularly by themselves: something on the PC polls on a timer - hardware monitoring, RGB or fan control software, an overlay updating, a USB device driver. Close such tools one at a time to find it.");
            }

            if (stutters.Count > 0 && stutters.Count(s => s.Second < 40) >= Math.Max(2, stutters.Count * 0.7) && report.Seconds > 80)
                findings.Add("Nearly all stutters fell in the first 40 seconds. That is the game streaming in its world and the graphics driver compiling shaders for it - it settles, and the second visit to the same game is smoother.");

            if (findings.Count == (report.CapFps > 0 ? 1 : 0) && stutters.Count > 0)
                findings.Add("Nothing on the PC's side lines up with the stutters: memory was free, no other program was busy at those moments and the processor had room. What is left is the game itself - scripts doing heavy work in one frame, or new parts of the map loading in.");
        }

        private sealed class Sampler
        {
            private readonly string _processName;
            private readonly Dictionary<int, (string Name, TimeSpan Cpu)> _previous = new();
            private DateTime _previousWall = DateTime.UtcNow;
            private uint _previousFaults;
            private bool _hasFaults;

            public Sampler(string processName)
            {
                _processName = processName;
                Snapshot(out _, out _);
            }

            private Dictionary<int, (string Name, TimeSpan Cpu)> Snapshot(out Process? game, out List<Process> all)
            {
                var result = new Dictionary<int, (string, TimeSpan)>();
                game = null;
                all = Process.GetProcesses().ToList();

                foreach (Process process in all)
                {
                    try
                    {
                        result[process.Id] = (process.ProcessName, process.TotalProcessorTime);

                        if (game is null && process.ProcessName.Equals(_processName, StringComparison.OrdinalIgnoreCase) && process.MainWindowHandle != IntPtr.Zero)
                            game = process;
                    }
                    catch
                    {
                    }
                }

                return result;
            }

            public PerformanceSample? Take(double second)
            {
                Dictionary<int, (string Name, TimeSpan Cpu)> current = Snapshot(out Process? game, out List<Process> all);

                try
                {
                    DateTime now = DateTime.UtcNow;
                    double wallMs = Math.Max(1, (now - _previousWall).TotalMilliseconds);

                    var sample = new PerformanceSample { Second = second };

                    string busiest = "";
                    double busiestPercent = 0;

                    foreach (KeyValuePair<int, (string Name, TimeSpan Cpu)> pair in current)
                    {
                        if (!_previous.TryGetValue(pair.Key, out var before) || before.Name != pair.Value.Name)
                            continue;

                        double percentOfCore = (pair.Value.Cpu - before.Cpu).TotalMilliseconds / wallMs * 100;

                        if (game is not null && pair.Key == game.Id)
                            sample.GameCpuPercent = percentOfCore / Environment.ProcessorCount;
                        else if (percentOfCore > busiestPercent && pair.Key != Environment.ProcessId && pair.Value.Name != "Idle")
                        {
                            busiestPercent = percentOfCore;
                            busiest = pair.Value.Name;
                        }
                    }

                    sample.BusiestOther = busiest;
                    sample.BusiestOtherPercent = busiestPercent;

                    if (game is not null)
                    {
                        sample.GameMemoryMb = game.WorkingSet64 / 1048576.0;

                        var counters = new PROCESS_MEMORY_COUNTERS { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>() };
                        if (GetProcessMemoryInfo(game.Handle, out counters, counters.cb))
                        {
                            if (_hasFaults)
                                sample.PageFaultsPerSecond = unchecked(counters.PageFaultCount - _previousFaults) / (wallMs / 1000.0);

                            _previousFaults = counters.PageFaultCount;
                            _hasFaults = true;
                        }
                    }

                    var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                    if (GlobalMemoryStatusEx(ref memory))
                        sample.FreeMemoryMb = memory.ullAvailPhys / 1048576.0;

                    _previous.Clear();
                    foreach (var pair in current)
                        _previous[pair.Key] = pair.Value;
                    _previousWall = now;

                    return game is null ? null : sample;
                }
                finally
                {
                    foreach (Process process in all)
                        process.Dispose();
                }
            }
        }

        private static IntPtr FindWindow(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.MainWindowHandle;
                }

                return IntPtr.Zero;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("psapi.dll")] private static extern bool GetProcessMemoryInfo(IntPtr process, out PROCESS_MEMORY_COUNTERS counters, uint size);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
    }
}
