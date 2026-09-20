using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingFrameRatePage
    {
        public RenderingFrameRatePage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }
    }
}
