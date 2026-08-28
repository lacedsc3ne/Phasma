namespace PhasmaStrap
{
    // limits how many logical processors the PhasmaStrap process itself may run on.
    // ported from Voidstrap - note this only affects the bootstrapper's own process,
    // not the Roblox client, since that's how the original implementation works
    public static class CpuCoreLimiter
    {
        private static readonly object Sync = new();

        private static IntPtr? _originalAffinity;

        public static void ApplyConfiguredLimit() => SetCpuCoreLimit(App.Settings.Prop.CpuCoreLimit);

        public static void SetCpuCoreLimit(int coreCount)
        {
            const string LOG_IDENT = "CpuCoreLimiter::SetCpuCoreLimit";

            int processorCount = Environment.ProcessorCount;

            if (processorCount > IntPtr.Size * 8)
                return;

            if (coreCount < 1 || coreCount > processorCount)
                coreCount = processorCount;

            lock (Sync)
            {
                try
                {
                    using Process process = Process.GetCurrentProcess();

                    _originalAffinity ??= process.ProcessorAffinity;

                    if (coreCount >= processorCount)
                    {
                        process.ProcessorAffinity = _originalAffinity.Value;
                        return;
                    }

                    ulong mask = ((1UL << coreCount) - 1UL) << (processorCount - coreCount);
                    process.ProcessorAffinity = (IntPtr)unchecked((long)mask);

                    App.Logger.WriteLine(LOG_IDENT, $"CPU limit set to the top {coreCount} logical processors");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"CPU limit change failed: {ex.Message}");
                }
            }
        }
    }
}
