using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class OverlaysStreamPage
    {
        public OverlaysStreamPage()
        {
            DataContext = RenderingViewModel.Shared.Overlays;
            InitializeComponent();
        }
    }
}
