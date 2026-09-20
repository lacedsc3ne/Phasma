using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace PhasmaStrap.Utility
{
    public sealed class InstantReplayRecorder : IDisposable
    {
        private const string LOG_IDENT = "InstantReplayRecorder";

        public const int MaxBufferMegabytes = 1200;
        private const long MaxBufferBytes = MaxBufferMegabytes * 1024L * 1024;

        private static readonly int EncodeWorkers = Math.Clamp(Environment.ProcessorCount - 3, 2, 8);

        private const int QueueCapacity = 16;

        private readonly object _sync = new();
        private readonly List<BufferedFrame> _frames = new();
        private long _bufferBytes;

        private Thread? _captureThread;
        private Thread[] _workers = Array.Empty<Thread>();
        private BlockingCollection<RawFrame>? _queue;
        private volatile bool _running;
        private volatile bool _mfStarted;

        private DesktopDuplicationGrabber? _duplication;
        private DateTime _duplicationRetryUtc = DateTime.MinValue;

        private IntPtr _hwnd;
        private DateTime _hwndCheckedUtc = DateTime.MinValue;

        private int _statCaptured, _statDropped, _statDuplication;
        private DateTime _statSinceUtc;

        private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        private sealed class RawFrame
        {
            public Bitmap Bitmap = null!;
            public DateTime CapturedUtc;
        }

        private sealed class BufferedFrame
        {
            public byte[] Jpeg = Array.Empty<byte>();
            public int Width;
            public int Height;
            public DateTime CapturedUtc;
        }

        public static string ClipsDir => Path.Combine(Paths.Base, "Replays");

        public bool IsRunning => _running;

        public bool OnGpu
        {
            get
            {
                lock (_modeLock)
                    return _gpu is not null;
            }
        }

        public static readonly int[] FpsOptions = { 15, 24, 30, 60, 90, 120, 144, 165, 240 };

        public const int MaxFps = 240;

        public static readonly int[] MaxHeightOptions = { 0, 1440, 1080, 720, 480 };

        public const int MaxClipSeconds = 300;

        private static int TargetFps => Math.Clamp(App.Settings.Prop.InstantReplayFps, 5, MaxFps);

        private static int MaxHeight => Math.Max(0, App.Settings.Prop.InstantReplayMaxHeight);

        private static int Quality => Math.Clamp(App.Settings.Prop.InstantReplayQuality, 0, 2);

        private static long JpegQuality => Quality switch { 0 => 72, 2 => 90, _ => 82 };

        private static double BitsPerPixel(int quality) => quality switch { 0 => 0.05, 2 => 0.13, _ => 0.085 };

        public static int BitrateFor(int width, int height, int fps, int quality)
        {
            double effectiveFps = fps <= 60 ? fps : 60 * Math.Sqrt(fps / 60.0);
            return (int)Math.Clamp((double)width * height * effectiveFps * BitsPerPixel(quality), 1_000_000, 40_000_000);
        }

        public static long EstimateBufferBytes(int width, int height, int fps, int seconds, int quality)
        {
            double bytesPerPixel = quality switch { 0 => 0.035, 2 => 0.065, _ => 0.048 };
            return (long)(width * (double)height * bytesPerPixel * fps * seconds);
        }

        private readonly object _modeLock = new();
        private GpuReplayRecorder? _gpu;
        private ReplayAudio? _audio;

        private static GpuReplayRecorder.Settings GpuSettings() => new()
        {
            Fps = TargetFps,
            MaxHeight = MaxHeight,
            ClipSeconds = Math.Clamp(App.Settings.Prop.InstantReplayClipSeconds, 5, MaxClipSeconds),
            ProcessName = App.RobloxPlayerAppName,
            BitrateFor = (width, height, fps) => BitrateFor(width, height, fps, Quality),
        };

        private void StartGpu()
        {
            GpuReplayRecorder.Log ??= message => App.Logger.WriteLine("GpuReplayRecorder", message);
            ReplayMuxer.Log ??= message => App.Logger.WriteLine("ReplayMuxer", message);
            ReplayAudio.Log ??= message => App.Logger.WriteLine("ReplayAudio", message);

            var gpu = new GpuReplayRecorder(GpuSettings);

            gpu.GaveUp += () => Task.Run(() =>
            {
                lock (_modeLock)
                {
                    if (!ReferenceEquals(_gpu, gpu))
                        return;

                    App.Logger.WriteLine(LOG_IDENT, $"GPU recording is not available here ({gpu.UnavailableReason.Trim()}) - switching to the CPU recorder");
                    StopGpu();

                    if (_running)
                        StartCpu();
                }
            });

            _gpu = gpu;

            if (App.Settings.Prop.InstantReplayAudio)
            {
                _audio = new ReplayAudio(
                    () => Math.Clamp(App.Settings.Prop.InstantReplayClipSeconds, 5, MaxClipSeconds) + GpuReplayRecorder.SegmentSeconds + 2,
                    App.RobloxPlayerAppName,
                    App.Settings.Prop.InstantReplayMicrophone,
                    App.Settings.Prop.InstantReplayMicrophoneDevice);
                _audio.Start();
            }

            gpu.Start();

            App.Logger.WriteLine(LOG_IDENT, $"Started on the GPU path ({TargetFps} fps target, {(MaxHeight == 0 ? "native resolution" : $"{MaxHeight}p")}, quality {Quality}, sound {(_audio is null ? "off" : App.Settings.Prop.InstantReplayMicrophone ? "game + microphone" : "game")})");
        }

        private void StopGpu()
        {
            GpuReplayRecorder? gpu = _gpu;
            ReplayAudio? audio = _audio;
            _gpu = null;
            _audio = null;

            try { gpu?.Dispose(); } catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Stopping the GPU recorder: {ex.Message}"); }
            try { audio?.Dispose(); } catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Stopping sound capture: {ex.Message}"); }
        }

        private string? SaveGpuClip(GpuReplayRecorder gpu, ReplayAudio? audio)
        {
            var timer = Stopwatch.StartNew();
            int seconds = Math.Clamp(App.Settings.Prop.InstantReplayClipSeconds, 5, MaxClipSeconds);

            using GpuReplayRecorder.Cut? cut = gpu.TakeCut(seconds);
            if (cut is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "SaveClip called with nothing buffered (the game has to be the window in front for it to record)");
                return null;
            }

            Directory.CreateDirectory(ClipsDir);
            string path = Path.Combine(ClipsDir, $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            try
            {
                ReplayMuxer.Mux(cut, path, audio is null ? null : audio.Read);
            }
            catch (Exception ex) when (audio is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Writing the clip with sound failed ({ex.Message.Trim()}) - writing it without");
                ReplayMuxer.Mux(cut, path, null);
            }

            double length = cut.Segments.Sum(s => s.EndTicks - s.StartTicks) / 1e7;
            App.Logger.WriteLine(LOG_IDENT, $"Saved {length:0.0}s ({cut.Segments[0].Width}x{cut.Segments[0].Height}, {cut.Segments.Sum(s => s.Frames)} frames, {cut.Segments.Count} segment(s), sound {(audio is null ? "off" : "on")}) in {timer.ElapsedMilliseconds}ms to {path}");
            return path;
        }

        public void Start()
        {
            lock (_modeLock)
            {
                if (_running)
                    return;

                _running = true;

                if (App.Settings.Prop.InstantReplayGpuEncoding)
                {
                    try
                    {
                        StartGpu();
                        return;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not start the GPU path ({ex.Message.Trim()}) - using the CPU recorder");
                        StopGpu();
                    }
                }

                StartCpu();
            }
        }

        private void StartCpu()
        {
            _statSinceUtc = DateTime.UtcNow;
            _statCaptured = _statDropped = _statDuplication = 0;

            DesktopDuplicationGrabber.Log ??= message => App.Logger.WriteLine("DesktopDuplicationGrabber", message);

            _queue = new BlockingCollection<RawFrame>(boundedCapacity: QueueCapacity);

            _workers = new Thread[EncodeWorkers];
            for (int i = 0; i < EncodeWorkers; i++)
            {
                _workers[i] = new Thread(EncodeLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"InstantReplayEncode{i}" };
                _workers[i].Start(_queue);
            }

            _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "InstantReplayCapture" };
            _captureThread.Start(_queue);

            App.Logger.WriteLine(LOG_IDENT, $"Started ({EncodeWorkers} compression workers, {TargetFps} fps target, {(MaxHeight == 0 ? "native resolution" : $"{MaxHeight}p")}, quality {Quality}, up to {App.Settings.Prop.InstantReplayClipSeconds}s buffered)");
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;

            lock (_modeLock)
                StopGpu();

            BlockingCollection<RawFrame>? queue = _queue;
            _queue = null;

            try { _captureThread?.Join(1500); } catch { }
            _captureThread = null;

            queue?.CompleteAdding();
            foreach (Thread worker in _workers)
            {
                try { worker.Join(1500); } catch { }
            }
            _workers = Array.Empty<Thread>();

            if (queue is not null)
            {
                while (queue.TryTake(out RawFrame? leftover))
                    leftover.Bitmap.Dispose();
                queue.Dispose();
            }

            _duplication?.Dispose();
            _duplication = null;

            lock (_sync)
            {
                _frames.Clear();
                _bufferBytes = 0;
            }

            App.Logger.WriteLine(LOG_IDENT, "Stopped");
        }

        private void CaptureLoop(object? state)
        {
            var queue = (BlockingCollection<RawFrame>)state!;

            timeBeginPeriod(1);

            try
            {
                var clock = Stopwatch.StartNew();
                double nextMs = 0;

                while (_running)
                {
                    double intervalMs = 1000.0 / TargetFps;

                    try
                    {
                        double slackMs = Math.Max(0, nextMs - clock.Elapsed.TotalMilliseconds);
                        Bitmap? bitmap = CaptureOnce((int)Math.Ceiling(slackMs + intervalMs));

                        if (bitmap is not null)
                        {
                            var frame = new RawFrame { Bitmap = bitmap, CapturedUtc = DateTime.UtcNow };

                            if (queue.IsAddingCompleted || !queue.TryAdd(frame))
                            {
                                bitmap.Dispose();
                                _statDropped++;
                            }
                            else
                            {
                                _statCaptured++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Capture tick failed: {ex.Message}");
                    }

                    ReportStats();

                    nextMs += intervalMs;
                    double waitMs = nextMs - clock.Elapsed.TotalMilliseconds;

                    if (waitMs < -intervalMs * 2)
                        nextMs = clock.Elapsed.TotalMilliseconds;
                    else if (waitMs > 2)
                        Thread.Sleep((int)(waitMs - 1.5));
                }
            }
            finally
            {
                timeEndPeriod(1);
            }
        }

        private Bitmap? CaptureOnce(int frameWaitMs)
        {
            IntPtr hwnd = ResolveWindow();
            if (hwnd == IntPtr.Zero || IsIconic(hwnd))
                return null;

            if (!GetClientRect(hwnd, out RECT client))
                return null;

            int width = (client.Right - client.Left) & ~1;
            int height = (client.Bottom - client.Top) & ~1;
            if (width < 64 || height < 64)
                return null;

            if (GetForegroundWindow() == hwnd && DateTime.UtcNow >= _duplicationRetryUtc)
            {
                var origin = new POINT();
                ClientToScreen(hwnd, ref origin);

                _duplication ??= new DesktopDuplicationGrabber();

                switch (_duplication.TryGrab(origin.X, origin.Y, width, height, Math.Clamp(frameWaitMs, 0, 100), out Bitmap? grabbed))
                {
                    case DesktopDuplicationGrabber.GrabResult.Frame:
                        _statDuplication++;
                        return grabbed;

                    case DesktopDuplicationGrabber.GrabResult.NoNewFrame:

                        return null;

                    default:
                        _duplicationRetryUtc = DateTime.UtcNow.AddSeconds(10);
                        break;
                }
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);

            try
            {
                using Graphics g = Graphics.FromImage(bitmap);
                IntPtr hdc = g.GetHdc();
                try
                {
                    PrintWindow(hwnd, hdc, 3);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private IntPtr ResolveWindow()
        {
            DateTime now = DateTime.UtcNow;

            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd) && (now - _hwndCheckedUtc).TotalSeconds < 5)
                return _hwnd;

            if (_hwnd == IntPtr.Zero && (now - _hwndCheckedUtc).TotalSeconds < 1)
                return IntPtr.Zero;

            _hwndCheckedUtc = now;
            _hwnd = IntPtr.Zero;

            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);
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

        private void ReportStats()
        {
            double seconds = (DateTime.UtcNow - _statSinceUtc).TotalSeconds;
            if (seconds < 60)
                return;

            int count;
            long bytes;
            lock (_sync)
            {
                count = _frames.Count;
                bytes = _bufferBytes;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Capturing {_statCaptured / seconds:0.0} fps (target {TargetFps}, {_statDuplication * 100 / Math.Max(1, _statCaptured + _statDropped)}% via duplication, {_statDropped} dropped) - buffer {count} frames, {bytes / 1048576.0:0.0} MB");

            _statSinceUtc = DateTime.UtcNow;
            _statCaptured = _statDropped = _statDuplication = 0;
        }

        private void EncodeLoop(object? state)
        {
            var queue = (BlockingCollection<RawFrame>)state!;

            try
            {
                foreach (RawFrame raw in queue.GetConsumingEnumerable())
                {
                    try
                    {
                        using Bitmap source = raw.Bitmap;

                        int maxHeight = MaxHeight;
                        Bitmap? scaled = null;

                        if (maxHeight > 0 && source.Height != maxHeight)
                        {
                            int h = maxHeight & ~1;
                            int w = Math.Max(2, (int)Math.Round(source.Width * (double)h / source.Height)) & ~1;
                            scaled = Resize(source, w, h);
                        }

                        try
                        {
                            Bitmap final = scaled ?? source;

                            using var stream = new MemoryStream(256 * 1024);
                            using (var parameters = new EncoderParameters(1))
                            {
                                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                                final.Save(stream, JpegCodec, parameters);
                            }

                            Add(new BufferedFrame
                            {
                                Jpeg = stream.ToArray(),
                                Width = final.Width,
                                Height = final.Height,
                                CapturedUtc = raw.CapturedUtc,
                            });
                        }
                        finally
                        {
                            scaled?.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Frame compression failed: {ex.Message}");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Add(BufferedFrame frame)
        {
            lock (_sync)
            {
                int index = _frames.Count;
                while (index > 0 && _frames[index - 1].CapturedUtc > frame.CapturedUtc)
                    index--;

                _frames.Insert(index, frame);
                _bufferBytes += frame.Jpeg.Length;

                DateTime cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(1, App.Settings.Prop.InstantReplayClipSeconds));

                int remove = 0;
                long freed = 0;
                while (remove < _frames.Count - 1 && (_frames[remove].CapturedUtc < cutoff || _bufferBytes - freed > MaxBufferBytes))
                {
                    freed += _frames[remove].Jpeg.Length;
                    remove++;
                }

                if (remove > 0)
                {
                    _frames.RemoveRange(0, remove);
                    _bufferBytes -= freed;
                }
            }
        }

        private static Bitmap Resize(Bitmap source, int width, int height)
        {
            var resized = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            return resized;
        }

        public string? SaveClip()
        {
            GpuReplayRecorder? gpu;
            ReplayAudio? audio;
            lock (_modeLock)
            {
                gpu = _gpu;
                audio = _audio;
            }

            if (gpu is not null)
            {
                try
                {
                    return SaveGpuClip(gpu, audio);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"SaveClip failed: {ex.Message.Trim()}");
                    return null;
                }
            }

            List<BufferedFrame> frames;

            lock (_sync)
                frames = new List<BufferedFrame>(_frames);

            if (frames.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "SaveClip called with nothing buffered");
                return null;
            }

            int width = frames[^1].Width;
            int height = frames[^1].Height;
            int first = frames.Count - 1;
            while (first > 0 && frames[first - 1].Width == width && frames[first - 1].Height == height)
                first--;
            if (first > 0)
                frames.RemoveRange(0, first);

            int fps = TargetFps;
            double seconds = Math.Max(0.001, (frames[^1].CapturedUtc - frames[0].CapturedUtc).TotalSeconds);
            int bitrate = BitrateFor(width, height, fps, Quality);

            Directory.CreateDirectory(ClipsDir);
            string path = Path.Combine(ClipsDir, $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            App.Logger.WriteLine(LOG_IDENT, $"Encoding {frames.Count} frame(s) over {seconds:0.0}s ({width}x{height}, {frames.Count / seconds:0.0} fps captured / {fps} target, {bitrate / 1000} kbps) to {path}");

            bool startedHere = false;
            IMFSinkWriter? writer = null;
            var timer = Stopwatch.StartNew();

            try
            {
                if (!_mfStarted)
                {
                    MediaFactory.MFStartup(false);
                    _mfStarted = true;
                    startedHere = true;
                }

                writer = CreateSinkWriter(path, width, height, fps, bitrate, out int streamIndex);

                long nominalDuration = 10_000_000L / Math.Max(1, fps);
                byte[] pixels = new byte[width * height * 4];
                DateTime origin = frames[0].CapturedUtc;

                for (int i = 0; i < frames.Count; i++)
                {
                    Decode(frames[i], pixels);

                    long time = (frames[i].CapturedUtc - origin).Ticks;
                    long duration = i + 1 < frames.Count
                        ? Math.Max(1, (frames[i + 1].CapturedUtc - frames[i].CapturedUtc).Ticks)
                        : nominalDuration;

                    using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(pixels.Length);
                    buffer.Lock(out IntPtr ptr, out int _, out int _);
                    Marshal.Copy(pixels, 0, ptr, pixels.Length);
                    buffer.Unlock();
                    buffer.CurrentLength = pixels.Length;

                    using IMFSample sample = MediaFactory.MFCreateSample();
                    sample.AddBuffer(buffer);
                    sample.SampleTime = time;
                    sample.SampleDuration = duration;

                    writer.WriteSample(streamIndex, sample);
                }

                writer.Finalize();

                App.Logger.WriteLine(LOG_IDENT, $"Saved {frames.Count} frame(s) ({width}x{height}, {seconds:0.0}s) in {timer.ElapsedMilliseconds}ms to {path}");
                return path;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"SaveClip failed: {ex.Message}");
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                return null;
            }
            finally
            {
                writer?.Dispose();

                if (startedHere)
                {
                    try { MediaFactory.MFShutdown(); } catch { }
                    _mfStarted = false;
                }
            }
        }

        private static void Decode(BufferedFrame frame, byte[] destination)
        {
            using var stream = new MemoryStream(frame.Jpeg, writable: false);
            using var bitmap = new Bitmap(stream);

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                int rowBytes = frame.Width * 4;

                if (data.Stride == rowBytes)
                {
                    Marshal.Copy(data.Scan0, destination, 0, rowBytes * frame.Height);
                }
                else
                {
                    for (int y = 0; y < frame.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, destination, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static ulong PackAttribute(uint high, uint low) => ((ulong)high << 32) | low;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt64Fn(IntPtr self, ref Guid key, ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt32Fn(IntPtr self, ref Guid key, uint value);

        private static void SetUInt64(IMFAttributes attributes, Guid key, ulong value)
        {
            IntPtr self = attributes.NativePointer;
            IntPtr vtable = Marshal.ReadIntPtr(self);
            IntPtr fn = Marshal.ReadIntPtr(vtable, 22 * IntPtr.Size);
            int hr = Marshal.GetDelegateForFunctionPointer<SetUInt64Fn>(fn)(self, ref key, value);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private static void SetUInt32(IMFAttributes attributes, Guid key, uint value)
        {
            IntPtr self = attributes.NativePointer;
            IntPtr vtable = Marshal.ReadIntPtr(self);
            IntPtr fn = Marshal.ReadIntPtr(vtable, 21 * IntPtr.Size);
            int hr = Marshal.GetDelegateForFunctionPointer<SetUInt32Fn>(fn)(self, ref key, value);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream, IntPtr pAttributes, out IntPtr ppSinkWriter);

        private static IMFSinkWriter CreateSinkWriter(string path, int width, int height, int fps, int bitrate, out int streamIndex)
        {
            var attempts = new List<(bool Hardware, int DeclaredFps)> { (true, fps), (false, fps) };
            if (fps > 60)
            {
                attempts.Add((true, 60));
                attempts.Add((false, 60));
            }

            Exception? last = null;

            foreach ((bool hardware, int declaredFps) in attempts)
            {
                IMFSinkWriter? writer = null;

                try
                {
                    writer = CreateSinkWriter(path, width, height, declaredFps, bitrate, hardware, out streamIndex);
                    writer.BeginWriting();

                    if (last is not null)
                        App.Logger.WriteLine(LOG_IDENT, $"Encoding with the {(hardware ? "hardware" : "software")} encoder, stream declared as {declaredFps}fps");

                    return writer;
                }
                catch (Exception ex)
                {
                    last = ex;
                    App.Logger.WriteLine(LOG_IDENT, $"{(hardware ? "Hardware" : "Software")} encoder refused {width}x{height}@{declaredFps}: {ex.Message}");
                    writer?.Dispose();
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                }
            }

            streamIndex = 0;
            throw last ?? new InvalidOperationException("No H.264 encoder available");
        }

        private static IMFSinkWriter CreateSinkWriter(string path, int width, int height, int fps, int bitrate, bool hardware, out int streamIndex)
        {
            using IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            SetUInt64(outputType, MediaTypeAttributeKeys.FrameSize, PackAttribute((uint)width, (uint)height));
            SetUInt64(outputType, MediaTypeAttributeKeys.FrameRate, PackAttribute((uint)fps, 1));
            SetUInt64(outputType, MediaTypeAttributeKeys.PixelAspectRatio, PackAttribute(1, 1));

            using IMFMediaType inputType = MediaFactory.MFCreateMediaType();
            inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            SetUInt64(inputType, MediaTypeAttributeKeys.FrameSize, PackAttribute((uint)width, (uint)height));
            SetUInt64(inputType, MediaTypeAttributeKeys.FrameRate, PackAttribute((uint)fps, 1));
            SetUInt64(inputType, MediaTypeAttributeKeys.PixelAspectRatio, PackAttribute(1, 1));

            inputType.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(width * 4));

            IMFAttributes? attributes = null;
            IMFSinkWriter? writer = null;

            try
            {
                if (hardware)
                {
                    attributes = MediaFactory.MFCreateAttributes(1);
                    SetUInt32(attributes, MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                }

                int hr = MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes?.NativePointer ?? IntPtr.Zero, out IntPtr ptr);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                writer = new IMFSinkWriter(ptr);
                streamIndex = writer.AddStream(outputType);
                writer.SetInputMediaType(streamIndex, inputType, null);
                return writer;
            }
            catch
            {
                writer?.Dispose();
                throw;
            }
            finally
            {
                attributes?.Dispose();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
