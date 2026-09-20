using System;
using System.Windows;
using System.Windows.Media;

using PhasmaStrap.Enums;

namespace PhasmaStrap.Integrations
{
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

        public static Brush BuildBrushFromSettings()
        {
            var s = App.Settings.Prop;
            Color solid = ParseColorOrDefault(s.HomepageBackgroundColor, Colors.Black);
            Color gradient = ParseColorOrDefault(s.HomepageBackgroundGradientColor, Colors.DarkBlue);
            return BuildBrush(s.HomepageBackgroundMode, solid, gradient, s.HomepageBackgroundGradientAngle);
        }
    }
}
