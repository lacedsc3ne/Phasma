using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

using PhasmaStrap.Integrations;
using PhasmaStrap.UI;

namespace PhasmaStrap.UI.ViewModels.ContextMenu
{
    internal class OutputConsoleViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private const int MaxBufferedLines = 5000;

        private readonly ActivityWatcher _activityWatcher;
        private readonly ConcurrentQueue<string> _pendingLines = new();
        private readonly Queue<string> _lines = new();
        private readonly StringBuilder _builder = new();
        private readonly DispatcherTimer _flushTimer;
        private bool _disposed = false;

        public event EventHandler? RequestCloseEvent;

        public string ConsoleText { get; private set; } = "";

        public bool HasEntries => _lines.Count != 0;

        public string StatusText => _lines.Count == 1
            ? Strings.ContextMenu_OutputConsole_LineCountSingular
            : String.Format(Strings.ContextMenu_OutputConsole_LineCountPlural, _lines.Count);

        public ICommand CloseWindowCommand { get; }

        public ICommand ClearCommand { get; }

        public ICommand ExportCommand { get; }

        public OutputConsoleViewModel(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher ?? throw new ArgumentNullException(nameof(activityWatcher));

            CloseWindowCommand = new RelayCommand(RequestClose);
            ClearCommand = new RelayCommand(Clear);
            ExportCommand = new RelayCommand(Export);

            _activityWatcher.OnLogEntry += OnLogEntry;

            _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _flushTimer.Tick += FlushTimer_Tick;
            _flushTimer.Start();
        }

        private void OnLogEntry(object? sender, string entry)
        {
            if (!_disposed)
                _pendingLines.Enqueue(entry);
        }

        private void FlushTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || _pendingLines.IsEmpty)
                return;

            while (_pendingLines.TryDequeue(out string? line))
            {
                _lines.Enqueue(line);
                _builder.Append(line).Append(Environment.NewLine);
            }

            bool trimmed = false;

            while (_lines.Count > MaxBufferedLines)
            {
                _lines.Dequeue();
                trimmed = true;
            }

            if (trimmed)
            {
                _builder.Clear();

                foreach (string line in _lines)
                    _builder.Append(line).Append(Environment.NewLine);
            }

            ConsoleText = _builder.ToString();

            OnPropertyChanged(nameof(ConsoleText));
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(StatusText));
        }

        private void Clear()
        {
            while (_pendingLines.TryDequeue(out _)) { }

            _lines.Clear();
            _builder.Clear();
            ConsoleText = "";

            OnPropertyChanged(nameof(ConsoleText));
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(StatusText));
        }

        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Log file (*.log)|*.log|Text file (*.txt)|*.txt",
                FileName = $"{App.ProjectName} Output Console.log"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, ConsoleText, new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OutputConsoleViewModel::Export", ex);
                Frontend.ShowMessageBox($"{Strings.ContextMenu_OutputConsole_ExportFailed} {ex.Message}", MessageBoxImage.Error);
            }
        }

        private void RequestClose() => RequestCloseEvent?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _flushTimer.Stop();
            _flushTimer.Tick -= FlushTimer_Tick;
            _activityWatcher.OnLogEntry -= OnLogEntry;
            RequestCloseEvent = null;

            GC.SuppressFinalize(this);
        }
    }
}
