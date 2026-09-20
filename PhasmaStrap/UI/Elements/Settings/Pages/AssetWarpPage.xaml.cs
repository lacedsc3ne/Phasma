using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class AssetWarpPage
    {
        public AssetWarpPage()
        {
            DataContext = AssetWarpViewModel.Shared;
            InitializeComponent();
        }
    }
}
