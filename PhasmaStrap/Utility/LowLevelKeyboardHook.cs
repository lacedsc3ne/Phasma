using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// A thin WH_KEYBOARD_LL wrapper shared by the hotkey listener (game-session process) and the
    /// hotkey capture box (Settings process). The handler runs on the thread that installed the
    /// hook, which must pump messages - both callers install from their UI thread.
    ///
    /// Low-level hooks see a key before RegisterHotKey-style hotkeys and before the focused
    /// application, and can swallow it. The handler has to return quickly: Windows silently drops
    /// a hook whose callback overruns LowLevelHooksTimeout, so do real work elsewhere.
    /// </summary>
    public sealed class LowLevelKeyboardHook : IDisposable
    {
        public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        public const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;

        // return true to swallow the key
        public delegate bool KeyHandler(int virtualKey, bool isDown);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private readonly KeyHandler _handler;
        private readonly HookProc _proc; // kept in a field: the native side holds the only other reference
        private IntPtr _hook;

        public bool IsInstalled => _hook != IntPtr.Zero;

        public LowLevelKeyboardHook(KeyHandler handler)
        {
            _handler = handler;
            _proc = Callback;
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero)
                return true;

            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            return _hook != IntPtr.Zero;
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int message = wParam.ToInt32();
                    bool isDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;

                    if (_handler((int)data.vkCode, isDown))
                        return (IntPtr)1;
                }
                catch
                {
                    // never let an exception escape into the hook chain - the key just passes through
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public static bool IsModifier(int vk) =>
            vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

        public void Dispose() => Uninstall();
    }
}
