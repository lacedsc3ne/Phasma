using System.Windows.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PhasmaStrap.Utility
{
    public static class ProcessName
    {
        private const string LOG_IDENT = "ProcessName";

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static System.Windows.Window? _nameplate;

        public static void Set(string title)
        {
            if (_nameplate is not null)
                return;

            try
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    _nameplate = new System.Windows.Window
                    {
                        Title = title,
                        Width = 1,
                        Height = 1,
                        Left = -32000,
                        Top = -32000,
                        ShowInTaskbar = true,
                        ShowActivated = false,
                        WindowStyle = System.Windows.WindowStyle.ToolWindow,
                        IsHitTestVisible = false,
                    };

                    var hWnd = (HWND)new WindowInteropHelper(_nameplate).EnsureHandle();

                    int exStyle = PInvoke.GetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                    exStyle &= ~WS_EX_APPWINDOW;
                    PInvoke.SetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);

                    _nameplate.Show();
                });

                App.Logger.WriteLine(LOG_IDENT, $"This process shows as \"{title}\"");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not name this process: {ex.Message}");
            }
        }
    }
}
