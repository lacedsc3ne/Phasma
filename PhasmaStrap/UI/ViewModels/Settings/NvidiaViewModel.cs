using System.Collections.ObjectModel;
using System.Windows;

using PhasmaStrap.Integrations.Nvidia;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    // Backs NvidiaPage. Talks directly to the NVIDIA driver (via NvApi/NvidiaProfileInspector)
    // to read and write a dedicated "PhasmaStrap" driver profile scoped to
    // RobloxPlayerBeta.exe/RobloxStudioBeta.exe - this is completely separate from Roblox's
    // own FastFlags or in-game settings, and only takes effect on NVIDIA GPUs.
    //
    // The setting IDs below are the community-known NVIDIA driver profile setting IDs (as
    // used by NVIDIA Profile Inspector) for each feature - ported over from the curated list
    // Voidstrap's NvidiaFastFlagsViewModel exposed, which is the actually-useful subset of
    // what NvidiaProfileInspector.cs can read/write. Voidstrap's raw arbitrary-setting editor,
    // .nip import/export, and "copy from another app" dialogs were intentionally not ported -
    // see NvidiaPage.xaml.cs for why.
    public class NvidiaViewModel : NotifyPropertyChangedViewModel
    {
        private const uint IdLowLatencyMode = 390467;
        private const uint IdFrlLowLatencyMode = 277041152;
        private const uint IdFrameRateLimit = 277041154;
        private const uint IdBackgroundFrameRateLimit = 277041157;
        private const uint IdResizableBar = 549198379;
        private const uint IdDlssSuperResolution = 283385345;
        private const uint IdDlssFrameGeneration = 283385347;
        private const uint IdMfaa = 10011052;
        private const uint IdFxaaEnable = 276089202;
        private const uint IdAntialiasingMode = 276757595;
        private const uint IdGammaCorrection = 276652957;
        private const uint IdLineGamma = 545898348;
        private const uint IdSilkSmoothness = 9990737;
        private const uint IdTextureLodBias = 7573135;
        private const uint IdTextureFilteringQuality = 13510289;
        private const uint IdAnisotropicFilteringMode = 282245910;
        private const uint IdTransparencySupersampling = 282364549;

        private static readonly uint[] AllTrackedIds = new[]
        {
            IdLowLatencyMode, IdFrlLowLatencyMode, IdFrameRateLimit, IdBackgroundFrameRateLimit,
            IdResizableBar, IdDlssSuperResolution, IdDlssFrameGeneration, IdMfaa,
            IdFxaaEnable, IdAntialiasingMode, IdGammaCorrection, IdLineGamma,
            IdSilkSmoothness, IdTextureLodBias, IdTextureFilteringQuality,
            IdAnisotropicFilteringMode, IdTransparencySupersampling,
        };

        private string _lowLatencyMode = "Off";
        private string _frlLowLatencyMode = "Off";
        private int _frameRateLimit;
        private int _backgroundFrameRateLimit;
        private bool _resizableBar;
        private bool _dlssSuperResolution;
        private bool _dlssFrameGeneration;
        private bool _mfaa;
        private bool _fxaa;
        private bool _gammaCorrection = true;
        private string _silkSmoothness = "Off";
        private int _textureLodBias;
        private string _statusMessage = string.Empty;

        public NvidiaViewModel()
        {
            IsAvailable = NvidiaProfileInspector.IsAvailable;
            UnavailableReason = NvidiaProfileInspector.UnavailableReason;

            if (IsAvailable)
                LoadFromDriver();
        }

        public bool IsAvailable { get; }

        public string UnavailableReason { get; }

        public Visibility AvailableVisibility => IsAvailable ? Visibility.Visible : Visibility.Collapsed;

        public Visibility UnavailableVisibility => IsAvailable ? Visibility.Collapsed : Visibility.Visible;

        public ObservableCollection<string> LowLatencyModes { get; } = new ObservableCollection<string> { "Off", "On", "Ultra" };

        public ObservableCollection<string> FrlLowLatencyModes { get; } = new ObservableCollection<string> { "Off", "On" };

        public ObservableCollection<string> SilkSmoothnessModes { get; } = new ObservableCollection<string> { "Off", "Low", "Medium", "High", "Ultra" };

        public string LowLatencyMode
        {
            get => _lowLatencyMode;
            set { _lowLatencyMode = value; OnPropertyChanged(nameof(LowLatencyMode)); }
        }

        public string FrlLowLatencyMode
        {
            get => _frlLowLatencyMode;
            set { _frlLowLatencyMode = value; OnPropertyChanged(nameof(FrlLowLatencyMode)); }
        }

        public int FrameRateLimit
        {
            get => _frameRateLimit;
            set { _frameRateLimit = Math.Clamp(value, 0, 1000); OnPropertyChanged(nameof(FrameRateLimit)); }
        }

        public int BackgroundFrameRateLimit
        {
            get => _backgroundFrameRateLimit;
            set { _backgroundFrameRateLimit = Math.Clamp(value, 0, 1000); OnPropertyChanged(nameof(BackgroundFrameRateLimit)); }
        }

        public bool ResizableBar
        {
            get => _resizableBar;
            set { _resizableBar = value; OnPropertyChanged(nameof(ResizableBar)); }
        }

        public bool DlssSuperResolution
        {
            get => _dlssSuperResolution;
            set { _dlssSuperResolution = value; OnPropertyChanged(nameof(DlssSuperResolution)); }
        }

        public bool DlssFrameGeneration
        {
            get => _dlssFrameGeneration;
            set { _dlssFrameGeneration = value; OnPropertyChanged(nameof(DlssFrameGeneration)); }
        }

        public bool Mfaa
        {
            get => _mfaa;
            set { _mfaa = value; OnPropertyChanged(nameof(Mfaa)); }
        }

        public bool Fxaa
        {
            get => _fxaa;
            set { _fxaa = value; OnPropertyChanged(nameof(Fxaa)); }
        }

        public bool GammaCorrection
        {
            get => _gammaCorrection;
            set { _gammaCorrection = value; OnPropertyChanged(nameof(GammaCorrection)); }
        }

        public string SilkSmoothness
        {
            get => _silkSmoothness;
            set { _silkSmoothness = value; OnPropertyChanged(nameof(SilkSmoothness)); }
        }

        public int TextureLodBias
        {
            get => _textureLodBias;
            set
            {
                _textureLodBias = Math.Clamp(value, -32, 120);
                OnPropertyChanged(nameof(TextureLodBias));
                OnPropertyChanged(nameof(TextureLodBiasLabel));
            }
        }

        public string TextureLodBiasLabel =>
            TextureLodBias != 0
                ? string.Format(CultureInfo.InvariantCulture, "LOD bias override: {0:0.###}", TextureLodBias / 8.0)
                : "Default (driver controlled)";

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        private void LoadFromDriver()
        {
            Dictionary<uint, uint> live = NvidiaProfileInspector.ReadValues(AllTrackedIds);

            LowLatencyMode = ReadEnum(live, IdLowLatencyMode, LowLatencyModes);
            FrlLowLatencyMode = ReadEnum(live, IdFrlLowLatencyMode, FrlLowLatencyModes);
            FrameRateLimit = ReadInt(live, IdFrameRateLimit);
            BackgroundFrameRateLimit = ReadInt(live, IdBackgroundFrameRateLimit);
            ResizableBar = ReadBool(live, IdResizableBar);
            DlssSuperResolution = ReadBool(live, IdDlssSuperResolution);
            DlssFrameGeneration = ReadBool(live, IdDlssFrameGeneration);
            Mfaa = ReadBool(live, IdMfaa);
            Fxaa = ReadBool(live, IdFxaaEnable) && ReadBool(live, IdAntialiasingMode);
            GammaCorrection = !ReadBool(live, IdGammaCorrection) || !ReadBool(live, IdLineGamma);
            SilkSmoothness = SilkFromValue(ReadInt(live, IdSilkSmoothness));
            TextureLodBias = live.TryGetValue(IdTextureLodBias, out uint bias) ? unchecked((int)bias) : 0;
        }

        // Runs on a background thread; the caller (NvidiaPage) is responsible for
        // marshalling any UI feedback back to the dispatcher thread.
        public NvidiaApplyResult ApplyToDriver()
        {
            Dictionary<uint, uint> settings = new Dictionary<uint, uint>
            {
                [IdLowLatencyMode] = (uint)LowLatencyModes.IndexOf(LowLatencyMode),
                [IdFrlLowLatencyMode] = (uint)FrlLowLatencyModes.IndexOf(FrlLowLatencyMode),
                [IdFrameRateLimit] = (uint)FrameRateLimit,
                [IdBackgroundFrameRateLimit] = (uint)BackgroundFrameRateLimit,
                [IdResizableBar] = ResizableBar ? 1u : 0u,
                [IdDlssSuperResolution] = DlssSuperResolution ? 1u : 0u,
                [IdDlssFrameGeneration] = DlssFrameGeneration ? 1u : 0u,
                [IdMfaa] = Mfaa ? 1u : 0u,
                [IdFxaaEnable] = Fxaa ? 1u : 0u,
                [IdAntialiasingMode] = Fxaa ? 1u : 0u,
                [IdSilkSmoothness] = (uint)SilkToValue(SilkSmoothness),
                [IdTextureLodBias] = unchecked((uint)TextureLodBias),
            };

            uint gamma = GammaCorrection ? 0u : 1u;
            settings[IdGammaCorrection] = gamma;
            settings[IdLineGamma] = gamma;

            if (TextureLodBias != 0)
            {
                settings[IdTextureFilteringQuality] = 20u;
                settings[IdAnisotropicFilteringMode] = 1u;
                settings[IdTransparencySupersampling] = 8u;
            }

            NvidiaApplyResult result = NvidiaProfileInspector.Apply(settings);
            StatusMessage = result.Message;
            return result;
        }

        public void ReloadFromDriver()
        {
            if (IsAvailable)
                LoadFromDriver();
        }

        private static string ReadEnum(Dictionary<uint, uint> live, uint id, ObservableCollection<string> options)
        {
            if (live.TryGetValue(id, out uint value) && value < (uint)options.Count)
                return options[(int)value];
            return options[0];
        }

        private static int ReadInt(Dictionary<uint, uint> live, uint id)
        {
            return live.TryGetValue(id, out uint value) ? unchecked((int)value) : 0;
        }

        private static bool ReadBool(Dictionary<uint, uint> live, uint id)
        {
            return live.TryGetValue(id, out uint value) && value != 0;
        }

        private static string SilkFromValue(int value)
        {
            return value switch
            {
                1 => "Low",
                2 => "Medium",
                3 => "High",
                4 => "Ultra",
                _ => "Off",
            };
        }

        private static int SilkToValue(string mode)
        {
            return mode switch
            {
                "Low" => 1,
                "Medium" => 2,
                "High" => 3,
                "Ultra" => 4,
                _ => 0,
            };
        }
    }
}
