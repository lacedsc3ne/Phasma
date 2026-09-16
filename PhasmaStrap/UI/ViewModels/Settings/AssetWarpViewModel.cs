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

        // --- presence spoofer (same underlying Settings.PresenceSpoofMode the Networking page exposes) ---

        public IEnumerable<PresenceSpoofMode> PresenceSpoofModes { get; } = Enum.GetValues(typeof(PresenceSpoofMode)).Cast<PresenceSpoofMode>();

        public PresenceSpoofMode SelectedPresenceSpoofMode
        {
            get => App.Settings.Prop.PresenceSpoofMode;
            set { App.Settings.Prop.PresenceSpoofMode = value; App.Settings.Save(); }
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
            set { App.Settings.Prop.SpoofOthersName = value; App.Settings.Save(); }
        }

        public bool SpoofOthersApplyIngame
        {
            get => App.Settings.Prop.SpoofOthersApplyIngame;
            set { App.Settings.Prop.SpoofOthersApplyIngame = value; App.Settings.Save(); }
        }

        public bool SpoofOthersVerified
        {
            get => App.Settings.Prop.SpoofOthersVerified;
            set { App.Settings.Prop.SpoofOthersVerified = value; App.Settings.Save(); }
        }

        public string SpoofSelfName
        {
            get => App.Settings.Prop.SpoofSelfName;
            set { App.Settings.Prop.SpoofSelfName = value; App.Settings.Save(); }
        }

        public bool SpoofSelfApplyIngame
        {
            get => App.Settings.Prop.SpoofSelfApplyIngame;
            set { App.Settings.Prop.SpoofSelfApplyIngame = value; App.Settings.Save(); }
        }

        public bool SpoofSelfVerified
        {
            get => App.Settings.Prop.SpoofSelfVerified;
            set { App.Settings.Prop.SpoofSelfVerified = value; App.Settings.Save(); }
        }

        public bool SpoofSelfGameCreator
        {
            get => App.Settings.Prop.SpoofSelfGameCreator;
            set { App.Settings.Prop.SpoofSelfGameCreator = value; App.Settings.Save(); }
        }

        // --- Robux adjuster (same underlying Settings.RobuxSpoofAmount the Networking page exposes) ---

        public string RobuxSpoofAmount
        {
            get => App.Settings.Prop.RobuxSpoofAmount;
            set { App.Settings.Prop.RobuxSpoofAmount = value; App.Settings.Save(); OnPropertyChanged(nameof(RobuxSpoofSummary)); }
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

                return AssetWarpPolicy.IsEnabled
                    ? Strings.Menu_AssetWarp_Status_Blocking
                    : Strings.Menu_AssetWarp_Status_NoneSelected;
            }
        }
    }
}
