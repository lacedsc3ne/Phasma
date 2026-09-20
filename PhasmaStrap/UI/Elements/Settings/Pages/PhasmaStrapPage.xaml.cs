using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PhasmaStrapPage
    {
        public PhasmaStrapPage()
        {
            DataContext = new PhasmaStrapViewModel();
            InitializeComponent();

            PhasmaAccountSection.DataContext = new PhasmaAccountViewModel();
        }

        private void HistoryExpander_Expanded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is PhasmaStrapViewModel viewModel)
                viewModel.RefreshBackups();
        }
    }
}
