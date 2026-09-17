using PhasmaStrap.Networking;
using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    // backs the "Asset Warp" tab on FastFlagSettingsPage. Deliberately separate from
    // NetworkingViewModel (which also surfaces these same App.Settings.Prop.AssetWarp*
    // properties on the Networking page) so this tab can behave as a self-contained
    // feature - flipping the master switch here also brings up PhasmaStrap's local proxy
    // if it isn't already running, rather than requiring the user to separately visit the
    // Networking page first. Turning the master switch off only turns AssetWarp itself off;
    // it deliberately leaves the shared proxy running, since other features (presence/Robux/
    // username spoofing on the Networking page) may still depend on it.
    public class AssetWarpViewModel : NotifyPropertyChangedViewModel
    {
        public bool AssetWarpEnabled
        {
            get => App.Settings.Prop.AssetWarpEnabled;
            set
            {
                if (value && !NetworkingController.IsActive)
                {
                    bool ok = NetworkingController.Enable();
                    if (!ok)
                    {
                        App.Logger.WriteLine("AssetWarpViewModel", "Could not start the local proxy, leaving Asset Warp off");
                        OnPropertyChanged(nameof(AssetWarpEnabled));
                        OnPropertyChanged(nameof(StatusText));
                        return;
                    }
                }

                App.Settings.Prop.AssetWarpEnabled = value;
                App.Settings.Save();

                OnPropertyChanged(nameof(AssetWarpEnabled));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool DisableAllTextures
        {
            get => App.Settings.Prop.AssetWarpDisableAllTextures;
            set { App.Settings.Prop.AssetWarpDisableAllTextures = value; App.Settings.Save(); OnPropertyChanged(nameof(DisableAllTextures)); OnPropertyChanged(nameof(StatusText)); }
        }

        public bool DisableAllDecals
        {
            get => App.Settings.Prop.AssetWarpDisableAllDecals;
            set { App.Settings.Prop.AssetWarpDisableAllDecals = value; App.Settings.Save(); OnPropertyChanged(nameof(DisableAllDecals)); OnPropertyChanged(nameof(StatusText)); }
        }

        public bool DisableAllImages
        {
            get => App.Settings.Prop.AssetWarpDisableAllImages;
            set { App.Settings.Prop.AssetWarpDisableAllImages = value; App.Settings.Save(); OnPropertyChanged(nameof(DisableAllImages)); OnPropertyChanged(nameof(StatusText)); }
        }

        public bool DisableAllAnimations
        {
            get => App.Settings.Prop.AssetWarpDisableAllAnimations;
            set { App.Settings.Prop.AssetWarpDisableAllAnimations = value; App.Settings.Save(); OnPropertyChanged(nameof(DisableAllAnimations)); OnPropertyChanged(nameof(StatusText)); }
        }

        public bool DisableAllMeshes
        {
            get => App.Settings.Prop.AssetWarpDisableAllMeshes;
            set { App.Settings.Prop.AssetWarpDisableAllMeshes = value; App.Settings.Save(); OnPropertyChanged(nameof(DisableAllMeshes)); OnPropertyChanged(nameof(StatusText)); }
        }

        // every spoofer below only does anything while the local proxy is routing Roblox's
        // traffic - so switching one ON brings the proxy up (one UAC prompt for the hosts file
        // the first time), exactly like the Asset Warp master switch does. Previously these
        // just saved the setting, which is why "none of the spoof settings work" when the
        // proxy had never been enabled from the Networking page.
        private bool EnsureProxyForSpoofer(bool turningOn)
        {
            if (!turningOn || NetworkingController.IsActive)
                return true;

            bool ok = NetworkingController.Enable();
            if (!ok)
                App.Logger.WriteLine("AssetWarpViewModel", "Could not start the local proxy, leaving the spoofer off");

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(SpooferStatusText));
            return ok;
        }

        public string SpooferStatusText
        {
            get
            {
                if (!NetworkingController.IsActive)
                    return "Local proxy is off - turn any spoofer on to start it (you'll get one admin prompt for the hosts file).";

                if (!AssetProxyCA.IsRobloxTrustBundlePatched())
                    return "Proxy is running, but Roblox's own certificate bundle isn't patched yet - it will be on the next launch.";

                return "Local proxy is running - spoofed values apply to this client only, the next time Roblox asks for them.";
            }
        }

        // --- presence spoofer (same underlying Settings.PresenceSpoofMode the Networking page exposes) ---

        public IEnumerable<PresenceSpoofMode> PresenceSpoofModes { get; } = Enum.GetValues(typeof(PresenceSpoofMode)).Cast<PresenceSpoofMode>();

        public PresenceSpoofMode SelectedPresenceSpoofMode
        {
            get => App.Settings.Prop.PresenceSpoofMode;
            set
            {
                if (!EnsureProxyForSpoofer(value != PresenceSpoofMode.Off))
                {
                    OnPropertyChanged(nameof(SelectedPresenceSpoofMode));
                    return;
                }

                App.Settings.Prop.PresenceSpoofMode = value;
                App.Settings.Save();
                OnPropertyChanged(nameof(SelectedPresenceSpoofMode));
            }
        }

        // --- preloading ---

        public bool PreloadEnabled
        {
            get => App.Settings.Prop.AssetWarpPreloadEnabled;
            set { App.Settings.Prop.AssetWarpPreloadEnabled = value; App.Settings.Save(); }
        }

        public int PreloadCacheMb
        {
            get => App.Settings.Prop.AssetWarpPreloadCacheMb;
            set { App.Settings.Prop.AssetWarpPreloadCacheMb = value; App.Settings.Save(); OnPropertyChanged(nameof(PreloadCacheMb)); OnPropertyChanged(nameof(PreloadCacheSizeDisplay)); }
        }

        public string PreloadCacheSizeDisplay => PreloadCacheMb >= 1024 ? $"{PreloadCacheMb / 1024.0:0.#} GB" : $"{PreloadCacheMb} MB";

        public bool PreloadAvatar
        {
            get => App.Settings.Prop.AssetWarpPreloadAvatar;
            set { App.Settings.Prop.AssetWarpPreloadAvatar = value; App.Settings.Save(); }
        }

        public bool PreloadCrossGame
        {
            get => App.Settings.Prop.AssetWarpPreloadCrossGame;
            set { App.Settings.Prop.AssetWarpPreloadCrossGame = value; App.Settings.Save(); }
        }

        // --- client spoofer (self vs. others) ---

        public string SpoofOthersName
        {
            get => App.Settings.Prop.SpoofOthersName;
            set { App.Settings.Prop.SpoofOthersName = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(SpoofOthersName)); }
        }

        public bool SpoofOthersApplyIngame
        {
            get => App.Settings.Prop.SpoofOthersApplyIngame;
            set => SetSpoofToggle(value, v => App.Settings.Prop.SpoofOthersApplyIngame = v, nameof(SpoofOthersApplyIngame));
        }

        public bool SpoofOthersVerified
        {
            get => App.Settings.Prop.SpoofOthersVerified;
            set => SetSpoofToggle(value, v => App.Settings.Prop.SpoofOthersVerified = v, nameof(SpoofOthersVerified));
        }

        public string SpoofSelfName
        {
            get => App.Settings.Prop.SpoofSelfName;
            set { App.Settings.Prop.SpoofSelfName = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(SpoofSelfName)); }
        }

        public bool SpoofSelfApplyIngame
        {
            get => App.Settings.Prop.SpoofSelfApplyIngame;
            set => SetSpoofToggle(value, v => App.Settings.Prop.SpoofSelfApplyIngame = v, nameof(SpoofSelfApplyIngame));
        }

        public bool SpoofSelfVerified
        {
            get => App.Settings.Prop.SpoofSelfVerified;
            set => SetSpoofToggle(value, v => App.Settings.Prop.SpoofSelfVerified = v, nameof(SpoofSelfVerified));
        }

        public bool SpoofSelfGameCreator
        {
            get => App.Settings.Prop.SpoofSelfGameCreator;
            set => SetSpoofToggle(value, v => App.Settings.Prop.SpoofSelfGameCreator = v, nameof(SpoofSelfGameCreator));
        }

        private void SetSpoofToggle(bool value, Action<bool> write, string propertyName)
        {
            if (!EnsureProxyForSpoofer(value))
            {
                OnPropertyChanged(propertyName);
                return;
            }

            write(value);
            App.Settings.Save();
            OnPropertyChanged(propertyName);
        }

        // --- Robux adjuster (same underlying Settings.RobuxSpoofAmount the Networking page exposes) ---

        public string RobuxSpoofAmount
        {
            get => App.Settings.Prop.RobuxSpoofAmount;
            set
            {
                if (!EnsureProxyForSpoofer(!string.IsNullOrWhiteSpace(value)))
                {
                    OnPropertyChanged(nameof(RobuxSpoofAmount));
                    return;
                }

                App.Settings.Prop.RobuxSpoofAmount = value;
                App.Settings.SaveDeferred();
                OnPropertyChanged(nameof(RobuxSpoofAmount));
                OnPropertyChanged(nameof(RobuxSpoofSummary));
            }
        }

        public string RobuxSpoofSummary =>
            string.IsNullOrWhiteSpace(App.Settings.Prop.RobuxSpoofAmount)
                ? Strings.Menu_AssetWarp_RobuxAdjuster_Empty
                : string.Format(Strings.Menu_AssetWarp_RobuxAdjuster_Set, App.Settings.Prop.RobuxSpoofAmount);

        public string StatusText
        {
            get
            {
                if (!App.Settings.Prop.AssetWarpEnabled)
                    return Strings.Menu_AssetWarp_Status_Off;

                if (!NetworkingController.IsActive)
                    return Strings.Menu_AssetWarp_Status_ProxyNotRunning;

                if (!AssetProxyCA.IsInstalledInTrustStore())
                    return "Proxy is running, but its certificate isn't trusted yet - Roblox will refuse the connection. Install it from the Networking page.";

                if (!AssetProxyCA.IsRobloxTrustBundlePatched())
                    return "Proxy is running, but Roblox's own certificate bundle isn't patched yet - it will be on the next launch.";

                return AssetWarpPolicy.IsEnabled
                    ? Strings.Menu_AssetWarp_Status_Blocking
                    : Strings.Menu_AssetWarp_Status_NoneSelected;
            }
        }
    }
}
