using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Installer.Pages
{
    /// <summary>
    /// Interaction logic for ExtensionsPage.xaml
    /// </summary>
    /// <remarks>
    /// This reuses the exact same <see cref="ExtensionsViewModel"/> as the Settings window's
    /// Extensions page (see UI/Elements/Settings/Pages/ExtensionsPage.xaml) - it has no dependency
    /// on the Settings MainWindow, so it slots into the installer wizard's DataContext unmodified.
    /// </remarks>
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
