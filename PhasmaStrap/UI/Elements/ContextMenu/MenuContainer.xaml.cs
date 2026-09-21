using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

using PhasmaStrap.Integrations;
using PhasmaStrap.Integrations.FrameGeneration;
using PhasmaStrap.Integrations.Overlays;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public partial class MenuContainer
    {
        private readonly Watcher _watcher;

        private ActivityWatcher? _activityWatcher => _watcher.ActivityWatcher;

        private ServerInformation? _serverInformationWindow;

        private ServerHistory? _gameHistoryWindow;

        private OutputConsole? _outputConsoleWindow;

        private ChatLogs? _chatLogsWindow;

        private RPCWindow? _rpcWindow;

        private readonly DispatcherTimer _sessionTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };

        private int _joinClosestActive;
        private MatchmakerCandidate? _lastClosest;
        private long _lastClosestPlaceId;

        public MenuContainer(Watcher watcher)
        {
            InitializeComponent();

            _watcher = watcher;

            if (_activityWatcher is not null)
            {
                _activityWatcher.OnLogOpen += ActivityWatcher_OnLogOpen;
                _activityWatcher.OnGameJoin += ActivityWatcher_OnGameJoin;
                _activityWatcher.OnGameLeave += ActivityWatcher_OnGameLeave;

                if (!App.Settings.Prop.UseDisableAppPatch)
                    GameHistoryMenuItem.Visibility = Visibility.Visible;
            }

            if (_watcher.RichPresence is not null)
                RichPresenceMenuItem.Visibility = Visibility.Visible;

            PopulateAccounts();

            if (App.Settings.Prop.GameChatEnabled)
                ChatLogsMenuItem.Visibility = Visibility.Visible;

            if (App.Settings.Prop.UseDiscordRichPresence)
                RPCDebugMenuItem.Visibility = Visibility.Visible;

            VersionTextBlock.Text = $"{App.ProjectName} v{App.Version}";

            FlagsTextBlock.Text = $"FastFlags applied: {(App.Settings.Prop.UseFastFlagManager ? App.FastFlags.Prop.Count : 0)}";
            FrameGenMenuItem.IsChecked = FrameGenSettings.ModeIndex > 0;
            OverlayFocusModeMenuItem.IsChecked = App.Settings.Prop.OverlayFocusModeEnabled;

            bool overlaysOn = App.Settings.Prop.OverlayHudEnabled || App.Settings.Prop.Crosshair;
            OverlayFocusModeMenuItem.Visibility = overlaysOn ? Visibility.Visible : Visibility.Collapsed;
            CantSeeOverlaysMenuItem.Visibility = overlaysOn && App.Settings.Prop.OverlayDiagnosticsEnabled ? Visibility.Visible : Visibility.Collapsed;

            TakeScreenshotMenuItem.Visibility = Visibility.Visible;
            SaveReplayMenuItem.Visibility = App.Settings.Prop.InstantReplayEnabled ? Visibility.Visible : Visibility.Collapsed;

            _sessionTimer.Tick += SessionTimer_Tick;
        }

        private void SessionTimer_Tick(object? sender, EventArgs e)
        {
            ActivityData? data = _activityWatcher?.Data;
            if (_activityWatcher?.InGame != true || data is null)
                return;

            PlayTimeTextBlock.Text = $"Play time: {DateTime.Now - data.TimeJoined:hh\\:mm\\:ss}";
            UpdateServerLine(data);

            try
            {
                int pid = _watcher.RobloxProcessId;
                if (pid != 0)
                {
                    using var process = Process.GetProcessById(pid);
                    MemoryTextBlock.Text = $"Roblox memory: {process.WorkingSet64 / 1048576.0:0} MB";
                }
            }
            catch
            {
            }
        }

        private void UpdateServerLine(ActivityData data)
        {
            string region = PhasmaStrap.Utility.ServerRegion.Current;
            string where = region.Length > 0 ? region : data.MachineAddressValid ? data.MachineAddress : "address pending";
            int ping = PhasmaStrap.Utility.ServerPingMonitor.LatestMs;

            ServerTextBlock.Text = $"Server: {data.ServerType} · {where}" + (ping >= 0 ? $" · {ping} ms" : "");
        }

        private async Task UpdateCurrentGameAsync(ActivityData data)
        {
            long universeId = data.UniverseId;
            if (universeId == 0)
                return;

            string name = "";
            string? iconUrl = null;

            try
            {
                UniverseDetails? details = UniverseDetails.LoadFromCache(universeId);
                if (details is null)
                {
                    await UniverseDetails.FetchSingle(universeId);
                    details = UniverseDetails.LoadFromCache(universeId);
                }

                if (details is not null)
                {
                    name = details.Data.Name;
                    iconUrl = details.Thumbnail?.ImageUrl;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MenuContainer::UpdateCurrentGame", $"Universe lookup failed: {ex.Message}");
            }

            if (string.IsNullOrEmpty(name))
            {
                var entry = PlayTimeStore.GetAll().FirstOrDefault(x => x.UniverseId == universeId);
                if (entry is not null)
                {
                    name = entry.Name;
                    iconUrl ??= entry.IconUrl;
                }
            }

            if (string.IsNullOrEmpty(name))
                name = $"Place {data.PlaceId}";

            await Dispatcher.InvokeAsync(() =>
            {
                if (_activityWatcher?.InGame != true || !ReferenceEquals(_activityWatcher.Data, data))
                    return;

                CurrentGameNameTextBlock.Text = name;
                try
                {
                    CurrentGameIcon.Source = string.IsNullOrEmpty(iconUrl) ? null : new BitmapImage(new Uri(iconUrl));
                }
                catch
                {
                    CurrentGameIcon.Source = null;
                }
                CurrentGameMenuItem.Visibility = Visibility.Visible;
            });
        }

        private async Task UpdateClosestServerLabelAsync(ActivityData data)
        {
            if (data.ServerType != ServerType.Public || data.PlaceId == 0)
                return;

            try
            {
                MatchmakerCandidate? best = await Matchmaker.PickBestJobIdAsync(data.PlaceId, exclude: new[] { data.JobId }, maxCandidates: 12);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (_activityWatcher?.InGame != true || !ReferenceEquals(_activityWatcher.Data, data))
                        return;

                    if (best is not null)
                    {
                        _lastClosest = best;
                        _lastClosestPlaceId = data.PlaceId;
                        JoinClosestServerTextBlock.Text = $"Join closest server ({best.DatacenterName}, ~{best.EstimatedPingMs}ms)";
                    }
                    else
                    {
                        JoinClosestServerTextBlock.Text = "Join closest server (none found)";
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MenuContainer::UpdateClosestServer", ex.Message);
            }
        }

        public void ShowServerInformationWindow()
        {
            if (_serverInformationWindow is null)
            {
                _serverInformationWindow = new(_watcher);
                _serverInformationWindow.Closed += (_, _) => _serverInformationWindow = null;
            }

            if (!_serverInformationWindow.IsVisible)
                _serverInformationWindow.ShowDialog();
            else
                _serverInformationWindow.Activate();
        }

        public void ActivityWatcher_OnLogOpen(object? sender, EventArgs e) =>
            Dispatcher.Invoke(() =>
            {
                LogTracerMenuItem.Visibility = Visibility.Visible;
                OutputConsoleMenuItem.Visibility = Visibility.Visible;
            });

        public void ActivityWatcher_OnGameJoin(object? sender, EventArgs e)
        {
            if (_activityWatcher is null)
                return;

            ActivityData data = _activityWatcher.Data;

            Dispatcher.Invoke(() => {
                if (data.ServerType == ServerType.Public)
                {
                    InviteDeeplinkMenuItem.Visibility = Visibility.Visible;
                    JoinClosestServerMenuItem.Visibility = Visibility.Visible;
                    JoinClosestServerTextBlock.Text = "Join closest server (checking...)";
                }

                ServerDetailsMenuItem.Visibility = Visibility.Visible;
                SessionInfoMenuItem.Visibility = Visibility.Visible;
                MeasurePerformanceMenuItem.Visibility = Visibility.Visible;
                UpdateServerLine(data);
                PlayTimeTextBlock.Text = "Play time: 00:00:00";
                _sessionTimer.Start();
            });

            _ = UpdateCurrentGameAsync(data);
            _ = UpdateClosestServerLabelAsync(data);
        }

        public void ActivityWatcher_OnGameLeave(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                _sessionTimer.Stop();
                InviteDeeplinkMenuItem.Visibility = Visibility.Collapsed;
                ServerDetailsMenuItem.Visibility = Visibility.Collapsed;
                JoinClosestServerMenuItem.Visibility = Visibility.Collapsed;
                SessionInfoMenuItem.Visibility = Visibility.Collapsed;
                MeasurePerformanceMenuItem.Visibility = Visibility.Collapsed;
                CurrentGameMenuItem.Visibility = Visibility.Collapsed;
                CurrentGameIcon.Source = null;
                CurrentGameNameTextBlock.Text = "";
                MemoryTextBlock.Text = "Roblox memory: 0 MB";
                _lastClosest = null;

                _serverInformationWindow?.Close();
            });
        }

        private async void JoinClosestServerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ActivityData? data = _activityWatcher?.Data;
            if (data is null || data.PlaceId == 0)
                return;

            if (Interlocked.Exchange(ref _joinClosestActive, 1) != 0)
                return;

            JoinClosestServerMenuItem.IsEnabled = false;

            try
            {
                MatchmakerCandidate? best = _lastClosest is not null && _lastClosestPlaceId == data.PlaceId
                    ? _lastClosest
                    : await Matchmaker.PickBestJobIdAsync(data.PlaceId, exclude: new[] { data.JobId }, maxCandidates: 12);

                if (best is null)
                {
                    Frontend.ShowMessageBox("No other public servers were found for this game.", MessageBoxImage.Information);
                    return;
                }

                PhasmaStrap.Utility.RobloxLaunch.Join(data.PlaceId, best.JobId);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("MenuContainer::JoinClosestServer", ex);
                Frontend.ShowMessageBox($"Could not join: {ex.Message}", MessageBoxImage.Error);
            }
            finally
            {
                JoinClosestServerMenuItem.IsEnabled = true;
                Interlocked.Exchange(ref _joinClosestActive, 0);
            }
        }

        private void TakeScreenshotMenuItem_Click(object sender, RoutedEventArgs e) => _watcher.TakeScreenshot();

        private void SaveReplayMenuItem_Click(object sender, RoutedEventArgs e) => _watcher.SaveInstantReplay();

        private void CleanRamMenuItem_Click(object sender, RoutedEventArgs e) => _watcher.CleanRamNow();

        private void FrameGenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Integrations.FrameGeneration.FrameGenManager.SetMode(FrameGenMenuItem.IsChecked ? 1 : 0, true);
            FrameGenMenuItem.IsChecked = Integrations.FrameGeneration.FrameGenSettings.ModeIndex > 0;
        }

        private void OverlayFocusModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _watcher.ToggleOverlayFocusMode();
            OverlayFocusModeMenuItem.IsChecked = App.Settings.Prop.OverlayFocusModeEnabled;
        }

        private void CantSeeOverlaysMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OverlayDiagnostics.RaiseOverlayWindows();
                Frontend.ShowMessageBox(OverlayDiagnostics.BuildReport(), MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"Could not build the overlay diagnostics: {ex.Message}", MessageBoxImage.Error);
            }
        }

        private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(Paths.Process, "-settings");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("MenuContainer::OpenSettings", ex);
            }
        }

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            HWND hWnd = (HWND)new WindowInteropHelper(this).Handle;

            int exStyle = PInvoke.GetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            exStyle |= 0x00000080;
            PInvoke.SetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
        }

        private void Window_Closed(object sender, EventArgs e) => App.Logger.WriteLine("MenuContainer::Window_Closed", "Context menu container closed");

        private void RichPresenceMenuItem_Click(object sender, RoutedEventArgs e) => _watcher.RichPresence?.SetVisibility(((MenuItem)sender).IsChecked);

        private void InviteDeeplinkMenuItem_Click(object sender, RoutedEventArgs e) => CopyInviteLink();

        public void CopyInviteLink()
        {
            string? link = _activityWatcher?.Data.GetInviteDeeplink();
            if (string.IsNullOrEmpty(link))
                return;

            Clipboard.SetDataObject(link);
            NotificationCenter.Notify("Invite link copied", "Anyone with PhasmaStrap or Roblox can open it to join your server.", NotificationCategory.General, kind: NotificationKindId.InviteLink);
        }

        public void BringRobloxToFront()
        {
            int pid = _watcher.RobloxProcessId;
            if (pid == 0)
                return;

            using Process process = Process.GetProcessById(pid);
            IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero)
                return;

            if (IsIconic(window))
                ShowWindow(window, 9 );

            SetForegroundWindow(window);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private void ServerDetailsMenuItem_Click(object sender, RoutedEventArgs e) => ShowServerInformationWindow();

        private void LogTracerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string? location = _activityWatcher?.LogLocation;

            if (location is not null)
                Utilities.ShellExecute(location);
        }

        private void MeasurePerformanceMenuItem_Click(object sender, RoutedEventArgs e) =>
            PhasmaStrap.Utility.PerformanceRuns.WriteRequest(new PhasmaStrap.Utility.MeasureRequest { Label = "Measured from the tray", Seconds = 60 });

        private void PopulateAccounts()
        {
            var accounts = PhasmaStrap.Utility.AccountQuickSwitch.List(Paths.AccountBackups);

            SwitchAccountMenuItem.Items.Clear();
            SwitchAccountMenuItem.Visibility = accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            long currentUserId = _activityWatcher?.Data?.UserId ?? 0;

            foreach (var account in accounts)
            {
                bool current = account.UserId == currentUserId;

                var item = new MenuItem
                {
                    Header = current ? $"{account.Title}  -  playing now" : account.Title,
                    IsEnabled = !current,
                    Tag = account,
                };

                if (!string.IsNullOrWhiteSpace(account.Note))
                    item.ToolTip = account.Note;

                item.Click += SwitchAccountItem_Click;
                SwitchAccountMenuItem.Items.Add(item);
            }
        }

        private void SwitchAccountMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, SwitchAccountMenuItem))
                PopulateAccounts();
        }

        private void SwitchAccountItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.Tag is not PhasmaStrap.Utility.AccountQuickSwitch.Account account)
                return;

            MessageBoxResult result = Frontend.ShowMessageBox(
                $"Switch to {account.Title}?\n\nRoblox closes (you leave the game you are in), signs in as this account and starts again.",
                MessageBoxImage.Question,
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                PhasmaStrap.Utility.FlagProfileSession.MarkIntentionalRestart();

                Process.Start(Paths.Process, $"-switchaccount {account.UserId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("MenuContainer::SwitchAccount", ex);
            }
        }

        private void CloseRobloxMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = Frontend.ShowMessageBox(
                Strings.ContextMenu_CloseRobloxMessage,
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo
            );

            if (result != MessageBoxResult.Yes)
                return;

            _watcher.KillRobloxProcess();
        }

        private void JoinLastServerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_activityWatcher is null)
                throw new ArgumentNullException(nameof(_activityWatcher));

            if (_gameHistoryWindow is null)
            {
                _gameHistoryWindow = new(_activityWatcher);
                _gameHistoryWindow.Closed += (_, _) => _gameHistoryWindow = null;
            }

            if (!_gameHistoryWindow.IsVisible)
                _gameHistoryWindow.ShowDialog();
            else
                _gameHistoryWindow.Activate();
        }

        private void OutputConsoleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_activityWatcher is null)
                throw new ArgumentNullException(nameof(_activityWatcher));

            if (_outputConsoleWindow is null)
            {
                _outputConsoleWindow = new(_activityWatcher);
                _outputConsoleWindow.Closed += (_, _) => _outputConsoleWindow = null;
            }

            if (!_outputConsoleWindow.IsVisible)
                _outputConsoleWindow.ShowDialog();
            else
                _outputConsoleWindow.Activate();
        }

        private void ChatLogsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_chatLogsWindow is null)
            {
                _chatLogsWindow = new();
                _chatLogsWindow.Closed += (_, _) => _chatLogsWindow = null;
            }

            if (!_chatLogsWindow.IsVisible)
                _chatLogsWindow.ShowDialog();
            else
                _chatLogsWindow.Activate();
        }

        private void DonateMenuItem_Click(object sender, RoutedEventArgs e) => Utilities.ShellExecute(App.ProjectDonateLink);

        private void RPCDebugMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_watcher.RichPresence is null)
                return;

            if (_rpcWindow is null)
            {
                _rpcWindow = new(_watcher.RichPresence);
                _rpcWindow.Closed += (_, _) => _rpcWindow = null;
            }

            if (!_rpcWindow.IsVisible)
                _rpcWindow.ShowDialog();
            else
                _rpcWindow.Activate();
        }
    }
}
