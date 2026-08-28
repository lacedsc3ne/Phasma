using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for IntegrationsPage.xaml
    /// </summary>
    public partial class IntegrationsPage
    {
        public IntegrationsPage()
        {
            DataContext = new IntegrationsViewModel();
            InitializeComponent();
        }

        public void CustomIntegrationSelection(object sender, SelectionChangedEventArgs e)
        {
            IntegrationsViewModel viewModel = (IntegrationsViewModel)DataContext;
            viewModel.SelectedCustomIntegration = (CustomIntegration)((ListBox)sender).SelectedItem;
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedCustomIntegration));
        }

        public void RPCTemplateSelection(object sender, SelectionChangedEventArgs e)
        {
            IntegrationsViewModel viewModel = (IntegrationsViewModel)DataContext;
            viewModel.SelectedRPCTemplate = (RPCTemplate)((ListBox)sender).SelectedItem;
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedRPCTemplate));
        }
    }
}
