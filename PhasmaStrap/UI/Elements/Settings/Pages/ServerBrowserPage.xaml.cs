using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ServerBrowserPage
    {
        public ServerBrowserPage()
        {
            DataContext = new ServerBrowserViewModel();
            InitializeComponent();
        }
    }
}
