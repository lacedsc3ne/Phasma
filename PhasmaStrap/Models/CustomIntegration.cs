namespace PhasmaStrap.Models
{
    public class CustomIntegration
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string LaunchArgs { get; set; } = "";
        public bool AutoClose { get; set; } = true;

        // per-game launching, ported from Voidstrap
        public bool SpecifyGame { get; set; } = false;
        public string GameID { get; set; } = "";
        public bool RunAsAdmin { get; set; } = false;
        public bool RunMinimized { get; set; } = false;
        public bool AutoCloseOnGame { get; set; } = false;
        public int Delay { get; set; } = 0;
    }
}
