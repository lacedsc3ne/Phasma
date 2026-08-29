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

        public bool AutoTranslate
        {
            get => App.Settings.Prop.AutoTranslate;
            set => App.Settings.Prop.AutoTranslate = value;
        }

        public bool RpcAutoTranslate
        {
            get => App.Settings.Prop.RpcAutoTranslate;
            set => App.Settings.Prop.RpcAutoTranslate = value;
        }

        public string AutoTranslateLanguage
        {
            get => App.Settings.Prop.AutoTranslateLanguage;
            set => App.Settings.Prop.AutoTranslateLanguage = value;
        }

        public Dictionary<string, string> AutoTranslateLanguages { get; } = PhasmaStrap.Utility.TranslationService.AvailableLanguages;
    }
}
