namespace PhasmaStrap.Integrations.Overlays
{
    public sealed class StreamSafeRegion
    {
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }

        public StreamSafeRegion Copy() => (StreamSafeRegion)MemberwiseClone();

        public StreamSafeRegion Clamped()
        {
            double x = Math.Clamp(X, 0, 1), y = Math.Clamp(Y, 0, 1);
            return new StreamSafeRegion
            {
                Name = Name,
                X = x,
                Y = y,
                W = Math.Clamp(W, 0, 1 - x),
                H = Math.Clamp(H, 0, 1 - y),
            };
        }
    }

    public static class StreamSafe
    {
        public const string WindowTitle = "PhasmaStrap Stream View";
        public const string WindowClass = "PhasmaStrapStreamView";

        public const string StylePixelate = "Pixelate";
        public const string StyleBlack = "Black";

        public const int MaxRegions = 16;

        public static List<StreamSafeRegion> Defaults() => new()
        {
            new StreamSafeRegion { Name = "Chat", X = 0.0, Y = 0.05, W = 0.34, H = 0.38 },
            new StreamSafeRegion { Name = "Player list", X = 0.77, Y = 0.05, W = 0.23, H = 0.45 },
        };

        public static IReadOnlyList<StreamSafeRegion> Regions =>
            (App.Settings.Prop.StreamSafeRegions ?? new List<StreamSafeRegion>())
                .Select(r => r.Clamped())
                .Where(r => r.W > 0.001 && r.H > 0.001)
                .Take(MaxRegions)
                .ToList();
    }
}
