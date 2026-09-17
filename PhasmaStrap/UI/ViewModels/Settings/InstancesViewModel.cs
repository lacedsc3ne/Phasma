using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using Windows.Win32;
using Windows.Win32.Foundation;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class RobloxInstanceRow
    {
        public int ProcessId { get; init; }
        public string MemoryDisplay { get; init; } = "";
        public string UptimeDisplay { get; init; } = "";
        public IntPtr WindowHandle { get; init; }
    }

    /// <summary>
    /// Backs the Instances page: shows currently running Roblox windows and lets a new one start
    /// alongside them via SingletonMutexBypass. Only the first Roblox instance in a launch session
    /// gets a Watcher (Watcher.cs's own "Watcher" InterProcessLock only allows one at a time), so
    /// additional instances run as plain Roblox windows without PhasmaStrap's Discord Rich
    /// Presence/hotkeys/overlays/auto-rejoin for that specific window - documented on the page
    /// rather than silently expected. Assigning a different signed-in account to each simultaneous
    /// instance isn't included here - Roblox only reads its login from one shared cookie file at
    /// startup, so that needs the auth-ticket launch mechanism (POST auth.roblox.com/v1/
    /// authentication-ticket, then RobloxPlayerBeta.exe -t &lt;ticket&gt;) wired into the launch
    /// pipeline, a deeper change than this page makes on its own.
    /// </summary>
    public sealed class InstancesViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "InstancesViewModel";

        public ObservableCollection<RobloxInstanceRow> Instances { get; } = new();

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        private bool _launching;
        public bool Launching { get => _launching; private set { _launching = value; OnPropertyChanged(nameof(Launching)); OnPropertyChanged(nameof(CanLaunch)); } }
        public bool CanLaunch => !_launching;

        public bool HasInstances => Instances.Count > 0;
        public Visibility EmptyStateVisibility => Instances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand { get; }
        public ICommand LaunchInstanceCommand { get; }
        public ICommand FocusCommand { get; }
        public ICommand CloseCommand { get; }

        private readonly DispatcherTimer _autoRefreshTimer;

        public InstancesViewModel()
        {
            RefreshCommand = new RelayCommand(Refresh);
            LaunchInstanceCommand = new AsyncRelayCommand(LaunchInstanceAsync);
            FocusCommand = new RelayCommand<RobloxInstanceRow?>(Focus);
            CloseCommand = new RelayCommand<RobloxInstanceRow?>(Close);

            Refresh();

            // uptime/memory are only ever computed at Refresh() time, not live-bound - without
            // this they'd sit frozen at whatever they read on the last manual Refresh click
            _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _autoRefreshTimer.Tick += (_, _) => Refresh();
            _autoRefreshTimer.Start();
        }

        public void Detach() => _autoRefreshTimer.Stop();

        private void Refresh()
        {
            Instances.Clear();

            foreach (Process process in Utilities.GetProcessesSafe().Where(p => p.ProcessName.Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Instances.Add(new RobloxInstanceRow
                    {
                        ProcessId = process.Id,
                        MemoryDisplay = $"{process.WorkingSet64 / 1048576.0:0.#} MB",
                        UptimeDisplay = $"Running for {DateTime.Now - process.StartTime:hh\\:mm\\:ss}",
                        WindowHandle = process.MainWindowHandle,
                    });
                }
                catch
                {
                    // process may have exited between enumeration and read - skip it
                }
                finally
                {
                    process.Dispose();
                }
            }

            Status = Instances.Count == 0 ? "No Roblox windows are currently running." : $"{Instances.Count} Roblox window(s) running.";
            OnPropertyChanged(nameof(HasInstances));
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        private async Task LaunchInstanceAsync()
        {
            if (Launching)
                return;

            Launching = true;

            try
            {
                var running = Utilities.GetProcessesSafe().Where(p => p.ProcessName.Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase)).ToList();

                try
                {
                    if (running.Count > 0)
                    {
                        Status = "Freeing the singleton lock...";
                        int closedTotal = 0;

                        foreach (Process process in running)
                            closedTotal += await Task.Run(() => SingletonMutexBypass.TryFreeSingleton(process.Id)).ConfigureAwait(true);

                        App.Logger.WriteLine(LOG_IDENT, $"Closed {closedTotal} singleton handle(s) across {running.Count} running instance(s)");
                    }

                    Status = "Launching a new instance...";

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Paths.Process,
                        Arguments = "-player",
                        UseShellExecute = false,
                    });

                    await Task.Delay(2500).ConfigureAwait(true);
                    Refresh();
                    Status = "Launched. Only the first instance in a session gets Discord Rich Presence/hotkeys/overlays - additional windows are plain Roblox.";
                }
                finally
                {
                    foreach (Process process in running)
                        process.Dispose();
                }
            }
            catch (Exception ex)
            {
                Status = $"Launch failed: {ex.Message}";
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            finally
            {
                Launching = false;
            }
        }

        private static void Focus(RobloxInstanceRow? row)
        {
            if (row is null || row.WindowHandle == IntPtr.Zero)
                return;

            try
            {
                PInvoke.SetForegroundWindow((HWND)row.WindowHandle);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Focus failed: {ex.Message}");
            }
        }

        private void Close(RobloxInstanceRow? row)
        {
            if (row is null)
                return;

            try
            {
                using Process process = Process.GetProcessById(row.ProcessId);
                process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Close failed: {ex.Message}");
            }

            Refresh();
        }
    }
}
