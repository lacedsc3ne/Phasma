using System.Windows;
using System.Windows.Controls;
using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.UI.ViewModels.Dialogs;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class ChannelListsDialog : WpfUiWindow
    {
        public string? Result { get; private set; }

        public ChannelListsDialog()
        {
            InitializeComponent();
            DataContext = new ChannelListsViewModel();
        }

        private void SelectCurrent()
        {
            if (ChannelDataGrid.SelectedItem is DeployInfoDisplay selected)
            {
                Result = selected.ChannelName;
                Close();
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e) => SelectCurrent();

        private void ChannelDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => SelectCurrent();

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            Close();
        }
    }
}
