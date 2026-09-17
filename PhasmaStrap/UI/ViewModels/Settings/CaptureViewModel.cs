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

        public bool InstantReplayEnabled
        {
            get => App.Settings.Prop.InstantReplayEnabled;
            set => App.Settings.Prop.InstantReplayEnabled = value;
        }

        public int InstantReplayClipSeconds
        {
            get => App.Settings.Prop.InstantReplayClipSeconds;
            set { App.Settings.Prop.InstantReplayClipSeconds = value; OnPropertyChanged(nameof(InstantReplayClipSeconds)); }
        }

        public string[] QualityOptions { get; } = { "Low", "Medium", "High" };

        public string SelectedQuality
        {
            get => QualityOptions[Math.Clamp(App.Settings.Prop.InstantReplayQuality, 0, QualityOptions.Length - 1)];
            set
            {
                int index = Array.IndexOf(QualityOptions, value);
                if (index >= 0)
                    App.Settings.Prop.InstantReplayQuality = index;
            }
        }

        public ObservableCollection<ReplayClipItem> Replays { get; } = new();

        public bool HasReplays => Replays.Count > 0;

        public ICommand RefreshReplaysCommand => new RelayCommand(RefreshReplays);
        public ICommand OpenReplayCommand => new RelayCommand<ReplayClipItem>(item =>
        {
            if (item is not null && File.Exists(item.Path))
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
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
