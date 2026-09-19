using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PhasmaStrap.UI
{
    // A looping, silent video behind the settings window (GlobalBackground builds it when the
    // chosen background is a video).
    //
    // Played with MediaElement.Play / Pause and restarted at its end - not through a repeating
    // MediaTimeline: a timeline is an animation clock, and while any clock is alive (paused or not)
    // WPF ticks at the monitor's refresh rate and re-renders the window every time. On a 240 Hz
    // screen that alone cost most of a CPU core. Decoding uses the graphics card where Windows can.
    //
    // It pauses while the window is minimised and - unless switched off - while another window is
    // in front, and it lets go of the file and the decoder as soon as it leaves the window. Should
    // the video not open after all (a codec removed since it was added), a still frame of it is
    // shown instead of a black window.
    internal sealed class VideoBackground : Grid
    {
        private const string LOG_IDENT = "VideoBackground";

        private readonly string _path;
        private readonly MediaElement _media;
        private readonly Image _fallback;
        private Window? _window;
        private bool _started;
        private bool _paused;

        public VideoBackground(string path)
        {
            _path = path;
            IsHitTestVisible = false;
            ClipToBounds = true;

            _fallback = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };

            _media = new MediaElement
            {
                Stretch = Stretch.UniformToFill,
                IsMuted = true,
                Volume = 0,
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                ScrubbingEnabled = false,
                Opacity = 0,
            };

            _media.MediaEnded += (_, _) =>
            {
                _media.Position = TimeSpan.Zero;
                if (!_paused)
                    _media.Play();
            };

            _media.MediaOpened += (_, _) =>
                _media.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = new QuadraticEase() });

            _media.MediaFailed += (_, e) =>
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not play '{System.IO.Path.GetFileName(_path)}': {e.ErrorException?.Message}");
                Stop();
                ShowStill();
            };

            Children.Add(_fallback);
            Children.Add(_media);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started)
                return;

            try
            {
                _media.Source = new Uri(_path, UriKind.Absolute);
                _started = true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not start '{System.IO.Path.GetFileName(_path)}': {ex.Message}");
                ShowStill();
                return;
            }

            _window = Window.GetWindow(this);
            if (_window is not null)
            {
                _window.Activated += WindowChanged;
                _window.Deactivated += WindowChanged;
                _window.StateChanged += WindowChanged;
                _window.Closed += WindowClosed;
            }

            // shows the first frame even when it starts out paused
            _media.Play();
            _paused = false;
            UpdatePlayState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Stop();

        private void WindowChanged(object? sender, EventArgs e) => UpdatePlayState();

        private void WindowClosed(object? sender, EventArgs e) => Stop();

        private void UpdatePlayState()
        {
            if (!_started || _window is null)
                return;

            bool hidden = _window.WindowState == System.Windows.WindowState.Minimized || !_window.IsVisible;
            // (a background UI-test window is never active - it can ask to keep playing anyway)
            bool behind = App.Settings.Prop.GlobalBackgroundVideoPauseInactive && !_window.IsActive
                && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_VIDEOPLAY") != "1";
            bool pause = hidden || behind;

            if (pause == _paused)
                return;

            _paused = pause;

            if (pause)
                _media.Pause();
            else
                _media.Play();
        }

        public void Stop()
        {
            if (_window is not null)
            {
                _window.Activated -= WindowChanged;
                _window.Deactivated -= WindowChanged;
                _window.StateChanged -= WindowChanged;
                _window.Closed -= WindowClosed;
                _window = null;
            }

            if (_started)
            {
                _started = false;
                try { _media.Stop(); } catch { }
                try { _media.Close(); } catch { }
            }
        }

        private void ShowStill()
        {
            string path = _path;

            Task.Run(() => BackgroundLibrary.LoadThumbnail(path, 1920)).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result is not null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _fallback.Source = task.Result;
                        _fallback.Visibility = Visibility.Visible;
                        _media.Visibility = Visibility.Collapsed;
                    }));
                }
            });
        }
    }
}
