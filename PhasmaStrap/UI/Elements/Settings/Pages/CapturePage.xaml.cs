using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class CapturePage
    {
        public CapturePage()
        {
            DataContext = new CaptureViewModel();
            InitializeComponent();
        }
    }
}
