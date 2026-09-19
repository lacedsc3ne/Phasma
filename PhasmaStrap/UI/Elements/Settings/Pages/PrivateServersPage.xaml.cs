using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Servers page > Private servers tab - see PrivateServersViewModel
    /// </summary>
    public partial class PrivateServersPage
    {
        public PrivateServersPage()
        {
            DataContext = new PrivateServersViewModel();
            InitializeComponent();
        }
    }
}
