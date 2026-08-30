using PhasmaStrap.UI.ViewModels.ContextMenu;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    /// <summary>
    /// Interaction logic for ChatLogs.xaml
    /// </summary>
    public partial class ChatLogs
    {
        private readonly ChatLogsViewModel _viewModel;

        public ChatLogs()
        {
            _viewModel = new ChatLogsViewModel();
            _viewModel.RequestCloseEvent += OnRequestClose;

            DataContext = _viewModel;
            InitializeComponent();

            Closed += OnClosed;
        }

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
