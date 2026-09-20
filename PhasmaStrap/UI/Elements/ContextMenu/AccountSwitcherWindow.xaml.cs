using PhasmaStrap.UI.ViewModels.ContextMenu;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public partial class AccountSwitcherWindow
    {
        private readonly AccountSwitcherViewModel _viewModel;

        public AccountSwitcherWindow()
        {
            _viewModel = new AccountSwitcherViewModel();

            DataContext = _viewModel;
            InitializeComponent();

            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;
            _viewModel.Dispose();
            DataContext = null;
        }
    }
}
