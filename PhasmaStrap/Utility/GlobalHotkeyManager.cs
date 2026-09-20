using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PhasmaStrap.Utility
{
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private const string LOG_IDENT = "GlobalHotkeyManager";

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private sealed class Binding
        {
            public string ActionId = "";
            public string GestureText = "";
            public ModifierKeys Modifiers;
            public int VirtualKey;
            public bool IsTypingKey;
            public Action Callback = () => { };
        }

        private readonly Dictionary<string, Action> _actions = new();
        private readonly LowLevelKeyboardHook _hook;
        private readonly System.Threading.Timer _keepAlive;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        private volatile Binding[] _bindings = Array.Empty<Binding>();

        private readonly Dictionary<int, bool> _heldSwallowed = new();

        private bool _disposed;

        public GlobalHotkeyManager()
        {
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _hook = new LowLevelKeyboardHook(OnKey);

            _keepAlive = new System.Threading.Timer(_ => _dispatcher.BeginInvoke(new Action(Reinstall)), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        }

        public void RegisterAction(string actionId, Action callback) => _actions[actionId] = callback;

        public void ApplyBindings()
        {
            if (_disposed)
                return;

            var list = new List<Binding>();

            foreach (var (actionId, callback) in _actions)
            {
                if (!App.Settings.Prop.HotkeyBindings.TryGetValue(actionId, out string? text) || string.IsNullOrWhiteSpace(text))
                    continue;

                if (!HotkeyGesture.TryParse(text, out ModifierKeys modifiers, out Key key))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not parse hotkey '{text}' for '{actionId}'");
                    continue;
                }

                int vk = KeyInterop.VirtualKeyFromKey(key);
                if (vk == 0)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Hotkey '{text}' for '{actionId}' has no virtual key");
                    continue;
                }

                list.Add(new Binding
                {
                    ActionId = actionId,
                    GestureText = text,
                    Modifiers = modifiers,
                    VirtualKey = vk,
                    IsTypingKey = HotkeyGesture.IsTypingGesture(modifiers, key),
                    Callback = callback,
                });
            }

            list.Sort((a, b) => CountModifiers(b.Modifiers).CompareTo(CountModifiers(a.Modifiers)));
            _bindings = list.ToArray();

            if (list.Count == 0)
            {
                _hook.Uninstall();
                App.Logger.WriteLine(LOG_IDENT, "No hotkeys bound - listener off");
                return;
            }

            if (!_hook.Install())
                App.Logger.WriteLine(LOG_IDENT, $"Could not install the keyboard hook (error {Marshal.GetLastWin32Error()}) - hotkeys will not work");
            else
                App.Logger.WriteLine(LOG_IDENT, "Listening for: " + string.Join(", ", list.Select(b => $"{b.GestureText} -> {b.ActionId}{(b.IsTypingKey ? " (typing key, Roblox window only)" : "")}")));
        }

        private void Reinstall()
        {
            if (_disposed || _bindings.Length == 0)
                return;

            _hook.Uninstall();
            _hook.Install();
        }

        private bool OnKey(int vk, bool isDown)
        {
            if (LowLevelKeyboardHook.IsModifier(vk))
                return false;

            if (!isDown)
                return _heldSwallowed.Remove(vk, out bool wasSwallowed) && wasSwallowed;

            if (_heldSwallowed.TryGetValue(vk, out bool repeatSwallowed))
                return repeatSwallowed;

            Binding[] bindings = _bindings;
            if (bindings.Length == 0)
                return false;

            ModifierKeys held = CurrentModifiers();

            foreach (Binding binding in bindings)
            {
                if (binding.VirtualKey != vk || (held & binding.Modifiers) != binding.Modifiers)
                    continue;

                if (((held & ~binding.Modifiers) & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
                    continue;

                if (binding.IsTypingKey)
                {
                    if ((held & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
                        continue;

                    if (!IsRobloxForeground())
                        return false;
                }

                bool swallow = !binding.IsTypingKey;
                _heldSwallowed[vk] = swallow;

                Fire(binding);
                return swallow;
            }

            return false;
        }

        private void Fire(Binding binding)
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                App.Logger.WriteLine(LOG_IDENT, $"'{binding.ActionId}' pressed ({binding.GestureText})");

                try
                {
                    binding.Callback();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Action '{binding.ActionId}' threw: {ex.Message}");
                }
            }));
        }

        private static ModifierKeys CurrentModifiers()
        {
            static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

            ModifierKeys modifiers = ModifierKeys.None;
            if (Down(LowLevelKeyboardHook.VK_CONTROL)) modifiers |= ModifierKeys.Control;
            if (Down(LowLevelKeyboardHook.VK_MENU)) modifiers |= ModifierKeys.Alt;
            if (Down(LowLevelKeyboardHook.VK_SHIFT)) modifiers |= ModifierKeys.Shift;
            if (Down(LowLevelKeyboardHook.VK_LWIN) || Down(LowLevelKeyboardHook.VK_RWIN)) modifiers |= ModifierKeys.Windows;
            return modifiers;
        }

        private static int CountModifiers(ModifierKeys modifiers)
        {
            int count = 0;
            for (int bits = (int)modifiers; bits != 0; bits &= bits - 1)
                count++;
            return count;
        }

        private uint _foregroundPid;
        private bool _foregroundIsRoblox;

        private bool IsRobloxForeground()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _foregroundPid)
                return _foregroundIsRoblox;

            bool isRoblox = false;

            try
            {
                using Process process = Process.GetProcessById((int)pid);
                isRoblox = string.Equals(process.ProcessName, App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }

            _foregroundPid = pid;
            _foregroundIsRoblox = isRoblox;
            return isRoblox;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _keepAlive.Dispose();
            _bindings = Array.Empty<Binding>();

            if (_dispatcher.CheckAccess())
                _hook.Uninstall();
            else
                _dispatcher.BeginInvoke(new Action(_hook.Uninstall));

            GC.SuppressFinalize(this);
        }
    }
}
