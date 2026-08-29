using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class NotificationsViewModel : NotifyPropertyChangedViewModel
    {
        public NotificationsViewModel()
        {
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
            }
        }

        public bool NotificationsJoinToastEnabled
        {
            get => App.Settings.Prop.NotificationsJoinToastEnabled;
            set
            {
                App.Settings.Prop.NotificationsJoinToastEnabled = value;
                OnPropertyChanged(nameof(NotificationsJoinToastEnabled));
            }
        }

        public bool NotificationsLeaveToastEnabled
        {
            get => App.Settings.Prop.NotificationsLeaveToastEnabled;
            set
            {
                App.Settings.Prop.NotificationsLeaveToastEnabled = value;
                OnPropertyChanged(nameof(NotificationsLeaveToastEnabled));
            }
        }

        public ObservableCollection<NotificationRecord> History { get; } = new();

        public bool HasHistory => History.Count > 0;

        public ICommand ClearHistoryCommand => new RelayCommand(NotificationCenter.ClearHistory);

        /// <summary>
        /// Called from the page's Unloaded handler so this viewmodel doesn't keep
        /// <see cref="NotificationCenter"/> subscribed for the lifetime of the process after the page
        /// itself has been navigated away from.
        /// </summary>
        public void Detach()
        {
            NotificationCenter.HistoryChanged -= OnHistoryChanged;
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
    }
}
