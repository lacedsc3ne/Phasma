using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

using PhasmaStrap.Integrations.GameChat;

namespace PhasmaStrap.UI.ViewModels.ContextMenu
{
    // Shows a live-updating view of messages sent through PhasmaStrap's own GameChat overlay (see
    // Integrations/GameChat/GameChatLog.cs), which is where every message the overlay renders is
    // also mirrored. This viewer is purely a passive read-along of that shared log.
    internal sealed class ChatLogsViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private const int MaxLogRows = 2000;

        private readonly ConcurrentQueue<GameChatLogEntry> _incoming = new();
        private int _drainScheduled;
        private bool _disposed;

        public ObservableCollection<GameChatLogEntry> MessageLogsCollection { get; } = new();

        public bool HasEntries => MessageLogsCollection.Count != 0;

        public ICommand CloseWindowCommand { get; }

        public ICommand ExportCommand { get; }

        public event EventHandler? RequestCloseEvent;

        public ChatLogsViewModel()
        {
            CloseWindowCommand = new RelayCommand(RequestClose);
            ExportCommand = new RelayCommand(Export);

            foreach (GameChatLogEntry entry in GameChatLog.Snapshot())
                MessageLogsCollection.Add(entry);

            GameChatLog.Added += OnMessageAdded;
        }

        private void OnMessageAdded(object? sender, GameChatLogEntry entry)
        {
            if (_disposed)
                return;

            _incoming.Enqueue(entry);

            while (_incoming.Count > MaxLogRows * 2 && _incoming.TryDequeue(out _))
            {
            }

            if (Interlocked.Exchange(ref _drainScheduled, 1) != 0)
                return;

            Dispatcher? dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                Interlocked.Exchange(ref _drainScheduled, 0);
                return;
            }

            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(DrainIncoming));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _drainScheduled, 0);
            }
        }

        // batches queued entries onto the UI thread rather than adding them one at a time, so a
        // burst of chat activity doesn't hammer the ObservableCollection with individual updates
        private void DrainIncoming()
        {
            Interlocked.Exchange(ref _drainScheduled, 0);

            if (_disposed)
                return;

            int added = 0;

            while (_incoming.TryDequeue(out GameChatLogEntry? entry))
            {
                MessageLogsCollection.Add(entry);
                added++;
            }

            if (added == 0)
                return;

            int overflow = MessageLogsCollection.Count - MaxLogRows;

            if (overflow > 0)
            {
                for (int i = 0; i < overflow; i++)
                    MessageLogsCollection.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasEntries));
        }

        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"{App.ProjectName} Chat Logs.csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var text = new StringBuilder("Time,Channel,Sender,Message\r\n");

                foreach (GameChatLogEntry entry in MessageLogsCollection)
                {
                    text.Append(Csv(entry.Time.ToString("O"))).Append(',')
                        .Append(Csv(entry.Channel)).Append(',')
                        .Append(Csv(entry.Sender)).Append(',')
                        .Append(Csv(entry.Message)).Append("\r\n");
                }

                File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ChatLogsViewModel::Export", ex);
                Frontend.ShowMessageBox($"{Strings.ContextMenu_ChatLogs_ExportFailed} {ex.Message}", MessageBoxImage.Error);
            }
        }

        // guards against CSV formula injection (Excel/Sheets treat a leading =, +, -, or @ as the
        // start of a formula when a cell is opened) by prefixing such values with a literal quote
        private static string Csv(string value)
        {
            if (value.Length > 0 && "=+-@".Contains(value[0]))
                value = "'" + value;

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private void RequestClose() => RequestCloseEvent?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            GameChatLog.Added -= OnMessageAdded;
            RequestCloseEvent = null;

            GC.SuppressFinalize(this);
        }
    }
}
