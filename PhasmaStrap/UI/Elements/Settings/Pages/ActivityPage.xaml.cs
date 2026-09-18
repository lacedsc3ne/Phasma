using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ActivityPage.xaml
    /// </summary>
    public partial class ActivityPage
    {
        public ActivityPage()
        {
            DataContext = new ActivityViewModel();
            InitializeComponent();
        }
    }
}
