using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HistoryPage
    {
        public HistoryPage()
        {
            DataContext = new HistoryViewModel();
            InitializeComponent();
        }
    }
}
