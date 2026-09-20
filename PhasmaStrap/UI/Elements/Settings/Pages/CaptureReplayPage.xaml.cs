using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class CaptureReplayPage
    {
        private readonly CaptureViewModel _viewModel = CaptureViewModel.Shared;
        private readonly HotkeyCaptureSession _hotkeys;

        public CaptureReplayPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            _hotkeys = new HotkeyCaptureSession(this, HotkeysViewModel.Shared.Hotkeys.ToList());

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.RefreshHotkeyHint();
            _hotkeys.Attach();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => _hotkeys.Detach();

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is HotkeyRow row)
                _hotkeys.Toggle(row);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is HotkeyRow row)
                _hotkeys.Clear(row);
        }
    }
}
