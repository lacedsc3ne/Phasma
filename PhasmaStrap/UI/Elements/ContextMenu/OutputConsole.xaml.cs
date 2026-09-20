using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.Integrations;
using PhasmaStrap.UI.ViewModels.ContextMenu;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public partial class OutputConsole
    {
        private readonly OutputConsoleViewModel _viewModel;

        public OutputConsole(ActivityWatcher watcher)
        {
            _viewModel = new OutputConsoleViewModel(watcher);
            _viewModel.RequestCloseEvent += OnRequestClose;

            DataContext = _viewModel;
            InitializeComponent();

            Closed += OnClosed;
        }

        private void ConsoleTextBox_TextChanged(object sender, TextChangedEventArgs e) => ConsoleTextBox.ScrollToEnd();

        private void OnRequestClose(object? sender, EventArgs e) => Close();

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;
            _viewModel.RequestCloseEvent -= OnRequestClose;
            _viewModel.Dispose();
            DataContext = null;
        }
    }
}
