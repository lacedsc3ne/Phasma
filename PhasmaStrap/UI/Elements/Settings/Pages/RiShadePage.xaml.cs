using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RiShadePage
    {
        public RiShadePage()
        {
            DataContext = RenderingViewModel.Shared.RiShade;
            InitializeComponent();
        }
    }
}
