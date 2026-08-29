using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ReleasesPage
    {
        public ReleasesPage()
        {
            DataContext = new ReleasesViewModel();
            InitializeComponent();
        }
    }
}
