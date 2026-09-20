namespace PhasmaStrap.Utility
{
    public static class SharedGameFrame
    {
        private static readonly object _lock = new();
        private static IntPtr _handle;
        private static int _width, _height;
        private static long _version;

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

    public static unsafe class KeyedMutexLock
    {
        public const int Acquired = 0;

        public static int Acquire(Vortice.DXGI.IDXGIKeyedMutex mutex, ulong key, uint milliseconds)
        {
            void** vtable = *(void***)mutex.NativePointer;
            var acquire = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)vtable[8];
            return acquire(mutex.NativePointer, key, milliseconds);
        }
    }
}
