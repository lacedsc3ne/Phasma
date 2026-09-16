using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HomePage
    {
        public HomePage()
        {
            DataContext = new HomeViewModel();
            InitializeComponent();
        }
    }
}
