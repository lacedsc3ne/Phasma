using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for RiShadePage.xaml
    /// </summary>
    public partial class RiShadePage
    {
        public RiShadePage()
        {
            DataContext = new RiShadeViewModel();
            InitializeComponent();
        }
    }
}
