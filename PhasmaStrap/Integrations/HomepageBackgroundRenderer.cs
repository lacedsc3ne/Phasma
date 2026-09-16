using System;
using System.Windows;
using System.Windows.Media;

using PhasmaStrap.Enums;

namespace PhasmaStrap.Integrations
{
    /// <summary>
    /// Shared rendering logic for the "Homepage Background" setting (Overlays tab, ModsPage):
    /// turns Mode/Color/Gradient/Angle into a WPF <see cref="Brush"/>. This used to live only in
    /// <c>HomepageBackgroundPreviewWindow</c>; it was pulled out here so it has exactly one
    /// implementation, ready to be reused wherever this setting is actually drawn.
    ///
    /// As of this writing that's still only the preview window opened by the settings page's
    /// own "Preview" button - see the doc comments on <c>HomepageBackgroundPreviewWindow</c> and
    /// on <c>Integrations.Overlays.OverlayCompositor</c> for why this doesn't yet composite live
    /// behind the running Roblox client.
    /// </summary>
    internal static class HomepageBackgroundRenderer
    {
        public static Color ParseColorOrDefault(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex)!;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Builds the brush for the given mode/colors/angle. <see cref="HomepageBackgroundMode.None"/>
        /// still returns a usable brush (the solid color) rather than null, matching the previous
        /// behavior in the preview window - callers that care about "is this actually on" should
        /// check the mode/enabled flag themselves before calling this.
        /// </summary>
        public static Brush BuildBrush(HomepageBackgroundMode mode, Color solidColor, Color gradientColor, double gradientAngleDegrees)
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

            return new SolidColorBrush(solidColor);
        }

        /// <summary>
        /// Convenience overload that reads Mode/Color/Gradient/Angle straight out of
        /// <see cref="App.Settings"/>, so a future live-compositing consumer (or any other caller)
        /// doesn't have to re-parse the persisted hex strings itself.
        /// </summary>
        public static Brush BuildBrushFromSettings()
        {
            var s = App.Settings.Prop;
            Color solid = ParseColorOrDefault(s.HomepageBackgroundColor, Colors.Black);
            Color gradient = ParseColorOrDefault(s.HomepageBackgroundGradientColor, Colors.DarkBlue);
            return BuildBrush(s.HomepageBackgroundMode, solid, gradient, s.HomepageBackgroundGradientAngle);
        }
    }
}
