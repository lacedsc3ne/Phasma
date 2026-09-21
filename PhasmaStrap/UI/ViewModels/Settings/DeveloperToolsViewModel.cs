using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Networking;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class SnapshotRow
    {
        public FastFlagSnapshot Snapshot { get; init; } = null!;
        public string Name => Snapshot.Name;
        public string CreatedDisplay => Snapshot.CreatedUtc.ToLocalTime().ToString("g");
        public int FlagCount => Snapshot.Flags.Count;
    }

    public sealed class DeveloperToolItem
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public Wpf.Ui.Common.SymbolRegular Symbol { get; init; }
    }

    public class DeveloperToolsViewModel : NotifyPropertyChangedViewModel
    {
        public IReadOnlyList<DeveloperToolItem> Tools { get; } = new[]
        {
            new DeveloperToolItem { Key = "snapshots", Label = Strings.Menu_DeveloperTools_FastFlagSnapshots, Symbol = Wpf.Ui.Common.SymbolRegular.Flag24 },
            new DeveloperToolItem { Key = "proxy", Label = Strings.Menu_DeveloperTools_LiveProxyTraffic, Symbol = Wpf.Ui.Common.SymbolRegular.ArrowSwap24 },
            new DeveloperToolItem { Key = "logs", Label = Strings.Menu_DeveloperTools_LogViewer, Symbol = Wpf.Ui.Common.SymbolRegular.DocumentText24 },
            new DeveloperToolItem { Key = "plugins", Label = Strings.Menu_DeveloperTools_StudioPluginInstaller, Symbol = Wpf.Ui.Common.SymbolRegular.PuzzleCube24 },
            new DeveloperToolItem { Key = "diagnostics", Label = Strings.Menu_PhasmaStrap_Section_Diagnostics_Header, Symbol = Wpf.Ui.Common.SymbolRegular.Stethoscope24 },
        };

        private DeveloperToolItem? _selectedTool;
        public DeveloperToolItem? SelectedTool
        {
            get => _selectedTool;
            set
            {
                if (value is null)
                    return;

                _selectedTool = value;
                OnPropertyChanged(nameof(SelectedTool));
                OnPropertyChanged(nameof(SnapshotsVisibility));
                OnPropertyChanged(nameof(ProxyVisibility));
                OnPropertyChanged(nameof(LogsVisibility));
                OnPropertyChanged(nameof(PluginsVisibility));
                OnPropertyChanged(nameof(DiagnosticsVisibility));
                OnPropertyChanged(nameof(ScrollableToolVisibility));

                if (value.Key == "proxy")
                    RefreshProxyTraffic();
            }
        }

        private Visibility VisibleWhen(string key) => _selectedTool?.Key == key ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SnapshotsVisibility => VisibleWhen("snapshots");
        public Visibility ProxyVisibility => VisibleWhen("proxy");
        public Visibility LogsVisibility => VisibleWhen("logs");
        public Visibility PluginsVisibility => VisibleWhen("plugins");
        public Visibility DiagnosticsVisibility => VisibleWhen("diagnostics");
        public Visibility ScrollableToolVisibility => _selectedTool?.Key == "diagnostics" ? Visibility.Collapsed : Visibility.Visible;

        public ObservableCollection<SnapshotRow> Snapshots { get; } = new();

        private string _newSnapshotName = "";
        public string NewSnapshotName
        {
            get => _newSnapshotName;
            set { _newSnapshotName = value; OnPropertyChanged(nameof(NewSnapshotName)); }
        }

        private string _snapshotStatus = "";
        public string SnapshotStatus
        {
            get => _snapshotStatus;
            private set { _snapshotStatus = value; OnPropertyChanged(nameof(SnapshotStatus)); }
        }

        private SnapshotRow? _selectedSnapshotA;
        public SnapshotRow? SelectedSnapshotA
        {
            get => _selectedSnapshotA;
            set { _selectedSnapshotA = value; OnPropertyChanged(nameof(SelectedSnapshotA)); RefreshDiff(); }
        }

        private SnapshotRow? _selectedSnapshotB;
        public SnapshotRow? SelectedSnapshotB
        {
            get => _selectedSnapshotB;
            set { _selectedSnapshotB = value; OnPropertyChanged(nameof(SelectedSnapshotB)); RefreshDiff(); }
        }

        public ObservableCollection<FastFlagDiffEntry> DiffEntries { get; } = new();

        public string DiffSummary => (SelectedSnapshotA, SelectedSnapshotB) switch
        {
            (null, _) or (_, null) => "Pick two snapshots above to compare them.",
            _ when ReferenceEquals(SelectedSnapshotA, SelectedSnapshotB) => "Those are the same snapshot, so there is nothing to compare.",
            _ when DiffEntries.Count == 0 => "No differences, these two snapshots have identical flags.",
            _ => $"{DiffEntries.Count} difference(s): {DiffEntries.Count(e => e.ChangeType == "Added")} added, {DiffEntries.Count(e => e.ChangeType == "Removed")} removed, {DiffEntries.Count(e => e.ChangeType == "Changed")} changed.",
        };

        public ICommand SaveSnapshotCommand => new RelayCommand(SaveSnapshot);
        public ICommand ApplySnapshotCommand => new RelayCommand<SnapshotRow>(ApplySnapshot);
        public ICommand DeleteSnapshotCommand => new RelayCommand<SnapshotRow>(DeleteSnapshot);

        private void SaveSnapshot()
        {
            string name = NewSnapshotName.Trim();

            if (string.IsNullOrEmpty(name))
            {
                SnapshotStatus = "Give the snapshot a name first.";
                return;
            }

            bool replacing = Snapshots.Any(row => string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
            int count = App.FastFlags.Prop.Count;

            try
            {
                FastFlagSnapshotManager.Save(name);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Saving snapshot '{name}' failed: {ex.Message}");
                SnapshotStatus = $"Could not save that snapshot: {ex.Message}";
                return;
            }

            NewSnapshotName = "";
            RefreshSnapshots();
            SnapshotStatus = $"{(replacing ? "Replaced" : "Saved")} '{name}' with {count} flag(s).";
        }

        private void ApplySnapshot(SnapshotRow? row)
        {
            if (row is null)
                return;

            MessageBoxResult confirm = Frontend.ShowMessageBox(
                $"Replace your current FastFlags entirely with the '{row.Name}' snapshot ({row.FlagCount} flag(s))? Anything not in this snapshot will be cleared.",
                MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                FastFlagSnapshotManager.Apply(row.Snapshot);
                SnapshotStatus = $"Applied '{row.Name}'. {App.FastFlags.Prop.Count} flag(s) written to ClientAppSettings.json. Roblox reads flags when it starts, so restart it for these to take effect.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Applying snapshot '{row.Name}' failed: {ex.Message}");
                SnapshotStatus = $"Could not apply that snapshot: {ex.Message}";
            }
        }

        private void DeleteSnapshot(SnapshotRow? row)
        {
            if (row is null)
                return;

            try
            {
                FastFlagSnapshotManager.Delete(row.Name);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Deleting snapshot '{row.Name}' failed: {ex.Message}");
                SnapshotStatus = $"Could not delete that snapshot: {ex.Message}";
                return;
            }

            RefreshSnapshots();
            SnapshotStatus = $"Deleted '{row.Name}'.";
        }

        private void RefreshSnapshots()
        {
            string? keptA = _selectedSnapshotA?.Name;
            string? keptB = _selectedSnapshotB?.Name;

            Snapshots.Clear();
            foreach (FastFlagSnapshot snapshot in FastFlagSnapshotManager.List())
                Snapshots.Add(new SnapshotRow { Snapshot = snapshot });

            OnPropertyChanged(nameof(Snapshots));

            _selectedSnapshotA = Snapshots.FirstOrDefault(row => row.Name == keptA);
            _selectedSnapshotB = Snapshots.FirstOrDefault(row => row.Name == keptB);
            OnPropertyChanged(nameof(SelectedSnapshotA));
            OnPropertyChanged(nameof(SelectedSnapshotB));
            RefreshDiff();
        }

        private void RefreshDiff()
        {
            DiffEntries.Clear();

            if (SelectedSnapshotA is not null && SelectedSnapshotB is not null && !ReferenceEquals(SelectedSnapshotA, SelectedSnapshotB))
            {
                foreach (FastFlagDiffEntry entry in FastFlagSnapshotManager.Diff(SelectedSnapshotA.Snapshot.Flags, SelectedSnapshotB.Snapshot.Flags))
                    DiffEntries.Add(entry);
            }

            OnPropertyChanged(nameof(DiffSummary));
        }

        public ObservableCollection<ProxyTrafficEntry> ProxyTraffic { get; } = new();

        private string _proxyStatus = "";
        public string ProxyStatus
        {
            get => _proxyStatus;
            private set { _proxyStatus = value; OnPropertyChanged(nameof(ProxyStatus)); }
        }

        public ICommand RefreshProxyTrafficCommand => new RelayCommand(RefreshProxyTraffic);
        public ICommand ClearProxyTrafficCommand => new RelayCommand(() => { ProxyTrafficLog.Clear(); RefreshProxyTraffic(); });

        private void RefreshProxyTraffic()
        {
            ProxyTraffic.Clear();
            foreach (ProxyTrafficEntry entry in ProxyTrafficLog.Recent)
                ProxyTraffic.Add(entry);

            RefreshProxyStatus();
        }

        private void RefreshProxyStatus()
        {
            try
            {
                if (AssetProxyServer.IsRunning)
                {
                    ProxyStatus = ProxyTraffic.Count == 0
                        ? "This window is hosting the proxy. Nothing has gone through it yet."
                        : $"This window is hosting the proxy. {ProxyTraffic.Count} request(s) recorded.";
                    return;
                }

                if (!App.Settings.Prop.NetworkingProxyEnabled)
                {
                    ProxyStatus = "The networking proxy is turned off, so no requests are being intercepted. Turn it on under Launching, Networking.";
                    return;
                }

                ProxyStatus = ProxyHealth.IsHostedAnywhere()
                    ? "Another PhasmaStrap process is hosting the proxy, so its requests cannot be read from this window."
                    : "The proxy is turned on but is not listening at the moment.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Could not read proxy state: {ex.Message}");
                ProxyStatus = "Could not work out where the proxy is running.";
            }
        }

        private readonly System.Windows.Threading.DispatcherTimer _trafficRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private bool _trafficRefreshHooked;

        private void OnProxyTrafficChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_trafficRefreshHooked)
                {
                    _trafficRefreshTimer.Tick += (_, _) =>
                    {
                        _trafficRefreshTimer.Stop();
                        RefreshProxyTraffic();
                    };
                    _trafficRefreshHooked = true;
                }

                if (!_trafficRefreshTimer.IsEnabled)
                    _trafficRefreshTimer.Start();
            }));
        }

        private string _logText = "";
        public string LogText
        {
            get => _logText;
            private set { _logText = value; OnPropertyChanged(nameof(LogText)); }
        }

        public string LogDescription => "The tail of the four newest PhasmaStrap logs and of the newest Roblox log, joined together.";

        private string _logStatus = "";
        public string LogStatus
        {
            get => _logStatus;
            private set { _logStatus = value; OnPropertyChanged(nameof(LogStatus)); }
        }

        public ICommand RefreshLogsCommand => new RelayCommand(RefreshLogs);
        public ICommand CopyLogsCommand => new RelayCommand(CopyLogs);

        private void CopyLogs()
        {
            if (string.IsNullOrEmpty(LogText))
            {
                LogStatus = "There is nothing to copy yet.";
                return;
            }

            try
            {
                Clipboard.SetText(LogText);
                LogStatus = $"Copied {LogText.Length:N0} characters to the clipboard.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Copy logs failed: {ex.Message}");
                LogStatus = $"Could not copy to the clipboard: {ex.Message}";
            }
        }

        private void RefreshLogs()
        {
            LogStatus = "Reading the logs...";

            _ = Task.Run(() =>
            {
                string text = BuildLogText();
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogText = text;
                    LogStatus = $"Read at {DateTime.Now:HH:mm:ss}.";
                }));
            });
        }

        private static string BuildLogText()
        {
            const int MaxCharsPerLog = 100_000;
            var sb = new System.Text.StringBuilder();

            try
            {
                var recentLogs = Directory.Exists(Paths.Logs)
                    ? new DirectoryInfo(Paths.Logs).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(4).ToList()
                    : new List<FileInfo>();

                if (recentLogs.Count == 0)
                {
                    sb.AppendLine("=== PhasmaStrap log ===");
                    AppendTail(sb, App.Logger.FileLocation, MaxCharsPerLog);
                }

                foreach (FileInfo log in recentLogs)
                {
                    bool isThisProcess = string.Equals(log.FullName, App.Logger.FileLocation, StringComparison.OrdinalIgnoreCase);
                    sb.AppendLine($"=== PhasmaStrap log: {log.Name}{(isThisProcess ? " (this window)" : "")} ===");
                    AppendTail(sb, log.FullName, MaxCharsPerLog / 2);
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not list PhasmaStrap logs: {ex.Message})");
            }

            sb.AppendLine("=== Most recent Roblox log ===");

            try
            {
                if (Directory.Exists(Paths.RobloxLogs))
                {
                    string? latest = new DirectoryInfo(Paths.RobloxLogs).GetFiles()
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault()?.FullName;

                    AppendTail(sb, latest, MaxCharsPerLog);
                }
                else
                {
                    sb.AppendLine("(not found)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not read Roblox logs: {ex.Message})");
            }

            return sb.ToString();
        }

        private static void AppendTail(System.Text.StringBuilder sb, string? path, int maxChars)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                sb.AppendLine("(not found)");
                return;
            }

            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                long start = Math.Max(0, stream.Length - (long)maxChars * 2);
                stream.Seek(start, SeekOrigin.Begin);

                using var reader = new StreamReader(stream);
                if (start > 0)
                    reader.ReadLine();

                string content = reader.ReadToEnd();
                sb.AppendLine(content.Length > maxChars ? content[^maxChars..] : content);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not read '{path}': {ex.Message})");
            }
        }

        private string _pluginAssetId = "";
        public string PluginAssetId
        {
            get => _pluginAssetId;
            set { _pluginAssetId = value; OnPropertyChanged(nameof(PluginAssetId)); }
        }

        private string _pluginInstallStatus = "";
        public string PluginInstallStatus
        {
            get => _pluginInstallStatus;
            private set { _pluginInstallStatus = value; OnPropertyChanged(nameof(PluginInstallStatus)); }
        }

        private static string PluginsFolder => Path.Combine(Paths.LocalAppData, "Roblox", "Plugins");

        public ICommand InstallPluginCommand => new AsyncRelayCommand(InstallPluginAsync);
        public ICommand OpenPluginsFolderCommand => new RelayCommand(OpenPluginsFolder);

        private void OpenPluginsFolder()
        {
            try
            {
                Directory.CreateDirectory(PluginsFolder);
                Process.Start(new ProcessStartInfo { FileName = PluginsFolder, UseShellExecute = true });
                PluginInstallStatus = $"Opened {PluginsFolder}.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Opening the plugins folder failed: {ex.Message}");
                PluginInstallStatus = $"Could not open {PluginsFolder}: {ex.Message}";
            }
        }

        private async Task InstallPluginAsync()
        {
            if (!long.TryParse(PluginAssetId.Trim(), out long assetId) || assetId <= 0)
            {
                PluginInstallStatus = "Enter a valid numeric asset ID first.";
                return;
            }

            PluginInstallStatus = "Downloading...";

            AssetDownloadResult result = await RobloxAssetDownloader.DownloadAssetAsync(assetId);
            if (!result.Success || result.Bytes is null)
            {
                PluginInstallStatus = result.Error ?? "Download failed.";
                return;
            }

            if (!LooksLikeRobloxModel(result.Bytes))
            {
                PluginInstallStatus = "That asset is not a Studio model file, so it cannot be a plugin. Check the ID.";
                return;
            }

            try
            {
                Directory.CreateDirectory(PluginsFolder);

                string destination = Path.Combine(PluginsFolder, $"Plugin_{assetId}.rbxm");
                File.WriteAllBytes(destination, result.Bytes);

                PluginInstallStatus = $"Installed to {destination}. Restart Studio to load it.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Installing plugin {assetId} failed: {ex.Message}");
                PluginInstallStatus = $"Install failed: {ex.Message}";
            }
        }

        private static bool LooksLikeRobloxModel(byte[] bytes)
        {
            byte[] signature = System.Text.Encoding.ASCII.GetBytes("<roblox");

            if (bytes.Length < signature.Length)
                return false;

            for (int i = 0; i < signature.Length; i++)
            {
                if (bytes[i] != signature[i])
                    return false;
            }

            return true;
        }

        public DeveloperToolsViewModel()
        {
            _selectedTool = Tools[0];

            RefreshSnapshots();
            RefreshProxyTraffic();

            LogText = "Loading logs...";
            LogStatus = "Reading the logs...";
            _ = Task.Run(() =>
            {
                string text = BuildLogText();
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogText = text;
                    LogStatus = $"Read at {DateTime.Now:HH:mm:ss}.";
                }));
            });

            ProxyTrafficLog.Changed += OnProxyTrafficChanged;
        }

        public void Attach()
        {
            ProxyTrafficLog.Changed -= OnProxyTrafficChanged;
            ProxyTrafficLog.Changed += OnProxyTrafficChanged;
            RefreshProxyTraffic();
        }

        public void Detach()
        {
            ProxyTrafficLog.Changed -= OnProxyTrafficChanged;
            _trafficRefreshTimer.Stop();
        }
    }
}
