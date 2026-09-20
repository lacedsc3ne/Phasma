using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingResolutionPage
    {
        public RenderingResolutionPage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }
    }
}
