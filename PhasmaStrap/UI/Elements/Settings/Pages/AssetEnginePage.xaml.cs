using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class AssetEnginePage
    {
        public AssetEnginePage()
        {
            DataContext = new AssetEngineViewModel();
            InitializeComponent();
        }
    }
}
