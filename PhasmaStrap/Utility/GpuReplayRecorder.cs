using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using D3D11 = Vortice.Direct3D11.D3D11;

namespace PhasmaStrap.Utility
{
    // Instant Replay's GPU path.
    //
    // The older recorder (InstantReplayRecorder) reads every frame back to system memory, JPEG
    // compresses it on the CPU (about half a core at 1080p60), keeps up to 1.2 GB of JPEGs, and
    // only encodes H.264 when a clip is saved - which takes as long again as the clip.
    //
    // Here the frame never leaves the GPU: desktop duplication hands over a texture, it is copied
    // (GPU to GPU) into a pooled texture, and that texture goes straight to a Media Foundation
    // sink writer that owns the same D3D device - colour conversion and scaling run on the GPU,
    // H.264 on the GPU's encoder block. What is kept is finished H.264, in memory, as a rolling
    // list of short MP4 segments (a few MB each - nothing is written to disk until a clip is
    // saved). Saving a clip is then just a remux of the newest segments (ReplayMuxer): no
    // re-encode, done in a fraction of a second.
    //
    // Why segments rather than one endless stream: the sink writer cannot hand out the encoder's
    // output, only write containers. Each segment is its own writer (so it starts on a keyframe
    // and can be dropped when it ages out); the next one is created ahead of time on a worker so
    // switching costs the capture thread nothing.
    //
    // A clip therefore runs from a segment boundary: it is the requested length or up to one
    // segment longer, never shorter (once that much has been buffered).
    //
    // Only what is on screen while the game is the FOREGROUND window is recorded - desktop
    // duplication sees the whole monitor, and other windows are nobody's business.
    //
    // No App dependencies, so it can be exercised from a console harness.
    public sealed class GpuReplayRecorder : IDisposable
    {
        public static Action<string>? Log;

        public sealed class Settings
        {
            public int Fps = 60;
            public int MaxHeight;                 // 0 = native
            public int ClipSeconds = 20;
            public string ProcessName = "RobloxPlayerBeta";
            public Func<int, int, int, int> BitrateFor = (w, h, fps) => 10_000_000;
        }

        public const int SegmentSeconds = 4;
        private const int MaxPoolTextures = 24;

        private static readonly FeatureLevel[] FeatureLevels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

        private readonly Func<Settings> _settings;
        private Thread? _thread;
        private volatile bool _running;

        // ---- owned by the capture thread
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IntPtr _deviceManager;
        private IDXGIOutputDuplication? _duplication;
        private int _outputLeft, _outputTop, _outputRight, _outputBottom;
        private DateTime _duplicationRetryUtc = DateTime.MinValue;
        private readonly List<PoolItem> _pool = new();
        private int _poolWidth, _poolHeight;
        private Segment? _current;
        private Task<Segment?>? _next;
        private IntPtr _hwnd;
        private DateTime _hwndCheckedUtc = DateTime.MinValue;

        // ---- shared
        private readonly object _sync = new();
        private readonly List<Segment> _closed = new();
        private readonly ManualResetEventSlim _cutDone = new(false);
        private volatile bool _cutRequested;
        private int _failures;
        private bool _everWorked;

        // closed segments that were dropped while a clip was still being written from them
        private readonly List<Segment> _limbo = new();

        // frames the game presented, as counted by desktop duplication - for FpsFeed
        private long _presented;
        private long _presentedSince;

        // stats
        private long _statFrames, _statDropped;
        private DateTime _statSinceUtc;

        public bool IsRunning => _running;

        /// <summary>Set when the GPU path cannot work on this machine - the caller falls back to the CPU recorder.</summary>
        public bool Unavailable { get; private set; }

        public string UnavailableReason { get; private set; } = "";

        /// <summary>Raised (once, from the capture thread) when the GPU path gives up.</summary>
        public event Action? GaveUp;

        public GpuReplayRecorder(Func<Settings> settings)
        {
            _settings = settings;
        }

        // 100ns ticks on the clock every timestamp here (and ReplayAudio) uses
        public static long Now() => (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency));

        private sealed class PoolItem : IDisposable
        {
            public ID3D11Texture2D Texture = null!;
            public IMFMediaBuffer Buffer = null!;

            public void Dispose()
            {
                Buffer.Dispose();
                Texture.Dispose();
            }
        }

        internal sealed class Segment : IDisposable
        {
            public IMFSinkWriter? Writer;
            public IntPtr ByteStream;
            public int Stream;
            public int SourceWidth, SourceHeight, Width, Height, Fps, Bitrate;
            public long StartTicks = -1, EndTicks;
            public int Frames;
            public Task? Finalizing;
            public bool Ok;

            public bool Matches(int sw, int sh, int w, int h, int fps, int bitrate) =>
                SourceWidth == sw && SourceHeight == sh && Width == w && Height == h && Fps == fps && Bitrate == bitrate;

            public void Dispose()
            {
                try { Writer?.Dispose(); } catch { }
                Writer = null;

                if (ByteStream != IntPtr.Zero)
                {
                    Marshal.Release(ByteStream);
                    ByteStream = IntPtr.Zero;
                }
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _statSinceUtc = DateTime.UtcNow;
            _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "GpuReplayCapture" };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;
            try { _thread?.Join(3000); } catch { }
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        // ------------------------------------------------------------------ capture thread

        private void CaptureLoop()
        {
            timeBeginPeriod(1);
            bool mfStarted = false;

            try
            {
                MediaFactory.MFStartup(false);
                mfStarted = true;

                var clock = Stopwatch.StartNew();
                double nextMs = 0;

                while (_running)
                {
                    Settings settings = _settings();
                    int fps = Math.Clamp(settings.Fps, 5, 240);
                    double intervalMs = 1000.0 / fps;

                    try
                    {
                        if (_cutRequested)
                        {
                            CloseCurrent();
                            _cutRequested = false;
                            _cutDone.Set();
                        }

                        double slackMs = Math.Max(0, nextMs - clock.Elapsed.TotalMilliseconds);
                        bool active = CaptureOnce(settings, fps, (int)Math.Ceiling(slackMs + intervalMs));

                        if (!active)
                        {
                            // nothing to record right now (not in front, minimised, no window). The
                            // segment ends here, so the pause becomes a cut in the clip rather than
                            // a frame frozen for as long as the game was out of sight.
                            CloseCurrent();
                            Thread.Sleep(100);
                            nextMs = clock.Elapsed.TotalMilliseconds;
                            continue;
                        }

                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"Capture tick failed: {ex.Message.Trim()}");
                        ReleaseDuplication();
                        DropCurrent();

                        // a pipeline that never gets going is a machine this path does not work on
                        if (++_failures >= 6 && !_everWorked)
                        {
                            Unavailable = true;
                            UnavailableReason = ex.Message;
                            Log?.Invoke("Giving up on GPU recording for this session");
                            break;
                        }

                        Thread.Sleep(500);
                        nextMs = clock.Elapsed.TotalMilliseconds;
                        continue;
                    }

                    ReportStats(fps);

                    nextMs += intervalMs;
                    double waitMs = nextMs - clock.Elapsed.TotalMilliseconds;

                    if (waitMs < -intervalMs * 2)
                        nextMs = clock.Elapsed.TotalMilliseconds;
                    else if (waitMs > 2)
                        Thread.Sleep((int)(waitMs - 1.5));
                }
            }
            catch (Exception ex)
            {
                Unavailable = true;
                UnavailableReason = ex.Message;
                Log?.Invoke($"GPU recording stopped: {ex}");
            }
            finally
            {
                _running = false;

                try { Teardown(); } catch (Exception ex) { Log?.Invoke($"Teardown: {ex.Message}"); }

                if (mfStarted)
                {
                    try { MediaFactory.MFShutdown(); } catch { }
                }

                _cutDone.Set();
                timeEndPeriod(1);

                if (Unavailable)
                {
                    try { GaveUp?.Invoke(); } catch { }
                }
            }
        }

        // false = nothing recordable is in front right now
        private bool CaptureOnce(Settings settings, int fps, int frameWaitMs)
        {
            IntPtr hwnd = ResolveWindow(settings.ProcessName);
            if (hwnd == IntPtr.Zero || IsIconic(hwnd) || GetForegroundWindow() != hwnd)
                return false;

            if (!GetClientRect(hwnd, out RECT client))
                return false;

            var origin = new POINT();
            ClientToScreen(hwnd, ref origin);

            int wantWidth = client.Right - client.Left, wantHeight = client.Bottom - client.Top;
            if (wantWidth < 64 || wantHeight < 64)
                return false;

            if (DateTime.UtcNow < _duplicationRetryUtc)
                return false;

            if (!EnsureDuplication(origin.X + wantWidth / 2, origin.Y + wantHeight / 2))
            {
                _duplicationRetryUtc = DateTime.UtcNow.AddSeconds(10);
                return false;
            }

            IDXGIResource? resource = null;
            bool acquired = false;

            try
            {
                try
                {
                    _duplication!.AcquireNextFrame(Math.Clamp(frameWaitMs, 0, 100), out OutduplFrameInfo info, out resource);
                    acquired = true;

                    // the grab rate is the recorder's; how many frames went by in between is the game's
                    CountPresented((int)info.AccumulatedFrames);

                    // a mouse-only update carries no new image
                    if (info.LastPresentTime == 0)
                        return true;
                }
                catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    return true;
                }
                catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
                {
                    // resolution / fullscreen change or the secure desktop - rebuilt on the next tick
                    ReleaseDuplication();
                    return true;
                }

                if (resource is null)
                    return true;

                using ID3D11Texture2D desktop = resource.QueryInterface<ID3D11Texture2D>();
                Texture2DDescription desc = desktop.Description;

                if (desc.Format != Format.B8G8R8A8_UNorm)
                    throw new NotSupportedException($"the desktop surface is {desc.Format} (HDR), not BGRA8");

                int left = Math.Max(0, origin.X - _outputLeft);
                int top = Math.Max(0, origin.Y - _outputTop);
                int width = (Math.Min(left + wantWidth, (int)desc.Width) - left) & ~1;
                int height = (Math.Min(top + wantHeight, (int)desc.Height) - top) & ~1;
                if (width < 64 || height < 64)
                    return true;

                int outWidth = width, outHeight = height;
                if (settings.MaxHeight > 0 && height > settings.MaxHeight)
                {
                    outHeight = settings.MaxHeight & ~1;
                    outWidth = (int)Math.Round(width * (double)outHeight / height) & ~1;
                }

                int bitrate = settings.BitrateFor(outWidth, outHeight, fps);
                long now = Now();

                if (_current is null || !_current.Matches(width, height, outWidth, outHeight, fps, bitrate))
                {
                    // first frame, or the window / a setting changed: a clip has one format, so
                    // what was buffered in the old one is let go
                    if (_current is not null)
                    {
                        DropCurrent();
                        DropClosed();
                    }

                    _current = TakePrepared(width, height, outWidth, outHeight, fps, bitrate) ?? CreateSegment(width, height, outWidth, outHeight, fps, bitrate);
                    _current.StartTicks = now;
                    Prepare(width, height, outWidth, outHeight, fps, bitrate);
                }
                else if (now - _current.StartTicks >= SegmentSeconds * 10_000_000L)
                {
                    CloseCurrent();
                    _current = TakePrepared(width, height, outWidth, outHeight, fps, bitrate) ?? CreateSegment(width, height, outWidth, outHeight, fps, bitrate);
                    _current.StartTicks = now;
                    Prepare(width, height, outWidth, outHeight, fps, bitrate);
                }

                PoolItem? item = Rent(width, height);
                if (item is null)
                {
                    _statDropped++;
                    return true;
                }

                _context!.CopySubresourceRegion(item.Texture, 0, 0, 0, 0, desktop, 0, new Vortice.Mathematics.Box(left, top, 0, left + width, top + height, 1));

                using IMFSample sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(item.Buffer);
                sample.SampleTime = now - _current.StartTicks;
                sample.SampleDuration = 10_000_000L / fps;

                _current.Writer!.WriteSample(_current.Stream, sample);
                _current.Frames++;
                _everWorked = true;
                _failures = 0;
                Interlocked.Increment(ref _statFrames);

                return true;
            }
            finally
            {
                resource?.Dispose();

                if (acquired)
                {
                    try { _duplication?.ReleaseFrame(); } catch { }
                }
            }
        }

        private void CountPresented(int frames)
        {
            long now = Now();

            if (_presentedSince == 0)
                _presentedSince = now;

            _presented += Math.Max(0, frames);

            long elapsed = now - _presentedSince;
            if (elapsed < 10_000_000)
                return;

            FpsFeed.Report(FpsFeed.Source.Recorder, _presented * 10_000_000.0 / elapsed);
            _presented = 0;
            _presentedSince = now;
        }

        // ------------------------------------------------------------------ segments

        private Segment CreateSegment(int sourceWidth, int sourceHeight, int width, int height, int fps, int bitrate)
        {
            EnsureDevice();

            // 1080p above ~170fps is outside every H.264 level; an encoder that enforces levels
            // refuses it, so the stream is then declared as 60fps - frames keep their real
            // timestamps, only the nominal rate in the header differs
            var attempts = new List<(Guid Input, int DeclaredFps)>
            {
                (VideoFormatGuids.Argb32, fps),
                (VideoFormatGuids.Rgb32, fps),
            };

            if (fps > 60)
            {
                attempts.Add((VideoFormatGuids.Argb32, 60));
                attempts.Add((VideoFormatGuids.Rgb32, 60));
            }

            Exception? last = null;
            string step = "";
            var failures = new List<string>();

            foreach ((Guid input, int declaredFps) in attempts)
            {
                var segment = new Segment { SourceWidth = sourceWidth, SourceHeight = sourceHeight, Width = width, Height = height, Fps = fps, Bitrate = bitrate };

                try
                {
                    step = "memory stream";
                    segment.ByteStream = MfInterop.CreateMemoryByteStream();

                    using IMFAttributes attributes = MediaFactory.MFCreateAttributes(4);
                    MfInterop.SetUInt32(attributes, MfInterop.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                    MfInterop.SetUnknown(attributes, MfInterop.MF_SINK_WRITER_D3D_MANAGER, _deviceManager);
                    MfInterop.SetGuid(attributes, MfInterop.MF_TRANSCODE_CONTAINERTYPE, MfInterop.MFTranscodeContainerType_MPEG4);
                    MfInterop.SetUInt32(attributes, MfInterop.MF_LOW_LATENCY, 1);

                    step = "sink writer";
                    segment.Writer = MfInterop.CreateSinkWriter(null, segment.ByteStream, attributes);
                    step = "media types";

                    using IMFMediaType output = MediaFactory.MFCreateMediaType();
                    output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
                    output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
                    output.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
                    MfInterop.SetUInt64(output, MediaTypeAttributeKeys.FrameSize, MfInterop.Pack((uint)width, (uint)height));
                    MfInterop.SetUInt64(output, MediaTypeAttributeKeys.FrameRate, MfInterop.Pack((uint)declaredFps, 1));
                    MfInterop.SetUInt64(output, MediaTypeAttributeKeys.PixelAspectRatio, MfInterop.Pack(1, 1));

                    using IMFMediaType inputType = MediaFactory.MFCreateMediaType();
                    inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    inputType.Set(MediaTypeAttributeKeys.Subtype, input);
                    inputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
                    MfInterop.SetUInt64(inputType, MediaTypeAttributeKeys.FrameSize, MfInterop.Pack((uint)sourceWidth, (uint)sourceHeight));
                    MfInterop.SetUInt64(inputType, MediaTypeAttributeKeys.FrameRate, MfInterop.Pack((uint)declaredFps, 1));
                    MfInterop.SetUInt64(inputType, MediaTypeAttributeKeys.PixelAspectRatio, MfInterop.Pack(1, 1));

                    step = "AddStream";
                    segment.Stream = segment.Writer.AddStream(output);
                    step = "SetInputMediaType";
                    segment.Writer.SetInputMediaType(segment.Stream, inputType, null);
                    step = "BeginWriting";
                    segment.Writer.BeginWriting();

                    if (last is not null)
                        Log?.Invoke($"Segment writer settled on {(input == VideoFormatGuids.Argb32 ? "ARGB32" : "RGB32")} input, stream declared as {declaredFps}fps");

                    return segment;
                }
                catch (Exception ex)
                {
                    last = ex;
                    failures.Add($"{(input == VideoFormatGuids.Argb32 ? "ARGB32" : "RGB32")}@{declaredFps}: {step} 0x{ex.HResult:X8}");
                    segment.Dispose();
                }
            }

            throw new InvalidOperationException($"no GPU encoder accepted {sourceWidth}x{sourceHeight} -> {width}x{height} @ {fps}fps ({string.Join("; ", failures)})", last);
        }

        // builds the next segment's writer on a worker, so that the switch is free
        private void Prepare(int sourceWidth, int sourceHeight, int width, int height, int fps, int bitrate)
        {
            _next = Task.Run<Segment?>(() =>
            {
                try
                {
                    return CreateSegment(sourceWidth, sourceHeight, width, height, fps, bitrate);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not prepare the next segment: {ex.Message}");
                    return null;
                }
            });
        }

        private Segment? TakePrepared(int sourceWidth, int sourceHeight, int width, int height, int fps, int bitrate)
        {
            Task<Segment?>? task = _next;
            _next = null;

            if (task is null)
                return null;

            Segment? segment = null;
            try { segment = task.Result; } catch { }

            if (segment is not null && !segment.Matches(sourceWidth, sourceHeight, width, height, fps, bitrate))
            {
                segment.Dispose();
                segment = null;
            }

            return segment;
        }

        // ends the segment being written and queues it for finalisation; the capture thread never waits
        private void CloseCurrent(long endTicks = 0)
        {
            Segment? segment = _current;
            _current = null;

            if (segment is null)
                return;

            if (segment.Frames == 0)
            {
                segment.Dispose();
                return;
            }

            segment.EndTicks = endTicks != 0 ? endTicks : Now();

            segment.Finalizing = Task.Run(() =>
            {
                try
                {
                    segment.Writer!.Finalize();
                    segment.Ok = true;
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Segment finalise failed: {ex.Message}");
                }

                try { segment.Writer?.Dispose(); } catch { }
                segment.Writer = null;
            });

            long keepFrom = segment.EndTicks - (_settings().ClipSeconds + SegmentSeconds) * 10_000_000L;
            var expired = new List<Segment>();

            lock (_sync)
            {
                _closed.Add(segment);

                // (while a clip is being written nothing is retired - next close catches up)
                while (Volatile.Read(ref _cutsOutstanding) == 0 && _closed.Count > 1 && _closed[0].EndTicks < keepFrom)
                {
                    expired.Add(_closed[0]);
                    _closed.RemoveAt(0);
                }
            }

            foreach (Segment old in expired)
                Retire(old);
        }

        private static void Retire(Segment segment)
        {
            Task finalizing = segment.Finalizing ?? Task.CompletedTask;
            finalizing.ContinueWith(_ => segment.Dispose());
        }

        private void DropCurrent()
        {
            Segment? segment = _current;
            _current = null;

            if (segment is not null)
                Task.Run(segment.Dispose);

            Task<Segment?>? next = _next;
            _next = null;
            next?.ContinueWith(t => { try { t.Result?.Dispose(); } catch { } });
        }

        private void DropClosed()
        {
            List<Segment> all;
            lock (_sync)
            {
                all = new List<Segment>(_closed);
                _closed.Clear();

                if (Volatile.Read(ref _cutsOutstanding) > 0)
                {
                    _limbo.AddRange(all);
                    return;
                }
            }

            foreach (Segment segment in all)
                Retire(segment);
        }

        private void ReleaseCut()
        {
            List<Segment> free = new();

            lock (_sync)
            {
                if (Interlocked.Decrement(ref _cutsOutstanding) == 0)
                {
                    free.AddRange(_limbo);
                    _limbo.Clear();
                }
            }

            foreach (Segment segment in free)
                Retire(segment);
        }

        // ------------------------------------------------------------------ saving

        // The newest `seconds` of what was recorded, as finished segments in order (each with the
        // moment it started, on the Now() clock). The caller must Release() the result.
        internal sealed class Cut : IDisposable
        {
            public List<Segment> Segments = new();
            public long StartTicks => Segments.Count > 0 ? Segments[0].StartTicks : 0;
            public long EndTicks => Segments.Count > 0 ? Segments[^1].EndTicks : 0;
            public Action? Released;

            public void Dispose() => Released?.Invoke();
        }

        private readonly object _cutLock = new();
        private int _cutsOutstanding;

        internal Cut? TakeCut(int seconds)
        {
            lock (_cutLock)
            {
                if (_running)
                {
                    // have the capture thread end the segment being written, so the clip runs right up to now
                    _cutDone.Reset();
                    _cutRequested = true;
                    _cutDone.Wait(2000);
                }

                List<Segment> picked = new();

                lock (_sync)
                {
                    long total = 0;
                    for (int i = _closed.Count - 1; i >= 0 && total < seconds * 10_000_000L; i--)
                    {
                        picked.Insert(0, _closed[i]);
                        total += _closed[i].EndTicks - _closed[i].StartTicks;
                    }

                    // they must survive until the clip has been written, whatever ages out meanwhile
                    Interlocked.Increment(ref _cutsOutstanding);
                }

                foreach (Segment segment in picked)
                {
                    try { segment.Finalizing?.Wait(5000); } catch { }
                }

                picked.RemoveAll(s => !s.Ok || s.ByteStream == IntPtr.Zero);

                // a clip has one format: keep the newest run of segments that share it. (Segments
                // need not follow on from each other in time - a stretch where the game was not in
                // front is simply not there, and ReplayMuxer joins what is.)
                for (int i = picked.Count - 1; i > 0; i--)
                {
                    if (!picked[i].Matches(picked[i - 1].SourceWidth, picked[i - 1].SourceHeight, picked[i - 1].Width, picked[i - 1].Height, picked[i - 1].Fps, picked[i - 1].Bitrate))
                    {
                        picked.RemoveRange(0, i);
                        break;
                    }
                }

                if (picked.Count == 0)
                {
                    ReleaseCut();
                    return null;
                }

                return new Cut { Segments = picked, Released = ReleaseCut };
            }
        }

        // ------------------------------------------------------------------ self test

        // Pushes generated frames through exactly the pipeline real frames take (same device, pool,
        // segment writers and rotation) without looking at the screen. Used by the test harness,
        // and cheap enough to answer "does the GPU path work on this PC" before relying on it.
        // Runs on the calling thread; the recorder must not be started.
        internal Cut? RunSynthetic(int sourceWidth, int sourceHeight, int width, int height, int fps, int bitrate, int frames, bool realTime)
        {
            if (_running)
                throw new InvalidOperationException("the recorder is running");

            MediaFactory.MFStartup(false);

            try
            {
                EnsureDevice();

                // a gradient, drawn once; per frame only the columns of the moving bar change
                byte[] pixels = new byte[sourceWidth * sourceHeight * 4];
                for (int y = 0; y < sourceHeight; y++)
                {
                    for (int x = 0; x < sourceWidth; x++)
                        Shade(pixels, sourceWidth, sourceHeight, x, y, false);
                }

                int previousBar = -1000;
                long start = Now();
                long frameTicks = 10_000_000L / fps;

                for (int n = 0; n < frames; n++)
                {
                    long now = realTime ? Now() : start + n * frameTicks;

                    if (_current is null)
                    {
                        _current = CreateSegment(sourceWidth, sourceHeight, width, height, fps, bitrate);
                        _current.StartTicks = now;
                        Prepare(sourceWidth, sourceHeight, width, height, fps, bitrate);
                    }
                    else if (now - _current.StartTicks >= SegmentSeconds * 10_000_000L)
                    {
                        CloseCurrent(now);
                        _current = TakePrepared(sourceWidth, sourceHeight, width, height, fps, bitrate) ?? CreateSegment(sourceWidth, sourceHeight, width, height, fps, bitrate);
                        _current.StartTicks = now;
                        Prepare(sourceWidth, sourceHeight, width, height, fps, bitrate);
                    }

                    PoolItem? item = null;
                    for (int wait = 0; wait < 200 && (item = Rent(sourceWidth, sourceHeight)) is null; wait++)
                        Thread.Sleep(5);

                    if (item is null)
                        throw new InvalidOperationException("the encoder never gave a texture back");

                    // a bar that sweeps across the gradient - easy to find again in a grabbed frame
                    int bar = (int)((long)n * sourceWidth / Math.Max(1, frames));
                    for (int x = Math.Max(0, Math.Min(bar, previousBar) - 12); x < Math.Min(sourceWidth, Math.Max(bar, previousBar) + 12); x++)
                    {
                        bool inBar = Math.Abs(x - bar) < 12;
                        for (int y = 0; y < sourceHeight; y++)
                            Shade(pixels, sourceWidth, sourceHeight, x, y, inBar);
                    }
                    previousBar = bar;

                    _context!.UpdateSubresource(pixels, item.Texture, 0, sourceWidth * 4);

                    using IMFSample sample = MediaFactory.MFCreateSample();
                    sample.AddBuffer(item.Buffer);
                    sample.SampleTime = now - _current.StartTicks;
                    sample.SampleDuration = frameTicks;

                    _current.Writer!.WriteSample(_current.Stream, sample);
                    _current.Frames++;

                    if (realTime)
                    {
                        long wait = (start + (n + 1) * frameTicks - Now()) / 10_000;
                        if (wait > 1)
                            Thread.Sleep((int)wait);
                    }
                }

                CloseCurrent(realTime ? Now() : start + frames * frameTicks);
                return TakeCut(int.MaxValue / 20_000_000);
            }
            finally
            {
                try { MediaFactory.MFShutdown(); } catch { }
            }
        }

        private static void Shade(byte[] pixels, int width, int height, int x, int y, bool bar)
        {
            int i = (y * width + x) * 4;
            pixels[i] = bar ? (byte)40 : (byte)(y * 255 / height);
            pixels[i + 1] = bar ? (byte)40 : (byte)(x * 255 / width);
            pixels[i + 2] = bar ? (byte)230 : (byte)60;
            pixels[i + 3] = 255;
        }

        // ------------------------------------------------------------------ device / pool

        private void EnsureDevice()
        {
            if (_device is not null)
                return;

            D3D11.D3D11CreateDevice((IDXGIAdapter)null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, FeatureLevels, out _device, out _context).CheckError();

            // Media Foundation drives this device from its own threads
            using (ID3D11Multithread multithread = _device!.QueryInterface<ID3D11Multithread>())
                multithread.SetMultithreadProtected(true);

            _deviceManager = MfInterop.CreateDeviceManager(_device.NativePointer);
        }

        private bool EnsureDuplication(int centerX, int centerY)
        {
            bool onOutput = centerX >= _outputLeft && centerX < _outputRight && centerY >= _outputTop && centerY < _outputBottom;
            if (_duplication is not null && onOutput)
                return true;

            ReleaseDuplication();
            EnsureDevice();

            using IDXGIDevice dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();

            try
            {
                for (int i = 0; ; i++)
                {
                    Result result = adapter.EnumOutputs(i, out IDXGIOutput output);
                    if (result.Failure || output is null)
                        break;

                    try
                    {
                        var bounds = output.Description.DesktopCoordinates;
                        if (centerX < bounds.Left || centerX >= bounds.Right || centerY < bounds.Top || centerY >= bounds.Bottom)
                            continue;

                        using IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                        _duplication = output1.DuplicateOutput(_device);
                        _outputLeft = bounds.Left;
                        _outputTop = bounds.Top;
                        _outputRight = bounds.Right;
                        _outputBottom = bounds.Bottom;

                        Log?.Invoke($"Desktop duplication active on the monitor at {bounds.Left},{bounds.Top} - {bounds.Right},{bounds.Bottom}");
                        return true;
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

            Log?.Invoke("No duplicable output contains the game window (it may be on a monitor driven by another GPU)");
            return false;
        }

        private void ReleaseDuplication()
        {
            try { _duplication?.Dispose(); } catch { }
            _duplication = null;
            _outputLeft = _outputTop = _outputRight = _outputBottom = 0;
        }

        // a texture the encoder is not holding any more; null when every one is still in flight
        private PoolItem? Rent(int width, int height)
        {
            if (width != _poolWidth || height != _poolHeight)
            {
                // textures still inside the encoder stay alive through its own references
                foreach (PoolItem old in _pool)
                    old.Dispose();
                _pool.Clear();
                _poolWidth = width;
                _poolHeight = height;
            }

            foreach (PoolItem item in _pool)
            {
                if (MfInterop.RefCount(item.Buffer.NativePointer) == 1)
                    return item;
            }

            if (_pool.Count >= MaxPoolTextures)
                return null;

            ID3D11Texture2D texture = _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            IMFMediaBuffer buffer;
            try
            {
                buffer = MfInterop.CreateSurfaceBuffer(texture.NativePointer);
                buffer.CurrentLength = width * height * 4;
            }
            catch
            {
                texture.Dispose();
                throw;
            }

            var created = new PoolItem { Texture = texture, Buffer = buffer };
            _pool.Add(created);
            return created;
        }

        private void Teardown()
        {
            CloseCurrent();

            Task<Segment?>? next = _next;
            _next = null;
            try { next?.Result?.Dispose(); } catch { }

            // a clip being written right now still needs its segments
            for (int i = 0; i < 100 && Volatile.Read(ref _cutsOutstanding) > 0; i++)
                Thread.Sleep(100);

            List<Segment> all;
            lock (_sync)
            {
                all = new List<Segment>(_closed);
                _closed.Clear();
            }

            foreach (Segment segment in all)
            {
                try { segment.Finalizing?.Wait(3000); } catch { }
                segment.Dispose();
            }

            foreach (PoolItem item in _pool)
                item.Dispose();
            _pool.Clear();
            _poolWidth = _poolHeight = 0;

            ReleaseDuplication();

            if (_deviceManager != IntPtr.Zero)
            {
                Marshal.Release(_deviceManager);
                _deviceManager = IntPtr.Zero;
            }

            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }

        // ------------------------------------------------------------------ misc

        private IntPtr ResolveWindow(string processName)
        {
            DateTime now = DateTime.UtcNow;

            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd) && (now - _hwndCheckedUtc).TotalSeconds < 5)
                return _hwnd;

            if (_hwnd == IntPtr.Zero && (now - _hwndCheckedUtc).TotalSeconds < 1)
                return IntPtr.Zero;

            _hwndCheckedUtc = now;
            _hwnd = IntPtr.Zero;

            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        _hwnd = process.MainWindowHandle;
                        break;
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }

            return _hwnd;
        }

        private void ReportStats(int fps)
        {
            double seconds = (DateTime.UtcNow - _statSinceUtc).TotalSeconds;
            if (seconds < 60)
                return;

            long frames = Interlocked.Exchange(ref _statFrames, 0);
            long dropped = Interlocked.Exchange(ref _statDropped, 0);
            _statSinceUtc = DateTime.UtcNow;

            int segments;
            long bytes = 0;
            lock (_sync)
            {
                segments = _closed.Count;
                foreach (Segment segment in _closed)
                {
                    try { if (segment.ByteStream != IntPtr.Zero && segment.Ok) bytes += MfInterop.ByteStreamLength(segment.ByteStream); } catch { }
                }
            }

            Log?.Invoke($"{frames / seconds:0.0} fps encoded / {fps} target, {dropped} dropped, {segments} segment(s) buffered = {bytes / 1048576.0:0.0} MB, {_pool.Count} pooled texture(s)");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
    }
}
