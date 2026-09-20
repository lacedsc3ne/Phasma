namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class RenderingViewModel
    {
        private static RenderingViewModel? _shared;

        public static RenderingViewModel Shared => _shared ??= new();

        public PerformanceViewModel Performance { get; } = new();
        public OverlaysViewModel Overlays { get; } = new();
        public RiShadeViewModel RiShade { get; } = new();

        private LowEndModeViewModel? _lowEnd;

        public LowEndModeViewModel LowEnd => _lowEnd ??= new();
    }
}
