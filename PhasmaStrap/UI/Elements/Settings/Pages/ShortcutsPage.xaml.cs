using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ShortcutsPage
    {
        private readonly ShortcutsViewModel _viewModel = new();

        public ShortcutsPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            Loaded += (_, _) => _viewModel.RefreshShortcuts();
        }
    }
}
