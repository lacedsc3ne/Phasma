using PhasmaStrap.Integrations;
using PhasmaStrap.UI.ViewModels.ContextMenu;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    /// <summary>
    /// Interaction logic for RPCWindow.xaml
    /// </summary>
    public partial class RPCWindow
    {
        private readonly RPCWindowViewModel _viewModel;

        public RPCWindow(DiscordRichPresence richPresence)
        {
            _viewModel = new RPCWindowViewModel(richPresence);
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
