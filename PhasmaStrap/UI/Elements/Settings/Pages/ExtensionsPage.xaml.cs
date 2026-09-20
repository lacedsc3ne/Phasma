using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ExtensionsPage
    {
        public ExtensionsPage()
        {
            DataContext = new ExtensionsViewModel();
            InitializeComponent();
        }
    }
}
