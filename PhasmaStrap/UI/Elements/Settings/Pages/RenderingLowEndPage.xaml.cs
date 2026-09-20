using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingLowEndPage
    {
        public RenderingLowEndPage()
        {
            DataContext = RenderingViewModel.Shared.LowEnd;
            InitializeComponent();
        }
    }
}
