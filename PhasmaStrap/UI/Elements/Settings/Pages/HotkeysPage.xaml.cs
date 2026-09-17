using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Settings;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HotkeysPage
    {
        public HotkeysPage()
        {
            DataContext = new HotkeysViewModel();
            InitializeComponent();
        }

        // Captures a key (or key combination) directly from the box the user clicked into.
        // Escape clears the binding; a lone modifier press waits for the real key. A key on its
        // own (F9, Numpad 5, Pause...) is a valid binding - the settings window's own shortcuts
        // (Ctrl+F for search etc.) are suppressed while one of these boxes has focus, see
        // MainWindow.MainWindow_SearchShortcut.
        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (sender is not FrameworkElement element || element.Tag is not HotkeyRow row)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                row.GestureText = "";
                return;
            }

            if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
            {
                // let Tab keep moving focus
                e.Handled = false;
                return;
            }

            if (HotkeyGesture.IsModifierKey(key) || key == Key.None || key == Key.ImeProcessed || key == Key.DeadCharProcessed)
                return;

            row.GestureText = HotkeyGesture.Format(Keyboard.Modifiers, key);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is HotkeyRow row)
                row.GestureText = "";
        }
    }
}
