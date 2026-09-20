using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingPerGamePage
    {
        public RenderingPerGamePage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }
    }
}
