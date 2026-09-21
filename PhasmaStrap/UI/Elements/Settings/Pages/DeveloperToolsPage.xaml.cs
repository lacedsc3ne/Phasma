using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class DeveloperToolsPage : Search.ISearchToolHost
    {
        private DiagnosticsPage? _diagnostics;

        public DeveloperToolsPage()
        {
            DataContext = new DeveloperToolsViewModel();
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Attach();

            if (SelectedKey() == "diagnostics")
                Dispatcher.BeginInvoke(new Action(LoadDiagnostics), System.Windows.Threading.DispatcherPriority.Background);
        }

        private string SelectedKey() => ToolRail.SelectedItem is DeveloperToolItem item ? item.Key : "";

        private void LoadDiagnostics()
        {
            if (_diagnostics is null)
            {
                _diagnostics = new DiagnosticsPage();
                DiagnosticsFrame.Navigate(_diagnostics);
                return;
            }

            (_diagnostics.DataContext as DiagnosticsViewModel)?.ResumePolling();
        }

        private void Page_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            ((DeveloperToolsViewModel)DataContext).Detach();
            (_diagnostics?.DataContext as DiagnosticsViewModel)?.StopPolling();
        }

        void Search.ISearchToolHost.ShowToolFor(Search.SettingsSearchEntry entry)
        {
            if (entry.NestedPageType == typeof(DiagnosticsPage))
            {
                ShowTool("diagnostics");
                return;
            }

            var tools = ((DeveloperToolsViewModel)DataContext).Tools;

            foreach (DeveloperToolItem item in tools)
            {
                if (string.Equals(item.Label, entry.Section, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Label, entry.Header, StringComparison.OrdinalIgnoreCase))
                {
                    ShowTool(item.Key);
                    return;
                }
            }
        }

        public void ShowTool(string key)
        {
            foreach (object entry in ToolRail.Items)
            {
                if (entry is DeveloperToolItem item && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    ToolRail.SelectedItem = item;
                    return;
                }
            }
        }

        private void ToolRail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedKey() == "diagnostics")
                LoadDiagnostics();
            else
                (_diagnostics?.DataContext as DiagnosticsViewModel)?.StopPolling();
        }
    }
}
