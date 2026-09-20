using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;

using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace PhasmaStrap.Utility
{
    public static class ClipboardShare
    {
        public static Action<string>? Log;

        public static bool CopyImageFile(string path) => Run(() =>
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { path });

            BitmapSource? bitmap = LoadBitmap(path);
            if (bitmap is not null)
                data.SetImage(bitmap);

            return data;
        }, path);

        public static bool CopyFile(string path) => Run(() =>
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { path });
            return data;
        }, path);

        public static bool CopyText(string text) => Run(() =>
        {
            var data = new DataObject();
            data.SetText(text);
            return data;
        }, null);

        private static BitmapSource? LoadBitmap(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not decode '{path}' for the clipboard, offering it as a file only: {ex.Message}");
                return null;
            }
        }

        private static bool Run(Func<DataObject> build, string? requiredFile)
        {
            if (requiredFile is not null && !File.Exists(requiredFile))
                return false;

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return Set(build);

            bool result = false;
            var thread = new Thread(() => result = Set(build)) { IsBackground = true, Name = "ClipboardShare" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
            return result;
        }

        private static bool Set(Func<DataObject> build)
        {
            try
            {
                DataObject data = build();

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Clipboard.SetDataObject(data, true);
                        return true;
                    }
                    catch (System.Runtime.InteropServices.COMException) when (attempt < 8)
                    {
                        Thread.Sleep(60);
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not write to the clipboard: {ex.Message}");
                return false;
            }
        }
    }
}
