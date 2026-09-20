using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PrivateServersPage
    {
        public PrivateServersPage()
        {
            DataContext = new PrivateServersViewModel();
            InitializeComponent();
        }
    }
}
