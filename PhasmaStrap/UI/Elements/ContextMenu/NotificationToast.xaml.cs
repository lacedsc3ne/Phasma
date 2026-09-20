using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public partial class NotificationToast : Window
    {
        private const int MaxQueuedNotifications = 20;

        private readonly Queue<NotificationQueueItem> _queue = new();
        private readonly CancellationTokenSource _lifetimeCts = new();
        private double _slideFromX = 440;
        private double _slideFromY;

        private bool _isProcessing;
        private bool _closed;
        private CancellationTokenSource? _currentItemCts;

        public bool IsUsable => !_closed;

        public NotificationToast()
        {
            InitializeComponent();
            Closed += Window_Closed;
        }

        public void ShowNotification(string title, string message, NotificationKindId kind, double durationSeconds = 5, Action? onClick = null, string? actionText = null, Action? action = null)
        {
            if (_closed)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowNotification(title, message, kind, durationSeconds, onClick, actionText, action)));
                return;
            }

            while (_queue.Count >= MaxQueuedNotifications)
                _queue.Dequeue();

            _queue.Enqueue(new NotificationQueueItem
            {
                Title = title,
                Message = message,
                Kind = kind,
                Duration = durationSeconds,
                OnClick = onClick,
                ActionText = actionText,
                Action = action,
            });

            if (_isProcessing)
                UpdateStackChrome();
            else
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

                    if (_queue.Count > 0 && _queue.Peek().Kind == item.Kind)
                        continue;

                    TitleText.Text = item.Title;
                    MessageText.Text = item.Message;
                    MessageText.Visibility = string.IsNullOrWhiteSpace(item.Message) ? Visibility.Collapsed : Visibility.Visible;
                    ApplyKindStyle(item.Kind);

                    _currentClick = item.OnClick;
                    _currentAction = item.Action;
                    ActionButton.Content = item.ActionText ?? "";
                    ActionButton.Visibility = item.Action is not null && !string.IsNullOrEmpty(item.ActionText) ? Visibility.Visible : Visibility.Collapsed;
                    CardBorder.Cursor = item.OnClick is null ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
                    HintText.Visibility = item.OnClick is null ? Visibility.Collapsed : Visibility.Visible;
                    UpdateStackChrome();

                    ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    ProgressScale.ScaleX = 0;
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                    NotificationBorder.BeginAnimation(OpacityProperty, null);
                    NotificationBorder.Opacity = 0;

                    if (!IsVisible)
                        Show();

                    ApplyAppearance();
                    InvalidateMeasure();
                    UpdateLayout();
                    UpdatePosition();

                    RootTranslate.X = _slideFromX;
                    RootTranslate.Y = _slideFromY;

                    var slideIn = new DoubleAnimation(_slideFromX, 0, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);

                    var riseIn = new DoubleAnimation(_slideFromY, 0, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    RootTranslate.BeginAnimation(TranslateTransform.YProperty, riseIn);

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
                        }
                    }
                    _currentItemCts = null;

                    var slideOut = new DoubleAnimation(0, _slideFromX, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    RootTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);

                    var riseOut = new DoubleAnimation(0, _slideFromY, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    RootTranslate.BeginAnimation(TranslateTransform.YProperty, riseOut);

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

        private void ApplyKindStyle(NotificationKindId kind)
        {
            NotificationStyle style = NotificationStyles.For(kind);

            var brush = new SolidColorBrush(style.Accent);
            brush.Freeze();

            CategoryIcon.Symbol = style.Symbol;
            CategoryIcon.Foreground = brush;
            ProgressBar.Fill = brush;
            IconBackground.Color = style.Accent;
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

            _currentItemCts?.Cancel();
        }

        private Action? _currentAction;

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            Action? action = _currentAction;
            if (action is null)
                return;

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
            _currentItemCts?.Cancel();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            _queue.Clear();
            UpdateStackChrome();
            _currentItemCts?.Cancel();
        }

        private void UpdateStackChrome()
        {
            int waiting = _queue.Count;

            StackCountText.Text = waiting + 1 + " NOTIFICATIONS";
            StackHeader.Visibility = waiting > 0 ? Visibility.Visible : Visibility.Collapsed;
            PeekNear.Visibility = waiting > 0 ? Visibility.Visible : Visibility.Collapsed;
            PeekFar.Visibility = waiting > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private const int SideMargin = 14;

        private void ApplyAppearance()
        {
            double card = Math.Clamp(App.Settings.Prop.NotificationWidth, 300, 700);

            Width = card + (SideMargin * 2);
            PeekNear.Width = Math.Max(40, card - 10);
            PeekFar.Width = Math.Max(30, card - 24);
        }

        private void UpdatePosition()
        {
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : 0;

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

            string position = App.Settings.Prop.NotificationPosition ?? "TopRight";
            bool bottom = position.StartsWith("Bottom", StringComparison.OrdinalIgnoreCase);
            bool left = position.EndsWith("Left", StringComparison.OrdinalIgnoreCase);
            bool middle = position.EndsWith("Middle", StringComparison.OrdinalIgnoreCase);

            double offsetX = Math.Clamp(App.Settings.Prop.NotificationOffsetX, 0, 2000);
            double offsetY = Math.Clamp(App.Settings.Prop.NotificationOffsetY, 0, 2000);

            Left = middle
                ? workArea.Left + ((workArea.Width - width) / 2) + offsetX
                : left
                    ? workArea.Left + offsetX
                    : workArea.Right - width - offsetX;

            Top = bottom ? workArea.Bottom - height - offsetY : workArea.Top + offsetY;

            Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));

            _slideFromX = middle ? 0 : left ? -width : width;
            _slideFromY = middle ? (bottom ? height : -height) : 0;
        }

        public static readonly string[] Positions = { "TopRight", "TopMiddle", "TopLeft", "BottomRight", "BottomMiddle", "BottomLeft" };

        public static readonly string[] PositionLabels = { "Top right", "Top middle", "Top left", "Bottom right", "Bottom middle", "Bottom left" };

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
            public NotificationKindId Kind { get; set; }
            public double Duration { get; set; } = 5;
            public Action? OnClick { get; set; }
            public string? ActionText { get; set; }
            public Action? Action { get; set; }
        }
    }
}
