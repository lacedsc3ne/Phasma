using System.Windows;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Settings page for PhasmaStrap's in-app notification center (<see cref="NotificationCenter"/>):
    /// a master enable switch, per-event-type toggles for the events PhasmaStrap actually has a local
    /// trigger for, and a scrollback of everything shown so far this session.
    /// </summary>
    /// <remarks>
    /// Voidstrap's own NotificationsPage is a live feed pulled from its website account backend
    /// (friend requests/accepts, follows, likes, wishlist updates, reports, warnings/bans, forum
    /// replies, mentions, quest completions, level-ups, black market activity) via WebsiteAuth/
    /// WebsiteNotifications - none of which PhasmaStrap has any equivalent for, since PhasmaStrap has
    /// no social/account backend, quest system, forum, or black market. Those event types are not
    /// ported here. What's kept is the general notification-center framework plus the two event types
    /// PhasmaStrap actually has a local trigger for: joining and leaving a Roblox server, both fired
    /// from ActivityWatcher.OnGameJoin/OnGameLeave (see NotifyIconWrapper).
    /// </remarks>
    public partial class NotificationsPage
    {
        private readonly NotificationsViewModel _viewModel;

        public NotificationsPage()
        {
            _viewModel = new NotificationsViewModel();
            DataContext = _viewModel;
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Detach();
        }
    }
}
