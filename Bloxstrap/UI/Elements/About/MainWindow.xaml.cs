using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;

namespace Bloxstrap.UI.Elements.About
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INavigationWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            App.Logger.WriteLine("MainWindow", "Initializing about window");

            if (Locale.CurrentCulture.Name.StartsWith("tr"))
                TranslatorsText.FontSize = 9;
        }

        private void MainWindow_MistLoaded(object sender, RoutedEventArgs e) =>
            ((Storyboard)Resources["MistDrift"]).Begin(this);

        #region INavigationWindow methods

        public Frame GetFrame() => RootFrame;

        public INavigation GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods
    }
}
