using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Utility
{
    public static class ClipThumbnails
    {
        private const string LOG_IDENT = "ClipThumbnails";
        private const int Size = 256;

        private static readonly BlockingCollection<(string Path, Action<BitmapSource?> Done)> _queue = new();
        private static Thread? _worker;
        private static readonly object _lock = new();

        public static void Request(string path, Action<BitmapSource?> done)
        {
            lock (_lock)
            {
                if (_worker is null)
                {
                    _worker = new Thread(Run) { IsBackground = true, Name = "ClipThumbnails", Priority = ThreadPriority.BelowNormal };
                    _worker.SetApartmentState(ApartmentState.STA);
                    _worker.Start();
                }
            }

            _queue.Add((path, done));
        }

        private static void Run()
        {
            foreach (var (path, done) in _queue.GetConsumingEnumerable())
            {
                BitmapSource? image = null;
                try
                {
                    image = Load(path);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"No thumbnail for {Path.GetFileName(path)}: {ex.Message}");
                }

                try
                {
                    done(image);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Thumbnail callback failed: {ex.Message}");
                }
            }
        }

        private static BitmapSource? Load(string path)
        {
            if (!File.Exists(path))
                return null;

            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                var gif = new BitmapImage();
                gif.BeginInit();
                gif.CacheOption = BitmapCacheOption.OnLoad;
                gif.DecodePixelWidth = Size;
                gif.UriSource = new Uri(path, UriKind.Absolute);
                gif.EndInit();
                gif.Freeze();
                return gif;
            }

            return FromShell(path) ?? FromClip(path);
        }

        private static BitmapSource? FromShell(string path)
        {
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out IShellItemImageFactory factory);
                try
                {
                    if (factory.GetImage(new SIZE { cx = Size, cy = Size }, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out IntPtr hbitmap) != 0 || hbitmap == IntPtr.Zero)
                        return null;

                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                        var opaque = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgr32, null, 0);
                        opaque.Freeze();
                        return opaque;
                    }
                    finally
                    {
                        DeleteObject(hbitmap);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BitmapSource? FromClip(string path)
        {
            List<ClipProcessor.Thumbnail> frames = ClipProcessor.GrabThumbnails(path, 2, Size);
            if (frames.Count == 0)
                return null;

            ClipProcessor.Thumbnail frame = frames[^1];
            var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, frame.Bgra, frame.Width * 4);
            source.Freeze();
            return source;
        }

        private const int SIIGBF_BIGGERSIZEOK = 0x1;
        private const int SIIGBF_THUMBNAILONLY = 0x8;

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory item);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
