using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// The FastFlag "Per-game flags" tab - see FastFlagGamesViewModel
    /// </summary>
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

            // profiles may have been added, renamed or deleted on the editor tab
            _viewModel.Reload();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => App.FlagProfiles.Edited -= OnProfilesEdited;

        private void OnProfilesEdited(object? sender, EventArgs e)
        {
            // this page's own edits refresh it directly; only outside changes need a reload, and
            // reloading while a row's ComboBox is committing would fight it
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
