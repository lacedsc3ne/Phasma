using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace PhasmaStrap.Utility
{
    public sealed class ClipInfo
    {
        public int Width;
        public int Height;
        public double Fps;
        public TimeSpan Duration;
        public long FileBytes;
    }

    public sealed class ClipEditOptions
    {
        public TimeSpan Start = TimeSpan.Zero;
        public TimeSpan End = TimeSpan.MaxValue;

        // crop rectangle in source pixels; Width/Height 0 = no crop
        public int CropX, CropY, CropWidth, CropHeight;

        // 0.25 - 4; 0.5 = slow motion, 2 = double speed
        public double Speed = 1.0;
    }

    // Trim / crop / re-time for the Capture page's replay clips. Everything goes through Media
    // Foundation's source reader (decode to RGB32) and sink writer (H.264 encode) - the same
    // writer path InstantReplayRecorder uses, so it needs nothing that isn't already in Windows.
    // Re-encoding rather than stream-copying is deliberate: a stream copy can only cut on
    // keyframes, which on a 10-20 second clip means the trim point can be seconds off, and crop
    // is impossible without decoding anyway. Clips are short and low frame rate, so a full
    // re-encode takes a second or two.
    //
    // Deliberately free of WPF/App dependencies so it can be exercised from a console harness.
    // Carries the same Vortice.MediaFoundation 2.1.0 workarounds as InstantReplayRecorder: the
    // UINT64 attribute accessors recurse forever, and the *FromURL factories are bound to
    // Mfplat.dll when the exports live in mfreadwrite.dll - both go through raw COM / P/Invoke.
    public static class ClipProcessor
    {
        public static Action<string>? Log;

        private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
        private const int MediaSource = unchecked((int)0xFFFFFFFF);
        private const int FlagEndOfStream = 0x2;

        private static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("FB394F3D-CCF1-42EE-BBB3-F9B845D5681D");
        private static readonly Guid MF_PD_DURATION = new("6C990D33-BB8E-477A-8598-0D5D96FCD88A");

        private static readonly object _mfLock = new();
        private static int _mfRefs;

        private static void Startup()
        {
            lock (_mfLock)
            {
                if (_mfRefs++ == 0)
                    MediaFactory.MFStartup(false);
            }
        }

        private static void Shutdown()
        {
            lock (_mfLock)
            {
                if (--_mfRefs == 0)
                {
                    try { MediaFactory.MFShutdown(); } catch { }
                }
            }
        }

        // ------------------------------------------------------------------ public API

        public static ClipInfo Probe(string path)
        {
            Startup();
            try
            {
                using Reader reader = Reader.Open(path);
                return reader.Info;
            }
            finally
            {
                Shutdown();
            }
        }

        // Returns the frame shown at `position` as top-down 32-bit BGRA, or null if the clip has no
        // frame there. Reads forward from the start instead of seeking - clips are short, and it
        // keeps the result frame-exact.
        public static byte[]? GrabFrame(string path, TimeSpan position, out int width, out int height)
        {
            width = height = 0;
            Startup();
            try
            {
                using Reader reader = Reader.Open(path);
                width = reader.Info.Width;
                height = reader.Info.Height;

                byte[]? last = null;
                byte[] scratch = new byte[width * height * 4];

                while (reader.Read(out IMFSample? sample, out long ts))
                {
                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        if (ts > position.Ticks && last is not null)
                            break;

                        reader.CopyFrame(sample, 0, 0, width, height, scratch);
                        last ??= new byte[scratch.Length];
                        Buffer.BlockCopy(scratch, 0, last, 0, scratch.Length);

                        if (ts >= position.Ticks)
                            break;
                    }
                }

                return last;
            }
            finally
            {
                Shutdown();
            }
        }

        public sealed class Thumbnail
        {
            public byte[] Bgra = Array.Empty<byte>();
            public int Width;
            public int Height;
        }

        // `count` evenly spaced, point-sampled thumbnails in a single forward pass - for the
        // editor's timeline strip.
        public static List<Thumbnail> GrabThumbnails(string path, int count, int maxWidth, CancellationToken cancel = default)
        {
            var result = new List<Thumbnail>();
            Startup();
            try
            {
                using Reader reader = Reader.Open(path);
                ClipInfo info = reader.Info;
                if (info.Duration <= TimeSpan.Zero || count <= 0)
                    return result;

                int step = Math.Max(1, (int)Math.Ceiling((double)info.Width / maxWidth));
                int tw = info.Width / step, th = info.Height / step;
                byte[] scratch = new byte[info.Width * info.Height * 4];
                long interval = info.Duration.Ticks / count;

                while (result.Count < count && reader.Read(out IMFSample? sample, out long ts))
                {
                    cancel.ThrowIfCancellationRequested();

                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        if (ts < result.Count * interval)
                            continue;

                        reader.CopyFrame(sample, 0, 0, info.Width, info.Height, scratch);

                        var thumb = new Thumbnail { Width = tw, Height = th, Bgra = new byte[tw * th * 4] };
                        for (int y = 0; y < th; y++)
                        {
                            int srcRow = y * step * info.Width * 4;
                            int dstRow = y * tw * 4;
                            for (int x = 0; x < tw; x++)
                                Buffer.BlockCopy(scratch, srcRow + x * step * 4, thumb.Bgra, dstRow + x * 4, 4);
                        }

                        result.Add(thumb);
                    }
                }

                return result;
            }
            finally
            {
                Shutdown();
            }
        }

        public static void Export(string source, string destination, ClipEditOptions options, Action<double>? progress = null, CancellationToken cancel = default)
        {
            Startup();

            IMFSinkWriter? writer = null;
            bool finished = false;

            try
            {
                using Reader reader = Reader.Open(source);
                ClipInfo info = reader.Info;

                double speed = Math.Clamp(options.Speed, 0.25, 4.0);
                long startTicks = Math.Max(0, options.Start.Ticks);
                long endTicks = options.End == TimeSpan.MaxValue ? long.MaxValue : options.End.Ticks;
                if (endTicks <= startTicks)
                    throw new ArgumentException("The trim end has to be after the trim start.");

                int cropX = 0, cropY = 0, outW = info.Width, outH = info.Height;
                if (options.CropWidth > 0 && options.CropHeight > 0)
                {
                    cropX = Math.Clamp(options.CropX, 0, info.Width - 2);
                    cropY = Math.Clamp(options.CropY, 0, info.Height - 2);
                    outW = Math.Clamp(options.CropWidth, 2, info.Width - cropX);
                    outH = Math.Clamp(options.CropHeight, 2, info.Height - cropY);
                }

                // H.264 wants even dimensions
                outW &= ~1;
                outH &= ~1;
                if (outW < 64 || outH < 64)
                    throw new ArgumentException("The crop area is too small - it has to be at least 64 x 64 pixels.");

                double outFps = Math.Clamp(info.Fps * speed, 1, 240);
                long sourceSpan = Math.Min(endTicks, info.Duration.Ticks) - startTicks;

                // keep roughly the source's bits-per-pixel (plus headroom for the generation loss),
                // scaled to the cropped area
                double sourceBitrate = info.Duration.TotalSeconds > 0.1 ? info.FileBytes * 8 / info.Duration.TotalSeconds : 4_000_000;
                double area = (double)(outW * outH) / (info.Width * info.Height);
                uint bitrate = (uint)Math.Clamp(sourceBitrate * 1.3 * area * Math.Max(1.0, speed), 1_000_000, 20_000_000);

                Log?.Invoke($"Export {source} -> {destination}: {startTicks / 1e7:0.00}s-{(endTicks == long.MaxValue ? info.Duration.TotalSeconds : endTicks / 1e7):0.00}s crop={cropX},{cropY} {outW}x{outH} speed={speed} fps={outFps:0.##} bitrate={bitrate}");

                // Sound rides along unchanged for a trim / crop. A speed change would need the
                // pitch corrected to be listenable, so a re-timed clip is saved without sound.
                using AudioPass? audio = Math.Abs(speed - 1.0) < 0.001 ? AudioPass.TryOpen(source) : null;

                // The graphics card's encoder first (several times faster), Microsoft's software one
                // if it refuses. High-frame-rate 1080p is outside the H.264 levels some encoders
                // enforce; the stream is then declared as 60fps - samples keep their real
                // timestamps, so nothing is lost.
                var attempts = new List<(bool Hardware, double Fps)> { (true, outFps), (false, outFps) };
                if (outFps > 60)
                {
                    attempts.Add((true, 60));
                    attempts.Add((false, 60));
                }

                int streamIndex = 0;
                Exception? refused = null;

                foreach ((bool hardware, double declaredFps) in attempts)
                {
                    try
                    {
                        writer = CreateSinkWriter(destination, outW, outH, declaredFps, bitrate, hardware, out streamIndex);
                        audio?.AddTo(writer);
                        writer.BeginWriting();

                        if (refused is not null)
                            Log?.Invoke($"Encoding with the {(hardware ? "hardware" : "software")} encoder, stream declared as {declaredFps:0.##}fps");

                        refused = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        refused = ex;
                        Log?.Invoke($"{(hardware ? "Hardware" : "Software")} encoder refused {outW}x{outH}@{declaredFps:0.##}: {ex.Message.Trim()}");
                        writer?.Dispose();
                        writer = null;
                        try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                    }
                }

                if (writer is null)
                    throw refused ?? new InvalidOperationException("No H.264 encoder is available.");

                byte[] frame = new byte[outW * outH * 4];
                long defaultDuration = (long)(10_000_000 / Math.Max(1, info.Fps));
                int written = 0;

                while (reader.Read(out IMFSample? sample, out long ts))
                {
                    cancel.ThrowIfCancellationRequested();

                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        if (ts < startTicks)
                            continue;
                        if (ts >= endTicks)
                            break;

                        reader.CopyFrame(sample, cropX, cropY, outW, outH, frame);

                        long duration = defaultDuration;
                        try { if (sample.SampleDuration > 0) duration = sample.SampleDuration; } catch { }

                        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(frame.Length);
                        buffer.Lock(out IntPtr ptr, out int _, out int _);
                        Marshal.Copy(frame, 0, ptr, frame.Length);
                        buffer.Unlock();
                        buffer.CurrentLength = frame.Length;

                        using IMFSample outSample = MediaFactory.MFCreateSample();
                        outSample.AddBuffer(buffer);
                        outSample.SampleTime = (long)((ts - startTicks) / speed);
                        outSample.SampleDuration = Math.Max(1, (long)(duration / speed));

                        writer.WriteSample(streamIndex, outSample);
                        written++;

                        // keep the sound level with the picture, so the file stays interleaved
                        audio?.Pump(writer, ts, startTicks, endTicks);

                        if (sourceSpan > 0)
                            progress?.Invoke(Math.Clamp((double)(ts - startTicks) / sourceSpan, 0, 1));
                    }
                }

                if (written == 0)
                    throw new InvalidOperationException("There are no frames between the trim start and end.");

                audio?.Pump(writer, long.MaxValue, startTicks, endTicks);

                writer.Finalize();
                finished = true;
                progress?.Invoke(1);
                Log?.Invoke($"Export wrote {written} frame(s){(audio is null ? "" : $" and {audio.Written} block(s) of sound")}");
            }
            finally
            {
                writer?.Dispose();
                Shutdown();

                if (!finished)
                {
                    try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                }
            }
        }

        // Same trim / crop / speed as Export, written as an animated GIF (see GifWriter). Source
        // frames are picked on a fixed grid of the GIF's frame rate; each one is held until the
        // next picked frame really appears, so a clip with uneven timing keeps its pacing.
        public static void ExportGif(string source, string destination, ClipEditOptions options, GifExportOptions gif, Action<double>? progress = null, CancellationToken cancel = default)
        {
            Startup();
            bool finished = false;

            try
            {
                using Reader reader = Reader.Open(source);
                ClipInfo info = reader.Info;

                double speed = Math.Clamp(options.Speed, 0.25, 4.0);
                long startTicks = Math.Max(0, options.Start.Ticks);
                long endTicks = options.End == TimeSpan.MaxValue ? long.MaxValue : options.End.Ticks;
                if (endTicks <= startTicks)
                    throw new ArgumentException("The trim end has to be after the trim start.");

                int cropX = 0, cropY = 0, cropW = info.Width, cropH = info.Height;
                if (options.CropWidth > 0 && options.CropHeight > 0)
                {
                    cropX = Math.Clamp(options.CropX, 0, info.Width - 2);
                    cropY = Math.Clamp(options.CropY, 0, info.Height - 2);
                    cropW = Math.Clamp(options.CropWidth, 2, info.Width - cropX);
                    cropH = Math.Clamp(options.CropHeight, 2, info.Height - cropY);
                }

                int outW = cropW, outH = cropH;
                if (gif.MaxWidth > 0 && cropW > gif.MaxWidth)
                {
                    outW = gif.MaxWidth;
                    outH = Math.Max(1, (int)Math.Round(cropH * (double)outW / cropW));
                }

                int fps = Math.Clamp(gif.Fps, 1, 50);
                long interval = 10_000_000 / fps;
                long sourceSpan = Math.Min(endTicks, info.Duration.Ticks) - startTicks;

                Log?.Invoke($"ExportGif {source} -> {destination}: {startTicks / 1e7:0.00}s-{(endTicks == long.MaxValue ? info.Duration.TotalSeconds : endTicks / 1e7):0.00}s crop={cropX},{cropY} {cropW}x{cropH} -> {outW}x{outH} speed={speed} fps={fps} dither={gif.Dither}");

                byte[] full = new byte[cropW * cropH * 4];
                byte[] scaled = outW == cropW && outH == cropH ? full : new byte[outW * outH * 4];
                byte[] pending = new byte[outW * outH * 4];
                long pendingTime = -1;
                long nextPick = 0;
                long writtenCs = 0; // centiseconds handed out so far - rounding never accumulates

                using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var writer = new GifWriter(file, outW, outH, gif.Dither))
                {
                    void Flush(long untilTime)
                    {
                        long targetCs = (long)Math.Round(untilTime / 100_000.0);
                        int delay = (int)Math.Max(2, targetCs - writtenCs);
                        writer.AddFrame(pending, delay);
                        writtenCs += delay;
                    }

                    while (reader.Read(out IMFSample? sample, out long ts))
                    {
                        cancel.ThrowIfCancellationRequested();

                        if (sample is null)
                            continue;

                        using (sample)
                        {
                            if (ts < startTicks)
                                continue;
                            if (ts >= endTicks)
                                break;

                            long outTime = (long)((ts - startTicks) / speed);
                            if (outTime < nextPick)
                                continue;

                            while (nextPick <= outTime)
                                nextPick += interval;

                            if (pendingTime >= 0)
                                Flush(outTime);

                            reader.CopyFrame(sample, cropX, cropY, cropW, cropH, full);
                            if (!ReferenceEquals(scaled, full))
                                Downscale(full, cropW, cropH, scaled, outW, outH);

                            Buffer.BlockCopy(scaled, 0, pending, 0, pending.Length);
                            pendingTime = outTime;

                            if (sourceSpan > 0)
                                progress?.Invoke(Math.Clamp((double)(ts - startTicks) / sourceSpan, 0, 1));
                        }
                    }

                    if (pendingTime < 0)
                        throw new InvalidOperationException("There are no frames between the trim start and end.");

                    // the last frame is held for one normal frame time
                    Flush(pendingTime + interval);

                    Log?.Invoke($"ExportGif wrote {writer.FrameCount} frame(s), {writtenCs / 100.0:0.00}s");
                }

                finished = true;
                progress?.Invoke(1);
            }
            finally
            {
                Shutdown();

                if (!finished)
                {
                    try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                }
            }
        }

        // area-average downscale of top-down BGRA (a plain point or bilinear sample shimmers on
        // game footage when shrinking by 2-4x)
        private static void Downscale(byte[] source, int sw, int sh, byte[] destination, int dw, int dh)
        {
            int[] x0 = new int[dw + 1];
            for (int x = 0; x <= dw; x++)
                x0[x] = (int)((long)x * sw / dw);

            for (int y = 0; y < dh; y++)
            {
                int top = (int)((long)y * sh / dh);
                int bottom = Math.Max(top + 1, (int)((long)(y + 1) * sh / dh));

                for (int x = 0; x < dw; x++)
                {
                    int left = x0[x];
                    int right = Math.Max(left + 1, x0[x + 1]);

                    int b = 0, g = 0, r = 0;
                    for (int sy = top; sy < bottom; sy++)
                    {
                        int i = (sy * sw + left) * 4;
                        for (int sx = left; sx < right; sx++, i += 4)
                        {
                            b += source[i];
                            g += source[i + 1];
                            r += source[i + 2];
                        }
                    }

                    int n = (bottom - top) * (right - left);
                    int o = (y * dw + x) * 4;
                    destination[o] = (byte)(b / n);
                    destination[o + 1] = (byte)(g / n);
                    destination[o + 2] = (byte)(r / n);
                    destination[o + 3] = 255;
                }
            }
        }

        // ------------------------------------------------------------------ sound

        // The clip's sound track, decoded to PCM by a reader of its own and re-encoded to AAC by
        // the export's writer. null when the clip is silent (or its sound is in a format the AAC
        // encoder cannot take) - the export then simply has no sound either.
        private sealed class AudioPass : IDisposable
        {
            private const int FirstAudioStream = unchecked((int)0xFFFFFFFD);
            private const int AllStreams = unchecked((int)0xFFFFFFFE);

            private readonly IMFSourceReader _reader;
            private readonly IMFMediaType _pcm;
            private readonly uint _rate, _channels;
            private int _stream = -1;
            private IMFSample? _pending;
            private long _pendingTime;
            private bool _done;

            public int Written { get; private set; }

            private AudioPass(IMFSourceReader reader, IMFMediaType pcm, uint rate, uint channels)
            {
                _reader = reader;
                _pcm = pcm;
                _rate = rate;
                _channels = channels;
            }

            public static AudioPass? TryOpen(string path)
            {
                IMFSourceReader? reader = null;
                IMFMediaType? current = null;

                try
                {
                    int hr = MFCreateSourceReaderFromURL(path, IntPtr.Zero, out IntPtr ptr);
                    if (hr < 0)
                        Marshal.ThrowExceptionForHR(hr);

                    reader = new IMFSourceReader(ptr);
                    reader.SetStreamSelection(AllStreams, false);
                    reader.SetStreamSelection(FirstAudioStream, true); // throws when the clip has no sound

                    using (IMFMediaType wanted = MediaFactory.MFCreateMediaType())
                    {
                        wanted.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                        wanted.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                        SetCurrentMediaType(reader, FirstAudioStream, wanted);
                    }

                    current = reader.GetCurrentMediaType(FirstAudioStream);

                    if (!TryGetUInt32(current, MediaTypeAttributeKeys.AudioSamplesPerSecond, out uint rate)
                        || !TryGetUInt32(current, MediaTypeAttributeKeys.AudioNumChannels, out uint channels)
                        || (rate != 44100 && rate != 48000) || channels is < 1 or > 2)
                    {
                        Log?.Invoke("The clip's sound is not something the AAC encoder takes - exporting without it");
                        current.Dispose();
                        reader.Dispose();
                        return null;
                    }

                    return new AudioPass(reader, current, rate, channels);
                }
                catch (Exception)
                {
                    // the ordinary case: a clip without a sound track
                    current?.Dispose();
                    reader?.Dispose();
                    return null;
                }
            }

            // before BeginWriting
            public void AddTo(IMFSinkWriter writer)
            {
                using IMFMediaType output = MediaFactory.MFCreateMediaType();
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                SetUInt32(output, MediaTypeAttributeKeys.AudioSamplesPerSecond, _rate);
                SetUInt32(output, MediaTypeAttributeKeys.AudioNumChannels, _channels);
                SetUInt32(output, MediaTypeAttributeKeys.AudioBitsPerSample, 16);
                SetUInt32(output, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000);

                _stream = writer.AddStream(output);
                writer.SetInputMediaType(_stream, _pcm, null);
            }

            // writes the sound up to `untilTicks` (source time), keeping only [startTicks, endTicks)
            public void Pump(IMFSinkWriter writer, long untilTicks, long startTicks, long endTicks)
            {
                while (!_done && _stream >= 0)
                {
                    if (_pending is null)
                    {
                        _reader.ReadSample(FirstAudioStream, 0, out int _, out int flags, out _pendingTime, out _pending);

                        if ((flags & FlagEndOfStream) != 0)
                        {
                            _pending?.Dispose();
                            _pending = null;
                            _done = true;
                            return;
                        }

                        if (_pending is null)
                            continue;
                    }

                    if (_pendingTime >= endTicks)
                    {
                        _done = true;
                        return;
                    }

                    if (_pendingTime > untilTicks)
                        return; // the picture has not got this far yet

                    long duration = 0;
                    try { duration = _pending.SampleDuration; } catch { }

                    if (_pendingTime + duration > startTicks)
                    {
                        _pending.SampleTime = Math.Max(0, _pendingTime - startTicks);
                        writer.WriteSample(_stream, _pending);
                        Written++;
                    }

                    _pending.Dispose();
                    _pending = null;
                }
            }

            public void Dispose()
            {
                _pending?.Dispose();
                _pcm.Dispose();
                _reader.Dispose();
            }
        }

        // ------------------------------------------------------------------ reader

        private sealed class Reader : IDisposable
        {
            private readonly IMFSourceReader _reader;
            private readonly int _stride; // signed: negative = bottom-up when only a flat buffer is available

            public ClipInfo Info { get; }

            private Reader(IMFSourceReader reader, ClipInfo info, int stride)
            {
                _reader = reader;
                Info = info;
                _stride = stride;
            }

            public static Reader Open(string path)
            {
                using IMFAttributes attributes = MediaFactory.MFCreateAttributes(1);
                SetUInt32(attributes, MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);

                int hr = MFCreateSourceReaderFromURL(path, attributes.NativePointer, out IntPtr ptr);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                var reader = new IMFSourceReader(ptr);

                try
                {
                    double fps = 12;
                    using (IMFMediaType native = reader.GetNativeMediaType(FirstVideoStream, 0))
                    {
                        if (TryGetUInt64(native, MediaTypeAttributeKeys.FrameRate, out ulong rate) && (uint)rate != 0)
                            fps = (double)(uint)(rate >> 32) / (uint)rate;
                    }

                    using (IMFMediaType wanted = MediaFactory.MFCreateMediaType())
                    {
                        wanted.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                        wanted.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                        SetCurrentMediaType(reader, FirstVideoStream, wanted);
                    }

                    int width, height, stride;
                    using (IMFMediaType current = reader.GetCurrentMediaType(FirstVideoStream))
                    {
                        if (!TryGetUInt64(current, MediaTypeAttributeKeys.FrameSize, out ulong size))
                            throw new InvalidOperationException("The clip has no readable frame size.");

                        width = (int)(uint)(size >> 32);
                        height = (int)(uint)size;

                        // MF treats RGB as bottom-up unless the type says otherwise
                        stride = TryGetUInt32(current, MediaTypeAttributeKeys.DefaultStride, out uint s) ? unchecked((int)s) : -(width * 4);
                    }

                    TimeSpan duration = TimeSpan.Zero;
                    try
                    {
                        object? value = reader.GetPresentationAttribute(MediaSource, MF_PD_DURATION).Value;
                        if (value is not null)
                            duration = TimeSpan.FromTicks(Convert.ToInt64(value));
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"Duration attribute unavailable: {ex.Message}");
                    }

                    var info = new ClipInfo
                    {
                        Width = width,
                        Height = height,
                        Fps = fps,
                        Duration = duration,
                        FileBytes = new FileInfo(path).Length,
                    };

                    return new Reader(reader, info, stride);
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
            }

            // false at end of stream; sample can be null on a stream gap
            public bool Read(out IMFSample? sample, out long timestamp)
            {
                _reader.ReadSample(FirstVideoStream, 0, out int _, out int flags, out timestamp, out sample);

                if ((flags & FlagEndOfStream) != 0)
                {
                    sample?.Dispose();
                    sample = null;
                    return false;
                }

                return true;
            }

            // Copies the (x, y, w, h) region of the decoded frame into `destination` as top-down BGRA.
            public void CopyFrame(IMFSample sample, int x, int y, int w, int h, byte[] destination)
            {
                using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();

                IMF2DBuffer? buffer2D = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
                if (buffer2D is not null)
                {
                    using (buffer2D)
                    {
                        buffer2D.Lock2D(out IntPtr scanline0, out int pitch);
                        try
                        {
                            CopyRows(scanline0, pitch, x, y, w, h, destination);
                        }
                        finally
                        {
                            buffer2D.Unlock2D();
                        }
                    }

                    return;
                }

                buffer.Lock(out IntPtr ptr, out int _, out int _);
                try
                {
                    int abs = Math.Abs(_stride);
                    IntPtr top = _stride < 0 ? ptr + (Info.Height - 1) * abs : ptr;
                    CopyRows(top, _stride, x, y, w, h, destination);
                }
                finally
                {
                    buffer.Unlock();
                }
            }

            private static void CopyRows(IntPtr topRow, int pitch, int x, int y, int w, int h, byte[] destination)
            {
                int rowBytes = w * 4;
                for (int row = 0; row < h; row++)
                {
                    IntPtr src = topRow + (y + row) * pitch + x * 4;
                    Marshal.Copy(src, destination, row * rowBytes, rowBytes);
                }
            }

            public void Dispose() => _reader.Dispose();
        }

        // ------------------------------------------------------------------ writer

        private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");

        private static IMFSinkWriter CreateSinkWriter(string path, int width, int height, double fps, uint bitrate, bool hardware, out int streamIndex)
        {
            uint fpsNum = (uint)Math.Round(fps * 1000);
            const uint fpsDen = 1000;

            using IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, bitrate);
            outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            SetUInt64(outputType, MediaTypeAttributeKeys.FrameSize, Pack((uint)width, (uint)height));
            SetUInt64(outputType, MediaTypeAttributeKeys.FrameRate, Pack(fpsNum, fpsDen));
            SetUInt64(outputType, MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));

            using IMFMediaType inputType = MediaFactory.MFCreateMediaType();
            inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            SetUInt64(inputType, MediaTypeAttributeKeys.FrameSize, Pack((uint)width, (uint)height));
            SetUInt64(inputType, MediaTypeAttributeKeys.FrameRate, Pack(fpsNum, fpsDen));
            SetUInt64(inputType, MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            // frames handed to the writer are top-down
            inputType.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(width * 4));

            using IMFAttributes? attributes = hardware ? MediaFactory.MFCreateAttributes(1) : null;
            if (attributes is not null)
                SetUInt32(attributes, MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);

            int hr = MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes?.NativePointer ?? IntPtr.Zero, out IntPtr ptr);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            var writer = new IMFSinkWriter(ptr);

            try
            {
                streamIndex = writer.AddStream(outputType);
                writer.SetInputMediaType(streamIndex, inputType, null);
                return writer;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        // ------------------------------------------------------------------ raw COM helpers

        private static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream, IntPtr pAttributes, out IntPtr ppSinkWriter);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSourceReaderFromURL(string pwszURL, IntPtr pAttributes, out IntPtr ppSourceReader);

        // Vortice's IMFSourceReader.SetCurrentMediaType passes a real pointer for pdwReserved, which
        // the API rejects with E_INVALIDARG - it has to be NULL. Vtable slot 7 (IUnknown 0-2,
        // GetStreamSelection, SetStreamSelection, GetNativeMediaType, GetCurrentMediaType,
        // SetCurrentMediaType).
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetCurrentMediaTypeFn(IntPtr self, int streamIndex, IntPtr reserved, IntPtr mediaType);

        private static void SetCurrentMediaType(IMFSourceReader reader, int streamIndex, IMFMediaType mediaType)
        {
            IntPtr vtable = Marshal.ReadIntPtr(reader.NativePointer);
            var fn = Marshal.GetDelegateForFunctionPointer<SetCurrentMediaTypeFn>(Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
            int hr = fn(reader.NativePointer, streamIndex, IntPtr.Zero, mediaType.NativePointer);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        // IMFAttributes vtable: IUnknown 0-2, GetItem 3, GetItemType 4, CompareItem 5, Compare 6,
        // GetUINT32 7, GetUINT64 8, ... GetUnknown 17, SetItem 18, DeleteItem 19,
        // DeleteAllItems 20, SetUINT32 21, SetUINT64 22
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetUInt32Fn(IntPtr self, ref Guid key, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetUInt64Fn(IntPtr self, ref Guid key, out ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt32Fn(IntPtr self, ref Guid key, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt64Fn(IntPtr self, ref Guid key, ulong value);

        private static T Slot<T>(IMFAttributes attributes, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(attributes.NativePointer);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }

        private static bool TryGetUInt32(IMFAttributes attributes, Guid key, out uint value)
            => Slot<GetUInt32Fn>(attributes, 7)(attributes.NativePointer, ref key, out value) >= 0;

        private static bool TryGetUInt64(IMFAttributes attributes, Guid key, out ulong value)
            => Slot<GetUInt64Fn>(attributes, 8)(attributes.NativePointer, ref key, out value) >= 0;

        private static void SetUInt32(IMFAttributes attributes, Guid key, uint value)
        {
            int hr = Slot<SetUInt32Fn>(attributes, 21)(attributes.NativePointer, ref key, value);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private static void SetUInt64(IMFAttributes attributes, Guid key, ulong value)
        {
            int hr = Slot<SetUInt64Fn>(attributes, 22)(attributes.NativePointer, ref key, value);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }
    }
}
