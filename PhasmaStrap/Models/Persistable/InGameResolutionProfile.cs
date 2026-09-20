namespace PhasmaStrap.Models.Persistable
{
    public sealed class InGameResolutionProfile
    {
        public string Monitor { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
    }
}
