using PhasmaStrap.UI.Elements.ContextMenu;

namespace PhasmaStrap.UI
{
    /// <summary>
    /// What triggered a notification. Used to gate individual notifications behind their own
    /// per-event-type setting (see <see cref="Models.Persistable.Settings.NotificationsJoinToastEnabled"/>
    /// etc.) and, in future, to pick an icon/accent per category.
    /// </summary>
    public enum NotificationCategory
    {
        General,
        GameJoin,
        GameLeave
    }

    /// <summary>
    /// A single entry in the notification center's history, as shown on the Notifications settings
    /// page. History is kept in memory only for the lifetime of the process - PhasmaStrap has no
    /// notification backend/server to source a persistent history from (unlike Voidstrap's website
    /// account notification feed, which this deliberately does not attempt to replicate - see
    /// NotificationsPage remarks).
    /// </summary>
    public sealed class NotificationRecord
    {
        public string Title { get; }
        public string Message { get; }
        public NotificationCategory Category { get; }
        public DateTime Timestamp { get; }

        public NotificationRecord(string title, string message, NotificationCategory category)
        {
            Title = title;
            Message = message;
            Category = category;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// PhasmaStrap's in-app notification center. Shows a custom animated toast popup
    /// (<see cref="NotificationToast"/>) sliding in from the corner of the screen, and keeps a capped,
    /// session-only history of everything shown so far for the Notifications settings page.
    /// </summary>
    /// <remarks>
    /// This is intentionally independent of <see cref="NotifyIconWrapper.ShowAlert"/>, which drives the
    /// separate Windows balloon-tip alerts already shown from the tray icon (server join info, etc).
    /// Routing through the balloon-tip mechanism instead of building this was considered, but a
    /// balloon tip can't be styled to match the app, doesn't queue/animate multiple notifications
    /// nicely, and Windows increasingly suppresses/throttles balloon tips outside of focus-assist
    /// exemptions - a custom always-on-top WPF window (matching Voidstrap's own approach) avoids all of
    /// that at the cost of one more window class, which is a good trade for a feature meant to be the
    /// primary in-app notification surface going forward.
    /// </remarks>
    public static class NotificationCenter
    {
        private const int MaxHistory = 50;

        private static readonly object s_lock = new();
        private static readonly LinkedList<NotificationRecord> s_history = new();

        private static NotificationToast? s_toast;

        /// <summary>
        /// Raised (off the UI thread - marshal before touching UI state) whenever <see cref="History"/>
        /// changes, so the settings page can refresh its list.
        /// </summary>
        public static event EventHandler? HistoryChanged;

        public static IReadOnlyList<NotificationRecord> History
        {
            get
            {
                lock (s_lock)
                    return s_history.ToArray();
            }
        }

        /// <summary>
        /// Shows a toast notification and records it in <see cref="History"/>, provided the master
        /// switch and (for <see cref="NotificationCategory.GameJoin"/>/<see cref="NotificationCategory.GameLeave"/>)
        /// the relevant per-event-type setting are both enabled.
        /// </summary>
        public static void Notify(string title, string message, NotificationCategory category = NotificationCategory.General, double durationSeconds = 5)
        {
            if (!App.Settings.Prop.NotificationsEnabled)
                return;

            if (category == NotificationCategory.GameJoin && !App.Settings.Prop.NotificationsJoinToastEnabled)
                return;

            if (category == NotificationCategory.GameLeave && !App.Settings.Prop.NotificationsLeaveToastEnabled)
                return;

            var record = new NotificationRecord(title, message, category);

            lock (s_lock)
            {
                s_history.AddFirst(record);

                while (s_history.Count > MaxHistory)
                    s_history.RemoveLast();
            }

            HistoryChanged?.Invoke(null, EventArgs.Empty);

            ShowToast(title, message, durationSeconds);
        }

        public static void ClearHistory()
        {
            lock (s_lock)
                s_history.Clear();

            HistoryChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void ShowToast(string title, string message, double durationSeconds)
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

                    s_toast.ShowNotification(title, message, durationSeconds);
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
