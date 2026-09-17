using System.Windows.Media.Imaging;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class CursorSetItem : NotifyPropertyChangedViewModel
    {
        public string Id { get; }

        public string Name { get; }

        public string Folder { get; }

        public CursorSetItem(CursorSetRecord record)
        {
            Id = record.Id;
            Name = record.Name;
            Folder = CursorSetStore.GetFolder(record.Id);
        }

        // decoded once per file and cached - these getters are re-evaluated by every binding
        // refresh, and decoding four PNGs from disk each time made the cursor tab hitch
        private readonly Dictionary<string, BitmapImage?> _previewCache = new(StringComparer.OrdinalIgnoreCase);

        public BitmapImage? ArrowCursorPreview => LoadPreview("ArrowCursor.png");

        public BitmapImage? ArrowFarCursorPreview => LoadPreview("ArrowFarCursor.png");

        public BitmapImage? IBeamCursorPreview => LoadPreview("IBeamCursor.png");

        public BitmapImage? ShiftlockCursorPreview => LoadPreview("MouseLockedCursor.png");

        public void RefreshPreviews()
        {
            _previewCache.Clear();
            OnPropertyChanged(nameof(ArrowCursorPreview));
            OnPropertyChanged(nameof(ArrowFarCursorPreview));
            OnPropertyChanged(nameof(IBeamCursorPreview));
            OnPropertyChanged(nameof(ShiftlockCursorPreview));
        }

        private BitmapImage? LoadPreview(string fileName)
        {
            if (_previewCache.TryGetValue(fileName, out BitmapImage? cached))
                return cached;

            BitmapImage? loaded = DecodePreview(fileName);
            _previewCache[fileName] = loaded;
            return loaded;
        }

        private BitmapImage? DecodePreview(string fileName)
        {
            string path = Path.Combine(Folder, fileName);

            if (!File.Exists(path))
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelHeight = 96;
                // avoid holding a file lock and pick up edits made outside a running app session
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
