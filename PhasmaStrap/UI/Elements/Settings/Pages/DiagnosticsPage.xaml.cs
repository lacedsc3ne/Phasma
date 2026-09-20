using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class DiagnosticsPage
    {
        public DiagnosticsPage()
        {
            DataContext = new DiagnosticsViewModel();
            InitializeComponent();
        }
    }
}
