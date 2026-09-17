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

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Detach();
        }
    }
}
