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

        // the "Home page background" group on Mods > Home page background: this is the one place
        // that setting is actually drawn (the page is rebuilt on every navigation, so changes made
        // on the Mods page show up the next time Home is opened)
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
