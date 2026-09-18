using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    // Sound for Instant Replay.
    //
    // The game's audio is captured with WASAPI *process* loopback: only what Roblox itself plays,
    // not Discord, music or anything else coming out of the speakers (Windows 10 2004 and later;
    // older systems fall back to loopback of the whole default output). Optionally the default
    // microphone is captured as well and mixed in.
    //
    // Everything is kept as 48 kHz 16-bit stereo PCM in a ring that only ever holds the last
    // clip's worth (about 11 MB per minute) and is laid out on the same clock GpuReplayRecorder
    // stamps its frames with, so "the sound between tick A and tick B" is an exact slice. Loopback
    // delivers nothing at all while the game is silent; those stretches are zeros in the ring.
    // It is encoded to AAC only when a clip is saved (ReplayMuxer).
    //
    // No App dependencies, so it can be exercised from a console harness.
    public sealed class ReplayAudio : IDisposable
    {
        public static Action<string>? Log;

        private const int Rate = 48000;
        private const int BytesPerFrame = 4;

        private readonly Func<int> _seconds;
        private readonly string _processName;
        private readonly bool _microphone;
        private readonly List<Source> _sources = new();
        private volatile bool _running;

        public ReplayAudio(Func<int> bufferSeconds, string processName, bool microphone = false)
        {
            _seconds = bufferSeconds;
            _processName = processName;
            _microphone = microphone;
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;

            _sources.Add(new Source(this, "game", SourceKind.Game));
            if (_microphone)
                _sources.Add(new Source(this, "microphone", SourceKind.Microphone));

            foreach (Source source in _sources)
                source.Start();
        }

        public void Dispose()
        {
            _running = false;

            foreach (Source source in _sources)
                source.Join();

            _sources.Clear();
        }

        // interleaved 16-bit 48 kHz stereo for exactly [startTicks, endTicks) on GpuReplayRecorder.Now()'s clock
        public byte[]? Read(long startTicks, long endTicks)
        {
            long frames = (endTicks - startTicks) * Rate / 10_000_000L;
            if (frames <= 0 || frames > Rate * 600L)
                return null;

            var mixed = new byte[frames * BytesPerFrame];
            var scratch = new byte[mixed.Length];
            bool first = true;

            foreach (Source source in _sources)
            {
                if (first)
                {
                    source.Read(startTicks, mixed);
                    first = false;
                    continue;
                }

                Array.Clear(scratch);
                source.Read(startTicks, scratch);

                for (int i = 0; i + 1 < mixed.Length; i += 2)
                {
                    int sum = (short)(mixed[i] | (mixed[i + 1] << 8)) + (short)(scratch[i] | (scratch[i + 1] << 8));
                    sum = Math.Clamp(sum, short.MinValue, short.MaxValue);
                    mixed[i] = (byte)sum;
                    mixed[i + 1] = (byte)(sum >> 8);
                }
            }

            return mixed;
        }

        private enum SourceKind { Game, Microphone }

        // ------------------------------------------------------------------ one capture stream + its ring

        private sealed class Source
        {
            private readonly ReplayAudio _owner;
            private readonly string _name;
            private readonly SourceKind _kind;
            private Thread? _thread;

            private readonly object _lock = new();
            private byte[] _ring = Array.Empty<byte>();
            private long _ringFrames;     // capacity in frames
            private long _headFrame;      // absolute frame index (on the ticks clock) one past the newest sample
            private bool _hasData;

            public Source(ReplayAudio owner, string name, SourceKind kind)
            {
                _owner = owner;
                _name = name;
                _kind = kind;
            }

            public void Start()
            {
                _thread = new Thread(Run) { IsBackground = true, Name = $"ReplayAudio-{_name}" };
                _thread.Start();
            }

            public void Join()
            {
                try { _thread?.Join(1500); } catch { }
                _thread = null;
            }

            private static long FrameOf(long ticks) => ticks * Rate / 10_000_000L;

            private void Write(long startTicks, IntPtr data, int frames, bool silent)
            {
                lock (_lock)
                {
                    long wanted = Math.Max(5, _owner._seconds()) * (long)Rate;
                    if (wanted != _ringFrames)
                    {
                        _ring = new byte[wanted * BytesPerFrame];
                        _ringFrames = wanted;
                        _hasData = false;
                    }

                    long start = FrameOf(startTicks);

                    if (!_hasData)
                    {
                        _headFrame = start;
                        _hasData = true;
                    }
                    else if (start > _headFrame + Rate / 25)
                    {
                        // a silent stretch (nothing was delivered): zeros up to where this packet belongs
                        long gap = Math.Min(start - _headFrame, _ringFrames);
                        long from = start - gap;
                        for (long f = from; f < start; f++)
                        {
                            long at = (f % _ringFrames) * BytesPerFrame;
                            _ring[at] = _ring[at + 1] = _ring[at + 2] = _ring[at + 3] = 0;
                        }
                        _headFrame = start;
                    }
                    else if (start < _headFrame - Rate / 25)
                    {
                        // the audio clock has crept ahead of the system clock - step back onto it
                        _headFrame = start;
                    }

                    // (small differences are jitter: the packet simply follows on from the last)
                    unsafe
                    {
                        byte* source = (byte*)data;
                        for (int f = 0; f < frames; f++)
                        {
                            long at = ((_headFrame + f) % _ringFrames) * BytesPerFrame;
                            if (silent || source == null)
                            {
                                _ring[at] = _ring[at + 1] = _ring[at + 2] = _ring[at + 3] = 0;
                            }
                            else
                            {
                                byte* p = source + (long)f * BytesPerFrame;
                                _ring[at] = p[0];
                                _ring[at + 1] = p[1];
                                _ring[at + 2] = p[2];
                                _ring[at + 3] = p[3];
                            }
                        }
                    }

                    _headFrame += frames;
                }
            }

            // fills `destination` (zeros where nothing was captured)
            public void Read(long startTicks, byte[] destination)
            {
                lock (_lock)
                {
                    if (!_hasData || _ringFrames == 0)
                        return;

                    long start = FrameOf(startTicks);
                    long count = destination.Length / BytesPerFrame;
                    long oldest = _headFrame - _ringFrames;

                    for (long f = 0; f < count; f++)
                    {
                        long frame = start + f;
                        if (frame < oldest || frame >= _headFrame || frame < 0)
                            continue;

                        long at = (frame % _ringFrames) * BytesPerFrame;
                        long to = f * BytesPerFrame;
                        destination[to] = _ring[at];
                        destination[to + 1] = _ring[at + 1];
                        destination[to + 2] = _ring[at + 2];
                        destination[to + 3] = _ring[at + 3];
                    }
                }
            }

            private void Run()
            {
                while (_owner._running)
                {
                    try
                    {
                        Capture();
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"{_name} capture stopped: {ex.Message}");
                    }

                    // the device went away, the game restarted... try again in a moment
                    for (int i = 0; i < 30 && _owner._running; i++)
                        Thread.Sleep(100);
                }
            }

            private void Capture()
            {
                IAudioClient? client = null;
                IAudioCaptureClient? capture = null;
                IntPtr format = IntPtr.Zero;
                IntPtr ready = CreateEventW(IntPtr.Zero, false, false, null);

                try
                {
                    uint flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
                    string how;

                    if (_kind == SourceKind.Microphone)
                    {
                        client = ActivateEndpoint(capture: true);
                        how = "default microphone";
                    }
                    else
                    {
                        flags |= AUDCLNT_STREAMFLAGS_LOOPBACK;

                        int pid = FindProcess(_owner._processName);
                        if (pid == 0)
                            return; // no game yet - Run() retries

                        try
                        {
                            client = ActivateProcessLoopback(pid);
                            how = $"process loopback of PID {pid}";
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke($"Process loopback is not available ({ex.Message}) - recording everything the default output plays instead");
                            client = ActivateEndpoint(capture: false);
                            how = "loopback of the default output";
                        }
                    }

                    format = Marshal.AllocHGlobal(18);
                    Marshal.WriteInt16(format, 0, 1);               // WAVE_FORMAT_PCM
                    Marshal.WriteInt16(format, 2, 2);               // channels
                    Marshal.WriteInt32(format, 4, Rate);
                    Marshal.WriteInt32(format, 8, Rate * BytesPerFrame);
                    Marshal.WriteInt16(format, 12, BytesPerFrame);  // block align
                    Marshal.WriteInt16(format, 14, 16);             // bits
                    Marshal.WriteInt16(format, 16, 0);

                    Check(client.Initialize(0 /* shared */, flags, 2_000_000 /* 200 ms */, 0, format, IntPtr.Zero), "IAudioClient.Initialize");
                    Check(client.SetEventHandle(ready), "SetEventHandle");

                    Guid iid = typeof(IAudioCaptureClient).GUID;
                    Check(client.GetService(ref iid, out object service), "GetService");
                    capture = (IAudioCaptureClient)service;

                    Check(client.Start(), "IAudioClient.Start");
                    Log?.Invoke($"Recording {_name} sound: {how}");

                    int idle = 0;

                    while (_owner._running)
                    {
                        if (WaitForSingleObject(ready, 200) != 0)
                        {
                            // process loopback of a process that has gone never signals again
                            if (_kind == SourceKind.Game && ++idle % 25 == 0 && FindProcess(_owner._processName) == 0)
                                return;
                            continue;
                        }

                        idle = 0;

                        while (true)
                        {
                            Check(capture.GetNextPacketSize(out uint pending), "GetNextPacketSize");
                            if (pending == 0)
                                break;

                            Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint bufferFlags, out ulong _, out ulong qpc), "GetBuffer");

                            long duration = frames * 10_000_000L / Rate;

                            // the packet's own capture time when the driver gives one; else "it just ended"
                            long start = qpc != 0 ? (long)qpc : GpuReplayRecorder.Now() - duration;

                            Write(start, data, (int)frames, (bufferFlags & AUDCLNT_BUFFERFLAGS_SILENT) != 0);

                            Check(capture.ReleaseBuffer(frames), "ReleaseBuffer");
                        }
                    }

                    client.Stop();
                }
                finally
                {
                    if (capture is not null) Marshal.ReleaseComObject(capture);
                    if (client is not null) Marshal.ReleaseComObject(client);
                    if (format != IntPtr.Zero) Marshal.FreeHGlobal(format);
                    if (ready != IntPtr.Zero) CloseHandle(ready);
                }
            }
        }

        // ------------------------------------------------------------------ activation

        private static int FindProcess(string name)
        {
            Process[] processes = Process.GetProcessesByName(name);
            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.Id;
                }

                return processes.Length > 0 ? processes[0].Id : 0;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0)
                throw new COMException($"{what} failed (0x{hr:X8})", hr);
        }

        private static IAudioClient ActivateEndpoint(bool capture)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            try
            {
                Check(enumerator.GetDefaultAudioEndpoint(capture ? 1 : 0, capture ? 2 /* communications */ : 0 /* console */, out IMMDevice device), "GetDefaultAudioEndpoint");
                try
                {
                    Guid iid = typeof(IAudioClient).GUID;
                    Check(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object client), "IMMDevice.Activate");
                    return (IAudioClient)client;
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
        {
            public readonly ManualResetEventSlim Done = new(false);

            public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                Done.Set();
                return 0;
            }
        }

        private static IAudioClient ActivateProcessLoopback(int processId)
        {
            // AUDIOCLIENT_ACTIVATION_PARAMS { PROCESS_LOOPBACK, { pid, INCLUDE_TARGET_PROCESS_TREE } }
            IntPtr parameters = Marshal.AllocHGlobal(12);
            // PROPVARIANT { vt = VT_BLOB, blob = { 12, parameters } }
            IntPtr variant = Marshal.AllocHGlobal(24);

            try
            {
                Marshal.WriteInt32(parameters, 0, 1);
                Marshal.WriteInt32(parameters, 4, processId);
                Marshal.WriteInt32(parameters, 8, 0);

                for (int i = 0; i < 24; i++)
                    Marshal.WriteByte(variant, i, 0);
                Marshal.WriteInt16(variant, 0, 65);
                Marshal.WriteInt32(variant, 8, 12);
                Marshal.WriteIntPtr(variant, 8 + IntPtr.Size, parameters);

                var handler = new ActivationHandler();
                Guid iid = typeof(IAudioClient).GUID;

                Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, variant, handler, out IActivateAudioInterfaceAsyncOperation operation), "ActivateAudioInterfaceAsync");

                try
                {
                    if (!handler.Done.Wait(5000))
                        throw new TimeoutException("activation did not complete");

                    Check(operation.GetActivateResult(out int result, out object? client), "GetActivateResult");
                    Check(result, "process loopback activation");

                    return (IAudioClient)(client ?? throw new InvalidOperationException("activation returned nothing"));
                }
                finally
                {
                    Marshal.ReleaseComObject(operation);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(variant);
                Marshal.FreeHGlobal(parameters);
            }
        }

        // ------------------------------------------------------------------ interop

        private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
        private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid, IntPtr activationParams, IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, string? name);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorClass { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);
        }
    }
}
