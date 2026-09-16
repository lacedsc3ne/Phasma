using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using PhasmaStrap.Enums;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// A small, real, on-screen render of the "Homepage Background" settings from ModsPage's
    /// Overlays tab (solid color / gradient + angle), so "Preview" actually proves out the
    /// rendering instead of just editing settings nobody can see take effect. This intentionally
    /// does not attempt to composite live over the running Roblox window - see the Overlays tab
    /// notes in ModsViewModel.cs for why that part was scoped out of this pass.
    /// </summary>
    public partial class HomepageBackgroundPreviewWindow : Window
    {
        public HomepageBackgroundPreviewWindow(HomepageBackgroundMode mode, Color solidColor, Color gradientColor, double gradientAngleDegrees)
        {
            InitializeComponent();

            RootGrid.Background = BuildBrush(mode, solidColor, gradientColor, gradientAngleDegrees);
        }

        private static Brush BuildBrush(HomepageBackgroundMode mode, Color solidColor, Color gradientColor, double gradientAngleDegrees)
        {
            if (mode == HomepageBackgroundMode.Gradient)
            {
                double radians = gradientAngleDegrees * Math.PI / 180.0;
                var direction = new Vector(Math.Cos(radians), Math.Sin(radians));

                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0.5) - direction * 0.5,
                    EndPoint = new Point(0.5, 0.5) + direction * 0.5,
                    MappingMode = BrushMappingMode.RelativeToBoundingBox
                };
                brush.GradientStops.Add(new GradientStop(solidColor, 0.0));
                brush.GradientStops.Add(new GradientStop(gradientColor, 1.0));
                return brush;
            }

            // Mode.None still gets a preview window (the button is only enabled once a mode is
            // chosen), but fall back to the solid color rather than leaving it blank
            return new SolidColorBrush(solidColor);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();
    }
}
