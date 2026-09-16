using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class RPCTemplatesWindow
    {
        public RPCTemplatesWindow(IntegrationsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private void RPCTemplateSelection(object sender, SelectionChangedEventArgs e)
        {
            IntegrationsViewModel viewModel = (IntegrationsViewModel)DataContext;
            viewModel.SelectedRPCTemplate = (RPCTemplate)((ListBox)sender).SelectedItem;
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedRPCTemplate));
        }
    }
}
