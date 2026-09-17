using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class FriendsPage
    {
        public FriendsPage()
        {
            DataContext = new FriendsViewModel();
            InitializeComponent();
        }
    }
}
