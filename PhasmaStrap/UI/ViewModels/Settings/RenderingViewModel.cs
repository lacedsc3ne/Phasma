namespace PhasmaStrap.UI.ViewModels.Settings
{
    // Performance/Overlays/RiShade all tune the same thing - what the game actually renders and
    // how it's presented on screen - and share the same audience, so they're tabs on one page
    // instead of three separate sidebar entries. Thin wrapper only: each existing ViewModel is
    // unchanged and untouched, just exposed as a property so each tab can bind to its own
    // instance (mirrors BehaviourViewModel.Matchmaker exposing ServerBrowserViewModel the same
    // way for the Deployment page's own tabs).
    public class RenderingViewModel
    {
        public PerformanceViewModel Performance { get; } = new();
        public OverlaysViewModel Overlays { get; } = new();
        public RiShadeViewModel RiShade { get; } = new();
    }
}
