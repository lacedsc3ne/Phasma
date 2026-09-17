using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;

namespace PhasmaStrap.Utility
{
    // System-wide hotkey listener, one instance owned by Watcher for the lifetime of a game
    // session (mirrors how GameChatKeyboardHook.cs owns its own global input hook). Uses
    // RegisterHotKey/WM_HOTKEY via a hidden message-only window rather than a low-level keyboard
    // hook like GameChat's, since these are simple modifier+key shortcuts, not free-form typing
    // capture - RegisterHotKey also gets us "this combo is already claimed by another app"
    // detection for free, which a raw hook wouldn't.
    //
    // Bindings themselves live in Settings.Prop.HotkeyBindings (actionId -> gesture string, e.g.
    // "Ctrl+Alt+R"), edited from HotkeysPage in the Settings process. This class only ever runs
    // in the Bootstrapper/Watcher process, since Settings has no live game session to act on.
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyIdBase = 0xB000;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private sealed class MessageWindow : NativeWindow
        {
            public event Action<int>? HotkeyPressed;

            public MessageWindow() => CreateHandle(new CreateParams());

            protected override void WndProc(ref System.Windows.Forms.Message m)
            {
                if (m.Msg == WM_HOTKEY)
                    HotkeyPressed?.Invoke(m.WParam.ToInt32());

                base.WndProc(ref m);
            }
        }

        private readonly MessageWindow _window = new();
        private readonly Dictionary<string, Action> _actions = new();
        private readonly Dictionary<int, (string ActionId, Action Callback)> _registered = new();
        private bool _disposed;

        public GlobalHotkeyManager()
        {
            _window.HotkeyPressed += OnHotkeyPressed;
        }

        public void RegisterAction(string actionId, Action callback) => _actions[actionId] = callback;

        // (re)reads Settings.Prop.HotkeyBindings and registers every bound action against the OS.
        // Safe to call again after a binding changes - unregisters everything first.
        public void ApplyBindings()
        {
            const string LOG_IDENT = "GlobalHotkeyManager::ApplyBindings";

            foreach (int id in _registered.Keys)
                UnregisterHotKey(_window.Handle, id);
            _registered.Clear();

            var bindings = App.Settings.Prop.HotkeyBindings;
            int nextId = HotkeyIdBase;

            foreach (var (actionId, callback) in _actions)
            {
                if (!bindings.TryGetValue(actionId, out string? gestureText) || string.IsNullOrWhiteSpace(gestureText))
                    continue;

                if (!TryParseGesture(gestureText, out uint modifiers, out uint vk))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not parse hotkey '{gestureText}' for '{actionId}'");
                    continue;
                }

                int id = nextId++;

                if (RegisterHotKey(_window.Handle, id, modifiers, vk))
                {
                    _registered[id] = (actionId, callback);
                    App.Logger.WriteLine(LOG_IDENT, $"Registered '{gestureText}' for '{actionId}'");
                }
                else
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to register '{gestureText}' for '{actionId}' - likely already bound by another app");
            }
        }

        private void OnHotkeyPressed(int id)
        {
            const string LOG_IDENT = "GlobalHotkeyManager::OnHotkeyPressed";

            if (!_registered.TryGetValue(id, out var entry))
                return;

            App.Logger.WriteLine(LOG_IDENT, $"'{entry.ActionId}' pressed");

            try
            {
                entry.Callback();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Action '{entry.ActionId}' threw: {ex.Message}");
            }
        }

        // Parses the same "Ctrl+Alt+R" / "F9" gesture text HotkeysPage displays/captures (see
        // HotkeyGesture). A key with no modifier is a valid binding.
        public static bool TryParseGesture(string text, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;

            if (!HotkeyGesture.TryParse(text, out ModifierKeys mods, out Key key))
                return false;

            if ((mods & ModifierKeys.Alt) != 0) modifiers |= MOD_ALT;
            if ((mods & ModifierKeys.Control) != 0) modifiers |= MOD_CONTROL;
            if ((mods & ModifierKeys.Shift) != 0) modifiers |= MOD_SHIFT;
            if ((mods & ModifierKeys.Windows) != 0) modifiers |= MOD_WIN;

            // don't auto-repeat while the key is held - one press, one action
            modifiers |= MOD_NOREPEAT;

            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (int id in _registered.Keys)
                UnregisterHotKey(_window.Handle, id);

            _registered.Clear();
            _window.HotkeyPressed -= OnHotkeyPressed;
            _window.DestroyHandle();

            GC.SuppressFinalize(this);
        }
    }
}
