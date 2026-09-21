namespace PhasmaStrap.Utility
{
    public static class ProcessName
    {
        private const string LOG_IDENT = "ProcessName";

        private static System.Windows.Window? _nameplate;

        // Every PhasmaStrap process is the same exe, so Task Manager shows them all as
        // PhasmaStrap with nothing to tell them apart. It does show a window title though,
        // so a background process gets one tiny off-screen tool window saying what it is.
        // A tool window is kept out of the taskbar and out of Alt+Tab, and being unowned is
        // what makes Windows report it as this process's main window.
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
                        WindowStyle = System.Windows.WindowStyle.ToolWindow,
                        IsHitTestVisible = false,
                    };

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
