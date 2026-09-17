using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class InstancesPage
    {
        public InstancesPage()
        {
            DataContext = new InstancesViewModel();
            InitializeComponent();
        }
    }
}
