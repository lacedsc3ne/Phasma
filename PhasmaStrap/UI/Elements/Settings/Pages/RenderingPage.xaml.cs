using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RenderingPage
    {
        public RenderingPage()
        {
            DataContext = new RenderingViewModel();
            InitializeComponent();

            // low-end mode has its own view model
            LowEndRoot.DataContext = new LowEndModeViewModel();
        }
    }
}
