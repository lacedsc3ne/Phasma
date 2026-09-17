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

    public class DeveloperToolsViewModel : NotifyPropertyChangedViewModel
    {
        // --- FastFlag snapshots: live A/B toggle + diff viewer, both built on the same
        // save/apply/diff primitives in FastFlagSnapshotManager ---

        public ObservableCollection<SnapshotRow> Snapshots { get; } = new();

        private string _newSnapshotName = "";
        public string NewSnapshotName
        {
            get => _newSnapshotName;
            set { _newSnapshotName = value; OnPropertyChanged(nameof(NewSnapshotName)); }
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
            _ when DiffEntries.Count == 0 => "No differences - these two snapshots have identical flags.",
            _ => $"{DiffEntries.Count} difference(s).",
        };

        public ICommand SaveSnapshotCommand => new RelayCommand(SaveSnapshot);
        public ICommand ApplySnapshotCommand => new RelayCommand<SnapshotRow>(ApplySnapshot);
        public ICommand DeleteSnapshotCommand => new RelayCommand<SnapshotRow>(DeleteSnapshot);

        private void SaveSnapshot()
        {
            if (string.IsNullOrWhiteSpace(NewSnapshotName))
                return;

            FastFlagSnapshotManager.Save(NewSnapshotName.Trim());
            NewSnapshotName = "";
            RefreshSnapshots();
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

            FastFlagSnapshotManager.Apply(row.Snapshot);
        }

        private void DeleteSnapshot(SnapshotRow? row)
        {
            if (row is null)
                return;

            FastFlagSnapshotManager.Delete(row.Name);
            RefreshSnapshots();
        }

        private void RefreshSnapshots()
        {
            Snapshots.Clear();
            foreach (FastFlagSnapshot snapshot in FastFlagSnapshotManager.List())
                Snapshots.Add(new SnapshotRow { Snapshot = snapshot });

            OnPropertyChanged(nameof(Snapshots));
        }

        private void RefreshDiff()
        {
            DiffEntries.Clear();

            if (SelectedSnapshotA is not null && SelectedSnapshotB is not null)
            {
                foreach (FastFlagDiffEntry entry in FastFlagSnapshotManager.Diff(SelectedSnapshotA.Snapshot.Flags, SelectedSnapshotB.Snapshot.Flags))
                    DiffEntries.Add(entry);
            }

            OnPropertyChanged(nameof(DiffSummary));
        }

        // --- live proxy traffic log ---

        public ObservableCollection<ProxyTrafficEntry> ProxyTraffic { get; } = new();

        public ICommand RefreshProxyTrafficCommand => new RelayCommand(RefreshProxyTraffic);
        public ICommand ClearProxyTrafficCommand => new RelayCommand(() => { ProxyTrafficLog.Clear(); RefreshProxyTraffic(); });

        private void RefreshProxyTraffic()
        {
            ProxyTraffic.Clear();
            foreach (ProxyTrafficEntry entry in ProxyTrafficLog.Recent)
                ProxyTraffic.Add(entry);
        }

        private void OnProxyTrafficChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshProxyTraffic));
        }

        // --- unified log viewer ---

        private string _logText = "";
        public string LogText
        {
            get => _logText;
            private set { _logText = value; OnPropertyChanged(nameof(LogText)); }
        }

        public ICommand RefreshLogsCommand => new RelayCommand(RefreshLogs);
        public ICommand CopyLogsCommand => new RelayCommand(() =>
        {
            try
            {
                Clipboard.SetText(LogText);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DeveloperToolsViewModel", $"Copy logs failed: {ex.Message}");
            }
        });

        private void RefreshLogs()
        {
            const int MaxCharsPerLog = 100_000;
            var sb = new System.Text.StringBuilder();

            // every PhasmaStrap process (this settings window, each Bootstrapper/Watcher game
            // session, elevated helpers) writes its own log file - showing only this process's
            // log hid everything that actually matters for diagnosing gameplay features
            // (overlays, hotkeys, replay, proxy), since those run in the Watcher process
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
                    string? latest = Directory.GetFiles(Paths.RobloxLogs)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                    AppendTail(sb, latest, MaxCharsPerLog);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not read Roblox logs: {ex.Message})");
            }

            LogText = sb.ToString();
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
                using var reader = new StreamReader(stream);
                string content = reader.ReadToEnd();

                sb.AppendLine(content.Length > maxChars ? content[^maxChars..] : content);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(could not read '{path}': {ex.Message})");
            }
        }

        // --- Studio plugin installer (by asset ID - see RobloxAssetDownloader's doc comment for
        // why this doesn't curate/endorse a specific plugin list) ---

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

        public ICommand InstallPluginCommand => new AsyncRelayCommand(InstallPluginAsync);
        public ICommand OpenPluginsFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(Paths.LocalAppData + @"\Roblox\Plugins");
            Process.Start("explorer.exe", Path.Combine(Paths.LocalAppData, "Roblox", "Plugins"));
        });

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

            try
            {
                string pluginsFolder = Path.Combine(Paths.LocalAppData, "Roblox", "Plugins");
                Directory.CreateDirectory(pluginsFolder);

                string destination = Path.Combine(pluginsFolder, $"Plugin_{assetId}.rbxm");
                File.WriteAllBytes(destination, result.Bytes);

                PluginInstallStatus = $"Installed to {destination}. Restart Studio to load it.";
            }
            catch (Exception ex)
            {
                PluginInstallStatus = $"Install failed: {ex.Message}";
            }
        }

        public DeveloperToolsViewModel()
        {
            RefreshSnapshots();
            RefreshProxyTraffic();
            RefreshLogs();

            ProxyTrafficLog.Changed += OnProxyTrafficChanged;
        }

        public void Detach()
        {
            ProxyTrafficLog.Changed -= OnProxyTrafficChanged;
        }
    }
}
