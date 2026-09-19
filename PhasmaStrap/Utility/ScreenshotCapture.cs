using System.Drawing;
using System.Drawing.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace PhasmaStrap.Utility
{
    // Captures the live Roblox window's actual content via PrintWindow(PW_RENDERFULLCONTENT) -
    // NOT Graphics.CopyFromScreen, which was this file's first version and turned out to just
    // capture whatever happens to be on top of that screen region at the moment. That's fine if
    // Roblox is the foreground window, but the common real case - clicking "Take Screenshot Now"
    // from Settings while alt-tabbed away from the game, or a hotkey press caught mid-alt-tab -
    // means Settings (or whatever else is on top) is what's actually visible there, and that's
    // what CopyFromScreen would grab instead of the game. PrintWindow asks the target window to
    // render ITSELF into a device context directly, independent of what's currently on screen or
    // which window has focus. PW_RENDERFULLCONTENT (Windows 8.1+) is specifically what makes this
    // work for DirectX-rendered windows like Roblox - without it, PrintWindow on a GPU-rendered
    // window typically comes back blank/black. Deliberately NOT built on OverlayCompositor's DXGI
    // desktop-duplication pipeline, since that one only exists while overlays are enabled during
    // active gameplay and is tightly coupled to its own device/swapchain lifecycle - a screenshot
    // should work any time Roblox is running, overlay or not. Window-finding mirrors
    // FakeExclusiveFullscreen.FindRobloxWindow's existing pattern.
    public static class ScreenshotCapture
    {
        private const string LOG_IDENT = "ScreenshotCapture";

        public static string ScreenshotsDir => Path.Combine(Paths.Base, "Screenshots");

        public static string? Capture()
        {
            using Bitmap? bitmap = Grab(out _);
            return bitmap is null ? null : Save(bitmap);
        }

        // saves a picture into the screenshots folder
        public static string? Save(Bitmap bitmap)
        {
            try
            {
                Directory.CreateDirectory(ScreenshotsDir);

                string path = Path.Combine(ScreenshotsDir, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                for (int n = 2; File.Exists(path); n++)
                    path = Path.Combine(ScreenshotsDir, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}_{n}.png");

                bitmap.Save(path, ImageFormat.Png);

                App.Logger.WriteLine(LOG_IDENT, $"Saved to {path}");
                return path;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save failed: {ex.Message}");
                return null;
            }
        }

        // the Roblox window's picture as it is now, and where the window is on screen (pixels)
        public static Bitmap? Grab(out Rectangle screenRect)
        {
            screenRect = Rectangle.Empty;
            HWND hwnd = FindRobloxWindow();
            if (hwnd.IsNull)
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox window not found - is it running?");
                return null;
            }

            if (PInvoke.IsIconic(hwnd))
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox window is minimized, cannot capture its content");
                return null;
            }

            if (!PInvoke.GetWindowRect(hwnd, out RECT rect))
                return null;

            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;

            if (width <= 0 || height <= 0)
                return null;

            screenRect = new Rectangle(rect.left, rect.top, width, height);

            Bitmap? bitmap = null;
            try
            {
                bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                bool ok;

                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        // PW_RENDERFULLCONTENT (0x2) - the flag that makes this work for DirectX-
                        // rendered windows like Roblox. Not a named member of this CsWin32 version's
                        // PRINT_WINDOW_FLAGS (it only defines PW_CLIENTONLY), but it's a real,
                        // documented Win32 constant since Windows 8.1 and perfectly valid to pass as
                        // an unnamed enum value.
                        ok = PInvoke.PrintWindow(hwnd, new HDC(hdc), (PRINT_WINDOW_FLAGS)2);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                if (!ok)
                {
                    App.Logger.WriteLine(LOG_IDENT, "PrintWindow failed");
                    bitmap.Dispose();
                    return null;
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Capture failed: {ex.Message}");
                bitmap?.Dispose();
                return null;
            }
        }

        private static HWND FindRobloxWindow()
        {
            Process[] processes = Process.GetProcessesByName(App.RobloxPlayerAppName);

            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return (HWND)process.MainWindowHandle;
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }

            return HWND.Null;
        }
    }
}
