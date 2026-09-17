using System.Drawing;
using System.Drawing.Imaging;
using Vortice.MediaFoundation;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PhasmaStrap.Utility
{
    // Always-on rolling video buffer of the Roblox window, saved to a real MP4 clip on demand
    // (the "Instant Replay" hotkey) - not a start/stop recorder, the buffer runs continuously
    // once Start() is called (mirrors how Medal/ShadowPlay-style instant replay works: press the
    // hotkey AFTER something happens, not before).
    //
    // Capture reuses the same plain GDI approach as ScreenshotCapture (Graphics.CopyFromScreen
    // over the window's GetWindowRect), not OverlayCompositor's DXGI desktop-duplication - same
    // reasoning as screenshots: this needs to work independent of whether overlays are enabled.
    // Frames are downscaled before buffering (capped resolution) since the buffer is held raw,
    // uncompressed, in memory for the whole clip length - buffering at full 1080p+ for 30s+ would
    // be multiple GB of RAM.
    //
    // Encoding is Windows Media Foundation's standard IMFSinkWriter pattern: feed it 32-bit RGB
    // frames and an H.264/MP4 output media type, and MF's own transform-resolution pipeline
    // inserts whatever color-space conversion and encoder MFTs are needed automatically -
    // including a hardware encoder MFT if the system has one registered (which is the normal case
    // on any GPU with H.264 encode support), without this code needing to enumerate or select a
    // specific hardware transform itself.
    public sealed class InstantReplayRecorder : IDisposable
    {
        private const string LOG_IDENT = "InstantReplayRecorder";

        private readonly object _sync = new();
        private readonly List<BufferedFrame> _frames = new();

        private System.Threading.Timer? _captureTimer;
        private volatile bool _running;
        private volatile bool _mfStarted;

        private sealed class BufferedFrame
        {
            public byte[] Bgra32 = Array.Empty<byte>();
            public int Width;
            public int Height;
            public DateTime CapturedUtc;
        }

        public static string ClipsDir => Path.Combine(Paths.Base, "Replays");

        public bool IsRunning => _running;

        public void Start()
        {
            if (_running)
                return;

            _running = true;

            int fps = CaptureFps;
            _captureTimer = new System.Threading.Timer(_ => CaptureFrame(), null, 0, 1000 / Math.Max(1, fps));

            App.Logger.WriteLine(LOG_IDENT, $"Started ({fps} fps, up to {App.Settings.Prop.InstantReplayClipSeconds}s buffered)");
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;
            _captureTimer?.Dispose();
            _captureTimer = null;

            lock (_sync)
                _frames.Clear();

            App.Logger.WriteLine(LOG_IDENT, "Stopped");
        }

        private static int CaptureFps => App.Settings.Prop.InstantReplayQuality switch
        {
            0 => 8,   // Low
            2 => 20,  // High
            _ => 12,  // Medium
        };

        private static int MaxCaptureWidth => App.Settings.Prop.InstantReplayQuality switch
        {
            0 => 854,
            2 => 1600,
            _ => 1280,
        };

        private void CaptureFrame()
        {
            if (!_running)
                return;

            try
            {
                HWND hwnd = FindRobloxWindow();
                if (hwnd.IsNull || !PInvoke.GetWindowRect(hwnd, out RECT rect))
                    return;

                int srcWidth = rect.right - rect.left;
                int srcHeight = rect.bottom - rect.top;
                if (srcWidth <= 0 || srcHeight <= 0)
                    return;

                int maxWidth = MaxCaptureWidth;
                double scale = srcWidth > maxWidth ? (double)maxWidth / srcWidth : 1.0;
                int width = Math.Max(2, (int)(srcWidth * scale)) & ~1;
                int height = Math.Max(2, (int)(srcHeight * scale)) & ~1;

                using var full = new Bitmap(srcWidth, srcHeight, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(full))
                    g.CopyFromScreen(rect.left, rect.top, 0, 0, new Size(srcWidth, srcHeight));

                using Bitmap scaled = scale < 1.0
                    ? ResizeBitmap(full, width, height)
                    : (Bitmap)full.Clone();

                byte[] bytes = BitmapToBgra32(scaled, out int actualWidth, out int actualHeight);

                var frame = new BufferedFrame
                {
                    Bgra32 = bytes,
                    Width = actualWidth,
                    Height = actualHeight,
                    CapturedUtc = DateTime.UtcNow,
                };

                lock (_sync)
                {
                    _frames.Add(frame);

                    DateTime cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(1, App.Settings.Prop.InstantReplayClipSeconds));
                    _frames.RemoveAll(f => f.CapturedUtc < cutoff);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Capture tick failed: {ex.Message}");
            }
        }

        private static Bitmap ResizeBitmap(Bitmap source, int width, int height)
        {
            var resized = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(source, 0, 0, width, height);
            return resized;
        }

        private static byte[] BitmapToBgra32(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.Width;
            height = bitmap.Height;

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

            try
            {
                int stride = data.Stride;
                byte[] buffer = new byte[stride * height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                return buffer;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        // Encodes whatever's currently buffered into a real MP4 file via Media Foundation's
        // SinkWriter and returns the saved path, or null if there was nothing to save / encoding
        // failed. This is genuinely the least-verifiable code in this whole feature batch - it
        // follows the standard documented IMFSinkWriter pattern precisely, but there is no way to
        // visually confirm the resulting file plays correctly from this environment (no GPU
        // capture or video playback available here). If clips come out wrong, check here first.
        public string? SaveClip()
        {
            List<BufferedFrame> frames;

            lock (_sync)
                frames = new List<BufferedFrame>(_frames);

            if (frames.Count == 0)
            {
                App.Logger.WriteLine(LOG_IDENT, "SaveClip called with nothing buffered");
                return null;
            }

            int width = frames[0].Width;
            int height = frames[0].Height;
            int fps = CaptureFps;

            Directory.CreateDirectory(ClipsDir);
            string path = Path.Combine(ClipsDir, $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            bool startedHere = false;

            try
            {
                if (!_mfStarted)
                {
                    MediaFactory.MFStartup(false);
                    _mfStarted = true;
                    startedHere = true;
                }

                IMFSinkWriter writer = CreateSinkWriter(path, width, height, fps, out int streamIndex);

                writer.BeginWriting();

                long frameDurationTicks = 10_000_000L / Math.Max(1, fps);
                long timestamp = 0;

                foreach (BufferedFrame frame in frames)
                {
                    IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(frame.Bgra32.Length);
                    buffer.CurrentLength = frame.Bgra32.Length;

                    buffer.Lock(out IntPtr ptr, out int _, out int _);
                    System.Runtime.InteropServices.Marshal.Copy(frame.Bgra32, 0, ptr, frame.Bgra32.Length);
                    buffer.Unlock();

                    IMFSample sample = MediaFactory.MFCreateSample();
                    sample.AddBuffer(buffer);
                    sample.SampleTime = timestamp;
                    sample.SampleDuration = frameDurationTicks;

                    writer.WriteSample(streamIndex, sample);

                    timestamp += frameDurationTicks;
                }

                writer.Finalize();

                App.Logger.WriteLine(LOG_IDENT, $"Saved {frames.Count} frame(s) ({width}x{height} @ {fps}fps) to {path}");
                return path;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"SaveClip failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (startedHere)
                {
                    try { MediaFactory.MFShutdown(); } catch { }
                    _mfStarted = false;
                }
            }
        }

        // FrameSize/FrameRate/PixelAspectRatio are packed as a single UINT64 (high 32 = first
        // value, low 32 = second) - the same convention the native MFSetAttributeSize/
        // MFSetAttributeRatio helper macros use; Vortice.MediaFoundation 2.1.0 doesn't expose
        // those helpers directly, only the generic IMFAttributes.Set(Guid, T), so this packs by
        // hand instead.
        private static ulong PackAttribute(uint high, uint low) => ((ulong)high << 32) | low;

        private static IMFSinkWriter CreateSinkWriter(string path, int width, int height, int fps, out int streamIndex)
        {
            IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)BitrateForQuality());
            outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            outputType.Set(MediaTypeAttributeKeys.FrameSize, PackAttribute((uint)width, (uint)height));
            outputType.Set(MediaTypeAttributeKeys.FrameRate, PackAttribute((uint)fps, 1));
            outputType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackAttribute(1, 1));

            IMFMediaType inputType = MediaFactory.MFCreateMediaType();
            inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            inputType.Set(MediaTypeAttributeKeys.FrameSize, PackAttribute((uint)width, (uint)height));
            inputType.Set(MediaTypeAttributeKeys.FrameRate, PackAttribute((uint)fps, 1));
            inputType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackAttribute(1, 1));

            IMFSinkWriter writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, null);
            streamIndex = writer.AddStream(outputType);
            writer.SetInputMediaType(streamIndex, inputType, null);

            return writer;
        }

        private static int BitrateForQuality() => App.Settings.Prop.InstantReplayQuality switch
        {
            0 => 2_000_000,
            2 => 8_000_000,
            _ => 4_000_000,
        };

        private static HWND FindRobloxWindow()
        {
            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);

            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return (HWND)process.MainWindowHandle;
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }

            return HWND.Null;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
