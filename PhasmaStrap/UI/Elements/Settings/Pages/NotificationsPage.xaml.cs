using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class NotificationsPage
    {
        private readonly NotificationsViewModel _viewModel;

        public NotificationsPage()
        {
            _viewModel = new NotificationsViewModel();
            DataContext = _viewModel;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Attach();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Detach();
        }
    }
}
