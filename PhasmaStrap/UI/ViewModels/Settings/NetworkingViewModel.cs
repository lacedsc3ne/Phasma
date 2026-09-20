using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Networking;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class NetworkingViewModel : NotifyPropertyChangedViewModel
    {
        public AssetWarpViewModel Spoofer => AssetWarpViewModel.Shared;

        private string _wipeStatus = "";

        public string WipeStatus
        {
            get => _wipeStatus;
            private set { _wipeStatus = value; OnPropertyChanged(nameof(WipeStatus)); }
        }

        public ICommand WipeAllCommand => new RelayCommand(WipeAll);

        private void WipeAll()
        {
            const string LOG_IDENT = "NetworkingViewModel::WipeAll";

            MessageBoxResult confirm = Frontend.ShowMessageBox(
                "This deletes your saved Roblox login (you'll be signed out of Roblox itself, not just PhasmaStrap's account switcher), every PhasmaStrap/Roblox cache and log file, and the AssetWarp preload cache. This cannot be undone.\n\nContinue?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return;

            bool signedOut = false;

            try
            {
                string cookiePath = RobloxCookie.LiveCookiesDatPath;
                if (File.Exists(cookiePath))
                {
                    PhasmaStrap.Utility.AccountGuard.MarkOwnAccess();
                    File.Delete(cookiePath);
                    signedOut = true;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to delete Roblox cookies: {ex.Message}");
            }

            int filesDeleted = 0;

            foreach ((string _, string directory) in Cleaner.Directories)
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    continue;

                foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                        filesDeleted++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to delete '{file}': {ex.Message}");
                    }
                }
            }

            AssetPreloadCache.ClearCache();

            WipeStatus = signedOut
                ? $"Wiped {filesDeleted} file(s) and signed you out of Roblox."
                : $"Wiped {filesDeleted} file(s). No active Roblox login was found to sign out of.";

            App.Logger.WriteLine(LOG_IDENT, WipeStatus);
        }

        public bool ProxyEnabled
        {
            get => App.Settings.Prop.NetworkingProxyEnabled;
            set
            {
                if (value)
                {
                    bool ok = NetworkingController.Enable();
                    if (!ok)
                        App.Logger.WriteLine("NetworkingViewModel", "Enabling the proxy failed or the elevation prompt was declined");
                }
                else
                {
                    NetworkingController.Disable();
                }

                OnPropertyChanged(nameof(ProxyEnabled));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText
        {
            get
            {
                if (!App.Settings.Prop.NetworkingProxyEnabled)
                    return "Off";

                if (!ProxyHealth.IsHostedAnywhere())
                    return "Enabled, but the local listener isn't running - check the log";

                if (!HostsFileManager.IsBlockPresent())
                    return "Running, but the hosts file entries are missing - Roblox isn't being routed through it";

                if (!AssetProxyCA.IsInstalledInTrustStore())
                    return "Running, but the certificate isn't trusted - Roblox will refuse the connection until it's installed below";

                if (!HostsFileManager.IsBlockCurrent())
                    return "Running, but the hosts file entries are from an older version - toggle the proxy off and on to rewrite them";

                if (!AssetProxyCA.IsRobloxTrustBundlePatched())
                    return "Running - Roblox's own certificate bundle will be patched on the next launch";

                return ProxyHealth.RobloxVerdict()
                    ?? "Ready - hosts entries and certificates are in place; open a game to see whether Roblox connects through it";
            }
        }

        public bool IsCertificateInstalled => AssetProxyCA.IsInstalledInTrustStore();

        public string CertificateStatusText
        {
            get
            {
                if (!IsCertificateInstalled)
                    return "Not installed - the proxy can't work until it is; enabling the proxy installs it automatically";

                if (!AssetProxyCA.IsRobloxTrustBundlePatched())
                    return "Installed to your Windows certificate store (current user only); Roblox's own bundle is patched the next time Roblox launches";

                return AssetProxyCA.RunningRobloxTrustsProxy() == false
                    ? "Installed to your Windows certificate store and Roblox's bundle - but the open Roblox started before that, so restart it"
                    : "Installed to your Windows certificate store (current user only) and to Roblox's own certificate bundle";
            }
        }

        public IEnumerable<PresenceSpoofMode> PresenceSpoofModes { get; } = Enum.GetValues(typeof(PresenceSpoofMode)).Cast<PresenceSpoofMode>();

        public PresenceSpoofMode SelectedPresenceSpoofMode
        {
            get => App.Settings.Prop.PresenceSpoofMode;
            set { App.Settings.Prop.PresenceSpoofMode = value; App.Settings.Save(); OnPropertyChanged(nameof(SelectedPresenceSpoofMode)); }
        }

        public string RobuxSpoofAmount
        {
            get => App.Settings.Prop.RobuxSpoofAmount;
            set { App.Settings.Prop.RobuxSpoofAmount = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(RobuxSpoofAmount)); }
        }

        public string UsernameSpoofName
        {
            get => App.Settings.Prop.UsernameSpoofName;
            set { App.Settings.Prop.UsernameSpoofName = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(UsernameSpoofName)); }
        }

        public ICommand OpenAssetWarpCommand => new RelayCommand(() =>
        {
            var window = System.Windows.Application.Current.Windows.OfType<UI.Elements.Settings.MainWindow>().FirstOrDefault();
            if (window is null)
                return;

            var entry = new UI.Elements.Settings.Search.SettingsSearchEntry(
                UI.Elements.Settings.Search.SettingsSearchEntryKind.Tab,
                Strings.Menu_FastFlagSettings_Tab_AssetWarp, "",
                typeof(UI.Elements.Settings.Pages.FastFlagSettingsPage), Strings.Menu_FastFlagSettings_Title,
                Strings.Menu_FastFlagSettings_Tab_AssetWarp, "", "", null);

            UI.Elements.Settings.Search.SettingsSearchNavigator.Reveal(window.GetNavigation(), window.GetFrame(), entry);
        });

        public ICommand InstallCertificateCommand => new RelayCommand(() =>
        {
            AssetProxyCA.InstallToTrustStore();
            AssetProxyCA.PatchRobloxTrustBundles();
            OnPropertyChanged(nameof(IsCertificateInstalled));
            OnPropertyChanged(nameof(CertificateStatusText));
            OnPropertyChanged(nameof(StatusText));
        });

        public bool BlockRobloxTelemetry
        {
            get => App.Settings.Prop.BlockRobloxTelemetry;
            set
            {
                bool ok = value ? Integrations.TelemetryBlocker.RequestApply() : Integrations.TelemetryBlocker.RequestRemove();

                if (ok)
                    App.Settings.Prop.BlockRobloxTelemetry = value;

                OnPropertyChanged(nameof(BlockRobloxTelemetry));
                OnPropertyChanged(nameof(TelemetryBlockerStatusText));
            }
        }

        public string TelemetryBlockerStatusText => Integrations.TelemetryBlocker.IsApplied()
            ? $"Blocking {Integrations.TelemetryBlocker.Domains.Length} telemetry domains"
            : "Off";

        public ICommand RemoveCertificateCommand => new RelayCommand(() =>
        {
            AssetProxyCA.RemoveFromTrustStore();
            AssetProxyCA.UnpatchRobloxTrustBundles();
            OnPropertyChanged(nameof(IsCertificateInstalled));
            OnPropertyChanged(nameof(CertificateStatusText));
            OnPropertyChanged(nameof(StatusText));
        });

        public ObservableCollection<AssetCacheEntry> PreloadCacheEntries { get; } = new();

        public string PreloadCacheSummary
        {
            get
            {
                if (PreloadCacheEntries.Count == 0)
                    return "Cache is empty.";

                long totalBytes = PreloadCacheEntries.Sum(e => e.SizeBytes);
                string sizeText = totalBytes >= 1024 * 1024 ? $"{totalBytes / 1048576.0:0.#} MB" : $"{totalBytes / 1024.0:0.#} KB";
                return $"{PreloadCacheEntries.Count} entrie(s), {sizeText}";
            }
        }

        public ICommand RefreshPreloadCacheCommand => new RelayCommand(RefreshPreloadCache);

        public ICommand ClearPreloadCacheCommand => new RelayCommand(() =>
        {
            AssetPreloadCache.ClearCache();
            RefreshPreloadCache();
        });

        public ICommand DeletePreloadCacheEntryCommand => new RelayCommand<AssetCacheEntry>(entry =>
        {
            if (entry is null)
                return;

            AssetPreloadCache.DeleteEntry(entry.FileName);
            RefreshPreloadCache();
        });

        public NetworkingViewModel()
        {
            RefreshPreloadCache();
        }

        private void RefreshPreloadCache()
        {
            PreloadCacheEntries.Clear();

            foreach (AssetCacheEntry entry in AssetPreloadCache.ListEntries())
                PreloadCacheEntries.Add(entry);

            OnPropertyChanged(nameof(PreloadCacheSummary));
        }
    }
}
