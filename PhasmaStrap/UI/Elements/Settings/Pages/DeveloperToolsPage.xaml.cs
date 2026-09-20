using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class DeveloperToolsPage
    {
        private bool _diagnosticsLoaded;

        public DeveloperToolsPage()
        {
            DataContext = new DeveloperToolsViewModel();
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Attach();

            if (!_diagnosticsLoaded)
                Dispatcher.BeginInvoke(new Action(LoadDiagnostics), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LoadDiagnostics()
        {
            if (_diagnosticsLoaded)
                return;

            _diagnosticsLoaded = true;
            DiagnosticsFrame.Navigate(new DiagnosticsPage());
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Detach();
        }

        private void ToolRail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ToolRail.SelectedItem is DeveloperToolItem item && item.Key == "diagnostics")
                LoadDiagnostics();
        }
    }
}
