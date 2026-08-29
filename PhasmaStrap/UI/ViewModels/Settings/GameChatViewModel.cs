namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class GameChatViewModel : NotifyPropertyChangedViewModel
    {
        public bool GameChatEnabled
        {
            get => App.Settings.Prop.GameChatEnabled;
            set => App.Settings.Prop.GameChatEnabled = value;
        }

        public string GameChatServerUrl
        {
            get => App.Settings.Prop.GameChatServerUrl;
            set => App.Settings.Prop.GameChatServerUrl = value.Trim();
        }

        public string GameChatFilterPreference
        {
            get => App.Settings.Prop.GameChatFilterPreference;
            set => App.Settings.Prop.GameChatFilterPreference = value;
        }

        public string[] FilterPreferenceOptions { get; } = new[] { "strict", "default", "relaxed" };
    }
}
