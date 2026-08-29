using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace PhasmaStrap.Integrations.GameChat
{
    /// <summary>
    /// Installs a global WH_KEYBOARD_LL hook so the chat overlay can be toggled/typed into while Roblox
    /// (not this process) has focus. The hook is only ever installed while <see cref="SetEnabled"/> has
    /// been called with true (i.e. while in an active game session and the master GameChatEnabled setting
    /// is on), and is always uninstalled via <see cref="Dispose"/>. Every keystroke that isn't specifically
    /// consumed is passed on to <see cref="CallNextHookEx"/> so we never black-hole the user's input.
    /// </summary>
    public class GameChatKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_CAPITAL = 0x14;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private readonly GameChatOverlay _form;
        private readonly uint _robloxPid;
        private IntPtr _hook = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc;
        private IntPtr _formHandle;
        private bool _chatMode;
        private bool _keyboardEnabled;
        private bool _disposed;
        private readonly ConcurrentQueue<Action> _pendingInput = new();
        private int _pendingInputCount;
        private int _inputFlushScheduled;
        private const int MaxPendingInput = 128;
        [ThreadStatic] private static byte[]? _keyState;
        [ThreadStatic] private static StringBuilder? _keyBuffer;

        public GameChatKeyboardHook(GameChatOverlay form, uint robloxPid)
        {
            _form = form;
            _robloxPid = robloxPid;

            _form.ChatModeRequested += OnChatModeRequested;
            _form.ChatModeExited += OnChatModeExited;

            _hookProc = HookCallback;
        }

        public void SetEnabled(bool enabled)
        {
            if (_disposed || enabled == _keyboardEnabled)
                return;
            _keyboardEnabled = enabled;
            if (enabled)
                StartKeyboardHook();
            else
            {
                _chatMode = false;
                StopKeyboardHook();
            }
        }

        private void StartKeyboardHook()
        {
            if (_disposed || !_keyboardEnabled || _hook != IntPtr.Zero || _hookProc == null)
                return;

            using var currentProcess = Process.GetCurrentProcess();
            using var mainModule = currentProcess.MainModule;
            IntPtr moduleHandle = mainModule != null ? GetModuleHandle(mainModule.ModuleName) : IntPtr.Zero;

            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
            if (_hook == IntPtr.Zero)
                App.Logger.WriteLine("GameChatKeyboardHook", "Failed to install keyboard hook");
        }

        private void StopKeyboardHook()
        {
            if (_hook != IntPtr.Zero)
            {
                try
                {
                    UnhookWindowsHookEx(_hook);
                }
                catch
                {
                }
                _hook = IntPtr.Zero;
            }
        }

        private static bool IsNonTextKey(Keys key) =>
            key == Keys.Escape ||
            key == Keys.Enter ||
            key == Keys.Back ||
            key == Keys.ControlKey ||
            key == Keys.ShiftKey ||
            key == Keys.LWin || key == Keys.RWin ||
            key == Keys.Menu ||
            key == Keys.Left || key == Keys.Right ||
            key == Keys.Up || key == Keys.Down;

        private bool IsChatContextForeground()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;
            if (_formHandle == IntPtr.Zero)
                _formHandle = _form.WindowHandle;
            if (_formHandle != IntPtr.Zero && hwnd == _formHandle)
                return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == _robloxPid;
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0 || _disposed || !_keyboardEnabled)
                return CallNextHookEx(_hook, code, wParam, lParam);

            int msg = wParam.ToInt32();
            if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN)
                return CallNextHookEx(_hook, code, wParam, lParam);

            try
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = (Keys)kbd.vkCode;

                if (!_form.IsOverlayVisible)
                    return CallNextHookEx(_hook, code, wParam, lParam);

                if (!IsChatContextForeground())
                    return CallNextHookEx(_hook, code, wParam, lParam);

                bool isWinKey = key == Keys.LWin || key == Keys.RWin;
                bool isWinModifier = GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0;
                if (isWinKey || isWinModifier)
                    return CallNextHookEx(_hook, code, wParam, lParam);

                bool control = GetAsyncKeyState(VK_CONTROL) < 0;
                bool shift = GetAsyncKeyState(VK_SHIFT) < 0;
                bool alt = GetAsyncKeyState(VK_MENU) < 0;

                if (!_chatMode)
                {
                    if (control && shift && key == Keys.C)
                    {
                        QueueInput(_form.ToggleVisibility);
                        return (IntPtr)1;
                    }

                    if (_form.IsWindowHidden)
                        return CallNextHookEx(_hook, code, wParam, lParam);

                    if (key == Keys.OemQuestion)
                    {
                        _chatMode = true;
                        QueueInput(_form.StartChatMode);
                        return (IntPtr)1;
                    }
                    return CallNextHookEx(_hook, code, wParam, lParam);
                }

                if (control && key == Keys.V)
                {
                    QueueInput(_form.PasteFromClipboard);
                    return (IntPtr)1;
                }

                if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
                {
                    QueueInput(() => _form.HandleNavigation(key));
                    return (IntPtr)1;
                }

                if (key == Keys.Escape)
                {
                    _chatMode = false;
                    QueueInput(_form.CancelChatMode);
                    return CallNextHookEx(_hook, code, wParam, lParam);
                }

                if (key == Keys.Enter)
                {
                    _chatMode = false;
                    QueueInput(SendPendingMessage);
                    return (IntPtr)1;
                }

                if (key == Keys.Back)
                {
                    QueueInput(_form.Backspace);
                    return (IntPtr)1;
                }

                string? text = TranslateKey(key, kbd.scanCode, control, shift, alt);
                if (!string.IsNullOrEmpty(text))
                {
                    QueueInput(() => _form.AppendTextFromKey(text));
                    return (IntPtr)1;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GameChatKeyboardHook", "Hook error: " + ex.Message);
            }

            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void QueueInput(Action action)
        {
            if (_disposed || _form.Dispatcher.HasShutdownStarted || _form.Dispatcher.HasShutdownFinished)
                return;
            _pendingInput.Enqueue(action);
            int count = Interlocked.Increment(ref _pendingInputCount);
            while (count > MaxPendingInput && _pendingInput.TryDequeue(out _))
                count = Interlocked.Decrement(ref _pendingInputCount);
            if (Interlocked.Exchange(ref _inputFlushScheduled, 1) != 0)
                return;
            try
            {
                _form.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FlushInput));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _inputFlushScheduled, 0);
            }
        }

        private void FlushInput()
        {
            int processed = 0;
            while (!_disposed && processed < 32 && _pendingInput.TryDequeue(out Action? action))
            {
                Interlocked.Decrement(ref _pendingInputCount);
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("GameChatKeyboardHook", "Input action failed: " + ex.Message);
                }
                processed++;
            }
            Interlocked.Exchange(ref _inputFlushScheduled, 0);
            if (!_pendingInput.IsEmpty)
                QueueInputFlush();
        }

        private void QueueInputFlush()
        {
            if (_disposed || Interlocked.Exchange(ref _inputFlushScheduled, 1) != 0)
                return;
            try
            {
                _form.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FlushInput));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _inputFlushScheduled, 0);
            }
        }

        private void SendPendingMessage()
        {
            _ = _form.Send();
        }

        private static string? TranslateKey(Keys key, uint scanCode, bool control, bool shift, bool alt)
        {
            if ((control || alt) && !(control && alt))
                return null;

            if (IsNonTextKey(key))
                return null;

            byte[] state = _keyState ??= new byte[256];
            Array.Clear(state, 0, state.Length);
            if (shift)
                state[VK_SHIFT] = 0x80;
            if (control)
                state[VK_CONTROL] = 0x80;
            if (alt)
                state[VK_MENU] = 0x80;
            if ((GetKeyState(VK_CAPITAL) & 1) != 0)
                state[VK_CAPITAL] = 0x01;

            StringBuilder sb = _keyBuffer ??= new StringBuilder(8);
            sb.Clear();
            IntPtr layout = GetKeyboardLayout(0);
            int result = ToUnicodeEx((uint)key, scanCode, state, sb, sb.Capacity, 0, layout);
            return result > 0 ? sb.ToString() : null;
        }

        public void ResetChatMode()
        {
            _chatMode = false;
        }

        private void OnChatModeRequested(object? sender, EventArgs e)
        {
            _chatMode = true;
        }

        private void OnChatModeExited(object? sender, EventArgs e)
        {
            _chatMode = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _form.ChatModeRequested -= OnChatModeRequested;
            _form.ChatModeExited -= OnChatModeExited;
            _keyboardEnabled = false;
            while (_pendingInput.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref _pendingInputCount, 0);
            Interlocked.Exchange(ref _inputFlushScheduled, 0);
            StopKeyboardHook();
            _hookProc = null;
            GC.SuppressFinalize(this);
        }
    }
}
