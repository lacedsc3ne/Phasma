using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PhasmaStrap.Utility
{
    // Toggles WPF's hardware/software render tier for every window the process opens, and strips
    // GPU compositing flags from embedded browser control arguments when software rendering is
    // forced. Ported from Voidstrap. PhasmaStrap already flips render mode per-window in
    // WpfUiWindow.OnSourceInitialized based on the same Settings.WPFSoftwareRender/NoGPUFlag
    // combination this class reads - that per-window path is left untouched, this class adds the
    // process-wide RenderOptions.ProcessRenderMode toggle (so newly created visuals default to the
    // right tier even before a window's OnSourceInitialized runs) plus a one-shot safety net: if a
    // hardware-rendered session never draws its first frame (a bad GPU driver hanging on init), the
    // next launch automatically falls back to software rendering.
    public static class RenderAcceleration
    {
        private static readonly object Sync = new();

        private static RenderMode? _appliedProcessMode;

        private static int _windowHandlerInstalled;

        private static int _sentinelArmed;

        private static string SentinelPath => Path.Combine(Paths.Base, "gpurender.pending");

        public static bool SoftwareOnly
        {
            get
            {
                try
                {
                    return App.Settings?.Prop?.WPFSoftwareRender == true || App.LaunchSettings.NoGPUFlag.Active;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void ApplyProcess()
        {
            TripSentinel();
            InstallWindowHandler();
            RenderMode mode = SoftwareOnly ? RenderMode.SoftwareOnly : RenderMode.Default;
            lock (Sync)
            {
                if (_appliedProcessMode == mode)
                    return;

                try
                {
                    RenderOptions.ProcessRenderMode = mode;
                    _appliedProcessMode = mode;
                    App.Logger?.WriteLine("RenderAcceleration::ApplyProcess", mode == RenderMode.SoftwareOnly ? "Software rendering enabled" : "Hardware rendering enabled");
                }
                catch (Exception ex)
                {
                    App.Logger?.WriteException("RenderAcceleration::ApplyProcess", ex);
                }
            }
        }

        public static string ApplyToBrowserArguments(string arguments)
        {
            string normalized = arguments?.Trim() ?? string.Empty;
            if (!SoftwareOnly || normalized.Contains("--disable-gpu", StringComparison.OrdinalIgnoreCase))
                return normalized;

            return string.IsNullOrEmpty(normalized)
                ? "--disable-gpu --disable-gpu-compositing"
                : normalized + " --disable-gpu --disable-gpu-compositing";
        }

        private static void InstallWindowHandler()
        {
            if (Interlocked.Exchange(ref _windowHandlerInstalled, 1) != 0)
                return;

            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
                ApplyWindow(window);

            ArmSentinel();
        }

        private static void TripSentinel()
        {
            try
            {
                if (!File.Exists(SentinelPath))
                    return;

                File.Delete(SentinelPath);

                if (App.Settings?.Prop == null || App.Settings.Prop.WPFSoftwareRender)
                    return;

                App.Settings.Prop.WPFSoftwareRender = true;
                App.Settings.Save();
                App.Logger?.WriteLine("RenderAcceleration::TripSentinel", "The last hardware rendered session never drew a frame, falling back to software rendering");
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RenderAcceleration::TripSentinel", ex);
            }
        }

        private static void ArmSentinel()
        {
            if (SoftwareOnly || Interlocked.Exchange(ref _sentinelArmed, 1) != 0)
                return;

            try
            {
                File.WriteAllText(SentinelPath, string.Empty);
                CompositionTarget.Rendering += OnFirstFrame;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RenderAcceleration::ArmSentinel", ex);
            }
        }

        private static void OnFirstFrame(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFirstFrame;

            try
            {
                File.Delete(SentinelPath);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RenderAcceleration::OnFirstFrame", ex);
            }
        }

        private static void ApplyWindow(Window window)
        {
            try
            {
                if (PresentationSource.FromVisual(window) is not HwndSource source || source.CompositionTarget == null)
                    return;

                RenderMode mode = SoftwareOnly ? RenderMode.SoftwareOnly : RenderMode.Default;
                if (source.CompositionTarget.RenderMode != mode)
                    source.CompositionTarget.RenderMode = mode;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RenderAcceleration::ApplyWindow", ex);
            }
        }
    }
}
