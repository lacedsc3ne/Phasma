using PhasmaStrap.UI.ViewModels.Dialogs;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class PartyInviteDialog
    {
        public PartyInviteDialog()
        {
            DataContext = new PartyInviteViewModel();
            InitializeComponent();
        }

        private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
