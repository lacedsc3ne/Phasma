using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RobloxVersionsPage
    {
        public RobloxVersionsPage()
        {
            DataContext = new RobloxVersionsViewModel();
            InitializeComponent();
        }
    }
}
