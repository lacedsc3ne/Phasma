using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for PerformancePage.xaml
    /// </summary>
    public partial class PerformancePage
    {
        public PerformancePage()
        {
            DataContext = new PerformanceViewModel();
            InitializeComponent();
        }
    }
}
