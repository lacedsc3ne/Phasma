using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Behaviour > Roblox version tab - see RobloxVersionsViewModel
    /// </summary>
    public partial class RobloxVersionsPage
    {
        public RobloxVersionsPage()
        {
            DataContext = new RobloxVersionsViewModel();
            InitializeComponent();
        }
    }
}
