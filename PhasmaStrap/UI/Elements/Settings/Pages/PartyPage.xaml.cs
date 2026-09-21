using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PartyPage
    {
        public PartyPage()
        {
            DataContext = new PartyViewModel();
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((PartyViewModel)DataContext).Attach();
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((PartyViewModel)DataContext).Detach();
        }
    }
}
