using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for PhasmaStrapPage.xaml
    /// </summary>
    public partial class PhasmaStrapPage
    {
        public PhasmaStrapPage()
        {
            DataContext = new PhasmaStrapViewModel();
            InitializeComponent();
        }
    }
}
