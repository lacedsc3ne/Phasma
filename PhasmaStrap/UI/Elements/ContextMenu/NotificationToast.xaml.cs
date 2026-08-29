using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    /// <summary>
    /// The actual toast popup window shown by <see cref="NotificationCenter"/>. A borderless,
    /// click-through-free, always-on-top window that slides/fades in from the top-right corner of the
    /// work area, holds for a duration (shown via a draining progress bar), then slides back out.
    /// Notifications shown while one is already animating are queued and shown one after another.
    /// </summary>
    /// <remarks>
    /// Ported from Voidstrap's UINotify.xaml(.cs) (Voidstrap.UI.Elements.Overlay.NotificationWindow),
    /// trimmed down to a title+message layout since PhasmaStrap has no equivalent to Voidstrap's
    /// avatar/flag image support, and simplified to always position against the primary work area
    /// rather than anchoring to the Roblox window (PhasmaStrap has no equivalent overlay-anchor
    /// utility to reuse for that, and the app-wide corner is a reasonable default for a general
    /// notification surface, not just server-join toasts).
    /// </remarks>
    public partial class NotificationToast : Window
    {
        private const int MaxQueuedNotifications = 20;
        private const int EdgeMargin = 10;

        private readonly Queue<NotificationQueueItem> _queue = new();
        private readonly CancellationTokenSource _lifetimeCts = new();
        private double _slideDistance = 360;

        private bool _isProcessing;
        private bool _closed;

        public bool IsUsable => !_closed;

        public NotificationToast()
        {
            InitializeComponent();
            Closed += Window_Closed;
        }

        public void ShowNotification(string title, string message, double durationSeconds = 5)
        {
            if (_closed)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowNotification(title, message, durationSeconds)));
                return;
            }

            while (_queue.Count >= MaxQueuedNotifications)
                _queue.Dequeue();

            _queue.Enqueue(new NotificationQueueItem
            {
                Title = title,
                Message = message,
                Duration = durationSeconds
            });

            if (!_isProcessing)
                _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            _isProcessing = true;

            try
            {
                while (_queue.Count > 0 && !_lifetimeCts.IsCancellationRequested)
                {
                    NotificationQueueItem item = _queue.Dequeue();
                    double duration = double.IsFinite(item.Duration) ? Math.Clamp(item.Duration, 0.5, 60) : 5;

                    TitleText.Text = item.Title;
                    MessageText.Text = item.Message;

                    ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    ProgressScale.ScaleX = 0;
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    RootTranslate.X = _slideDistance;
                    NotificationBorder.BeginAnimation(OpacityProperty, null);
                    NotificationBorder.Opacity = 0;

                    if (!IsVisible)
                        Show();

                    UpdateLayout();
                    UpdatePosition();

                    var slideIn = new DoubleAnimation(_slideDistance, 0, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);

                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    NotificationBorder.BeginAnimation(OpacityProperty, fadeIn);

                    var progressAnim = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromSeconds(duration)
                    };
                    ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, progressAnim);

                    await Task.Delay(TimeSpan.FromSeconds(duration), _lifetimeCts.Token);

                    var slideOut = new DoubleAnimation(0, _slideDistance, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };
                    NotificationBorder.BeginAnimation(OpacityProperty, fadeOut);
                    ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    ProgressScale.ScaleX = 0;

                    await Task.Delay(340, _lifetimeCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _queue.Clear();
                App.Logger.WriteLine("NotificationToast::ProcessQueue", "Notification processing stopped: " + ex.Message);
            }
            finally
            {
                _isProcessing = false;

                if (!_closed)
                {
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    NotificationBorder.BeginAnimation(OpacityProperty, null);
                    NotificationBorder.Opacity = 0;
                    Hide();
                }
            }
        }

        private void UpdatePosition()
        {
            Rect workArea = SystemParameters.WorkArea;
            _slideDistance = ActualWidth > 0 ? ActualWidth : Width;
            Left = workArea.Right - _slideDistance - EdgeMargin;
            Top = workArea.Top + EdgeMargin;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _closed = true;
            Closed -= Window_Closed;
            _lifetimeCts.Cancel();
            _queue.Clear();
            RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            NotificationBorder.BeginAnimation(OpacityProperty, null);
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _lifetimeCts.Dispose();
        }

        private sealed class NotificationQueueItem
        {
            public string Title { get; set; } = "";
            public string Message { get; set; } = "";
            public double Duration { get; set; } = 5;
        }
    }
}
