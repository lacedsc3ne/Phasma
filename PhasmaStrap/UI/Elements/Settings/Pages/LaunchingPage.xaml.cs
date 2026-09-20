using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class LaunchingPage : ISectionHostPage
    {
        public LaunchingPage()
        {
            InitializeComponent();
        }

        public SectionHost SectionHost => Host;
    }
}
