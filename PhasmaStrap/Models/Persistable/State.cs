namespace PhasmaStrap.Models.Persistable
{
    public class State
    {
        public bool PromptWebView2Install { get; set; } = true;

        public bool ForceReinstall { get; set; } = false;

        public WindowState SettingsWindow { get; set; } = new();

        public List<string> RecentSettingsSearches { get; set; } = new();

        public string AccountId { get; set; } = "";

        public string AccountName { get; set; } = "";

        public string AccountAvatar { get; set; } = "";

        public string LastAutoBackup { get; set; } = "";
    }
}
