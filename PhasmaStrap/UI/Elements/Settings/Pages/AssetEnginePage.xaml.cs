using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for AssetEnginePage.xaml
    /// </summary>
    public partial class AssetEnginePage
    {
        public AssetEnginePage()
        {
            DataContext = new AssetEngineViewModel();
            InitializeComponent();
        }
    }
}
