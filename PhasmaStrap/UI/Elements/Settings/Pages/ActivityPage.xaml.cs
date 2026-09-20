using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ActivityPage
    {
        public ActivityPage()
        {
            DataContext = new ActivityViewModel();
            InitializeComponent();
        }
    }
}
