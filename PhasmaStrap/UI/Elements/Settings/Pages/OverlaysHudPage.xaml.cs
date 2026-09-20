using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class OverlaysHudPage
    {
        public OverlaysHudPage()
        {
            DataContext = RenderingViewModel.Shared.Overlays;
            InitializeComponent();
        }
    }
}
