using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.About;
using PhasmaStrap.UI.Elements.ContextMenu;

namespace PhasmaStrap.UI
{
    public class NotifyIconWrapper : IDisposable
    {
        private bool _disposing = false;

        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

        private readonly MenuContainer _menuContainer;

        private readonly Watcher _watcher;

        private ActivityWatcher? _activityWatcher => _watcher.ActivityWatcher;

        EventHandler? _alertClickHandler;

        public NotifyIconWrapper(Watcher watcher)
        {
            App.Logger.WriteLine("NotifyIconWrapper::NotifyIconWrapper", "Initializing notification area icon");

            _watcher = watcher;

            _notifyIcon = new(new System.ComponentModel.Container())
            {
                Icon = Properties.Resources.IconPhasmaStrap,
                Text = App.ProjectName,
                Visible = true
            };

            _notifyIcon.MouseClick += MouseClickEventHandler;
            _notifyIcon.MouseDoubleClick += MouseDoubleClickEventHandler;

            if (_activityWatcher is not null && App.Settings.Prop.ShowServerDetails)
                _activityWatcher.OnGameJoin += OnGameJoin;

            if (_activityWatcher is not null)
            {
                _activityWatcher.OnGameJoin += OnGameJoinToast;
                _activityWatcher.OnGameLeave += OnGameLeaveToast;
            }

            _menuContainer = new(_watcher);
            _menuContainer.Show();
        }

        #region Context menu
        public void MouseClickEventHandler(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Right)
                return;

            OpenMenu();
        }

        private void OpenMenu()
        {
            _menuContainer.Activate();
            _menuContainer.ContextMenu.IsOpen = true;
        }

        public void MouseDoubleClickEventHandler(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return;

            TrayDoubleClickAction action = App.Settings.Prop.TrayDoubleClickAction;
            App.Logger.WriteLine("NotifyIconWrapper::DoubleClick", $"Tray icon double-clicked: {action}");

            try
            {
                switch (action)
                {
                    case TrayDoubleClickAction.OpenSettings:
                        Process.Start(Paths.Process, "-settings");
                        break;

                    case TrayDoubleClickAction.ShowRoblox:
                        _menuContainer.BringRobloxToFront();
                        break;

                    case TrayDoubleClickAction.OpenMenu:
                        OpenMenu();
                        break;

                    case TrayDoubleClickAction.TakeScreenshot:
                        _watcher.TakeScreenshot();
                        break;

                    case TrayDoubleClickAction.SaveReplay:
                        _watcher.SaveInstantReplay();
                        break;

                    case TrayDoubleClickAction.ServerDetails:
                        _menuContainer.ShowServerInformationWindow();
                        break;

                    case TrayDoubleClickAction.CopyInviteLink:
                        _menuContainer.CopyInviteLink();
                        break;

                    case TrayDoubleClickAction.CleanRam:
                        _watcher.CleanRamNow();
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NotifyIconWrapper::DoubleClick", ex);
            }
        }
        #endregion

        #region Activity handlers
        public async void OnGameJoin(object? sender, EventArgs e)
        {
            if (_activityWatcher is null)
                return;

            string? serverLocation = await _activityWatcher.Data.QueryServerLocation();

            if (string.IsNullOrEmpty(serverLocation))
            {
                ShowAlert(
                    string.Format(Strings.Dialog_Connectivity_UnableToConnect, "ipinfo.io"),
                    Strings.ActivityWatcher_LocationQueryFailed,
                    10,
                    null
                );

                return;
            }

            string title = _activityWatcher.Data.ServerType switch
            {
                ServerType.Public => Strings.ContextMenu_ServerInformation_Notification_Title_Public,
                ServerType.Private => Strings.ContextMenu_ServerInformation_Notification_Title_Private,
                ServerType.Reserved => Strings.ContextMenu_ServerInformation_Notification_Title_Reserved,
                _ => ""
            };

            ShowAlert(
                title,
                String.Format(Strings.ContextMenu_ServerInformation_Notification_Text, serverLocation),
                10,
                (_, _) => _menuContainer.ShowServerInformationWindow()
            );
        }

        private void OnGameJoinToast(object? sender, EventArgs e)
        {
            NotificationCenter.Notify(
                Strings.Menu_Notifications_Event_GameJoin_Title,
                Strings.Menu_Notifications_Event_GameJoin_Message,
                NotificationCategory.GameJoin,
                kind: NotificationKindId.ServerJoined
            );
        }

        private void OnGameLeaveToast(object? sender, EventArgs e)
        {
            NotificationCenter.Notify(
                Strings.Menu_Notifications_Event_GameLeave_Title,
                Strings.Menu_Notifications_Event_GameLeave_Message,
                NotificationCategory.GameLeave,
                kind: NotificationKindId.ServerLeft
            );
        }
        #endregion

        public void ShowAlert(string caption, string message, int duration, EventHandler? clickHandler)
        {
            string id = Guid.NewGuid().ToString()[..8];

            string LOG_IDENT = $"NotifyIconWrapper::ShowAlert.{id}";

            App.Logger.WriteLine(LOG_IDENT, $"Showing alert for {duration} seconds (clickHandler={clickHandler is not null})");
            App.Logger.WriteLine(LOG_IDENT, $"{caption}: {message.Replace("\n", "\\n")}");

            _notifyIcon.BalloonTipTitle = caption;
            _notifyIcon.BalloonTipText = message;

            if (_alertClickHandler is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Previous alert still present, erasing click handler");
                _notifyIcon.BalloonTipClicked -= _alertClickHandler;
            }

            _alertClickHandler = clickHandler;
            _notifyIcon.BalloonTipClicked += clickHandler;

            _notifyIcon.ShowBalloonTip(duration);

            Task.Run(async () =>
            {
                await Task.Delay(duration * 1000);

                _notifyIcon.BalloonTipClicked -= clickHandler;

                App.Logger.WriteLine(LOG_IDENT, "Duration over, erasing current click handler");

                if (_alertClickHandler == clickHandler)
                    _alertClickHandler = null;
                else
                    App.Logger.WriteLine(LOG_IDENT, "Click handler has been overridden by another alert");
            });
        }

        public void Dispose()
        {
            if (_disposing)
                return;

            _disposing = true;

            App.Logger.WriteLine("NotifyIconWrapper::Dispose", "Disposing NotifyIcon");

            _menuContainer.Dispatcher.Invoke(_menuContainer.Close);
            _notifyIcon.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
