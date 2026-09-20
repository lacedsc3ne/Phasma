using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class CaptureStoragePage
    {
        public CaptureStoragePage()
        {
            DataContext = CaptureViewModel.Shared;
            InitializeComponent();
        }
    }
}
