using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingSystemPage
    {
        public RenderingSystemPage()
        {
            DataContext = RenderingViewModel.Shared.Performance;
            InitializeComponent();
        }
    }
}
