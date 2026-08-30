using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;

namespace PhasmaStrap.UI.ViewModels.ContextMenu
{
    /// <summary>
    /// A single Rich Presence button, as shown in the RPC Debug viewer's button list.
    /// </summary>
    public sealed class RPCButtonRow
    {
        public string Label { get; init; } = "";
        public string Url { get; init; } = "";
    }

    // Shows a live-updating, read-only view of whatever PhasmaStrap is currently telling Discord to
    // display (see Integrations/DiscordRichPresence.cs's CurrentSnapshot/PresenceChanged). This viewer
    // is purely a passive read-along of that state - it never sends anything to Discord itself.
    internal sealed class RPCWindowViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private readonly DiscordRichPresence _richPresence;
        private bool _disposed;

        private bool _isActive;
        private string _details = "";
        private string _state = "";
        private string _largeImageKey = "";
        private string _largeImageText = "";
        private string _smallImageKey = "";
        private string _smallImageText = "";
        private string _timestampStart = "";
        private string _timestampEnd = "";
        private string _lastUpdated = "";

        public bool IsActive
        {
            get => _isActive;
            private set { _isActive = value; OnPropertyChanged(nameof(IsActive)); }
        }

        public string Details
        {
            get => _details;
            private set { _details = value; OnPropertyChanged(nameof(Details)); }
        }

        public string State
        {
            get => _state;
            private set { _state = value; OnPropertyChanged(nameof(State)); }
        }

        public string LargeImageKey
        {
            get => _largeImageKey;
            private set { _largeImageKey = value; OnPropertyChanged(nameof(LargeImageKey)); }
        }

        public string LargeImageText
        {
            get => _largeImageText;
            private set { _largeImageText = value; OnPropertyChanged(nameof(LargeImageText)); }
        }

        public string SmallImageKey
        {
            get => _smallImageKey;
            private set { _smallImageKey = value; OnPropertyChanged(nameof(SmallImageKey)); }
        }

        public string SmallImageText
        {
            get => _smallImageText;
            private set { _smallImageText = value; OnPropertyChanged(nameof(SmallImageText)); }
        }

        public string TimestampStart
        {
            get => _timestampStart;
            private set { _timestampStart = value; OnPropertyChanged(nameof(TimestampStart)); }
        }

        public string TimestampEnd
        {
            get => _timestampEnd;
            private set { _timestampEnd = value; OnPropertyChanged(nameof(TimestampEnd)); }
        }

        public string LastUpdated
        {
            get => _lastUpdated;
            private set { _lastUpdated = value; OnPropertyChanged(nameof(LastUpdated)); }
        }

        public ObservableCollection<RPCButtonRow> Buttons { get; } = new();

        public bool HasButtons => Buttons.Count != 0;

        public ICommand CloseWindowCommand { get; }

        public event EventHandler? RequestCloseEvent;

        public RPCWindowViewModel(DiscordRichPresence richPresence)
        {
            _richPresence = richPresence;

            CloseWindowCommand = new RelayCommand(RequestClose);

            ApplySnapshot(_richPresence.CurrentSnapshot);

            _richPresence.PresenceChanged += OnPresenceChanged;
        }

        private void OnPresenceChanged(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            RichPresenceSnapshot snapshot = _richPresence.CurrentSnapshot;

            Dispatcher? dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ApplySnapshot(snapshot)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ApplySnapshot(RichPresenceSnapshot snapshot)
        {
            if (_disposed)
                return;

            IsActive = snapshot.IsActive;
            Details = snapshot.Details ?? "";
            State = snapshot.State ?? "";
            LargeImageKey = snapshot.LargeImageKey ?? "";
            LargeImageText = snapshot.LargeImageText ?? "";
            SmallImageKey = snapshot.SmallImageKey ?? "";
            SmallImageText = snapshot.SmallImageText ?? "";
            TimestampStart = snapshot.TimestampStart?.ToLocalTime().ToString("G") ?? "";
            TimestampEnd = snapshot.TimestampEnd?.ToLocalTime().ToString("G") ?? "";
            LastUpdated = snapshot.CapturedAt.ToString("G");

            Buttons.Clear();

            foreach (var (label, url) in snapshot.Buttons)
                Buttons.Add(new RPCButtonRow { Label = label, Url = url });

            OnPropertyChanged(nameof(HasButtons));
        }

        private void RequestClose() => RequestCloseEvent?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _richPresence.PresenceChanged -= OnPresenceChanged;
            RequestCloseEvent = null;

            GC.SuppressFinalize(this);
        }
    }
}
