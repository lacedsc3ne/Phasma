namespace PhasmaStrap.Integrations.Overlays
{
    // One area of the game picture that stream-safe mode hides, as fractions of the Roblox window
    // (0..1), so it stays in place whatever size the window is.
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

    // Stream-safe mode: a second window, "PhasmaStrap Stream View", shows the game with the marked
    // areas pixelated (or blacked out). OBS captures that window instead of the game, so chat, the
    // player list or anything else marked never reaches the stream - while your own screen is
    // untouched. It sits under every other window (behind Roblox); Window Capture still sees it.
    //
    // It can't go through PhasmaStrap's normal overlay: that window is excluded from capture on
    // purpose (the compositor captures the screen, and would otherwise capture itself).
    public static class StreamSafe
    {
        public const string WindowTitle = "PhasmaStrap Stream View";
        public const string WindowClass = "PhasmaStrapStreamView";

        public const string StylePixelate = "Pixelate";
        public const string StyleBlack = "Black";

        public const int MaxRegions = 16;

        // where Roblox's own chat and player list sit by default
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
