using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PerformancePage : ISectionHostPage
    {
        public PerformancePage()
        {
            InitializeComponent();
        }

        public SectionHost SectionHost => Host;
    }
}
