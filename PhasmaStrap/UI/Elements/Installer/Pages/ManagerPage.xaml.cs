using System.Windows;

using PhasmaStrap.UI.ViewModels.Installer;

namespace PhasmaStrap.UI.Elements.Installer.Pages
{
    /// <summary>
    /// Interaction logic for ManagerPage.xaml
    /// </summary>
    public partial class ManagerPage
    {
        private readonly ManagerViewModel _viewModel = new();

        public ManagerPage()
        {
            DataContext = _viewModel;
            InitializeComponent();
        }

        private void UiPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow window)
            {
                window.SetNextButtonText(Strings.Common_Navigation_Next);
                window.SetButtonEnabled("next", true);
                window.SetButtonEnabled("back", true);
            }

            _viewModel.Refresh();
        }
    }
}
