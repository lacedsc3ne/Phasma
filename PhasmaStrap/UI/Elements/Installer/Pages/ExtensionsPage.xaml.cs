using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Installer.Pages
{
    public partial class ExtensionsPage
    {
        private readonly ExtensionsViewModel _viewModel = new();

        public ExtensionsPage()
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
        }
    }
}
