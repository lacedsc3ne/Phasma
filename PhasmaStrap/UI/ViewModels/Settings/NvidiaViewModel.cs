using System.Collections.ObjectModel;
using System.Windows;

using PhasmaStrap.Integrations.Nvidia;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class NvidiaViewModel : NotifyPropertyChangedViewModel
    {
        private const uint IdLowLatencyMode = 390467;
        private const uint IdFrlLowLatencyMode = 277041152;
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
        private const uint IdBenchmarkOverlay = 2945366;

        private static readonly uint[] AllTrackedIds = new[]
        {
            IdLowLatencyMode, IdFrlLowLatencyMode, IdBackgroundFrameRateLimit,
            IdResizableBar, IdDlssSuperResolution, IdDlssFrameGeneration, IdMfaa,
            IdFxaaEnable, IdAntialiasingMode, IdGammaCorrection, IdLineGamma,
            IdSilkSmoothness, IdTextureLodBias, IdTextureFilteringQuality,
            IdAnisotropicFilteringMode, IdTransparencySupersampling, IdBenchmarkOverlay,
        };

        private static readonly (string Label, uint Value)[] BenchmarkOverlayOptions =
        {
            (Strings.Menu_Nvidia_BenchmarkOverlay_Disabled, 0u),
            (Strings.Menu_Nvidia_BenchmarkOverlay_GraphFlipFps, 1u),
            (Strings.Menu_Nvidia_BenchmarkOverlay_GraphPresentFps, 2u),
            (Strings.Menu_Nvidia_BenchmarkOverlay_GraphAppPresentFps, 4u),
            (Strings.Menu_Nvidia_BenchmarkOverlay_DisplayPaging, 8u),
            (Strings.Menu_Nvidia_BenchmarkOverlay_DisplayAppThreadWait, 16u),
            (Strings.Menu_Nvidia_BenchmarkOverlay_Enabled, 511u),
        };

        private string _lowLatencyMode = Strings.Menu_Nvidia_Mode_Off;
        private string _frlLowLatencyMode = Strings.Menu_Nvidia_Mode_Off;
        private int _backgroundFrameRateLimit;
        private bool _resizableBar;
        private bool _dlssSuperResolution;
        private bool _dlssFrameGeneration;
        private bool _mfaa;
        private bool _fxaa;
        private bool _gammaCorrection = true;
        private string _silkSmoothness = Strings.Menu_Nvidia_Mode_Off;
        private int _textureLodBias;
        private string _benchmarkOverlayMode = Strings.Menu_Nvidia_BenchmarkOverlay_Disabled;
        private string _statusMessage = string.Empty;
        private bool _nvidiaEditorViewMode;

        public NvidiaViewModel()
        {
            IsAvailable = NvidiaProfileInspector.IsAvailable;
            UnavailableReason = NvidiaProfileInspector.UnavailableReason;

            RefreshFlagHistory();
            NvidiaFlagHistory.Changed += OnFlagHistoryChanged;

            if (IsAvailable)
            {
                StatusMessage = "Reading the NVIDIA driver profile...";
                _ = LoadFromDriverAsync();
            }
        }

        private async Task LoadFromDriverAsync()
        {
            try
            {
                List<NvidiaSetting> profile = await Task.Run(() => NvidiaProfileInspector.ReadProfile());
                ApplyProfile(profile);
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not read the NVIDIA profile: {ex.Message}";
            }
        }

        public void Detach()
        {
            NvidiaFlagHistory.Changed -= OnFlagHistoryChanged;
        }

        public void Attach()
        {
            NvidiaFlagHistory.Changed -= OnFlagHistoryChanged;
            NvidiaFlagHistory.Changed += OnFlagHistoryChanged;
            RefreshFlagHistory();
        }

        public bool IsAvailable { get; }

        public string UnavailableReason { get; }

        public Visibility AvailableVisibility => IsAvailable ? Visibility.Visible : Visibility.Collapsed;

        public Visibility UnavailableVisibility => IsAvailable ? Visibility.Collapsed : Visibility.Visible;

        public ObservableCollection<string> LowLatencyModes { get; } = new ObservableCollection<string> { Strings.Menu_Nvidia_Mode_Off, Strings.Menu_Nvidia_Mode_On, Strings.Menu_Nvidia_Mode_Ultra };

        public ObservableCollection<string> FrlLowLatencyModes { get; } = new ObservableCollection<string> { Strings.Menu_Nvidia_Mode_Off, Strings.Menu_Nvidia_Mode_On };

        public ObservableCollection<string> SilkSmoothnessModes { get; } = new ObservableCollection<string> { Strings.Menu_Nvidia_Mode_Off, Strings.Menu_Nvidia_Mode_Low, Strings.Menu_Nvidia_Mode_Medium, Strings.Menu_Nvidia_Mode_High, Strings.Menu_Nvidia_Mode_Ultra };

        public ObservableCollection<string> BenchmarkOverlayModes { get; } = new ObservableCollection<string>(BenchmarkOverlayOptions.Select(option => option.Label));

        public ObservableCollection<NvidiaSetting> CustomSettings { get; } = new ObservableCollection<NvidiaSetting>();

        public ObservableCollection<NvidiaHistoryEntry> FlagHistory { get; } = new ObservableCollection<NvidiaHistoryEntry>();

        public bool NvidiaEditorViewMode
        {
            get => _nvidiaEditorViewMode;
            set { _nvidiaEditorViewMode = value; OnPropertyChanged(nameof(NvidiaEditorViewMode)); }
        }

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
                ? string.Format(CultureInfo.InvariantCulture, Strings.Menu_Nvidia_LodBiasLabel_Override, TextureLodBias / 8.0)
                : Strings.Menu_Nvidia_LodBiasLabel_Default;

        public string BenchmarkOverlayMode
        {
            get => _benchmarkOverlayMode;
            set { _benchmarkOverlayMode = value; OnPropertyChanged(nameof(BenchmarkOverlayMode)); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        private void LoadFromDriver()
        {
            ApplyProfile(NvidiaProfileInspector.ReadProfile());
        }

        private void ApplyProfile(List<NvidiaSetting> profile)
        {
            HashSet<uint> tracked = new HashSet<uint>(AllTrackedIds);
            Dictionary<uint, uint> live = new Dictionary<uint, uint>();
            foreach (NvidiaSetting setting in profile)
            {
                if (setting.Type == NvSettingType.Dword && tracked.Contains(setting.Id))
                    live[setting.Id] = setting.Value;
            }

            LowLatencyMode = ReadEnum(live, IdLowLatencyMode, LowLatencyModes);
            FrlLowLatencyMode = ReadEnum(live, IdFrlLowLatencyMode, FrlLowLatencyModes);
            BackgroundFrameRateLimit = ReadInt(live, IdBackgroundFrameRateLimit);
            ResizableBar = ReadBool(live, IdResizableBar);
            DlssSuperResolution = ReadBool(live, IdDlssSuperResolution);
            DlssFrameGeneration = ReadBool(live, IdDlssFrameGeneration);
            Mfaa = ReadBool(live, IdMfaa);
            Fxaa = ReadBool(live, IdFxaaEnable) && ReadBool(live, IdAntialiasingMode);
            GammaCorrection = !ReadBool(live, IdGammaCorrection) || !ReadBool(live, IdLineGamma);
            SilkSmoothness = SilkFromValue(ReadInt(live, IdSilkSmoothness));
            TextureLodBias = live.TryGetValue(IdTextureLodBias, out uint bias) ? unchecked((int)bias) : 0;
            BenchmarkOverlayMode = BenchmarkOverlayFromValue(live.TryGetValue(IdBenchmarkOverlay, out uint overlay) ? overlay : 0u);

            CustomSettings.Clear();
            foreach (NvidiaSetting setting in profile)
            {
                if (setting.Type != NvSettingType.Dword || tracked.Contains(setting.Id))
                    continue;

                CustomSettings.Add(setting);
            }
        }

        public bool IsCustomSettingIdTaken(uint id)
        {
            if (Array.IndexOf(AllTrackedIds, id) >= 0)
                return true;

            foreach (NvidiaSetting setting in CustomSettings)
            {
                if (setting.Id == id)
                    return true;
            }

            return false;
        }

        public void AddCustomSetting(string name, uint id, uint value)
        {
            string trimmedName = string.IsNullOrWhiteSpace(name) ? "Setting " + id.ToString(CultureInfo.InvariantCulture) : name.Trim();
            CustomSettings.Add(new NvidiaSetting
            {
                Id = id,
                Name = trimmedName,
                Value = value,
                Type = NvSettingType.Dword,
            });

            NvidiaFlagHistory.Log("Added " + trimmedName + " (0x" + id.ToString("X8", CultureInfo.InvariantCulture) + ")");
        }

        public void RemoveCustomSetting(NvidiaSetting setting)
        {
            RemoveCustomSettingCore(setting);
            NvidiaFlagHistory.Log("Removed " + setting.Name + " (0x" + setting.Id.ToString("X8", CultureInfo.InvariantCulture) + ")");
        }

        public void RemoveCustomSettings(IEnumerable<NvidiaSetting> settings)
        {
            List<NvidiaSetting> list = settings is List<NvidiaSetting> already ? already : new List<NvidiaSetting>(settings);
            if (list.Count == 0)
                return;

            foreach (NvidiaSetting setting in list)
                RemoveCustomSettingCore(setting);

            NvidiaFlagHistory.Log(list.Count == 1
                ? "Removed " + list[0].Name + " (0x" + list[0].Id.ToString("X8", CultureInfo.InvariantCulture) + ")"
                : "Deleted " + list.Count + " custom setting(s)");
        }

        public void ClearCustomSettings()
        {
            RemoveCustomSettings(new List<NvidiaSetting>(CustomSettings));
        }

        private void RemoveCustomSettingCore(NvidiaSetting setting)
        {
            CustomSettings.Remove(setting);

            if (!IsAvailable)
                return;

            try
            {
                NvidiaApplyResult result = NvidiaProfileInspector.Reset(new[] { setting.Id });
                if (!result.Ok)
                    App.Logger.WriteLine("NvidiaViewModel", "Failed to reset custom setting 0x" + setting.Id.ToString("X8", CultureInfo.InvariantCulture) + ": " + result.Message);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("NvidiaViewModel", "Failed to reset custom setting: " + ex.Message);
            }
        }

        public NvidiaApplyResult ResetProfile()
        {
            if (!IsAvailable)
            {
                NvidiaApplyResult unavailable = new NvidiaApplyResult { Ok = false, Message = UnavailableReason };
                return unavailable;
            }

            List<NvidiaSetting> live = NvidiaProfileInspector.ReadProfile();
            uint[] ids = live.Select(setting => setting.Id).ToArray();

            NvidiaApplyResult result = NvidiaProfileInspector.Reset(ids);
            NvidiaFlagHistory.Log(result.Ok
                ? "Reset NIP: cleared " + result.Applied + " setting(s) to the driver default"
                : "Reset NIP failed: " + result.Message);

            if (result.Ok)
                LoadFromDriver();

            return result;
        }

        private Dictionary<uint, uint> BuildSettingsDictionary()
        {
            Dictionary<uint, uint> settings = new Dictionary<uint, uint>
            {
                [IdLowLatencyMode] = (uint)LowLatencyModes.IndexOf(LowLatencyMode),
                [IdFrlLowLatencyMode] = (uint)FrlLowLatencyModes.IndexOf(FrlLowLatencyMode),
                [IdBackgroundFrameRateLimit] = (uint)BackgroundFrameRateLimit,
                [IdResizableBar] = ResizableBar ? 1u : 0u,
                [IdDlssSuperResolution] = DlssSuperResolution ? 1u : 0u,
                [IdDlssFrameGeneration] = DlssFrameGeneration ? 1u : 0u,
                [IdMfaa] = Mfaa ? 1u : 0u,
                [IdFxaaEnable] = Fxaa ? 1u : 0u,
                [IdAntialiasingMode] = Fxaa ? 1u : 0u,
                [IdSilkSmoothness] = (uint)SilkToValue(SilkSmoothness),
                [IdTextureLodBias] = unchecked((uint)TextureLodBias),
                [IdBenchmarkOverlay] = BenchmarkOverlayToValue(BenchmarkOverlayMode),
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

            foreach (NvidiaSetting custom in CustomSettings)
                settings[custom.Id] = custom.Value;

            return settings;
        }

        public NvidiaApplyResult ApplyToDriver()
        {
            Dictionary<uint, uint> settings = BuildSettingsDictionary();
            NvidiaApplyResult result = NvidiaProfileInspector.Apply(settings);
            StatusMessage = result.Message;
            NvidiaFlagHistory.Log(result.Ok
                ? "Applied " + result.Applied + " setting(s)"
                : "Apply failed: " + result.Message);
            return result;
        }

        private static readonly Dictionary<uint, string> SettingNames = new Dictionary<uint, string>
        {
            [IdLowLatencyMode] = "Low Latency Mode",
            [IdFrlLowLatencyMode] = "FRL Low Latency Mode",
            [IdBackgroundFrameRateLimit] = "Background Frame Rate Limiter",
            [IdResizableBar] = "Resizable BAR",
            [IdDlssSuperResolution] = "DLSS Super Resolution",
            [IdDlssFrameGeneration] = "DLSS Frame Generation",
            [IdMfaa] = "MFAA",
            [IdFxaaEnable] = "FXAA Enable",
            [IdAntialiasingMode] = "Antialiasing Mode",
            [IdGammaCorrection] = "Gamma Correction (AA)",
            [IdLineGamma] = "Gamma Correction (Line)",
            [IdSilkSmoothness] = "SILK Smoothness",
            [IdTextureLodBias] = "Texture Filtering LOD Bias",
            [IdTextureFilteringQuality] = "Texture Filtering Quality",
            [IdAnisotropicFilteringMode] = "Anisotropic Filtering Mode",
            [IdTransparencySupersampling] = "Transparency Supersampling",
            [IdBenchmarkOverlay] = "Benchmark Overlay",
        };

        public List<NvidiaSetting> BuildSettingsSnapshot()
        {
            Dictionary<uint, uint> settings = BuildSettingsDictionary();
            Dictionary<uint, string> customNames = CustomSettings.ToDictionary(setting => setting.Id, setting => setting.Name);
            List<NvidiaSetting> results = new List<NvidiaSetting>();

            foreach (KeyValuePair<uint, uint> pair in settings)
            {
                string name = SettingNames.TryGetValue(pair.Key, out string? curatedName)
                    ? curatedName
                    : customNames.TryGetValue(pair.Key, out string? customName)
                        ? customName
                        : "Setting " + pair.Key;

                results.Add(new NvidiaSetting
                {
                    Id = pair.Key,
                    Name = name,
                    Value = pair.Value,
                    Type = NvSettingType.Dword,
                });
            }

            return results;
        }

        public void ReloadFromDriver()
        {
            if (IsAvailable)
                LoadFromDriver();
        }

        private void OnFlagHistoryChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshFlagHistory));
        }

        private void RefreshFlagHistory()
        {
            FlagHistory.Clear();

            foreach (NvidiaHistoryEntry entry in NvidiaFlagHistory.Entries)
                FlagHistory.Add(entry);

            OnPropertyChanged(nameof(FlagHistory));
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
                1 => Strings.Menu_Nvidia_Mode_Low,
                2 => Strings.Menu_Nvidia_Mode_Medium,
                3 => Strings.Menu_Nvidia_Mode_High,
                4 => Strings.Menu_Nvidia_Mode_Ultra,
                _ => Strings.Menu_Nvidia_Mode_Off,
            };
        }

        private static int SilkToValue(string mode)
        {
            if (mode == Strings.Menu_Nvidia_Mode_Low)
                return 1;
            if (mode == Strings.Menu_Nvidia_Mode_Medium)
                return 2;
            if (mode == Strings.Menu_Nvidia_Mode_High)
                return 3;
            if (mode == Strings.Menu_Nvidia_Mode_Ultra)
                return 4;
            return 0;
        }

        private static string BenchmarkOverlayFromValue(uint value)
        {
            foreach ((string Label, uint Value) option in BenchmarkOverlayOptions)
            {
                if (option.Value == value)
                    return option.Label;
            }

            return BenchmarkOverlayOptions[0].Label;
        }

        private static uint BenchmarkOverlayToValue(string label)
        {
            foreach ((string Label, uint Value) option in BenchmarkOverlayOptions)
            {
                if (option.Label == label)
                    return option.Value;
            }

            return 0u;
        }
    }
}
