using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class AppPage : ISectionHostPage
    {
        public AppPage()
        {
            InitializeComponent();
        }

        public SectionHost SectionHost => Host;
    }
}
