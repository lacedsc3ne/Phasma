namespace PhasmaStrap.Models.Persistable
{
    public class State
    {
        public bool PromptWebView2Install { get; set; } = true;

        public bool ForceReinstall { get; set; } = false;

        public WindowState SettingsWindow { get; set; } = new();

        // settings-search results opened most recently (see MainWindow's search) - newest first
        public List<string> RecentSettingsSearches { get; set; } = new();
    }
}
