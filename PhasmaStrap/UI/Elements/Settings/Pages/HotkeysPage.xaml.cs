using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using PhasmaStrap.UI.ViewModels.Settings;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class HotkeysPage
    {
        private const int VK_ESCAPE = 0x1B;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly HotkeysViewModel _viewModel = HotkeysViewModel.Shared;
        private readonly LowLevelKeyboardHook _hook;
        private readonly DispatcherTimer _listenTimeout;
        private readonly DispatcherTimer _drainTimeout;
        private readonly HashSet<int> _modifiersDown = new();

        private HotkeyRow? _listening;
        private int _drainKey;
        private Window? _window;

        public HotkeysPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            _hook = new LowLevelKeyboardHook(OnKey);

            _listenTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _listenTimeout.Tick += (_, _) => StopListening("Cancelled - no key was pressed.");

            _drainTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _drainTimeout.Tick += (_, _) => EndDrain();

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _window = Window.GetWindow(this);
            if (_window is not null)
            {
                _window.Deactivated += Window_Deactivated;
                _window.PreviewMouseDown += Window_PreviewMouseDown;
                _window.Closing += Window_Closing;
            }

            foreach (HotkeyRow row in _viewModel.Hotkeys)
                DescribeBinding(row, null);

            _viewModel.Refresh();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopListening(null);
            EndDrain();

            if (_window is not null)
            {
                _window.Deactivated -= Window_Deactivated;
                _window.PreviewMouseDown -= Window_PreviewMouseDown;
                _window.Closing -= Window_Closing;
                _window = null;
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e) => StopListening(null);

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopListening(null);
            EndDrain();
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_listening is null)
                return;

            for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null; node = GetParent(node))
            {
                if (node is ButtonBase button && button.Uid == "Capture" && ReferenceEquals(button.Tag, _listening))
                    return;
            }

            StopListening(null);
        }

        private static DependencyObject? GetParent(DependencyObject node) =>
            node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not HotkeyRow row)
                return;

            if (ReferenceEquals(row, _listening))
            {
                StopListening(null);
                return;
            }

            StopListening(null);
            EndDrain();

            _modifiersDown.Clear();

            if (!_hook.Install())
            {
                row.SetStatus("Couldn't start listening for keys. Try again, or restart PhasmaStrap.", warning: true);
                return;
            }

            _listening = row;
            row.SetStatus("");
            row.IsListening = true;
            _listenTimeout.Start();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            StopListening(null);

            if (sender is FrameworkElement element && element.Tag is HotkeyRow row)
            {
                row.GestureText = "";
                row.SetStatus("");
            }

            _viewModel.RefreshKeyboard();
        }

        private void StopListening(string? message)
        {
            HotkeyRow? row = _listening;
            _listening = null;

            if (row is not null)
                FinishListening(row, message);
            else if (_drainKey == 0)
                _hook.Uninstall();
        }

        private void FinishListening(HotkeyRow row, string? message)
        {
            _listenTimeout.Stop();
            _modifiersDown.Clear();

            if (_listening is null && _drainKey == 0)
                _hook.Uninstall();

            row.IsListening = false;
            DescribeBinding(row, message);
        }

        private void BeginDrain(int virtualKey)
        {
            _drainKey = virtualKey;
            _drainTimeout.Stop();
            _drainTimeout.Start();
        }

        private void EndDrain()
        {
            _drainTimeout.Stop();
            _drainKey = 0;

            if (_listening is null)
                _hook.Uninstall();
        }

        private bool OnKey(int vk, bool isDown)
        {
            bool isModifier = LowLevelKeyboardHook.IsModifier(vk);

            if (_listening is null)
            {
                if (_drainKey == 0)
                    return false;

                if (!isDown && vk == _drainKey)
                    Dispatcher.BeginInvoke(new Action(EndDrain));

                return isDown || !isModifier;
            }

            if (isModifier)
            {
                if (isDown)
                    _modifiersDown.Add(vk);
                else
                    _modifiersDown.Remove(vk);

                _listening.HeldModifiers = HeldModifiers();
                return isDown;
            }

            if (!isDown)
                return true;

            HotkeyRow row = _listening;
            ModifierKeys modifiers = HeldModifiers();

            _listening = null;
            BeginDrain(vk);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (vk == VK_ESCAPE && modifiers == ModifierKeys.None)
                    FinishListening(row, null);
                else
                    Commit(row, modifiers, vk);
            }));

            return true;
        }

        private ModifierKeys HeldModifiers()
        {
            ModifierKeys modifiers = ModifierKeys.None;

            foreach (int vk in _modifiersDown)
            {
                modifiers |= vk switch
                {
                    LowLevelKeyboardHook.VK_CONTROL or LowLevelKeyboardHook.VK_LCONTROL or LowLevelKeyboardHook.VK_RCONTROL => ModifierKeys.Control,
                    LowLevelKeyboardHook.VK_MENU or LowLevelKeyboardHook.VK_LMENU or LowLevelKeyboardHook.VK_RMENU => ModifierKeys.Alt,
                    LowLevelKeyboardHook.VK_SHIFT or LowLevelKeyboardHook.VK_LSHIFT or LowLevelKeyboardHook.VK_RSHIFT => ModifierKeys.Shift,
                    _ => ModifierKeys.Windows,
                };
            }

            return modifiers;
        }

        private void Commit(HotkeyRow row, ModifierKeys modifiers, int vk)
        {
            if (!HotkeyGesture.TryFromVirtualKey(modifiers, vk, out string text))
            {
                FinishListening(row, null);
                row.SetStatus("That key can't be used as a hotkey - the previous one was kept.", warning: true);
                return;
            }

            string? movedFrom = null;
            foreach (HotkeyRow other in _viewModel.Hotkeys)
            {
                if (ReferenceEquals(other, row) || !string.Equals(other.GestureText, text, StringComparison.OrdinalIgnoreCase))
                    continue;

                other.GestureText = "";
                other.SetStatus($"Cleared - {HotkeyGesture.ToDisplay(text)} is now used by \"{row.DisplayName}\".");
                movedFrom = other.DisplayName;
            }

            row.GestureText = text;

            FinishListening(row, movedFrom is null ? null : $"Moved here from \"{movedFrom}\".");

            _viewModel.RefreshKeyboard();
        }

        private static void DescribeBinding(HotkeyRow row, string? prefix)
        {
            if (!HotkeyGesture.TryParse(row.GestureText, out ModifierKeys modifiers, out Key key))
            {
                row.SetStatus(prefix ?? "");
                return;
            }

            string lead = string.IsNullOrEmpty(prefix) ? "" : prefix + " ";

            if (HotkeyGesture.IsTypingGesture(modifiers, key))
            {
                row.SetStatus(lead + "This is a typing key, so it only fires while Roblox is the active window and it still types in chat. Add Ctrl or Alt, or use an F-key, if you don't want it going off while you chat.", warning: true);
                return;
            }

            if (IsRegisteredElsewhere(modifiers, key))
            {
                row.SetStatus(lead + "Another program has registered this shortcut too. PhasmaStrap takes it over while a game is running, so the other program won't react to it then.");
                return;
            }

            row.SetStatus(prefix ?? "");
        }

        private static bool IsRegisteredElsewhere(ModifierKeys modifiers, Key key)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0)
                return false;

            uint native = MOD_NOREPEAT;
            if ((modifiers & ModifierKeys.Alt) != 0) native |= 0x0001;
            if ((modifiers & ModifierKeys.Control) != 0) native |= 0x0002;
            if ((modifiers & ModifierKeys.Shift) != 0) native |= 0x0004;
            if ((modifiers & ModifierKeys.Windows) != 0) native |= 0x0008;

            const int probeId = 0x7A11;

            if (!RegisterHotKey(IntPtr.Zero, probeId, native, (uint)vk))
                return true;

            UnregisterHotKey(IntPtr.Zero, probeId);
            return false;
        }
    }
}
