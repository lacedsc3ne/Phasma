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

    public sealed class ReplayClipItem : NotifyPropertyChangedViewModel
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

        private System.Windows.Media.Imaging.BitmapSource? _thumbnail;
        private bool _thumbnailRequested;

        // made in the background the first time the list asks for it (Utility/ClipThumbnails)
        public System.Windows.Media.Imaging.BitmapSource? Thumbnail
        {
            get
            {
                if (!_thumbnailRequested)
                {
                    _thumbnailRequested = true;
                    var dispatcher = System.Windows.Application.Current.Dispatcher;
                    ClipThumbnails.Request(Path, image => dispatcher.BeginInvoke(() =>
                    {
                        _thumbnail = image;
                        OnPropertyChanged(nameof(Thumbnail));
                        OnPropertyChanged(nameof(PlaceholderVisibility));
                    }));
                }
                return _thumbnail;
            }
        }

        public System.Windows.Visibility PlaceholderVisibility => _thumbnail is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
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
            set { App.Settings.Prop.InstantReplayClipSeconds = value; ReplaySettingChanged(nameof(InstantReplayClipSeconds)); OnPropertyChanged(nameof(ClipLengthText)); }
        }

        public int MaxClipSeconds => InstantReplayRecorder.MaxClipSeconds;

        // "45s" under a minute, "2:30" from there
        public string ClipLengthText
        {
            get
            {
                int seconds = App.Settings.Prop.InstantReplayClipSeconds;
                return seconds < 60 ? $"{seconds}s" : $"{seconds / 60}:{seconds % 60:00}";
            }
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
            set { App.Settings.Prop.InstantReplayGpuEncoding = value; ReplaySettingChanged(nameof(GpuEncoding)); OnPropertyChanged(nameof(SoundAvailable)); OnPropertyChanged(nameof(MicrophonePickerEnabled)); }
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
            set { App.Settings.Prop.InstantReplayMicrophone = value; ReplaySettingChanged(nameof(RecordMicrophone)); OnPropertyChanged(nameof(MicrophonePickerEnabled)); }
        }

        public bool MicrophonePickerEnabled => SoundAvailable && RecordMicrophone;

        public sealed record MicrophoneOption(string Id, string Name)
        {
            public override string ToString() => Name;
        }

        private List<MicrophoneOption>? _microphones;

        // Windows' default first, then every recording device that's plugged in
        public List<MicrophoneOption> MicrophoneOptions
        {
            get
            {
                if (_microphones is not null)
                    return _microphones;

                _microphones = new() { new MicrophoneOption("", "Windows default microphone") };
                _microphones.AddRange(ReplayAudio.ListMicrophones().Select(m => new MicrophoneOption(m.Id, m.Unprocessed ? m.Name : $"{m.Name}  (keeps its own processing)")));

                string saved = App.Settings.Prop.InstantReplayMicrophoneDevice;
                if (saved.Length > 0 && !_microphones.Any(m => m.Id == saved))
                    _microphones.Add(new MicrophoneOption(saved, "Chosen microphone (not plugged in)"));

                return _microphones;
            }
        }

        public MicrophoneOption? SelectedMicrophone
        {
            get => MicrophoneOptions.FirstOrDefault(m => m.Id == App.Settings.Prop.InstantReplayMicrophoneDevice) ?? MicrophoneOptions[0];
            set
            {
                if (value is null || value.Id == App.Settings.Prop.InstantReplayMicrophoneDevice)
                    return;
                App.Settings.Prop.InstantReplayMicrophoneDevice = value.Id;
                ReplaySettingChanged(nameof(SelectedMicrophone));
            }
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

        // the page shows only the newest few; everything else is in the Captures window
        private const int RecentCount = 3;
        public ObservableCollection<ScreenshotItem> RecentScreenshots { get; } = new();
        public ObservableCollection<ReplayClipItem> RecentReplays { get; } = new();

        public System.Windows.Visibility RecentHintVisibility => Screenshots.Count > RecentCount ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string AllScreenshotsText => $"All screenshots ({Screenshots.Count})";
        public string AllClipsText => $"All clips ({Replays.Count})";

        private static void SyncRecent<T>(ObservableCollection<T> all, ObservableCollection<T> recent)
        {
            var newest = all.Take(RecentCount).ToList();
            if (newest.SequenceEqual(recent))
                return;
            recent.Clear();
            foreach (T item in newest)
                recent.Add(item);
        }

        public ICommand OpenScreenshotLibraryCommand => new RelayCommand(() => OpenLibrary(PhasmaStrap.UI.ViewModels.Dialogs.CaptureLibraryViewModel.ScreenshotsTab));
        public ICommand OpenClipLibraryCommand => new RelayCommand(() => OpenLibrary(PhasmaStrap.UI.ViewModels.Dialogs.CaptureLibraryViewModel.ClipsTab));

        private static void OpenLibrary(int tab) =>
            PhasmaStrap.UI.Elements.Dialogs.CaptureLibraryWindow.Open(tab, System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault());

        public ICommand RenameScreenshotCommand => new RelayCommand<ScreenshotItem>(item => { if (item is not null) Rename(item.Path, image: true); });
        public ICommand RenameReplayCommand => new RelayCommand<ReplayClipItem>(item => { if (item is not null) Rename(item.Path, image: false); });

        private void Rename(string path, bool image)
        {
            var owner = System.Windows.Application.Current.Windows.OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>().FirstOrDefault();
            string current = Path.GetFileNameWithoutExtension(path);

            while (true)
            {
                var dialog = new PhasmaStrap.UI.Elements.Dialogs.TextInputDialog("Rename", $"New name for {Path.GetFileName(path)}:", current) { Owner = owner };
                dialog.ShowDialog();
                if (!dialog.Confirmed)
                    return;

                string? renamed = CaptureLibrary.Rename(path, dialog.Value, out string? error);
                if (renamed is not null)
                {
                    Report(image, $"Renamed to {Path.GetFileName(renamed)}.");
                    if (image) RefreshGallery(); else RefreshReplays();
                    return;
                }

                Frontend.ShowMessageBox(error ?? "That name can't be used.", System.Windows.MessageBoxImage.Warning);
                current = dialog.Value;
            }
        }

        public bool ScreenshotPickArea
        {
            get => App.Settings.Prop.ScreenshotPickArea;
            set { App.Settings.Prop.ScreenshotPickArea = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(ScreenshotPickArea)); }
        }

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

            if (!CaptureLibrary.Recycle(item.Path, out string? error))
                ReplayStatus = $"Couldn't delete {item.FileName}: {error}";

            RefreshReplays();
        });
        public ICommand OpenReplaysFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(InstantReplayRecorder.ClipsDir);
            Process.Start("explorer.exe", InstantReplayRecorder.ClipsDir);
        });

        private void RefreshReplays()
        {
            // clips that haven't changed keep their item, so their thumbnail isn't made again
            var known = Replays.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
            var fresh = new List<ReplayClipItem>();

            if (Directory.Exists(InstantReplayRecorder.ClipsDir))
            {
                var files = new DirectoryInfo(InstantReplayRecorder.ClipsDir)
                    .GetFiles()
                    .Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Name.EndsWith(".editing.mp4", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime);

                foreach (FileInfo file in files)
                {
                    fresh.Add(known.TryGetValue(file.FullName, out ReplayClipItem? item) && item.Bytes == file.Length && item.Taken == file.LastWriteTime
                        ? item
                        : new ReplayClipItem { Path = file.FullName, Taken = file.LastWriteTime, Bytes = file.Length });
                }
            }

            if (!fresh.SequenceEqual(Replays))
            {
                Replays.Clear();
                foreach (ReplayClipItem item in fresh)
                    Replays.Add(item);
            }

            SyncRecent(Replays, RecentReplays);
            OnPropertyChanged(nameof(HasReplays));
            OnPropertyChanged(nameof(AllClipsText));
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

            if (!CaptureLibrary.Recycle(item.Path, out string? error))
                Status = $"Couldn't delete {item.FileName}: {error}";

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
            WatchFolders();
        }

        // ---- new clips and screenshots show up by themselves. They're saved by the Watcher (the
        // process that runs while you play), so this page watches the folders instead of being told.

        private readonly List<FileSystemWatcher> _watchers = new();
        private System.Windows.Threading.DispatcherTimer? _clipsDebounce;
        private System.Windows.Threading.DispatcherTimer? _screenshotsDebounce;

        private void WatchFolders()
        {
            _clipsDebounce = Debouncer(RefreshReplays);
            _screenshotsDebounce = Debouncer(RefreshGallery);
            Watch(InstantReplayRecorder.ClipsDir, _clipsDebounce);
            Watch(ScreenshotCapture.ScreenshotsDir, _screenshotsDebounce);
        }

        // a clip is written over a moment - refresh once it has been quiet for a second
        private static System.Windows.Threading.DispatcherTimer Debouncer(Action refresh)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                refresh();
            };
            return timer;
        }

        private void Watch(string folder, System.Windows.Threading.DispatcherTimer debounce)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };

                var dispatcher = System.Windows.Application.Current.Dispatcher;
                void Changed(object? sender, FileSystemEventArgs e) => dispatcher.BeginInvoke(() =>
                {
                    debounce.Stop();
                    debounce.Start();
                });

                watcher.Created += Changed;
                watcher.Changed += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += (s, e) => Changed(s, e);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("CaptureViewModel", $"Can't watch {folder} for new files: {ex.Message}");
            }
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
            // unchanged screenshots keep their item (and decoded thumbnail)
            var known = Screenshots.ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
            var fresh = new List<ScreenshotItem>();

            if (Directory.Exists(ScreenshotCapture.ScreenshotsDir))
            {
                var files = new DirectoryInfo(ScreenshotCapture.ScreenshotsDir)
                    .GetFiles("*.png")
                    .OrderByDescending(f => f.LastWriteTime);

                foreach (FileInfo file in files)
                {
                    fresh.Add(known.TryGetValue(file.FullName, out ScreenshotItem? item) && item.Taken == file.LastWriteTime
                        ? item
                        : new ScreenshotItem { Path = file.FullName, Taken = file.LastWriteTime });
                }
            }

            if (!fresh.SequenceEqual(Screenshots))
            {
                Screenshots.Clear();
                foreach (ScreenshotItem item in fresh)
                    Screenshots.Add(item);
            }

            SyncRecent(Screenshots, RecentScreenshots);
            OnPropertyChanged(nameof(HasScreenshots));
            OnPropertyChanged(nameof(AllScreenshotsText));
            OnPropertyChanged(nameof(RecentHintVisibility));
            OnPropertyChanged(nameof(StorageUsageText));
        }
    }
}
