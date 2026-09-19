using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for DiagnosticsPage.xaml
    /// </summary>
    public partial class DiagnosticsPage
    {
        public DiagnosticsPage()
        {
            DataContext = new DiagnosticsViewModel();
            InitializeComponent();
        }
    }
}
