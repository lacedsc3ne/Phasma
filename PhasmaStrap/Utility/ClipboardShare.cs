using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;

using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace PhasmaStrap.Utility
{
    // Puts a screenshot or clip on the clipboard so it can be pasted straight into a chat.
    //
    // A file is offered as a file drop (what Discord, Explorer and mail clients take as "a file
    // was pasted"); a screenshot additionally as a bitmap, for the programs that only take
    // pictures (Paint, image editors, some web pages).
    //
    // The clipboard is an STA-only API and is frequently held open for a moment by whatever
    // clipboard manager is running, so every call goes through an STA thread and retries.
    //
    // No App dependencies, so it can be exercised from a console harness.
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
                image.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
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
                        // copy: true - the data outlives this process
                        Clipboard.SetDataObject(data, true);
                        return true;
                    }
                    catch (System.Runtime.InteropServices.COMException) when (attempt < 8)
                    {
                        // CLIPBRD_E_CANT_OPEN: another program has it open right now
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
