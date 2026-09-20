using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class OverlaysCrosshairPage
    {
        public OverlaysCrosshairPage()
        {
            DataContext = RenderingViewModel.Shared.Overlays;
            InitializeComponent();
        }
    }
}
