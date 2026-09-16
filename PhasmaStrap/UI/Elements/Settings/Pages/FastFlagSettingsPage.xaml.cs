using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagSettingsPage.xaml - consolidated "Roblox FFlags" /
    /// "NVIDIA FFlags" / "Asset Warp" tab host. The first two tabs embed the existing
    /// FastFlagsPage/NvidiaPage via Frame, each still constructing and owning its own
    /// ViewModel exactly as before; only the Asset Warp tab's content is bound directly
    /// against this page's own DataContext.
    /// </summary>
    public partial class FastFlagSettingsPage
    {
        public FastFlagSettingsPage()
        {
            DataContext = new AssetWarpViewModel();
            InitializeComponent();
        }
    }
}
