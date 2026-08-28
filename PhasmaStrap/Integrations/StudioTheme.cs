using System.Windows.Media;

namespace PhasmaStrap.Integrations
{
    // reads PhasmaStrap's own live theme colors so the Studio companion plugin can match
    // them, instead of the fixed single-color palette StudioBridge previously sent. Ported
    // from Voidstrap, with DWM accent-color detection dropped since PhasmaStrap always uses
    // its own coral accent rather than following the Windows system accent.
    public static class StudioTheme
    {
        private const string AccentHex = "#F4554B";

        private static readonly object _lock = new();
        private static string? _cached;
        private static DateTime _builtUtc;

        public static string GetPaletteJson()
        {
            lock (_lock)
            {
                if (_cached is not null && DateTime.UtcNow - _builtUtc < TimeSpan.FromSeconds(30))
                    return _cached;

                _cached = Build();
                _builtUtc = DateTime.UtcNow;
                return _cached;
            }
        }

        private static string Build()
        {
            Color background = Composite(ResColor(new[] { "ApplicationBackgroundBrush", "ApplicationBackgroundColor" }, Color.FromRgb(14, 14, 18)), Color.FromRgb(14, 14, 18));
            Color section = Composite(ResColor(new[] { "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorDefault" }, Color.FromRgb(22, 22, 27)), background);
            Color row = Composite(ResColor(new[] { "ControlFillColorDefaultBrush", "ControlFillColorDefault" }, Color.FromRgb(29, 29, 35)), section);
            Color text = Composite(ResColor(new[] { "TextFillColorPrimaryBrush", "TextFillColorPrimary" }, Color.FromRgb(233, 236, 242)), row);
            Color sub = Composite(ResColor(new[] { "TextFillColorSecondaryBrush", "TextFillColorSecondary" }, Color.FromRgb(150, 155, 165)), row);

            return $"{{\"accent\":\"{AccentHex}\",\"bg\":\"{Hex(background)}\",\"section\":\"{Hex(section)}\",\"row\":\"{Hex(row)}\",\"text\":\"{Hex(text)}\",\"sub\":\"{Hex(sub)}\"}}";
        }

        private static Color Composite(Color fg, Color back)
        {
            if (fg.A == byte.MaxValue)
                return fg;

            double alpha = fg.A / 255.0;
            byte r = (byte)Math.Round(fg.R * alpha + back.R * (1.0 - alpha));
            byte g = (byte)Math.Round(fg.G * alpha + back.G * (1.0 - alpha));
            byte b = (byte)Math.Round(fg.B * alpha + back.B * (1.0 - alpha));
            return Color.FromRgb(r, g, b);
        }

        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color ResColor(string[] keys, Color fallback)
        {
            try
            {
                System.Windows.Application? app = System.Windows.Application.Current;
                if (app is null)
                    return fallback;

                Color? result = null;
                app.Dispatcher.Invoke(() =>
                {
                    foreach (string key in keys)
                    {
                        object? resource = app.TryFindResource(key);
                        if (resource is Color color)
                        {
                            result = color;
                            break;
                        }
                        if (resource is SolidColorBrush brush)
                        {
                            result = brush.Color;
                            break;
                        }
                    }
                });

                return result ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
