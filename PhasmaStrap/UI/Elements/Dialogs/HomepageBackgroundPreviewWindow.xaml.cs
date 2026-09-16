using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using PhasmaStrap.Enums;
using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// A small, real, on-screen render of the "Homepage Background" settings from ModsPage's
    /// Overlays tab (solid color / gradient + angle), so "Preview" actually proves out the
    /// rendering instead of just editing settings nobody can see take effect. Rendering itself is
    /// <see cref="HomepageBackgroundRenderer"/>, shared so this window isn't the only place that
    /// knows how to turn Mode/Color/Gradient/Angle into pixels.
    ///
    /// This window intentionally does not attempt to composite live over the running Roblox
    /// window. Two independent reasons, both load-bearing:
    ///  - PhasmaStrap's only mechanism for drawing content into the live Roblox view
    ///    (Integrations.Overlays.OverlayCompositor, which RiShade/crosshair/HUD/AntiAliasing/
    ///    FrameGen all plug into) works by desktop-duplication-capturing whatever is already on
    ///    screen under Roblox's window and re-presenting that capture, with its own layers drawn
    ///    ON TOP, in a separate topmost window placed over Roblox. It never draws "behind"
    ///    Roblox's own already-rendered pixels - RiShade in particular is a screen-space
    ///    post-process filter on that captured copy, not a hook into Roblox's own DirectX
    ///    present/draw calls, so there's no existing in-process hook to reuse for this. Doing
    ///    this "for real" (a background that Roblox's own home-menu buttons/game tiles render on
    ///    top of, rather than a topmost layer that hides them) needs a D3D present/draw hook
    ///    injected into RobloxPlayerBeta.exe itself - a substantially larger new subsystem with
    ///    no existing precedent in this codebase.
    ///  - Independent of the above, <c>OverlaySettings.AnyEnabled</c> - which gates whether
    ///    OverlayCompositor runs at all - requires <c>OverlayHub.InGame</c>. The compositor is
    ///    deliberately never started while Roblox is only showing its home/games menu; it exists
    ///    only during actual gameplay. So even the "draw on top" mechanism the other overlays use
    ///    isn't running during the exact state (the home/games menu) this feature targets.
    /// </summary>
    public partial class HomepageBackgroundPreviewWindow : Window
    {
        public HomepageBackgroundPreviewWindow(HomepageBackgroundMode mode, Color solidColor, Color gradientColor, double gradientAngleDegrees)
        {
            InitializeComponent();

            RootGrid.Background = HomepageBackgroundRenderer.BuildBrush(mode, solidColor, gradientColor, gradientAngleDegrees);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();
    }
}
