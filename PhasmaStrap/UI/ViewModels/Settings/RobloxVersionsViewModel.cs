using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class RobloxVersionRow
    {
        public RobloxVersionRecord Record { get; init; } = null!;
        public bool IsInstalled { get; init; }

        public string Title => (Record.FileVersion.Length > 0 ? Record.FileVersion : "Unknown version") + (IsInstalled ? "  (in use)" : "");

        public Visibility UseVisibility => IsInstalled ? Visibility.Collapsed : Visibility.Visible;
        public string Detail => $"{Record.Guid}  ·  installed {Record.InstalledUtc.ToLocalTime():d MMM yyyy}  ·  {SizeText}";

        public string SizeText
        {
            get
            {
                try
                {
                    long bytes = new DirectoryInfo(RobloxVersions.FolderOf(Record.Guid)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    return $"{bytes / 1024.0 / 1024:0} MB";
                }
                catch
                {
                    return "?";
                }
            }
        }
    }

    /// <summary>
    /// Behaviour > Roblox version: hold or pin the Roblox version, keep the previous one for going
    /// back, and see which of your flags an update removed. Settings apply when Save is pressed.
    /// </summary>
    public sealed class RobloxVersionsViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<RobloxVersionRow> OnDisk { get; } = new();

        public RobloxVersionsViewModel()
        {
            Refresh();
        }

        private void Refresh()
        {
            string installed = App.PlayerState.Prop.VersionGuid;

            OnDisk.Clear();
            foreach (RobloxVersionRecord record in RobloxVersions.OnDisk())
                OnDisk.Add(new RobloxVersionRow { Record = record, IsInstalled = string.Equals(record.Guid, installed, StringComparison.OrdinalIgnoreCase) });

            // the version in use is listed even before it has its package list copy (next launch)
            if (!string.IsNullOrEmpty(installed) && !OnDisk.Any(r => r.IsInstalled) && File.Exists(Path.Combine(RobloxVersions.FolderOf(installed), "RobloxPlayerBeta.exe")))
                OnDisk.Insert(0, new RobloxVersionRow { Record = new RobloxVersionRecord { Guid = installed, FileVersion = RobloxVersions.FileVersionOf(installed), InstalledUtc = Directory.GetCreationTimeUtc(RobloxVersions.FolderOf(installed)) }, IsInstalled = true });

            OnPropertyChanged(nameof(InstalledText));
            OnPropertyChanged(nameof(LatestText));
            OnPropertyChanged(nameof(PinChoices));
            OnPropertyChanged(nameof(MissingFlagsText));
            OnPropertyChanged(nameof(MissingFlagsVisibility));
            OnPropertyChanged(nameof(NothingKeptVisibility));
        }

        public string InstalledText
        {
            get
            {
                string guid = App.PlayerState.Prop.VersionGuid;
                if (string.IsNullOrEmpty(guid))
                    return "Roblox isn't installed through PhasmaStrap yet.";

                string version = RobloxVersions.FileVersionOf(guid);
                return $"In use: {(version.Length > 0 ? version : "unknown")}  ({guid})";
            }
        }

        public string LatestText
        {
            get
            {
                RobloxVersionData data = RobloxVersions.Load();
                if (string.IsNullOrEmpty(data.LatestGuid))
                    return "Roblox's newest version is looked up at the next launch.";

                return data.LatestGuid == App.PlayerState.Prop.VersionGuid
                    ? $"That's Roblox's newest version ({data.LatestFileVersion})."
                    : $"Roblox's newest is {data.LatestFileVersion} ({data.LatestGuid}) - you're not on it.";
            }
        }

        // ------------------------------------------------------------------ mode

        private string Mode
        {
            get => App.Settings.Prop.RobloxVersionMode;
            set
            {
                App.Settings.Prop.RobloxVersionMode = value;
                OnPropertyChanged(nameof(ModeLatest));
                OnPropertyChanged(nameof(ModeHold));
                OnPropertyChanged(nameof(ModePin));
                OnPropertyChanged(nameof(WarningVisibility));
            }
        }

        public bool ModeLatest { get => Mode != RobloxVersions.ModeHold && Mode != RobloxVersions.ModePin; set { if (value) Mode = RobloxVersions.ModeLatest; } }

        public bool ModeHold { get => Mode == RobloxVersions.ModeHold; set { if (value) Mode = RobloxVersions.ModeHold; } }

        public bool ModePin
        {
            get => Mode == RobloxVersions.ModePin;
            set
            {
                if (!value)
                    return;

                Mode = RobloxVersions.ModePin;
                if (string.IsNullOrEmpty(App.Settings.Prop.RobloxPinnedVersion))
                    PinnedVersion = PinChoices.FirstOrDefault()?.Record.Guid ?? "";
            }
        }

        public Visibility WarningVisibility => ModeLatest ? Visibility.Collapsed : Visibility.Visible;

        public List<RobloxVersionRow> PinChoices => OnDisk.Where(r => RobloxVersions.IsUsable(r.Record.Guid)).ToList();

        public string PinnedVersion
        {
            get => App.Settings.Prop.RobloxPinnedVersion;
            set { App.Settings.Prop.RobloxPinnedVersion = value ?? ""; OnPropertyChanged(nameof(PinnedVersion)); }
        }

        public ICommand PinCommand => new RelayCommand<RobloxVersionRow>(row =>
        {
            if (row is null)
                return;

            PinnedVersion = row.Record.Guid;
            Mode = RobloxVersions.ModePin;
            OnPropertyChanged(nameof(ModePin));
        });

        public bool KeepPrevious
        {
            get => App.Settings.Prop.RobloxKeepPreviousVersion;
            set { App.Settings.Prop.RobloxKeepPreviousVersion = value; OnPropertyChanged(nameof(KeepPrevious)); }
        }

        public Visibility NothingKeptVisibility => OnDisk.Count(r => !r.IsInstalled) == 0 ? Visibility.Visible : Visibility.Collapsed;

        // ------------------------------------------------------------------ flags

        public bool CheckFlags
        {
            get => App.Settings.Prop.RobloxCheckFlagsAfterUpdate;
            set { App.Settings.Prop.RobloxCheckFlagsAfterUpdate = value; OnPropertyChanged(nameof(CheckFlags)); }
        }

        public string MissingFlagsText
        {
            get
            {
                RobloxVersionData data = RobloxVersions.Load();
                if (string.IsNullOrEmpty(data.CheckedGuid))
                    return "";

                string version = data.History.FirstOrDefault(r => r.Guid == data.CheckedGuid)?.FileVersion ?? data.CheckedGuid;
                return data.MissingFlags.Count == 0
                    ? $"The update to {version} kept every flag of yours that the previous version had."
                    : $"The update to {version} removed {data.MissingFlags.Count} of your flags - they do nothing now:\n" + string.Join("\n", data.MissingFlags);
            }
        }

        public Visibility MissingFlagsVisibility => MissingFlagsText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private string _compareStatus = "";
        public string CompareStatus { get => _compareStatus; private set { _compareStatus = value; OnPropertyChanged(nameof(CompareStatus)); } }

        // the same check by hand: the version in use against the newest other one on this PC
        public ICommand CompareCommand => new AsyncRelayCommand(async () =>
        {
            string current = App.PlayerState.Prop.VersionGuid;
            RobloxVersionRow? other = OnDisk.FirstOrDefault(r => !r.IsInstalled);

            if (string.IsNullOrEmpty(current) || other is null)
            {
                CompareStatus = "This needs a second Roblox version on this PC to compare with. Turn on \"Keep the previous version\" and it's there after the next update.";
                return;
            }

            CompareStatus = "Comparing...";

            List<string> dropped = await Task.Run(() => RobloxVersions.DroppedFlags(
                Path.Combine(RobloxVersions.FolderOf(other.Record.Guid), "RobloxPlayerBeta.exe"),
                Path.Combine(RobloxVersions.FolderOf(current), "RobloxPlayerBeta.exe"),
                RobloxVersions.YourFlagNames()));

            CompareStatus = dropped.Count == 0
                ? $"Every flag of yours that {other.Record.FileVersion} had is still in the version you use."
                : $"{dropped.Count} of your flags were in {other.Record.FileVersion} but aren't in the version you use:\n" + string.Join("\n", dropped);
        });
    }
}
