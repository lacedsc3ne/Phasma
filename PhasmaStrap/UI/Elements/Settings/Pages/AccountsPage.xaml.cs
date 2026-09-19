using PhasmaStrap.UI.ViewModels.ContextMenu;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class AccountsPage
    {
        public AccountsPage()
        {
            DataContext = new AccountSwitcherViewModel();
            InitializeComponent();

            // the account guard section has its own view model
            GuardRoot.DataContext = new ViewModels.Settings.AccountGuardViewModel();
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((AccountSwitcherViewModel)DataContext).Dispose();
        }
    }
}
