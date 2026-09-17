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
        public string TakenDisplay => Taken.ToString("g");
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

                return $"With these settings a fullscreen game records at {width} × {height}, {fps} fps. The rolling buffer uses about {bufferMb:0} MB of RAM, and a full {seconds}s clip is about {clipMb:0} MB on disk.";
            }
        }

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
            if (item is null || !File.Exists(item.Path))
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
                    .GetFiles("*.mp4")
                    .OrderByDescending(f => f.LastWriteTime);

                foreach (FileInfo file in files)
                    Replays.Add(new ReplayClipItem { Path = file.FullName, Taken = file.LastWriteTime });
            }

            OnPropertyChanged(nameof(HasReplays));
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
        }
    }
}
