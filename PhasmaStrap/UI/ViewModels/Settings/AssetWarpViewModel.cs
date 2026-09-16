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
