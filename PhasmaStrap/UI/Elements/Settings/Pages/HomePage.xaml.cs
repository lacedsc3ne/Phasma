using System.Windows;
using System.Windows.Media;

using PhasmaStrap.Enums;
using PhasmaStrap.Integrations;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HomePage
    {
        public HomePage()
        {
            DataContext = new HomeViewModel();
            InitializeComponent();

            Loaded += (_, _) => ApplyHomeBackground();
        }

        private void ApplyHomeBackground()
        {
            var prop = App.Settings.Prop;

            if (!prop.HomepageBackgroundEnabled || prop.HomepageBackgroundMode == HomepageBackgroundMode.None)
            {
                Background = Brushes.Transparent;
                return;
            }

            try
            {
                Brush brush = HomepageBackgroundRenderer.BuildBrushFromSettings();
                brush.Freeze();
                Background = brush;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomePage", $"Could not apply the Home page background: {ex.Message}");
                Background = Brushes.Transparent;
            }
        }
    }
}
