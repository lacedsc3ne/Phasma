using System.Drawing;
using System.Drawing.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PhasmaStrap.Utility
{
    // A plain GDI screen-region capture of the live Roblox window (Graphics.CopyFromScreen over
    // its GetWindowRect bounds) - deliberately NOT built on OverlayCompositor's DXGI desktop-
    // duplication pipeline, since that one only exists while overlays are enabled during active
    // gameplay (OverlaySettings.AnyEnabled) and is tightly coupled to its own device/swapchain
    // lifecycle. A screenshot should work any time Roblox is running, overlay or not, so this is
    // its own small, independent capture path. Window-finding mirrors
    // FakeExclusiveFullscreen.FindRobloxWindow's existing pattern.
    public static class ScreenshotCapture
    {
        private const string LOG_IDENT = "ScreenshotCapture";

        public static string ScreenshotsDir => Path.Combine(Paths.Base, "Screenshots");

        public static string? Capture()
        {
            HWND hwnd = FindRobloxWindow();
            if (hwnd.IsNull)
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox window not found - is it running?");
                return null;
            }

            if (!PInvoke.GetWindowRect(hwnd, out RECT rect))
                return null;

            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;

            if (width <= 0 || height <= 0)
                return null;

            try
            {
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(rect.left, rect.top, 0, 0, new Size(width, height));

                Directory.CreateDirectory(ScreenshotsDir);

                string path = Path.Combine(ScreenshotsDir, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bitmap.Save(path, ImageFormat.Png);

                App.Logger.WriteLine(LOG_IDENT, $"Saved to {path}");
                return path;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Capture failed: {ex.Message}");
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
