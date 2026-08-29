using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for OverlaysPage.xaml
    /// </summary>
    public partial class OverlaysPage
    {
        public OverlaysPage()
        {
            DataContext = new OverlaysViewModel();
            InitializeComponent();
        }
    }
}
