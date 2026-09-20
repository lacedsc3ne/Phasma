using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingBoostPage
    {
        public RenderingBoostPage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }
    }
}
