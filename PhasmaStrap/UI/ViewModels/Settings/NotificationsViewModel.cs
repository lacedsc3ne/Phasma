using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class NotificationKind : NotifyPropertyChangedViewModel
    {
        public string Name { get; init; } = "";

        public string Trigger { get; init; } = "";

        public Func<bool>? Read { get; init; }

        public Action<bool>? Write { get; init; }

        public Action? Changed { get; init; }

        public bool HasToggle => Read is not null && Write is not null;

        public Wpf.Ui.Common.SymbolRegular Symbol { get; init; }

        public System.Windows.Media.Brush? Accent { get; init; }

        public System.Windows.Media.Brush? Tint { get; init; }

        public bool IsOn
        {
            get => Read?.Invoke() ?? false;
            set
            {
                Write?.Invoke(value);
                OnPropertyChanged(nameof(IsOn));
                Changed?.Invoke();
            }
        }

        public void Refresh() => OnPropertyChanged(nameof(IsOn));
    }

    public sealed class NotificationKindGroup
    {
        public string Name { get; init; } = "";

        public ObservableCollection<NotificationKind> Kinds { get; } = new();
    }

    public class NotificationsViewModel : NotifyPropertyChangedViewModel
    {
        public NotificationsViewModel()
        {
            BuildCatalogue();
            RefreshHistory();
            NotificationCenter.HistoryChanged += OnHistoryChanged;
        }

        public bool NotificationsEnabled
        {
            get => App.Settings.Prop.NotificationsEnabled;
            set
            {
                App.Settings.Prop.NotificationsEnabled = value;
                OnPropertyChanged(nameof(NotificationsEnabled));
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public bool NotificationsJoinToastEnabled
        {
            get => App.Settings.Prop.NotificationsJoinToastEnabled;
            set
            {
                App.Settings.Prop.NotificationsJoinToastEnabled = value;
                OnPropertyChanged(nameof(NotificationsJoinToastEnabled));
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public bool NotificationsLeaveToastEnabled
        {
            get => App.Settings.Prop.NotificationsLeaveToastEnabled;
            set
            {
                App.Settings.Prop.NotificationsLeaveToastEnabled = value;
                OnPropertyChanged(nameof(NotificationsLeaveToastEnabled));
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public bool DoNotDisturbEnabled
        {
            get => App.Settings.Prop.DoNotDisturbEnabled;
            set
            {
                App.Settings.Prop.DoNotDisturbEnabled = value;
                OnPropertyChanged(nameof(DoNotDisturbEnabled));
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public string[] NotificationPositionOptions { get; } = Elements.ContextMenu.NotificationToast.PositionLabels;

        public int NotificationPositionIndex
        {
            get
            {
                int index = Array.IndexOf(Elements.ContextMenu.NotificationToast.Positions, App.Settings.Prop.NotificationPosition);
                return index < 0 ? 0 : index;
            }
            set
            {
                if (value < 0 || value >= Elements.ContextMenu.NotificationToast.Positions.Length)
                    return;

                App.Settings.Prop.NotificationPosition = Elements.ContextMenu.NotificationToast.Positions[value];
                OnPropertyChanged(nameof(NotificationPositionIndex));
                OnPropertyChanged(nameof(PlacementSummary));
            }
        }

        public int NotificationOffsetX
        {
            get => App.Settings.Prop.NotificationOffsetX;
            set
            {
                App.Settings.Prop.NotificationOffsetX = value;
                OnPropertyChanged(nameof(NotificationOffsetX));
                OnPropertyChanged(nameof(PlacementSummary));
            }
        }

        public int NotificationOffsetY
        {
            get => App.Settings.Prop.NotificationOffsetY;
            set
            {
                App.Settings.Prop.NotificationOffsetY = value;
                OnPropertyChanged(nameof(NotificationOffsetY));
                OnPropertyChanged(nameof(PlacementSummary));
            }
        }

        public int NotificationWidth
        {
            get => App.Settings.Prop.NotificationWidth;
            set
            {
                App.Settings.Prop.NotificationWidth = value;
                OnPropertyChanged(nameof(NotificationWidth));
                OnPropertyChanged(nameof(PlacementSummary));
            }
        }

        public string PlacementSummary =>
            $"{NotificationPositionOptions[NotificationPositionIndex]}, {NotificationOffsetX} across and {NotificationOffsetY} down from that corner, {NotificationWidth} wide.";

        public ICommand ResetPlacementCommand => new RelayCommand(() =>
        {
            App.Settings.Prop.NotificationPosition = "TopRight";
            App.Settings.Prop.NotificationOffsetX = 10;
            App.Settings.Prop.NotificationOffsetY = 10;
            App.Settings.Prop.NotificationWidth = 420;

            OnPropertyChanged(nameof(NotificationPositionIndex));
            OnPropertyChanged(nameof(NotificationOffsetX));
            OnPropertyChanged(nameof(NotificationOffsetY));
            OnPropertyChanged(nameof(NotificationWidth));
            OnPropertyChanged(nameof(PlacementSummary));
        });

        public string SummaryText =>
            NotificationsEnabled
                ? $"Notifications on, server join {OnOff(NotificationsJoinToastEnabled)}, server leave {OnOff(NotificationsLeaveToastEnabled)}, Do Not Disturb {OnOff(DoNotDisturbEnabled)}"
                : "Notifications off, so nothing pops up while you play";

        private static string OnOff(bool value) => value ? "on" : "off";

        public ObservableCollection<NotificationKindGroup> Catalogue { get; } = new();

        public ObservableCollection<NotificationRecord> History { get; } = new();

        public bool HasHistory => History.Count > 0;

        public ICommand ClearHistoryCommand => new RelayCommand(NotificationCenter.ClearHistory);

        public ICommand SendTestNotificationCommand => new RelayCommand(() =>
            NotificationCenter.Notify("Test notification", "This is what a PhasmaStrap toast looks like.", NotificationCategory.General));

        public void Detach()
        {
            NotificationCenter.HistoryChanged -= OnHistoryChanged;
        }

        public void Attach()
        {
            NotificationCenter.HistoryChanged -= OnHistoryChanged;
            NotificationCenter.HistoryChanged += OnHistoryChanged;
            RefreshHistory();

            foreach (NotificationKindGroup group in Catalogue)
                foreach (NotificationKind kind in group.Kinds)
                    kind.Refresh();

            OnPropertyChanged(nameof(SummaryText));
        }

        private void OnHistoryChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshHistory));
        }

        private void RefreshHistory()
        {
            History.Clear();

            foreach (NotificationRecord record in NotificationCenter.History)
                History.Add(record);

            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(HasHistory));
        }

        private static System.Windows.Media.Brush Frozen(System.Windows.Media.Color color, double opacity = 1)
        {
            var brush = new System.Windows.Media.SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        private NotificationKind Kind(NotificationKindId id, string name, string trigger)
        {
            NotificationStyle style = NotificationStyles.For(id);

            return new NotificationKind
            {
                Name = name,
                Trigger = trigger,
                Symbol = style.Symbol,
                Accent = Frozen(style.Accent),
                Tint = Frozen(style.Accent, 0.16),
                Read = () => NotificationCenter.IsKindEnabled(id),
                Write = value =>
                {
                    NotificationCenter.SetKindEnabled(id, value);
                    OnPropertyChanged(nameof(NotificationsJoinToastEnabled));
                    OnPropertyChanged(nameof(NotificationsLeaveToastEnabled));
                },
                Changed = () => OnPropertyChanged(nameof(SummaryText))
            };
        }

        private void BuildCatalogue()
        {
            var play = new NotificationKindGroup { Name = "While you play" };

            play.Kinds.Add(Kind(NotificationKindId.ServerJoined, "Server joined", "You join a Roblox server."));
            play.Kinds.Add(Kind(NotificationKindId.ServerLeft, "Server left", "You leave a server."));
            play.Kinds.Add(Kind(NotificationKindId.ServerRegion, "Server region", "The datacenter your server runs in is worked out after you join, with the ping if it is known yet."));
            play.Kinds.Add(Kind(NotificationKindId.RobloxClosed, "Roblox closed unexpectedly", "Roblox exits on its own and the crash looks real enough to report."));
            play.Kinds.Add(Kind(NotificationKindId.AutoRejoin, "Auto Rejoin progress", "Auto Rejoin starts a retry after a crash, and again when it succeeds or runs out of attempts."));
            play.Kinds.Add(Kind(NotificationKindId.FastFlagProfile, "FastFlag profile not active", "You join a game with its own flag profile while Roblox is running someone else's flags."));
            play.Kinds.Add(Kind(NotificationKindId.FrameRateLimit, "Frame rate limit changed", "You press the Raise or Lower Frame Rate Limit hotkey, or the driver refuses the new limit."));
            play.Kinds.Add(Kind(NotificationKindId.RamCleaned, "RAM cleaned", "Clean RAM runs, from the Performance page or its hotkey."));
            play.Kinds.Add(Kind(NotificationKindId.OverlayFocusMode, "Overlay Focus Mode", "You toggle Focus Mode, so the HUD and crosshair hide or come back."));
            play.Kinds.Add(Kind(NotificationKindId.PerformanceRun, "Performance measurement", "A measurement run starts, finishes with a verdict, or ends with nothing to measure."));
            play.Kinds.Add(Kind(NotificationKindId.InviteLink, "Invite link copied", "You copy a server invite link from the tray menu."));

            var captures = new NotificationKindGroup { Name = "Captures" };

            captures.Kinds.Add(Kind(NotificationKindId.Screenshot, "Screenshot saved", "A screenshot is taken, or the Roblox window could not be found."));
            captures.Kinds.Add(Kind(NotificationKindId.Replay, "Replay saved", "An Instant Replay clip finishes encoding, or fails while it encodes."));
            captures.Kinds.Add(Kind(NotificationKindId.ReplayNotReady, "Replay not ready", "You press the replay hotkey while Instant Replay is off, still buffering, or still saving."));

            var friends = new NotificationKindGroup { Name = "Friends" };

            friends.Kinds.Add(Kind(NotificationKindId.FriendOnline, "A friend came online", "Someone on your friends list stops being offline."));
            friends.Kinds.Add(Kind(NotificationKindId.FriendPlaying, "A friend started playing", "Someone on your friends list joins a game."));

            var warnings = new NotificationKindGroup { Name = "Warnings" };

            warnings.Kinds.Add(Kind(NotificationKindId.AccountGuard, "Account Guard alert", "A guard scan finds a change to your account or session that it does not recognise."));
            warnings.Kinds.Add(Kind(NotificationKindId.ProxyCertificate, "Proxy certificate not accepted", "Roblox's certificate bundle could not be patched, so Asset Warp and the spoofers are off for the session."));

            var updates = new NotificationKindGroup { Name = "Updates" };

            updates.Kinds.Add(Kind(NotificationKindId.FlagsRemoved, "Roblox removed your flags", "A Roblox update drops FastFlags you had set, so they no longer do anything."));

            Catalogue.Add(play);
            Catalogue.Add(captures);
            Catalogue.Add(friends);
            Catalogue.Add(warnings);
            Catalogue.Add(updates);
        }
    }
}
