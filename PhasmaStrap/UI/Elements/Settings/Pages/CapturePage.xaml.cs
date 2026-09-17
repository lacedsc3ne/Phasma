using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class CapturePage
    {
        public CapturePage()
        {
            DataContext = new CaptureViewModel();
            InitializeComponent();

            // the page is cached between visits - re-check the hotkey bindings each time it shows
            Loaded += (_, _) => ((CaptureViewModel)DataContext).RefreshHotkeyHint();
        }
    }
}
