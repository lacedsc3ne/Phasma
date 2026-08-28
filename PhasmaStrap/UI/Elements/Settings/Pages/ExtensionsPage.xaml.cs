using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ExtensionsPage.xaml
    /// </summary>
    public partial class ExtensionsPage
    {
        public ExtensionsPage()
        {
            DataContext = new ExtensionsViewModel();
            InitializeComponent();
        }
    }
}
