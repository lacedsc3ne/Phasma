using System.ComponentModel;
using System.Windows;

using PhasmaStrap.UI.ViewModels.Dialogs;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class CrosshairEditorWindow
    {
        private readonly CrosshairEditorViewModel _viewModel = new();
        private bool _applied;

        public bool Applied => _applied;

        public CrosshairEditorWindow()
        {
            DataContext = _viewModel;
            _viewModel.Applied += () => _applied = true;

            InitializeComponent();
            Closing += OnClosing;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_viewModel.HasChanges)
                return;

            MessageBoxResult answer = Frontend.ShowMessageBox("Use this crosshair?\n\nYes applies it, No closes the editor without changing anything.", MessageBoxImage.Question, MessageBoxButton.YesNoCancel);

            if (answer == MessageBoxResult.Cancel)
                e.Cancel = true;
            else if (answer == MessageBoxResult.Yes)
                _viewModel.Apply();
        }
    }
}
