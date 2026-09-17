using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingPage
    {
        public RenderingPage()
        {
            DataContext = new RenderingViewModel();
            InitializeComponent();
        }
    }
}
