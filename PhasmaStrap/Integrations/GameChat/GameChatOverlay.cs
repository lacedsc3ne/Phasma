using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhasmaStrap.Integrations.GameChat
{
    /// <summary>
    /// The chat overlay window itself. Renders on top of Roblox (WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE, topmost,
    /// click-through until the user opens chat mode) and tracks Roblox's window rectangle so it stays glued to
    /// the game window.
    ///
    /// Voidstrap's original overlay positioned itself using a separate always-on "RobloxWindowTracker" overlay
    /// subsystem and rendered three tabs (a per-server chat, a cross-server "Global" chat, and a "Bootstrappers"
    /// tab bridged through a third-party community server). This port tracks the Roblox window itself (see
    /// <see cref="OnTrackerTick"/>) and keeps the Chat/Global tabs, but drops the third-party bridge tab -
    /// piping fork users' messages through an external community server without their explicit knowledge isn't
    /// something this port should do silently.
    /// </summary>
    public class GameChatOverlay : Window
    {
        private const string LogTag = "GameChatOverlay";

        private static readonly System.Windows.Media.FontFamily UiFont = new("Segoe UI");
        private static readonly Color ContainerColor = Color.FromArgb(166, 24, 24, 27);
        private static readonly Color InputColor = Color.FromArgb(191, 15, 15, 17);
        private static readonly Brush TabActiveBrush = FreezeBrush(Color.FromRgb(245, 245, 250));
        private static readonly Brush TabIdleBrush = FreezeBrush(Color.FromRgb(128, 130, 140));

        private static Brush FreezeBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private readonly Canvas _rootCanvas;
        private readonly Border _mainContainer;
        private readonly RichTextBox _chatBox;
        private readonly Border _inputBorder;
        private readonly GameChatInputBox _inputBox;
        private readonly GameChatRoundButton _toggleBtn;
        private readonly GameChatResizeGrip _grip;

        private const int TabChatIndex = 0;
        private const int TabGlobalIndex = 1;

        private readonly Border _tabChat;
        private readonly Border _tabGlobal;
        private readonly TextBlock _tabChatText;
        private readonly TextBlock _tabGlobalText;
        private readonly Border _tabChatUnderline;
        private readonly Border _tabGlobalUnderline;
        private int _selectedTab = TabChatIndex;
        private string _jobId = "global";

        private readonly GameChatClient _client;
        private readonly ActivityWatcher? _activityWatcher;
        private readonly uint _robloxPid;
        private readonly HashSet<string> _mutedUsers = new(StringComparer.OrdinalIgnoreCase);
        private GameChatProfileWindow? _profileWindow;

        private FlowDocument _docChat;
        private FlowDocument _docGlobal;
        private Border? _suggestPanel;
        private StackPanel? _suggestList;
        private bool _isDebugConsoleOpen;
        private bool _bugBusy;
        private DateTime _lastBugSentUtc = DateTime.MinValue;
        private readonly ConcurrentQueue<Action> _pendingUi = new();
        private int _pendingUiCount;
        private int _flushScheduled;
        private const int MaxPendingUiActions = 64;
        private const int MaxUiActionsPerFlush = 8;
        private volatile bool _closed;
        private bool _wasForeground;

        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _leaveTimer;
        private readonly DispatcherTimer _healthTimer;
        private readonly DispatcherTimer _trackerTimer;
        private bool _settingsDirty;
        private long _lastSettingsChangeMs;
        private long _lastHeartbeatMs;

        private Point _defaultOffset = new(2, 9);
        private Point _currentOffset;
        private bool _isUserMovingWindow;
        private bool _inGame;

        private double _baseWidth;
        private double _baseHeight;
        private double _scale = 1.0;
        private double _dpiScale = 1.0;
        private readonly ScaleTransform _rootScale = new(1.0, 1.0);

        private const double ContainerLeft = 7;
        private const double ContainerTop = 54;
        private const double ContainerRightInset = 21;
        private const double ContainerBottomInset = 49;
        private const double MinBaseWidth = 200;
        private const double MinBaseHeight = 150;
        private const double DefaultBaseWidth = 500;
        private const double DefaultBaseHeight = 400;
        private const double MaxBaseWidth = 720;
        private const double MaxBaseHeight = 560;

        private double ContainerWidth => Math.Max(100, _baseWidth - ContainerLeft - ContainerRightInset);
        private double ContainerHeight => Math.Max(80, _baseHeight - ContainerTop - ContainerBottomInset);

        public event EventHandler? ChatModeRequested;
        public event EventHandler? ChatModeExited;

        private const float ChatOnOpacity = 1.0f;
        private const float ChatOffOpacity = 0.7f;
        private float _targetOpacity = ChatOffOpacity;

        private bool _isChatting;
        private string _rawInputText = "";
        private IntPtr _windowHandle;
        private const int MaxInputLength = 1000;

        public bool IsWindowHidden { get; private set; }

        private volatile bool _overlayVisible;
        public bool IsOverlayVisible => _overlayVisible;
        public IntPtr WindowHandle => _windowHandle;
        public long LastHeartbeatMs => Volatile.Read(ref _lastHeartbeatMs);

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        public GameChatOverlay(ActivityWatcher? activityWatcher, uint robloxPid)
        {
            _activityWatcher = activityWatcher;
            _robloxPid = robloxPid;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var prop = App.Settings.Prop;
            _baseWidth = Math.Clamp(prop.GameChatWindowWidth, (int)MinBaseWidth, (int)MaxBaseWidth);
            _baseHeight = Math.Clamp(prop.GameChatWindowHeight, (int)MinBaseHeight, (int)MaxBaseHeight);
            Width = _baseWidth;
            Height = _baseHeight;
            _currentOffset = Math.Abs(prop.GameChatOffsetX) > 10000 || Math.Abs(prop.GameChatOffsetY) > 10000
                ? _defaultOffset
                : new Point(prop.GameChatOffsetX, prop.GameChatOffsetY);

            _rootCanvas = new Canvas { RenderTransform = _rootScale, UseLayoutRounding = true, SnapsToDevicePixels = true };
            Content = _rootCanvas;

            _toggleBtn = new GameChatRoundButton(null, null);
            Canvas.SetLeft(_toggleBtn, 115);
            Canvas.SetTop(_toggleBtn, 2);
            _toggleBtn.Clicked += (_, _) => ToggleVisibility();
            _toggleBtn.TripleTapped += (_, _) => { ResetToDefaults(); AppendSystemMessage(GameChatStrings.ResetToDefault); };
            _toggleBtn.Dragged += OnToggleDragged;
            _toggleBtn.DragEnded += OnToggleDragEnded;
            _rootCanvas.Children.Add(_toggleBtn);

            _chatBox = new RichTextBox
            {
                IsReadOnly = true,
                IsUndoEnabled = false,
                IsInactiveSelectionHighlightEnabled = false,
                SelectionBrush = Brushes.Transparent,
                SelectionOpacity = 0,
                CaretBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                FontFamily = UiFont,
                FontWeight = FontWeights.Medium,
                FontSize = 13.333,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                IsReadOnlyCaretVisible = false,
                IsTabStop = false,
                IsHitTestVisible = false,
                Cursor = Cursors.Arrow,
                IsManipulationEnabled = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
            };
            TextOptions.SetTextFormattingMode(_chatBox, TextFormattingMode.Display);
            SpellCheck.SetIsEnabled(_chatBox, false);
            _chatBox.ContextMenu = null;
            _chatBox.ContextMenuOpening += (_, e) => e.Handled = true;
            _chatBox.PreviewMouseRightButtonUp += OnChatPreviewMouseRightButtonUp;
            _docChat = NewDoc();
            _docGlobal = NewDoc();
            _chatBox.Document = _docChat;

            _inputBox = new GameChatInputBox();
            _inputBox.Clicked += (_, _) => RequestChatMode();
            _inputBox.SendRequested += (_, _) => { _ = Send(); };
            _inputBorder = new Border
            {
                Height = 45,
                Background = FreezeBrush(InputColor),
                CornerRadius = new CornerRadius(8),
                Child = _inputBox,
            };
            DockPanel.SetDock(_inputBorder, Dock.Bottom);

            _suggestList = new StackPanel();
            _suggestPanel = new Border
            {
                Background = FreezeBrush(Color.FromRgb(30, 30, 34)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(4),
                Visibility = Visibility.Collapsed,
                Child = _suggestList,
            };
            DockPanel.SetDock(_suggestPanel, Dock.Bottom);

            _tabChatText = BuildTabText("Chat", true);
            _tabChatUnderline = BuildTabUnderline(true);
            _tabChat = BuildTab(_tabChatText, _tabChatUnderline);
            _tabChat.MouseLeftButtonUp += (_, _) => SelectTab(TabChatIndex);

            _tabGlobalText = BuildTabText("Global", false);
            _tabGlobalUnderline = BuildTabUnderline(false);
            _tabGlobal = BuildTab(_tabGlobalText, _tabGlobalUnderline);
            _tabGlobal.MouseLeftButtonUp += (_, _) => SelectTab(TabGlobalIndex);

            var tabRow = new Grid { Height = 40 };
            tabRow.ColumnDefinitions.Add(new ColumnDefinition());
            tabRow.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(_tabChat, 0);
            Grid.SetColumn(_tabGlobal, 1);
            var tabDivider = new Border { Height = 1, Background = FreezeBrush(Color.FromRgb(42, 42, 48)), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumnSpan(tabDivider, 2);
            tabRow.Children.Add(_tabChat);
            tabRow.Children.Add(_tabGlobal);
            tabRow.Children.Add(tabDivider);
            DockPanel.SetDock(tabRow, Dock.Top);

            var dock = new DockPanel { Margin = new Thickness(10, 8, 30, 10), LastChildFill = true };
            dock.Children.Add(_inputBorder);
            dock.Children.Add(_suggestPanel);
            dock.Children.Add(tabRow);
            dock.Children.Add(_chatBox);

            _grip = new GameChatResizeGrip { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            _grip.ResizeDragged += OnGripResizeDragged;

            var containerGrid = new Grid { ClipToBounds = true };
            containerGrid.Children.Add(dock);
            containerGrid.Children.Add(_grip);

            _mainContainer = new Border
            {
                Background = FreezeBrush(ContainerColor),
                CornerRadius = new CornerRadius(10),
                Width = ContainerWidth,
                Height = ContainerHeight,
                Child = containerGrid,
            };
            Canvas.SetLeft(_mainContainer, ContainerLeft);
            Canvas.SetTop(_mainContainer, ContainerTop);
            _mainContainer.AddHandler(MouseDownEvent, new MouseButtonEventHandler((_, _) => { if (!_isUserMovingWindow) RequestChatMode(); }), true);
            _rootCanvas.Children.Add(_mainContainer);

            Opacity = ChatOffOpacity;

            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
            _autoSaveTimer.Tick += OnAutoSaveTick;

            _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _leaveTimer.Tick += (_, _) => HideOverlay();

            _healthTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _healthTimer.Tick += (_, _) => Volatile.Write(ref _lastHeartbeatMs, Environment.TickCount64);
            Volatile.Write(ref _lastHeartbeatMs, Environment.TickCount64);
            _healthTimer.Start();

            _trackerTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            _trackerTimer.Tick += OnTrackerTick;

            _client = new GameChatClient();
            _client.OnSystemMessage += (_, text) => AppendSystemMessage(text);
            _client.OnMessage += OnClientMessage;
            _client.OnRejected += OnClientRejected;
            if (_activityWatcher?.Data != null && _activityWatcher.Data.UserId > 0)
                _client.OwnRobloxId = _activityWatcher.Data.UserId;
            else if (App.Settings.Prop.GameChatRobloxUserId > 0)
                _client.OwnRobloxId = App.Settings.Prop.GameChatRobloxUserId;

            SourceInitialized += OnSourceInitializedHandler;
            IsVisibleChanged += (_, _) => _overlayVisible = IsVisible;

            AppendText(GameChatStrings.StartupText);
        }

        public void EnterGame(string jobId)
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(() => EnterGame(jobId)); return; }
            _leaveTimer.Stop();
            _inGame = true;
            string newJob = string.IsNullOrEmpty(jobId) ? "global" : jobId;
            if (_jobId != "global" && newJob != _jobId)
            {
                _docChat = NewDoc();
                if (_selectedTab == TabChatIndex)
                    _chatBox.Document = _docChat;
            }
            _jobId = newJob;
            if (App.Settings.Prop.GameChatRobloxUserId <= 0 && _activityWatcher?.Data?.UserId > 0)
            {
                _client.OwnRobloxId = _activityWatcher.Data.UserId;
                App.Settings.Prop.GameChatRobloxUserId = _activityWatcher.Data.UserId;
                App.Settings.Save();
            }
            _trackerTimer.Start();
            _ = SwitchChannelAsync(_selectedTab == TabGlobalIndex ? "global" : _jobId);
        }

        public void LeaveGame()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(LeaveGame); return; }
            // Voidstrap's original overlay used an ActivityWatcher.IsTeleporting flag here to grant a
            // grace period before hiding, so a teleport between servers wouldn't flicker the overlay off
            // and back on. PhasmaStrap's ActivityWatcher doesn't expose a live "currently teleporting"
            // flag (only a completed-join Data.IsTeleport marker), so we hide immediately instead - a
            // teleport will briefly hide/reshow the overlay rather than staying up through the gap.
            HideOverlay();
        }

        private void HideOverlay()
        {
            _leaveTimer.Stop();
            if (_activityWatcher?.InGame == true)
                return;
            _inGame = false;
            _trackerTimer.Stop();
            _client.Stop();
            if (_isChatting)
                CancelChatMode();
            if (IsVisible)
                Hide();
        }

        public async Task SwitchChannelAsync(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
                return;
            if (_client.ChannelId == channelId && _client.Connected)
                return;
            _client.ChannelId = channelId;
            await _client.RestartAsync(false);
        }

        private void SelectTab(int tab)
        {
            if (_selectedTab == tab)
                return;
            _selectedTab = tab;
            _tabChatText.Foreground = tab == TabChatIndex ? TabActiveBrush : TabIdleBrush;
            _tabChatUnderline.Visibility = tab == TabChatIndex ? Visibility.Visible : Visibility.Collapsed;
            _tabGlobalText.Foreground = tab == TabGlobalIndex ? TabActiveBrush : TabIdleBrush;
            _tabGlobalUnderline.Visibility = tab == TabGlobalIndex ? Visibility.Visible : Visibility.Collapsed;
            _chatBox.Document = tab == TabGlobalIndex ? _docGlobal : _docChat;
            _ = SwitchChannelAsync(tab == TabGlobalIndex ? "global" : _jobId);
        }

        private void OnSourceInitializedHandler(object? sender, EventArgs e)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(_windowHandle, GWL_EXSTYLE);
            SetWindowLong(_windowHandle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private void OnTrackerTick(object? sender, EventArgs e)
        {
            if (_closed)
                return;
            try
            {
                using var process = Process.GetProcessById((int)_robloxPid);
                if (process.HasExited)
                {
                    ApplyTrackerRect(default, false, false);
                    return;
                }
                IntPtr hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero || IsIconic(hwnd) || !GetWindowRect(hwnd, out RECT rect))
                {
                    ApplyTrackerRect(default, false, false);
                    return;
                }
                bool foreground = GetForegroundWindow() == hwnd;
                ApplyTrackerRect(rect, foreground, true);
            }
            catch (ArgumentException)
            {
                ApplyTrackerRect(default, false, false);
            }
        }

        private void ApplyTrackerRect(RECT rect, bool foreground, bool valid)
        {
            if (!valid || !_inGame || !foreground)
            {
                _wasForeground = false;
                if (_isChatting)
                    CancelChatMode();
                if (IsVisible)
                    Hide();
                return;
            }

            if (!_wasForeground && _windowHandle != IntPtr.Zero)
                SetWindowPos(_windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            _wasForeground = true;

            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _dpiScale = dpi > 0 ? dpi : 1.0;
            double rectWidth = rect.Right - rect.Left;
            double rectHeight = rect.Bottom - rect.Top;

            UpdateScale(rectWidth, dpi);
            ClampGeometry(rectWidth, rectHeight, dpi);

            if (!_isUserMovingWindow)
            {
                Left = rect.Left / dpi + _currentOffset.X;
                Top = rect.Top / dpi + _currentOffset.Y;
            }

            if (!IsVisible)
                Show();
        }

        private void ClampGeometry(double rectWidth, double rectHeight, double dpi)
        {
            double availableWidth = Math.Max(MinBaseWidth, rectWidth / dpi);
            double availableHeight = Math.Max(MinBaseHeight, rectHeight / dpi);
            _baseWidth = Math.Clamp(_baseWidth, MinBaseWidth, Math.Min(MaxBaseWidth, availableWidth));
            _baseHeight = Math.Clamp(_baseHeight, MinBaseHeight, Math.Min(MaxBaseHeight, availableHeight));
            ApplyWindowSize();
            double maxX = Math.Max(0, availableWidth - Width);
            double maxY = Math.Max(0, availableHeight - Height);
            _currentOffset = new Point(Math.Clamp(_currentOffset.X, 0, maxX), Math.Clamp(_currentOffset.Y, 0, maxY));
        }

        private void UpdateScale(double rectWidth, double dpi)
        {
            double monitorWidthPx = SystemParameters.PrimaryScreenWidth * dpi;
            if (monitorWidthPx <= 1 || rectWidth <= 1)
                return;
            ApplyScale(rectWidth / monitorWidthPx);
        }

        private void ApplyScale(double target)
        {
            if (double.IsNaN(target) || double.IsInfinity(target))
                return;
            target = Math.Clamp(target, 0.5, 1.0);
            if (Math.Abs(target - _scale) < 0.01)
                return;
            _scale = target;
            _rootScale.ScaleX = _scale;
            _rootScale.ScaleY = _scale;
            ApplyWindowSize();
        }

        private void SetTargetOpacity(float target)
        {
            if (Math.Abs(_targetOpacity - target) < 0.001f && Math.Abs(Opacity - target) < 0.01)
                return;
            _targetOpacity = target;
            Opacity = _targetOpacity;
        }

        private void OnAutoSaveTick(object? sender, EventArgs e)
        {
            _autoSaveTimer.Stop();
            if (!_settingsDirty)
                return;
            long remaining = 5000 - (Environment.TickCount64 - _lastSettingsChangeMs);
            if (remaining > 0)
            {
                _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, remaining));
                _autoSaveTimer.Start();
                return;
            }
            SaveSettingsToDisk();
            _settingsDirty = false;
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(5000);
        }

        private void MarkSettingsDirty()
        {
            _settingsDirty = true;
            _lastSettingsChangeMs = Environment.TickCount64;
            if (!_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(5000);
                _autoSaveTimer.Start();
            }
        }

        private void SaveSettingsToDisk()
        {
            var prop = App.Settings.Prop;
            prop.GameChatOffsetX = (int)_currentOffset.X;
            prop.GameChatOffsetY = (int)_currentOffset.Y;
            prop.GameChatWindowWidth = (int)_baseWidth;
            prop.GameChatWindowHeight = (int)_baseHeight;
            App.Settings.Save();
        }

        private void ApplyWindowSize()
        {
            _mainContainer.Width = ContainerWidth;
            _mainContainer.Height = ContainerHeight;
            Width = (IsWindowHidden ? 170 : _baseWidth) * _scale;
            Height = (IsWindowHidden ? 54 : _baseHeight) * _scale;
        }

        private void ResetToDefaults()
        {
            _baseWidth = DefaultBaseWidth;
            _baseHeight = DefaultBaseHeight;
            ApplyWindowSize();
            _currentOffset = _defaultOffset;
            MarkSettingsDirty();
        }

        public void ToggleVisibility()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(ToggleVisibility); return; }
            IsWindowHidden = !IsWindowHidden;
            if (IsWindowHidden && _isChatting)
                CancelChatMode();
            _mainContainer.Visibility = IsWindowHidden ? Visibility.Collapsed : Visibility.Visible;
            _toggleBtn.IsActive = !IsWindowHidden;
            _toggleBtn.InvalidateVisual();
            _client.SlowPoll = IsWindowHidden;
            ApplyWindowSize();
        }

        private void OnToggleDragged(object? sender, Vector deltaPx)
        {
            _isUserMovingWindow = true;
            double dx = deltaPx.X / _dpiScale;
            double dy = deltaPx.Y / _dpiScale;
            _currentOffset = new Point(_currentOffset.X + dx, _currentOffset.Y + dy);
            Left += dx;
            Top += dy;
        }

        private void OnToggleDragEnded(object? sender, EventArgs e)
        {
            if (!_isUserMovingWindow)
                return;
            _isUserMovingWindow = false;
            MarkSettingsDirty();
        }

        private void OnGripResizeDragged(object? sender, Vector deltaPx)
        {
            double dx = deltaPx.X / _dpiScale / _scale;
            double dy = deltaPx.Y / _dpiScale / _scale;
            _baseWidth = Math.Clamp(_baseWidth + dx, MinBaseWidth, MaxBaseWidth);
            _baseHeight = Math.Clamp(_baseHeight + dy, MinBaseHeight, MaxBaseHeight);
            ApplyWindowSize();
            MarkSettingsDirty();
        }

        public void RequestChatMode()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(RequestChatMode); return; }
            if (IsWindowHidden || _isChatting)
                return;
            _inputBox.CaretIndex = _rawInputText.Length;
            StartChatMode();
        }

        public void StartChatMode()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(StartChatMode); return; }
            _isChatting = true;
            _chatBox.IsHitTestVisible = true;
            SetTargetOpacity(ChatOnOpacity);
            SyncInput();
            ChatModeRequested?.Invoke(this, EventArgs.Empty);
        }

        public void CancelChatMode()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(CancelChatMode); return; }
            _isChatting = false;
            _chatBox.IsHitTestVisible = false;
            SetTargetOpacity(ChatOffOpacity);
            SyncInput();
            ChatModeExited?.Invoke(this, EventArgs.Empty);
        }

        public void AppendTextFromKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (!Dispatcher.CheckAccess()) { DispatchUi(() => AppendTextFromKey(text)); return; }
            int remaining = MaxInputLength - _rawInputText.Length;
            if (remaining <= 0)
                return;
            if (text.Length > remaining)
                text = text.Substring(0, remaining);
            int caret = Math.Min(_inputBox.CaretIndex, _rawInputText.Length);
            _rawInputText = _rawInputText.Insert(caret, text);
            _inputBox.CaretIndex = caret + text.Length;
            SyncInput();
        }

        public void PasteFromClipboard()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(PasteFromClipboard); return; }
            try
            {
                AppendTextFromKey(Clipboard.GetText());
            }
            catch
            {
            }
        }

        public void Backspace()
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(Backspace); return; }
            if (_rawInputText.Length > 0 && _inputBox.CaretIndex > 0)
            {
                _rawInputText = _rawInputText.Remove(_inputBox.CaretIndex - 1, 1);
                _inputBox.CaretIndex--;
                SyncInput();
            }
        }

        public void HandleNavigation(System.Windows.Forms.Keys key)
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(() => HandleNavigation(key)); return; }
            if (key == System.Windows.Forms.Keys.Left)
                _inputBox.CaretIndex = Math.Max(0, _inputBox.CaretIndex - 1);
            else if (key == System.Windows.Forms.Keys.Right)
                _inputBox.CaretIndex = Math.Min(_rawInputText.Length, _inputBox.CaretIndex + 1);
            _inputBox.InvalidateVisual();
        }

        private void SyncInput()
        {
            _inputBox.RawText = _rawInputText;
            _inputBox.IsChatting = _isChatting;
            if (_inputBox.CaretIndex > _rawInputText.Length)
                _inputBox.CaretIndex = _rawInputText.Length;
            _inputBox.InvalidateVisual();
            UpdateSuggestions();
        }

        private void UpdateSuggestions()
        {
            if (_suggestPanel == null || _suggestList == null)
                return;

            string text = _rawInputText.TrimStart();
            bool show = _isChatting && text.StartsWith("/") && !text.Contains(' ');
            if (!show)
            {
                if (_suggestPanel.Visibility != Visibility.Collapsed)
                    _suggestPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _suggestList.Children.Clear();
            int shown = 0;
            foreach (var (token, desc) in GameChatStrings.CommandTokens)
            {
                if (!token.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    continue;
                _suggestList.Children.Add(BuildSuggestionRow(token, desc));
                if (++shown >= 7)
                    break;
            }

            _suggestPanel.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border BuildSuggestionRow(string token, string desc)
        {
            var line = new TextBlock { FontFamily = UiFont, FontSize = 12.5 };
            line.Inlines.Add(new Run(token) { Foreground = FreezeBrush(Color.FromRgb(86, 156, 255)) });
            line.Inlines.Add(new Run("  " + desc) { Foreground = FreezeBrush(Color.FromRgb(176, 184, 196)) });
            return new Border { Padding = new Thickness(6, 4, 6, 4), Child = line };
        }

        private static TextBlock BuildTabText(string text, bool active) => new()
        {
            Text = text,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = active ? TabActiveBrush : TabIdleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static Border BuildTabUnderline(bool active) => new()
        {
            Height = 2,
            Background = FreezeBrush(Color.FromRgb(120, 170, 255)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = active ? Visibility.Visible : Visibility.Collapsed,
        };

        private static Border BuildTab(TextBlock text, Border underline)
        {
            var grid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
            grid.Children.Add(text);
            grid.Children.Add(underline);
            return new Border { Child = grid };
        }

        private FlowDocument NewDoc() => new() { PagePadding = new Thickness(0, 0, 0, 4) };

        public void AppendText(string text)
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(() => AppendText(text)); return; }
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            p.Inlines.Add(new Run(text) { Foreground = Brushes.LightGray });
            _docChat.Blocks.Add(p);
            ScrollToEnd();
        }

        private void AppendSystemMessage(string text)
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(() => AppendSystemMessage(text)); return; }
            var doc = _selectedTab == TabGlobalIndex ? _docGlobal : _docChat;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            p.Inlines.Add(new Run("[System] ") { Foreground = FreezeBrush(Color.FromRgb(150, 150, 160)), FontWeight = FontWeights.SemiBold });
            p.Inlines.Add(new Run(text) { Foreground = FreezeBrush(Color.FromRgb(200, 200, 210)) });
            doc.Blocks.Add(p);
            if (_chatBox.Document == doc)
                ScrollToEnd();
        }

        private void AppendChatMessage(GameChatMessage msg)
        {
            var doc = _client.ChannelId == "global" ? _docGlobal : _docChat;
            if (_mutedUsers.Contains(msg.Sender))
                return;
            if (msg.HasScores && GameChatFilter.ShouldHideMessageByFilter(msg.Scores))
            {
                var hidden = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                hidden.Inlines.Add(new Run(GameChatStrings.MessageHiddenDueToFilterSettings) { Foreground = FreezeBrush(Color.FromRgb(130, 130, 140)), FontStyle = FontStyles.Italic });
                doc.Blocks.Add(hidden);
                if (_chatBox.Document == doc)
                    ScrollToEnd();
                return;
            }

            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            SolidColorBrush nameBrush = GameChatNameColor.GetNameBrush(msg.Sender);

            if (msg.Type == "whisper")
            {
                string label = msg.IsTo ? string.Format(GameChatStrings.WhisperTo, msg.Target) : string.Format(GameChatStrings.WhisperFrom, msg.Sender);
                p.Inlines.Add(new Run("[" + label + "] ") { Foreground = FreezeBrush(Color.FromRgb(200, 140, 255)), FontWeight = FontWeights.SemiBold });
            }

            var nameRun = new Run(msg.Sender) { Foreground = nameBrush, FontWeight = FontWeights.Bold };
            var nameLink = new Hyperlink(nameRun) { Foreground = nameBrush, TextDecorations = null };
            nameLink.Click += (_, _) => ShowProfile(msg.SenderId, msg.Sender);
            p.Inlines.Add(nameLink);
            p.Inlines.Add(new Run(": " + msg.Text) { Foreground = Brushes.WhiteSmoke });

            p.Tag = msg;
            doc.Blocks.Add(p);
            if (_chatBox.Document == doc)
                ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            try
            {
                _chatBox.ScrollToEnd();
            }
            catch
            {
            }
        }

        private void ShowProfile(long userId, string fallbackName)
        {
            if (!Dispatcher.CheckAccess()) { DispatchUi(() => ShowProfile(userId, fallbackName)); return; }
            try
            {
                _profileWindow?.Close();
            }
            catch
            {
            }
            _profileWindow = new GameChatProfileWindow(userId, fallbackName, () => _client.SendBugAsync($"[report] {fallbackName} ({userId})"));
            _profileWindow.Owner = this;
            _profileWindow.Show();
        }

        private static Paragraph? FindParagraph(TextPointer? pointer)
        {
            DependencyObject? obj = pointer?.Parent;
            while (obj != null && obj is not Paragraph)
                obj = LogicalTreeHelper.GetParent(obj);
            return obj as Paragraph;
        }

        private void OnChatPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            TextPointer? position = _chatBox.GetPositionFromPoint(e.GetPosition(_chatBox), true);
            Paragraph? paragraph = FindParagraph(position);
            if (paragraph?.Tag is not GameChatMessage msg)
                return;

            var menu = new ContextMenu();
            var copyMsg = new MenuItem { Header = GameChatStrings.CtxCopyMessage };
            copyMsg.Click += (_, _) => TrySetClipboard(msg.Text);
            var copyUser = new MenuItem { Header = GameChatStrings.CtxCopyUsername };
            copyUser.Click += (_, _) => TrySetClipboard(msg.Sender);
            var copyId = new MenuItem { Header = GameChatStrings.CtxCopyUserId };
            copyId.Click += (_, _) => TrySetClipboard(msg.SenderId.ToString());
            var profile = new MenuItem { Header = GameChatStrings.CtxViewProfile };
            profile.Click += (_, _) => ShowProfile(msg.SenderId, msg.Sender);
            bool muted = _mutedUsers.Contains(msg.Sender);
            var mute = new MenuItem { Header = muted ? GameChatStrings.CtxUnmuteUser : GameChatStrings.CtxMuteUser };
            mute.Click += (_, _) => ToggleMute(msg.Sender, !muted);

            menu.Items.Add(copyMsg);
            menu.Items.Add(copyUser);
            menu.Items.Add(copyId);
            menu.Items.Add(profile);
            menu.Items.Add(mute);
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static void TrySetClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
            }
        }

        private void ToggleMute(string speaker, bool mute)
        {
            if (mute)
            {
                _mutedUsers.Add(speaker);
                AppendSystemMessage(string.Format(GameChatStrings.MutedSpeaker, speaker));
            }
            else
            {
                _mutedUsers.Remove(speaker);
                AppendSystemMessage(string.Format(GameChatStrings.UnmutedSpeaker, speaker));
            }
        }

        private void OnClientMessage(object? sender, GameChatMessage msg)
        {
            DispatchUi(() => AppendChatMessage(msg));
        }

        private void OnClientRejected(object? sender, GameChatRejection rejection)
        {
            DispatchUi(() => AppendSystemMessage(string.IsNullOrEmpty(rejection.Reason) ? GameChatStrings.MessageRejectedUnknown : rejection.Reason));
        }

        public Task RefreshAccountAsync() => Task.CompletedTask;

        public async Task<bool> Send()
        {
            string text = _rawInputText.Trim();
            _rawInputText = "";
            SyncInput();
            CancelChatMode();

            if (string.IsNullOrEmpty(text))
                return false;

            if (text.StartsWith('/'))
            {
                await ProcessCommandAsync(text).ConfigureAwait(true);
                return true;
            }

            await _client.SendMessageAsync(text).ConfigureAwait(true);
            return true;
        }

        private async Task ProcessCommandAsync(string raw)
        {
            string body = raw[1..].Trim();
            int spaceIdx = body.IndexOf(' ');
            string cmd = (spaceIdx < 0 ? body : body[..spaceIdx]).ToLowerInvariant();
            string args = spaceIdx < 0 ? "" : body[(spaceIdx + 1)..].Trim();

            switch (cmd)
            {
                case "help":
                case "?":
                    AppendSystemMessage(GameChatStrings.HelpHeader);
                    foreach (var (command, desc) in GameChatStrings.HelpEntries)
                        AppendSystemMessage($"{command} - {desc}");
                    return;

                case "about":
                    AppendSystemMessage(GameChatStrings.AboutText);
                    return;

                case "reconnect":
                case "rc":
                    await _client.RestartAsync().ConfigureAwait(true);
                    return;

                case "clear":
                    (_selectedTab == TabGlobalIndex ? _docGlobal : _docChat).Blocks.Clear();
                    return;

                case "id":
                    AppendSystemMessage($"{GameChatStrings.CurrentChannelID}: {_client.ChannelId}");
                    return;

                case "w":
                case "whisper":
                {
                    int wSpace = args.IndexOf(' ');
                    if (wSpace < 0)
                    {
                        AppendSystemMessage(GameChatStrings.UsageWhisper);
                        return;
                    }
                    string target = args[..wSpace];
                    string message = args[(wSpace + 1)..].Trim();
                    if (message.Length == 0)
                    {
                        AppendSystemMessage(GameChatStrings.UsageWhisper);
                        return;
                    }
                    await _client.SendWhisperAsync(target, message).ConfigureAwait(true);
                    return;
                }

                case "mute":
                    if (args.Length == 0) { AppendSystemMessage(GameChatStrings.UsageMute); return; }
                    ToggleMute(args, true);
                    return;

                case "unmute":
                    if (args.Length == 0) { AppendSystemMessage(GameChatStrings.UsageUnmute); return; }
                    ToggleMute(args, false);
                    return;

                case "filter":
                    if (args.Length == 0)
                    {
                        AppendSystemMessage(string.Format(GameChatStrings.FilterPreferenceCurrent, GameChatFilter.GetCurrentFilterPreference()));
                        return;
                    }
                    if (!GameChatFilter.ValidFilterPreferences.Contains(args))
                    {
                        AppendSystemMessage(GameChatStrings.UsageFilter);
                        return;
                    }
                    AppendSystemMessage(string.Format(GameChatStrings.FilterPreferenceSet, GameChatFilter.SetPreference(args.ToLowerInvariant())));
                    return;

                case "echo":
                    if (args.Length == 0)
                        return;
                    await _client.SendEchoAsync(args).ConfigureAwait(true);
                    return;

                case "console":
                case "debug":
                    _isDebugConsoleOpen = !_isDebugConsoleOpen;
                    AppendSystemMessage(_isDebugConsoleOpen
                        ? string.Format(GameChatStrings.DebugConsoleInitialized, DateTime.Now)
                        : GameChatStrings.DebugConsoleUseClose);
                    return;

                case "bug":
                    await SendBugReportAsync(args).ConfigureAwait(true);
                    return;

                default:
                    AppendSystemMessage(string.Format(GameChatStrings.UnknownCommand, "/" + cmd));
                    return;
            }
        }

        private async Task SendBugReportAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 5)
            {
                AppendSystemMessage(GameChatStrings.UsageBug);
                return;
            }
            if (_bugBusy || (DateTime.UtcNow - _lastBugSentUtc).TotalSeconds < 60)
            {
                AppendSystemMessage(GameChatStrings.BugCooldown);
                return;
            }
            _bugBusy = true;
            AppendSystemMessage(GameChatStrings.BugSending);
            try
            {
                GameChatBugResult result = await _client.SendBugAsync(text).ConfigureAwait(true);
                _lastBugSentUtc = DateTime.UtcNow;
                AppendSystemMessage(result switch
                {
                    GameChatBugResult.Ok => GameChatStrings.BugSent,
                    GameChatBugResult.RateLimited => GameChatStrings.BugCooldown,
                    GameChatBugResult.NotConnected => GameChatStrings.NotConnected,
                    _ => GameChatStrings.BugFailed,
                });
            }
            finally
            {
                _bugBusy = false;
            }
        }

        private void DispatchUi(Action action)
        {
            if (_closed)
                return;
            _pendingUi.Enqueue(action);
            int count = Interlocked.Increment(ref _pendingUiCount);
            while (count > MaxPendingUiActions && _pendingUi.TryDequeue(out _))
                count = Interlocked.Decrement(ref _pendingUiCount);
            if (Interlocked.Exchange(ref _flushScheduled, 1) != 0)
                return;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushUi));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
            }
        }

        private void FlushUi()
        {
            int processed = 0;
            while (!_closed && processed < MaxUiActionsPerFlush && _pendingUi.TryDequeue(out Action? action))
            {
                Interlocked.Decrement(ref _pendingUiCount);
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LogTag + "::FlushUi", ex);
                }
                processed++;
            }
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_pendingUi.IsEmpty)
                DispatchUiFlush();
        }

        private void DispatchUiFlush()
        {
            if (_closed || Interlocked.Exchange(ref _flushScheduled, 1) != 0)
                return;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushUi));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
            }
        }

        public new void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _autoSaveTimer.Stop();
            _leaveTimer.Stop();
            _healthTimer.Stop();
            _trackerTimer.Stop();
            if (_settingsDirty)
                SaveSettingsToDisk();
            _client.OnMessage -= OnClientMessage;
            _client.OnRejected -= OnClientRejected;
            _client.Stop();
            _client.Dispose();
            _toggleBtn.Shutdown();
            _inputBox.Shutdown();
            _grip.Shutdown();
            try
            {
                _profileWindow?.Close();
            }
            catch
            {
            }
            base.Close();
        }
    }
}
