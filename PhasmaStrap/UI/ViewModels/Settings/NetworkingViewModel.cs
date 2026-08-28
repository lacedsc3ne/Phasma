using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Networking;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class NetworkingViewModel : NotifyPropertyChangedViewModel
    {
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
    }
}
