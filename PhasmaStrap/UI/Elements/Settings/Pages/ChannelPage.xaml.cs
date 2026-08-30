using System.Windows;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ChannelPage.xaml
    /// </summary>
    public partial class ChannelPage
    {
        public ChannelPage()
        {
            DataContext = new ChannelViewModel();
            InitializeComponent();
        }

        private void BrowseChannelsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ChannelListsDialog { Owner = Window.GetWindow(this) };

            dialog.ShowDialog();

            if (!string.IsNullOrEmpty(dialog.Result) && DataContext is ChannelViewModel viewModel)
                viewModel.RobloxChannel = dialog.Result;
        }
    }
}
