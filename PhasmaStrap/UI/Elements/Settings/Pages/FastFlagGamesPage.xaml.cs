using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class FastFlagGamesPage
    {
        private readonly FastFlagGamesViewModel _viewModel = new();

        public FastFlagGamesPage()
        {
            DataContext = _viewModel;
            _viewModel.OpenEditorRequested += OpenEditor;

            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App.FlagProfiles.Edited -= OnProfilesEdited;
            App.FlagProfiles.Edited += OnProfilesEdited;

            _viewModel.Reload();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => App.FlagProfiles.Edited -= OnProfilesEdited;

        private void OnProfilesEdited(object? sender, EventArgs e)
        {
            if (!IsKeyboardFocusWithin)
                _viewModel.Reload();
            else
                foreach (GameRuleRow row in _viewModel.Rules)
                    row.Refresh();
        }

        private void OpenEditor(string profileId)
        {
            FlagEditorRequest.ProfileToOpen = profileId;
            FastFlagSettingsPage.SelectTab(this, "FastFlagEditorPage");
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
                _viewModel.SearchCommand.Execute(null);
        }
    }
}
