using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PhasmaStrap.UI.ViewModels.Dialogs;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class CaptureLibraryWindow
    {
        private const double CardWidth = 262;

        private static CaptureLibraryWindow? _open;

        private readonly CaptureLibraryViewModel _viewModel;

        private CaptureLibraryWindow(int tab)
        {
            _viewModel = new CaptureLibraryViewModel(tab) { Owner = this };
            DataContext = _viewModel;
            InitializeComponent();

            Closed += (_, _) =>
            {
                _viewModel.Dispose();
                if (ReferenceEquals(_open, this))
                    _open = null;
            };
        }

        public static void Open(int tab, Window? owner)
        {
            if (_open is not null)
            {
                _open._viewModel.Tab = tab;
                if (_open.WindowState == System.Windows.WindowState.Minimized)
                    _open.WindowState = System.Windows.WindowState.Normal;
                _open.Activate();
                return;
            }

            _open = new CaptureLibraryWindow(tab) { Owner = owner };
            _open.Show();
        }

        private void CardRows_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _viewModel.SetColumns((int)((CardRows.ActualWidth - 18) / CardWidth));
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            DependencyObject? node = sender as DependencyObject;
            while (node is not null && !(node is Border { Name: "Card" }))
                node = VisualTreeHelper.GetParent(node);

            if (node is Border { ContextMenu: System.Windows.Controls.ContextMenu menu } card)
            {
                menu.PlacementTarget = card;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        }
    }
}
