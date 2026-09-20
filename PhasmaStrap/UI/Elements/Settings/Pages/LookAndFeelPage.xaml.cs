using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class LookAndFeelPage : ISectionHostPage
    {
        public LookAndFeelPage()
        {
            InitializeComponent();
        }

        public SectionHost SectionHost => Host;
    }
}
