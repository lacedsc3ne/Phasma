using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class DeveloperToolsPage
    {
        public DeveloperToolsPage()
        {
            DataContext = new DeveloperToolsViewModel();
            InitializeComponent();
        }

        // wpfui caches page instances, so Unloaded/Loaded fire on every navigation away/back -
        // without re-attaching here the live traffic log went silent after the first visit
        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Attach();
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Detach();
        }
    }
}
