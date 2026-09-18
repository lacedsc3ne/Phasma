using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class ScreenshotItem
    {
        public string Path { get; init; } = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public DateTime Taken { get; init; }
        public string TakenDisplay => Taken.ToString("g");

        private System.Windows.Media.Imaging.BitmapImage? _thumbnail;

        // binding the raw path made WPF decode every full-resolution screenshot (tens of MB each)
        // just to draw a 140px card; decode at card size instead, once, and keep the file unlocked
        public System.Windows.Media.Imaging.BitmapImage? Thumbnail
        {
            get
            {
                if (_thumbnail is not null)
                    return _thumbnail;

                try
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.DecodePixelHeight = 160;
                    image.UriSource = new Uri(Path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    _thumbnail = image;
                }
                catch (Exception)
                {
                    _thumbnail = null;
                }

                return _thumbnail;
            }
        }
    }

    public sealed class ReplayClipItem
    {
        public string Path { get; init; } = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public DateTime Taken { get; init; }
        public long Bytes { get; init; }
        public string TakenDisplay => $"{Taken:g}  ·  {Bytes / 1048576.0:0.0} MB";

        // GIFs exported from the clip editor are listed with the clips; only videos can be edited
        public bool IsGif => Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        public System.Windows.Visibility EditVisibility => IsGif ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        public Wpf.Ui.Common.SymbolRegular Symbol => IsGif ? Wpf.Ui.Common.SymbolRegular.Gif24 : Wpf.Ui.Common.SymbolRegular.VideoClip24;
    }

    public class CaptureViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<ScreenshotItem> Screenshots { get; } = new();

        // --- Instant Replay settings (the actual recorder only runs in the Watcher/Bootstrapper
        // process during a live game session - same process-separation limitation as everything
        // else in this app that needs a live Roblox session, see HomeViewModel/IntegrationsViewModel's
        // similar notes. This page only edits Settings.Prop; InstantReplayRecorder itself lives
        // in Watcher.cs.) ---

        // --- "no hotkey bound" hint: the most common reason screenshots/replays "don't work" ---

        public System.Windows.Visibility HotkeyHintVisibility => string.IsNullOrEmpty(HotkeyHintText) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public string HotkeyHintText
        {
            get
            {
                var bindings = App.Settings.Prop.HotkeyBindings;
                bool screenshot = bindings.TryGetValue(PhasmaStrap.Utility.HotkeyActions.TakeScreenshot, out string? s) && !string.IsNullOrWhiteSpace(s);
                bool replay = bindings.TryGetValue(PhasmaStrap.Utility.HotkeyActions.SaveInstantReplay, out string? r) && !string.IsNullOrWhiteSpace(r);

                if (screenshot && replay)
                    return "";
                if (!screenshot && !replay)
                    return "No hotkeys are bound for Take Screenshot or Save Instant Replay yet - nothing will capture during a game until you set them.";
                return !screenshot
                    ? "No hotkey is bound for Take Screenshot yet."
                    : "No hotkey is bound for Save Instant Replay yet - Instant Replay buffers, but there's no way to save a clip.";
            }
        }

        public ICommand OpenHotkeysCommand => new RelayCommand(() =>
        {
            var window = System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault();
            window?.Navigate(typeof(PhasmaStrap.UI.Elements.Settings.Pages.HotkeysPage));
        });

        public void RefreshHotkeyHint()
        {
            OnPropertyChanged(nameof(HotkeyHintText));
            OnPropertyChanged(nameof(HotkeyHintVisibility));
        }

        // saved immediately (not just on the Save button) so the running game session's watcher
        // picks it up through SettingsHotReload and starts/stops buffering without a relaunch
        public bool InstantReplayEnabled
        {
            get => App.Settings.Prop.InstantReplayEnabled;
            set
            {
                App.Settings.Prop.InstantReplayEnabled = value;
                App.Settings.Save();
                OnPropertyChanged(nameof(InstantReplayEnabled));
            }
        }

        // every replay setting saves straight away: the recorder lives in the game-session process,
        // which follows Settings.json (SettingsHotReload) and re-reads these values live
        public int InstantReplayClipSeconds
        {
            get => App.Settings.Prop.InstantReplayClipSeconds;
            set { App.Settings.Prop.InstantReplayClipSeconds = value; ReplaySettingChanged(nameof(InstantReplayClipSeconds)); }
        }

        public string[] QualityOptions { get; } = { "Low", "Medium", "High" };

        public string SelectedQuality
        {
            get => QualityOptions[Math.Clamp(App.Settings.Prop.InstantReplayQuality, 0, QualityOptions.Length - 1)];
            set
            {
                int index = Array.IndexOf(QualityOptions, value);
                if (index >= 0)
                {
                    App.Settings.Prop.InstantReplayQuality = index;
                    ReplaySettingChanged(nameof(SelectedQuality));
                }
            }
        }

        public string[] FpsOptions { get; } = InstantReplayRecorder.FpsOptions.Select(fps => $"{fps} fps").ToArray();

        public string SelectedFps
        {
            get
            {
                int index = Array.IndexOf(InstantReplayRecorder.FpsOptions, App.Settings.Prop.InstantReplayFps);
                return FpsOptions[index >= 0 ? index : Array.IndexOf(InstantReplayRecorder.FpsOptions, 30)];
            }
            set
            {
                int index = Array.IndexOf(FpsOptions, value);
                if (index >= 0)
                {
                    App.Settings.Prop.InstantReplayFps = InstantReplayRecorder.FpsOptions[index];
                    ReplaySettingChanged(nameof(SelectedFps));
                }
            }
        }

        public string[] ResolutionOptions { get; } = InstantReplayRecorder.MaxHeightOptions.Select(h => h == 0 ? "Native" : $"{h}p").ToArray();

        public string SelectedResolution
        {
            get
            {
                int index = Array.IndexOf(InstantReplayRecorder.MaxHeightOptions, App.Settings.Prop.InstantReplayMaxHeight);
                return ResolutionOptions[Math.Max(0, index)];
            }
            set
            {
                int index = Array.IndexOf(ResolutionOptions, value);
                if (index >= 0)
                {
                    App.Settings.Prop.InstantReplayMaxHeight = InstantReplayRecorder.MaxHeightOptions[index];
                    ReplaySettingChanged(nameof(SelectedResolution));
                }
            }
        }

        public bool GpuEncoding
        {
            get => App.Settings.Prop.InstantReplayGpuEncoding;
            set { App.Settings.Prop.InstantReplayGpuEncoding = value; ReplaySettingChanged(nameof(GpuEncoding)); OnPropertyChanged(nameof(SoundAvailable)); }
        }

        public bool SoundAvailable => App.Settings.Prop.InstantReplayGpuEncoding;

        public bool RecordSound
        {
            get => App.Settings.Prop.InstantReplayAudio;
            set { App.Settings.Prop.InstantReplayAudio = value; ReplaySettingChanged(nameof(RecordSound)); }
        }

        public bool RecordMicrophone
        {
            get => App.Settings.Prop.InstantReplayMicrophone;
            set { App.Settings.Prop.InstantReplayMicrophone = value; ReplaySettingChanged(nameof(RecordMicrophone)); }
        }

        // what the current choices cost, worked out for this PC's main screen
        public string ReplayEstimateText
        {
            get
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                int width = screen.Width & ~1;
                int height = screen.Height & ~1;

                int maxHeight = App.Settings.Prop.InstantReplayMaxHeight;
                if (maxHeight > 0 && height > maxHeight)
                {
                    width = (int)Math.Round(width * (double)maxHeight / height) & ~1;
                    height = maxHeight;
                }

                int fps = App.Settings.Prop.InstantReplayFps;
                int seconds = App.Settings.Prop.InstantReplayClipSeconds;
                int quality = App.Settings.Prop.InstantReplayQuality;

                double bufferMb = InstantReplayRecorder.EstimateBufferBytes(width, height, fps, seconds, quality) / 1048576.0;
                double clipMb = InstantReplayRecorder.BitrateFor(width, height, fps, quality) / 8.0 * seconds / 1048576.0;

                if (App.Settings.Prop.InstantReplayGpuEncoding)
                {
                    // the buffer IS the finished video (plus one spare segment), and sound as plain PCM
                    double videoMb = clipMb * (seconds + GpuReplayRecorder.SegmentSeconds) / seconds;
                    double soundMb = App.Settings.Prop.InstantReplayAudio ? (seconds + GpuReplayRecorder.SegmentSeconds + 2) * 48000 * 4 / 1048576.0 * (App.Settings.Prop.InstantReplayMicrophone ? 2 : 1) : 0;

                    return $"With these settings a fullscreen game records at {width} × {height}, {fps} fps, encoded on the graphics card as you play. The rolling buffer uses about {videoMb + soundMb:0} MB of RAM, a {seconds}s clip is about {clipMb:0} MB on disk and saves almost instantly. Clips start on a {GpuReplayRecorder.SegmentSeconds}-second boundary, so they can run up to {GpuReplayRecorder.SegmentSeconds}s longer than the length you picked - never shorter. Only what is on screen while Roblox is the window in front is recorded.";
                }

                string text = $"With these settings a fullscreen game records at {width} × {height}, {fps} fps. The rolling buffer uses about {Math.Min(bufferMb, InstantReplayRecorder.MaxBufferMegabytes):0} MB of RAM, and a full {seconds}s clip is about {clipMb:0} MB on disk.";

                if (bufferMb > InstantReplayRecorder.MaxBufferMegabytes)
                    text += $" That is over the {InstantReplayRecorder.MaxBufferMegabytes} MB buffer limit, so clips will come out at roughly {seconds * InstantReplayRecorder.MaxBufferMegabytes / bufferMb:0}s - lower the frame rate, resolution or clip length to keep the full {seconds}s.";

                if (fps > 60)
                    text += " High frame rates use noticeably more CPU while you play.";

                return text;
            }
        }

        public bool CopyScreenshotToClipboard
        {
            get => App.Settings.Prop.CaptureCopyScreenshotToClipboard;
            set { App.Settings.Prop.CaptureCopyScreenshotToClipboard = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(CopyScreenshotToClipboard)); }
        }

        public bool CopyReplayToClipboard
        {
            get => App.Settings.Prop.CaptureCopyReplayToClipboard;
            set { App.Settings.Prop.CaptureCopyReplayToClipboard = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(CopyReplayToClipboard)); }
        }

        // --- storage limits (CaptureStorage) ---

        private static readonly int[] LimitValuesMb = { 0, 1024, 2048, 5120, 10240, 25600, 51200 };
        public string[] StorageLimitOptions { get; } = { "No limit", "1 GB", "2 GB", "5 GB", "10 GB", "25 GB", "50 GB" };

        public string SelectedStorageLimit
        {
            get => StorageLimitOptions[Math.Max(0, Array.IndexOf(LimitValuesMb, App.Settings.Prop.CaptureStorageLimitMB))];
            set
            {
                int index = Array.IndexOf(StorageLimitOptions, value);
                if (index < 0)
                    return;

                App.Settings.Prop.CaptureStorageLimitMB = LimitValuesMb[index];
                App.Settings.SaveDeferred();
                OnPropertyChanged(nameof(SelectedStorageLimit));
                OnPropertyChanged(nameof(StorageUsageText));
            }
        }

        private static readonly int[] AgeValuesDays = { 0, 7, 30, 90, 365 };
        public string[] MaxAgeOptions { get; } = { "Keep forever", "7 days", "30 days", "90 days", "1 year" };

        public string SelectedMaxAge
        {
            get => MaxAgeOptions[Math.Max(0, Array.IndexOf(AgeValuesDays, App.Settings.Prop.CaptureMaxAgeDays))];
            set
            {
                int index = Array.IndexOf(MaxAgeOptions, value);
                if (index < 0)
                    return;

                App.Settings.Prop.CaptureMaxAgeDays = AgeValuesDays[index];
                App.Settings.SaveDeferred();
                OnPropertyChanged(nameof(SelectedMaxAge));
                OnPropertyChanged(nameof(StorageUsageText));
            }
        }

        private static string Size(long bytes) => bytes >= 1073741824L ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576.0:0} MB";

        public string StorageUsageText
        {
            get
            {
                List<CaptureStorage.Entry> shots = CaptureStorage.Scan(new[] { ScreenshotCapture.ScreenshotsDir });
                List<CaptureStorage.Entry> clips = CaptureStorage.Scan(new[] { InstantReplayRecorder.ClipsDir });

                string text = $"Screenshots: {shots.Count} ({Size(shots.Sum(e => e.Bytes))})  ·  Clips and GIFs: {clips.Count} ({Size(clips.Sum(e => e.Bytes))})";

                List<CaptureStorage.Entry> due = CaptureStorage.Plan(shots.Concat(clips).ToList(),
                    App.Settings.Prop.CaptureStorageLimitMB * 1048576L, App.Settings.Prop.CaptureMaxAgeDays, DateTime.Now);

                if (due.Count > 0)
                    text += $"\nWith these limits the {due.Count} oldest ({Size(due.Sum(e => e.Bytes))}) will move to the Recycle Bin the next time you take a screenshot or save a clip.";

                return text;
            }
        }

        // --- sharing: the buttons and right-click menus on the gallery cards ---

        private void Copy(string path, bool image)
        {
            if (!File.Exists(path))
            {
                Report(image, $"{Path.GetFileName(path)} is no longer there.");
                return;
            }

            ClipboardShare.Log ??= message => App.Logger.WriteLine("ClipboardShare", message);

            bool ok = image ? ClipboardShare.CopyImageFile(path) : ClipboardShare.CopyFile(path);

            Report(image, ok
                ? $"Copied {Path.GetFileName(path)} - paste it into a chat with Ctrl+V."
                : "Could not reach the clipboard - another program is holding it. Try again.");
        }

        private void CopyPath(string path, bool image)
        {
            Report(image, ClipboardShare.CopyText(path) ? "Copied the file path." : "Could not reach the clipboard - another program is holding it. Try again.");
        }

        // screenshots and clips each report next to their own list - the page is long
        private void Report(bool screenshot, string message)
        {
            if (screenshot)
                Status = message;
            else
                ReplayStatus = message;
        }

        private string _replayStatus = "";
        public string ReplayStatus
        {
            get => _replayStatus;
            private set { _replayStatus = value; OnPropertyChanged(nameof(ReplayStatus)); }
        }

        public ICommand CopyScreenshotCommand => new RelayCommand<ScreenshotItem>(item => { if (item is not null) Copy(item.Path, image: true); });
        public ICommand CopyScreenshotPathCommand => new RelayCommand<ScreenshotItem>(item => { if (item is not null) CopyPath(item.Path, image: true); });
        public ICommand RevealScreenshotCommand => new RelayCommand<ScreenshotItem>(item => { if (item is not null) NotificationCenter.RevealFile(item.Path)(); });

        public ICommand CopyReplayCommand => new RelayCommand<ReplayClipItem>(item => { if (item is not null) Copy(item.Path, image: false); });
        public ICommand CopyReplayPathCommand => new RelayCommand<ReplayClipItem>(item => { if (item is not null) CopyPath(item.Path, image: false); });
        public ICommand RevealReplayCommand => new RelayCommand<ReplayClipItem>(item => { if (item is not null) NotificationCenter.RevealFile(item.Path)(); });

        private void ReplaySettingChanged(string property)
        {
            App.Settings.SaveDeferred();
            OnPropertyChanged(property);
            OnPropertyChanged(nameof(ReplayEstimateText));
        }

        public ObservableCollection<ReplayClipItem> Replays { get; } = new();

        public bool HasReplays => Replays.Count > 0;

        public ICommand RefreshReplaysCommand => new RelayCommand(RefreshReplays);
        public ICommand OpenReplayCommand => new RelayCommand<ReplayClipItem>(item =>
        {
            if (item is not null && File.Exists(item.Path))
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        });
        public ICommand EditReplayCommand => new RelayCommand<ReplayClipItem>(item =>
        {
            if (item is null || item.IsGif || !File.Exists(item.Path))
                return;

            var editor = new PhasmaStrap.UI.Elements.Dialogs.ClipEditorWindow(item.Path)
            {
                Owner = System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault()
            };

            editor.ShowDialog();

            if (editor.Saved)
            {
                RefreshReplays();
                RefreshGallery(); // "Save frame" drops a PNG into the screenshots gallery
            }
        });

        public ICommand DeleteReplayCommand => new RelayCommand<ReplayClipItem>(item =>
        {
            if (item is null)
                return;

            try
            {
                if (File.Exists(item.Path))
                    File.Delete(item.Path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("CaptureViewModel", $"Failed to delete '{item.Path}': {ex.Message}");
            }

            RefreshReplays();
        });
        public ICommand OpenReplaysFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(InstantReplayRecorder.ClipsDir);
            Process.Start("explorer.exe", InstantReplayRecorder.ClipsDir);
        });

        private void RefreshReplays()
        {
            Replays.Clear();

            if (Directory.Exists(InstantReplayRecorder.ClipsDir))
            {
                var files = new DirectoryInfo(InstantReplayRecorder.ClipsDir)
                    .GetFiles()
                    .Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Name.EndsWith(".editing.mp4", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime);

                foreach (FileInfo file in files)
                    Replays.Add(new ReplayClipItem { Path = file.FullName, Taken = file.LastWriteTime, Bytes = file.Length });
            }

            OnPropertyChanged(nameof(HasReplays));
            OnPropertyChanged(nameof(StorageUsageText));
        }

        public bool HasScreenshots => Screenshots.Count > 0;

        private string _status = "";
        public string Status
        {
            get => _status;
            private set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public ICommand TakeScreenshotCommand => new RelayCommand(TakeScreenshot);
        public ICommand RefreshCommand => new RelayCommand(RefreshGallery);
        public ICommand OpenScreenshotCommand => new RelayCommand<ScreenshotItem>(item =>
        {
            if (item is not null && File.Exists(item.Path))
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        });
        public ICommand EditScreenshotCommand => new RelayCommand<ScreenshotItem>(item =>
        {
            if (item is null || !File.Exists(item.Path))
                return;

            var editor = new PhasmaStrap.UI.Elements.Dialogs.ScreenshotEditorWindow(item.Path)
            {
                Owner = System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault()
            };

            editor.ShowDialog();

            if (editor.Saved)
                RefreshGallery();
        });

        public ICommand DeleteScreenshotCommand => new RelayCommand<ScreenshotItem>(item =>
        {
            if (item is null)
                return;

            try
            {
                if (File.Exists(item.Path))
                    File.Delete(item.Path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("CaptureViewModel", $"Failed to delete '{item.Path}': {ex.Message}");
            }

            RefreshGallery();
        });
        public ICommand OpenFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(ScreenshotCapture.ScreenshotsDir);
            Process.Start("explorer.exe", ScreenshotCapture.ScreenshotsDir);
        });

        public CaptureViewModel()
        {
            RefreshGallery();
            RefreshReplays();
        }

        private void TakeScreenshot()
        {
            // Manual capture from Settings itself - only useful for testing this page, since
            // Settings has no live Roblox session most of the time; the real everyday path is
            // the Take Screenshot hotkey, which runs in the Watcher process during actual
            // gameplay (see Watcher.cs's HotkeyActions.TakeScreenshot registration).
            string? path = ScreenshotCapture.Capture();

            Status = path is not null
                ? $"Saved {Path.GetFileName(path)}"
                : "Could not find the Roblox window - this only works while Roblox is actually running.";

            RefreshGallery();
        }

        private void RefreshGallery()
        {
            Screenshots.Clear();

            if (Directory.Exists(ScreenshotCapture.ScreenshotsDir))
            {
                var files = new DirectoryInfo(ScreenshotCapture.ScreenshotsDir)
                    .GetFiles("*.png")
                    .OrderByDescending(f => f.LastWriteTime);

                foreach (FileInfo file in files)
                    Screenshots.Add(new ScreenshotItem { Path = file.FullName, Taken = file.LastWriteTime });
            }

            OnPropertyChanged(nameof(HasScreenshots));
            OnPropertyChanged(nameof(StorageUsageText));
        }
    }
}
