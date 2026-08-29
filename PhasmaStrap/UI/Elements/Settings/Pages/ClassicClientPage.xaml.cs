using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ClassicClientPage.xaml
    /// </summary>
    public partial class ClassicClientPage
    {
        public ClassicClientPage()
        {
            DataContext = new ClassicClientViewModel();
            InitializeComponent();
        }
    }
}
