using PhasmaStrap.UI.Elements.ContextMenu;

namespace PhasmaStrap.UI
{
    public enum NotificationCategory
    {
        General,
        GameJoin,
        GameLeave
    }

    public enum NotificationKindId
    {
        Other,
        ServerJoined,
        ServerLeft,
        ServerRegion,
        RobloxClosed,
        AutoRejoin,
        FastFlagProfile,
        RamCleaned,
        OverlayFocusMode,
        PerformanceRun,
        InviteLink,
        Screenshot,
        Replay,
        ReplayNotReady,
        FriendOnline,
        FriendPlaying,
        AccountGuard,
        ProxyCertificate,
        FlagsRemoved
    }

    public sealed class NotificationRecord
    {
        public string Title { get; }
        public string Message { get; }
        public NotificationCategory Category { get; }
        public DateTime Timestamp { get; }

        public Action? OnClick { get; }

        public NotificationKindId Kind { get; }

        public NotificationRecord(string title, string message, NotificationCategory category, Action? onClick = null, NotificationKindId kind = NotificationKindId.Other)
        {
            Title = title;
            Message = message;
            Category = category;
            Timestamp = DateTime.Now;
            OnClick = onClick;
            Kind = kind;
        }
    }

    public static class NotificationCenter
    {
        private const int MaxHistory = 50;

        private static readonly object s_lock = new();
        private static readonly LinkedList<NotificationRecord> s_history = new();

        private static NotificationToast? s_toast;

        public static event EventHandler? HistoryChanged;

        public static IReadOnlyList<NotificationRecord> History
        {
            get
            {
                lock (s_lock)
                    return s_history.ToArray();
            }
        }

        public static bool IsKindEnabled(NotificationKindId kind) => kind switch
        {
            NotificationKindId.ServerJoined => App.Settings.Prop.NotificationsJoinToastEnabled,
            NotificationKindId.ServerLeft => App.Settings.Prop.NotificationsLeaveToastEnabled,
            NotificationKindId.ServerRegion => App.Settings.Prop.NotificationServerRegionEnabled,
            NotificationKindId.RobloxClosed => App.Settings.Prop.NotificationRobloxClosedEnabled,
            NotificationKindId.AutoRejoin => App.Settings.Prop.NotificationAutoRejoinEnabled,
            NotificationKindId.FastFlagProfile => App.Settings.Prop.NotificationFastFlagProfileEnabled,
            NotificationKindId.RamCleaned => App.Settings.Prop.NotificationRamCleanedEnabled,
            NotificationKindId.OverlayFocusMode => App.Settings.Prop.NotificationOverlayFocusEnabled,
            NotificationKindId.PerformanceRun => App.Settings.Prop.NotificationPerformanceRunEnabled,
            NotificationKindId.InviteLink => App.Settings.Prop.NotificationInviteLinkEnabled,
            NotificationKindId.Screenshot => App.Settings.Prop.NotificationScreenshotEnabled,
            NotificationKindId.Replay => App.Settings.Prop.NotificationReplayEnabled,
            NotificationKindId.ReplayNotReady => App.Settings.Prop.NotificationReplayNotReadyEnabled,
            NotificationKindId.FriendOnline => App.Settings.Prop.NotificationFriendOnlineEnabled,
            NotificationKindId.FriendPlaying => App.Settings.Prop.NotificationFriendPlayingEnabled,
            NotificationKindId.AccountGuard => App.Settings.Prop.NotificationAccountGuardEnabled,
            NotificationKindId.ProxyCertificate => App.Settings.Prop.NotificationProxyCertificateEnabled,
            NotificationKindId.FlagsRemoved => App.Settings.Prop.NotificationFlagsRemovedEnabled,
            _ => true
        };

        public static void SetKindEnabled(NotificationKindId kind, bool enabled)
        {
            switch (kind)
            {
                case NotificationKindId.ServerJoined: App.Settings.Prop.NotificationsJoinToastEnabled = enabled; break;
                case NotificationKindId.ServerLeft: App.Settings.Prop.NotificationsLeaveToastEnabled = enabled; break;
                case NotificationKindId.ServerRegion: App.Settings.Prop.NotificationServerRegionEnabled = enabled; break;
                case NotificationKindId.RobloxClosed: App.Settings.Prop.NotificationRobloxClosedEnabled = enabled; break;
                case NotificationKindId.AutoRejoin: App.Settings.Prop.NotificationAutoRejoinEnabled = enabled; break;
                case NotificationKindId.FastFlagProfile: App.Settings.Prop.NotificationFastFlagProfileEnabled = enabled; break;
                case NotificationKindId.RamCleaned: App.Settings.Prop.NotificationRamCleanedEnabled = enabled; break;
                case NotificationKindId.OverlayFocusMode: App.Settings.Prop.NotificationOverlayFocusEnabled = enabled; break;
                case NotificationKindId.PerformanceRun: App.Settings.Prop.NotificationPerformanceRunEnabled = enabled; break;
                case NotificationKindId.InviteLink: App.Settings.Prop.NotificationInviteLinkEnabled = enabled; break;
                case NotificationKindId.Screenshot: App.Settings.Prop.NotificationScreenshotEnabled = enabled; break;
                case NotificationKindId.Replay: App.Settings.Prop.NotificationReplayEnabled = enabled; break;
                case NotificationKindId.ReplayNotReady: App.Settings.Prop.NotificationReplayNotReadyEnabled = enabled; break;
                case NotificationKindId.FriendOnline: App.Settings.Prop.NotificationFriendOnlineEnabled = enabled; break;
                case NotificationKindId.FriendPlaying: App.Settings.Prop.NotificationFriendPlayingEnabled = enabled; break;
                case NotificationKindId.AccountGuard: App.Settings.Prop.NotificationAccountGuardEnabled = enabled; break;
                case NotificationKindId.ProxyCertificate: App.Settings.Prop.NotificationProxyCertificateEnabled = enabled; break;
                case NotificationKindId.FlagsRemoved: App.Settings.Prop.NotificationFlagsRemovedEnabled = enabled; break;
            }
        }

        public static void Notify(string title, string message, NotificationCategory category = NotificationCategory.General, double durationSeconds = 5, Action? onClick = null, string? actionText = null, Action? action = null, NotificationKindId kind = NotificationKindId.Other)
        {
            if (!App.Settings.Prop.NotificationsEnabled)
                return;

            NotificationKindId effective = kind != NotificationKindId.Other
                ? kind
                : category switch
                {
                    NotificationCategory.GameJoin => NotificationKindId.ServerJoined,
                    NotificationCategory.GameLeave => NotificationKindId.ServerLeft,
                    _ => NotificationKindId.Other
                };

            if (!IsKindEnabled(effective))
                return;

            var record = new NotificationRecord(title, message, category, onClick, effective);

            lock (s_lock)
            {
                s_history.AddFirst(record);

                while (s_history.Count > MaxHistory)
                    s_history.RemoveLast();
            }

            HistoryChanged?.Invoke(null, EventArgs.Empty);

            if (!App.Settings.Prop.DoNotDisturbEnabled)
                ShowToast(title, message, effective, durationSeconds, onClick, actionText, action);
        }

        public static Action RevealFile(string path) => () =>
        {
            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                    Process.Start("explorer.exe", Path.GetDirectoryName(path) ?? path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("NotificationCenter::RevealFile", $"Could not open '{path}': {ex.Message}");
            }
        };

        public static void ClearHistory()
        {
            lock (s_lock)
                s_history.Clear();

            HistoryChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void ShowToast(string title, string message, NotificationKindId kind, double durationSeconds, Action? onClick, string? actionText, Action? action)
        {
            var app = System.Windows.Application.Current;

            if (app is null)
                return;

            void ShowOnDispatcher()
            {
                try
                {
                    if (s_toast is null || !s_toast.IsUsable)
                        s_toast = new NotificationToast();

                    s_toast.ShowNotification(title, message, kind, durationSeconds, onClick, actionText, action);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("NotificationCenter::ShowToast", ex);
                }
            }

            if (app.Dispatcher.CheckAccess())
                ShowOnDispatcher();
            else
                app.Dispatcher.BeginInvoke(new Action(ShowOnDispatcher));
        }
    }
}
