using System.Drawing;
using System.Drawing.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace PhasmaStrap.Utility
{
    public static class ScreenshotCapture
    {
        private const string LOG_IDENT = "ScreenshotCapture";

        public static string ScreenshotsDir => Path.Combine(Paths.Base, "Screenshots");

        public static string? Capture()
        {
            using Bitmap? bitmap = Grab(out _);
            return bitmap is null ? null : Save(bitmap);
        }

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
