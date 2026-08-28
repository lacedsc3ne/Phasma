using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for NetworkingPage.xaml
    /// </summary>
    public partial class NetworkingPage
    {
        public NetworkingPage()
        {
            DataContext = new NetworkingViewModel();
            InitializeComponent();
        }
    }
}
