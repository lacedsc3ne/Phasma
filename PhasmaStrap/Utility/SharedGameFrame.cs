namespace PhasmaStrap.Utility
{
    // Windows lets a process have only one screen capture (desktop duplication) per monitor. The
    // Instant Replay recorder and the overlay compositor both live in the game-session process and
    // both want one: whichever asked second got E_INVALIDARG, so the overlay (or the recorder)
    // silently didn't work - "sometimes it shows, sometimes it doesn't".
    //
    // The recorder has priority (a clip can't be recorded any other way). While it runs it keeps
    // its newest picture of the game in a shared, keyed-mutex texture and publishes it here; the
    // compositor, instead of asking Windows for a capture of its own, opens that texture and
    // copies from it. The recorder also reports the game's frame count, for the FPS readout.
    public static class SharedGameFrame
    {
        private static readonly object _lock = new();
        private static IntPtr _handle;
        private static int _width, _height;
        private static long _version;

        // the recorder is running: nobody else should hold a screen capture of the monitor
        public static volatile bool RecorderActive;

        public static void Publish(IntPtr sharedHandle, int width, int height)
        {
            lock (_lock)
            {
                _handle = sharedHandle;
                _width = width;
                _height = height;
            }
        }

        // a new picture was copied in
        public static void Updated() => Interlocked.Increment(ref _version);

        public static void Withdraw()
        {
            lock (_lock)
            {
                _handle = IntPtr.Zero;
                _width = _height = 0;
            }
        }

        public static long Version => Interlocked.Read(ref _version);

        public static bool TryGet(out IntPtr handle, out int width, out int height)
        {
            lock (_lock)
            {
                handle = _handle;
                width = _width;
                height = _height;
                return handle != IntPtr.Zero;
            }
        }
    }

    // IDXGIKeyedMutex::AcquireSync with its real result: Vortice's wrapper returns nothing, so a
    // timeout (a success code, 0x102) looks the same as getting the lock
    public static unsafe class KeyedMutexLock
    {
        public const int Acquired = 0;

        public static int Acquire(Vortice.DXGI.IDXGIKeyedMutex mutex, ulong key, uint milliseconds)
        {
            // IUnknown (3) + IDXGIObject (4) + IDXGIDeviceSubObject (1) -> AcquireSync is slot 8
            void** vtable = *(void***)mutex.NativePointer;
            var acquire = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)vtable[8];
            return acquire(mutex.NativePointer, key, milliseconds);
        }
    }
}
