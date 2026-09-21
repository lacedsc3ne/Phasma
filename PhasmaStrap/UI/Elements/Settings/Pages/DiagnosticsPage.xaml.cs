using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class DiagnosticsPage
    {
        public DiagnosticsPage()
        {
            DataContext = new DiagnosticsViewModel();
            InitializeComponent();

            Loaded += (_, _) => (DataContext as DiagnosticsViewModel)?.ResumePolling();
            Unloaded += (_, _) => (DataContext as DiagnosticsViewModel)?.StopPolling();
        }
    }
}
