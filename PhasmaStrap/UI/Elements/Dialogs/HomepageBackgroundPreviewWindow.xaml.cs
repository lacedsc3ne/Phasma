using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using PhasmaStrap.Enums;
using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class HomepageBackgroundPreviewWindow : Window
    {
        public HomepageBackgroundPreviewWindow(HomepageBackgroundMode mode, Color solidColor, Color gradientColor, double gradientAngleDegrees)
        {
            InitializeComponent();

            RootGrid.Background = HomepageBackgroundRenderer.BuildBrush(mode, solidColor, gradientColor, gradientAngleDegrees);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();
    }
}
