using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HotkeysPage
    {
        public HotkeysPage()
        {
            DataContext = new HotkeysViewModel();
            InitializeComponent();
        }

        // Captures a key combination directly from the box the user clicked into, rather than
        // using a separate "record" button/dialog - matches the simplest common pattern for this
        // kind of control. Escape clears the binding; a lone modifier press is ignored (waits for
        // the real key); a key with no modifier at all is ignored too, so this can never bind a
        // single ordinary key as a system-wide hotkey.
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

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
                return;

            ModifierKeys modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None)
                return;

            string? gesture = new KeyGestureConverter().ConvertToString(new KeyGesture(key, modifiers));
            if (string.IsNullOrEmpty(gesture))
                return;

            row.GestureText = gesture;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is HotkeyRow row)
                row.GestureText = "";
        }
    }
}
