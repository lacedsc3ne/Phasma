using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PeoplePage : ISectionHostPage
    {
        public PeoplePage()
        {
            InitializeComponent();
        }

        public SectionHost SectionHost => Host;
    }
}
