using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class NetworkingPage
    {
        public NetworkingPage()
        {
            DataContext = new NetworkingViewModel();
            InitializeComponent();
        }
    }
}
