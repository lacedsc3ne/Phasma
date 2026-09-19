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
        private double _slideDistance = 420;

        private bool _isProcessing;
        private bool _closed;
        private CancellationTokenSource? _currentItemCts;

        public bool IsUsable => !_closed;

        public NotificationToast()
        {
            InitializeComponent();
            Closed += Window_Closed;
        }

        public void ShowNotification(string title, string message, NotificationCategory category, double durationSeconds = 5, Action? onClick = null, string? actionText = null, Action? action = null)
        {
            if (_closed)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowNotification(title, message, category, durationSeconds, onClick, actionText, action)));
                return;
            }

            while (_queue.Count >= MaxQueuedNotifications)
                _queue.Dequeue();

            _queue.Enqueue(new NotificationQueueItem
            {
                Title = title,
                Message = message,
                Category = category,
                Duration = durationSeconds,
                OnClick = onClick,
                ActionText = actionText,
                Action = action,
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

                    // a queued item of the same category as one still animating almost always means
                    // the user is rapidly re-pressing a toggle hotkey and wants to see the LATEST
                    // state now, not wait out the previous toast's full hold time first
                    if (_queue.Count > 0 && _queue.Peek().Category == item.Category)
                        continue;

                    TitleText.Text = item.Title;
                    MessageText.Text = item.Message;
                    MessageText.Visibility = string.IsNullOrWhiteSpace(item.Message) ? Visibility.Collapsed : Visibility.Visible;
                    ApplyCategoryStyle(item.Category);

                    _currentClick = item.OnClick;
                    _currentAction = item.Action;
                    ActionButton.Content = item.ActionText ?? "";
                    ActionButton.Visibility = item.Action is not null && !string.IsNullOrEmpty(item.ActionText) ? Visibility.Visible : Visibility.Collapsed;
                    NotificationBorder.Cursor = item.OnClick is null ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
                    if (item.OnClick is not null)
                        SourceText.Text += "  \u00b7  Click to open";

                    ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    ProgressScale.ScaleX = 0;
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    RootTranslate.X = _slideDistance;
                    NotificationBorder.BeginAnimation(OpacityProperty, null);
                    NotificationBorder.Opacity = 0;

                    if (!IsVisible)
                        Show();

                    // SizeToContent="Height": let the new text measure before we place the window
                    InvalidateMeasure();
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

                    using (_currentItemCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token))
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(duration), _currentItemCts.Token);
                        }
                        catch (OperationCanceledException) when (!_lifetimeCts.IsCancellationRequested)
                        {
                            // dismissed early via the close button - fall through to the slide-out
                            // below instead of the outer catch tearing down the whole queue
                        }
                    }
                    _currentItemCts = null;

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

        private void ApplyCategoryStyle(NotificationCategory category)
        {
            (Wpf.Ui.Common.SymbolRegular symbol, string accentKey, string source) = category switch
            {
                NotificationCategory.GameJoin => (Wpf.Ui.Common.SymbolRegular.PlayCircle24, "SystemFillColorSuccessBrush", "Game session"),
                NotificationCategory.GameLeave => (Wpf.Ui.Common.SymbolRegular.DoorArrowRight20, "SystemFillColorCautionBrush", "Game session"),
                _ => (Wpf.Ui.Common.SymbolRegular.Info24, "SystemAccentColorPrimaryBrush", "PhasmaStrap"),
            };

            CategoryIcon.Symbol = symbol;
            SourceText.Text = source;

            if (Application.Current.TryFindResource(accentKey) is SolidColorBrush brush)
            {
                AccentBar.Background = brush;
                ProgressBar.Fill = brush;
                CategoryIcon.Foreground = brush;
                IconBackground.Color = brush.Color;
            }
        }

        private Action? _currentClick;

        private void NotificationBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Action? action = _currentClick;
            if (action is null)
                return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("NotificationToast::Click", $"Click action failed: {ex.Message}");
            }

            // the toast did its job - dismiss it
            _currentItemCts?.Cancel();
        }

        private Action? _currentAction;

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            Action? action = _currentAction;
            if (action is null)
                return;

            // dismiss first, so the toast isn't left over whatever the action opens
            _currentItemCts?.Cancel();

            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("NotificationToast::Action", $"Action failed: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // cancel just the current item's hold delay, not the whole toast lifetime - lets the
            // queue continue normally to whatever's next
            _currentItemCts?.Cancel();
        }

        private void UpdatePosition()
        {
            _slideDistance = ActualWidth > 0 ? ActualWidth : Width;

            // SystemParameters.WorkArea is cached by WPF and goes stale when a fullscreen game
            // switches the display resolution - the toast then lands off the right edge of the
            // (now smaller) screen. Ask Windows for the live work area of the monitor the game
            // (or the cursor) is on, and convert device pixels to WPF units for this window's DPI.
            Rect workArea = SystemParameters.WorkArea;
            try
            {
                System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
                foreach (Process process in Process.GetProcessesByName(App.RobloxPlayerAppName))
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        screen = System.Windows.Forms.Screen.FromHandle(process.MainWindowHandle);
                        break;
                    }
                }

                var area = screen.WorkingArea;
                Matrix fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                Point topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
                Point bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
                workArea = new Rect(topLeft, bottomRight);
            }
            catch (Exception)
            {
            }

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
            public NotificationCategory Category { get; set; }
            public double Duration { get; set; } = 5;
            public Action? OnClick { get; set; }
            public string? ActionText { get; set; }
            public Action? Action { get; set; }
        }
    }
}
