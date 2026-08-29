using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for GameChatPage.xaml
    /// </summary>
    public partial class GameChatPage
    {
        public GameChatPage()
        {
            DataContext = new GameChatViewModel();
            InitializeComponent();
        }
    }
}
