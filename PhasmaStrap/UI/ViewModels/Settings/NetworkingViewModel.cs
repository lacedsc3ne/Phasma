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
        // --- panic wipe: signs out + clears every cache/log directory the Cleanup feature
        // already knows about (Cleaner.Directories), plus AssetWarp's preload cache. A single,
        // immediate, synchronous action - not the scheduled Cleaner, which only runs after
        // Roblox closes and only for whichever directories the user opted into. ---

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

        public string StatusText => App.Settings.Prop.NetworkingProxyEnabled
            ? (NetworkingController.IsActive ? "Running" : "Enabled, but not confirmed running - check the log")
            : "Off";

        public bool IsCertificateInstalled => AssetProxyCA.IsInstalledInTrustStore();

        public string CertificateStatusText => IsCertificateInstalled
            ? "Installed to your Windows certificate store (current user only)"
            : "Not installed - the proxy can run without this, but Roblox will show TLS certificate warnings/errors until it's installed";

        public IEnumerable<PresenceSpoofMode> PresenceSpoofModes { get; } = Enum.GetValues(typeof(PresenceSpoofMode)).Cast<PresenceSpoofMode>();

        public PresenceSpoofMode SelectedPresenceSpoofMode
        {
            get => App.Settings.Prop.PresenceSpoofMode;
            set => App.Settings.Prop.PresenceSpoofMode = value;
        }

        public string RobuxSpoofAmount
        {
            get => App.Settings.Prop.RobuxSpoofAmount;
            set => App.Settings.Prop.RobuxSpoofAmount = value;
        }

        public string UsernameSpoofName
        {
            get => App.Settings.Prop.UsernameSpoofName;
            set => App.Settings.Prop.UsernameSpoofName = value;
        }

        public ICommand InstallCertificateCommand => new RelayCommand(() =>
        {
            AssetProxyCA.InstallToTrustStore();
            OnPropertyChanged(nameof(IsCertificateInstalled));
            OnPropertyChanged(nameof(CertificateStatusText));
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
            OnPropertyChanged(nameof(IsCertificateInstalled));
            OnPropertyChanged(nameof(CertificateStatusText));
        });

        public bool AssetWarpEnabled
        {
            get => App.Settings.Prop.AssetWarpEnabled;
            set
            {
                App.Settings.Prop.AssetWarpEnabled = value;
                OnPropertyChanged(nameof(AssetWarpEnabled));
                OnPropertyChanged(nameof(AssetWarpStatusText));
            }
        }

        public bool AssetWarpDisableAllTextures
        {
            get => App.Settings.Prop.AssetWarpDisableAllTextures;
            set
            {
                App.Settings.Prop.AssetWarpDisableAllTextures = value;
                OnPropertyChanged(nameof(AssetWarpDisableAllTextures));
                OnPropertyChanged(nameof(AssetWarpStatusText));
            }
        }

        public bool AssetWarpDisableAllDecals
        {
            get => App.Settings.Prop.AssetWarpDisableAllDecals;
            set
            {
                App.Settings.Prop.AssetWarpDisableAllDecals = value;
                OnPropertyChanged(nameof(AssetWarpDisableAllDecals));
                OnPropertyChanged(nameof(AssetWarpStatusText));
            }
        }

        public bool AssetWarpDisableAllImages
        {
            get => App.Settings.Prop.AssetWarpDisableAllImages;
            set
            {
                App.Settings.Prop.AssetWarpDisableAllImages = value;
                OnPropertyChanged(nameof(AssetWarpDisableAllImages));
                OnPropertyChanged(nameof(AssetWarpStatusText));
            }
        }

        public bool AssetWarpDisableAllAnimations
        {
            get => App.Settings.Prop.AssetWarpDisableAllAnimations;
            set
            {
                App.Settings.Prop.AssetWarpDisableAllAnimations = value;
                OnPropertyChanged(nameof(AssetWarpDisableAllAnimations));
                OnPropertyChanged(nameof(AssetWarpStatusText));
            }
        }

        public bool AssetWarpDisableAllMeshes
        {
            get => App.Settings.Prop.AssetWarpDisableAllMeshes;
            set
            {
                App.Settings.Prop.AssetWarpDisableAllMeshes = value;
                OnPropertyChanged(nameof(AssetWarpDisableAllMeshes));
                OnPropertyChanged(nameof(AssetWarpStatusText));
            }
        }

        public string AssetWarpStatusText => AssetWarpPolicy.IsEnabled
            ? "Blocking selected asset type(s) through the local proxy"
            : (App.Settings.Prop.AssetWarpEnabled ? "On, but no asset types selected below - nothing is blocked yet" : "Off");

        // --- preload cache browser (AssetPreloadCache) - entries are hashed request keys, not
        // asset names (see AssetCacheEntry's doc comment), so this is a size/age view, not a
        // content browser: see how much space preloading is using, clear stale entries. ---

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
