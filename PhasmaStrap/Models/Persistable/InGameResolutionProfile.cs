namespace PhasmaStrap.Models.Persistable
{
    // one game's own in-game resolution (Rendering > Performance > In-game resolution)
    public sealed class InGameResolutionProfile
    {
        public string Monitor { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
    }
}
