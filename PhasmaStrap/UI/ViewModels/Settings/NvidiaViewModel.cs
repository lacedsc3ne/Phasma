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
    // what NvidiaProfileInspector.cs can read/write. Voidstrap's multi-type NIP import/export
    // and "copy from another app" dialogs were intentionally not ported (PhasmaStrap has no
    // .nip round-trip - see NvidiaProfileManager.cs), but the ability to add a setting beyond
    // this curated list *was* ported as CustomSettings below, adapted to read/write straight
    // from the live driver profile instead of a persisted .nip file.
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
        private const uint IdBenchmarkOverlay = 2945366;

        private static readonly uint[] AllTrackedIds = new[]
        {
            IdLowLatencyMode, IdFrlLowLatencyMode, IdFrameRateLimit, IdBackgroundFrameRateLimit,
            IdResizableBar, IdDlssSuperResolution, IdDlssFrameGeneration, IdMfaa,
            IdFxaaEnable, IdAntialiasingMode, IdGammaCorrection, IdLineGamma,
            IdSilkSmoothness, IdTextureLodBias, IdTextureFilteringQuality,
            IdAnisotropicFilteringMode, IdTransparencySupersampling, IdBenchmarkOverlay,
        };

        // Single source of truth for the Benchmark Overlay combo box: both the display list
        // (BenchMarkOverlayModes) and the value lookups (BenchmarkOverlayFromValue/ToValue)
        // read from this same array, so a translated label can never desync between what's
        // shown and what's compared against (see the ComboBox desync bug this project has hit
        // before, e.g. with SILK/latency mode strings).
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
        private int _frameRateLimit;
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
                // reading the driver profile is a slow NVAPI round-trip; do it once, in the
                // background, so clicking the NVIDIA tab doesn't freeze the window
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

        /// <summary>
        /// Called from NvidiaPage's Unloaded handler so this viewmodel doesn't keep
        /// <see cref="NvidiaFlagHistory"/> subscribed for the lifetime of the process after the
        /// page itself has been navigated away from (mirrors NotificationsViewModel.Detach).
        /// </summary>
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

        // Driver profile settings the user has added beyond the curated list above (see
        // AddNvidiaCustomSettingDialog / NvidiaPage's "Custom settings" section) - ported from
        // Voidstrap's arbitrary setting editor (AddNvidiaFFlagWindow/NvidiaFFlagEditorPage), but
        // adapted to PhasmaStrap's live-driver-read architecture instead of a persisted .nip
        // file: NvidiaProfileInspector.ReadProfile() already returns every setting sitting in
        // the driver's "PhasmaStrap" profile (curated and custom alike), so a custom entry the
        // user applies is automatically picked back up here on the next load with no separate
        // storage needed.
        public ObservableCollection<NvidiaSetting> CustomSettings { get; } = new ObservableCollection<NvidiaSetting>();

        // Recent actions taken from the grid view (add/remove/delete/reset/apply), refreshed
        // from the shared NvidiaFlagHistory log - see that class' remarks for why this is
        // session-only rather than persisted to disk.
        public ObservableCollection<NvidiaHistoryEntry> FlagHistory { get; } = new ObservableCollection<NvidiaHistoryEntry>();

        // Toggles NvidiaPage between the collapsed-card view (curated toggles + custom settings
        // list) and the raw grid "Advanced Editor" view - both live in the same page/DataContext,
        // this just flips which section is visible (see the DataTriggers in NvidiaPage.xaml).
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

        // one profile read feeds both the curated toggles and the custom-settings list (this used
        // to read the whole profile twice)
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
            BenchmarkOverlayMode = BenchmarkOverlayFromValue(live.TryGetValue(IdBenchmarkOverlay, out uint overlay) ? overlay : 0u);

            // whatever else is in the driver's "PhasmaStrap" profile that isn't curated above
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

        // Removes a custom setting from the list and, if the driver is reachable, immediately
        // resets it in the "PhasmaStrap" driver profile too - PhasmaStrap has no persisted .nip
        // file for these (see CustomSettings' doc comment) so simply dropping it from the
        // in-memory list would otherwise leave the old value sitting in the live driver profile
        // until something else happened to overwrite that setting ID.
        public void RemoveCustomSetting(NvidiaSetting setting)
        {
            RemoveCustomSettingCore(setting);
            NvidiaFlagHistory.Log("Removed " + setting.Name + " (0x" + setting.Id.ToString("X8", CultureInfo.InvariantCulture) + ")");
        }

        // Bulk version used by the grid view's "Delete Selected" and "Delete all" buttons - same
        // per-setting removal/driver-reset as RemoveCustomSetting, just logged as one history
        // entry instead of one per row.
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

        // "Delete all" in the grid view - clears every custom setting currently shown.
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

        // "Reset NIP" in the grid view. Resets *every* setting actually sitting in the live
        // "PhasmaStrap" driver profile right now (read straight from the driver via ReadProfile,
        // not guessed) back to its driver default - this includes both the curated
        // toggles/sliders on the card view and every custom setting, since both are ultimately
        // stored in the same profile. Reloads all page state from the driver afterwards so the
        // card view and the grid both reflect the reset immediately.
        //
        // Runs on a background thread; the caller (NvidiaPage) is responsible for marshalling
        // any UI feedback back to the dispatcher thread, same as ApplyToDriver.
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

            // Custom (non-curated) settings the user added via AddNvidiaCustomSettingDialog are
            // folded into the exact same dictionary that both ApplyToDriver and
            // BuildSettingsSnapshot read from, rather than being applied/exported through a
            // separate path - this is the one place that ever needs to know about them.
            foreach (NvidiaSetting custom in CustomSettings)
                settings[custom.Id] = custom.Value;

            return settings;
        }

        // Runs on a background thread; the caller (NvidiaPage) is responsible for
        // marshalling any UI feedback back to the dispatcher thread.
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

        // Friendly names for the tracked setting IDs, used only when exporting/copying the
        // currently-configured values as a standalone .nip document (NvidiaProfileManager) -
        // the driver itself is never asked for these, ReadEnum/ReadInt/ReadBool above only
        // ever deal in raw IDs and values.
        private static readonly Dictionary<uint, string> SettingNames = new Dictionary<uint, string>
        {
            [IdLowLatencyMode] = "Low Latency Mode",
            [IdFrlLowLatencyMode] = "FRL Low Latency Mode",
            [IdFrameRateLimit] = "Frame Rate Limiter",
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

        // Snapshot of the currently-configured (not necessarily yet-applied) settings, for
        // exporting to a .nip file or copying to the clipboard via NvidiaPage. Includes custom
        // settings (using the name the user gave them) since BuildSettingsDictionary folds
        // those into the same dictionary this reads from.
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

        // Benchmark Overlay isn't an ordinal enum like the modes above - the driver setting is a
        // bitmask (Disabled=0, individual graph/indicator bits, Enabled=511 for "everything") -
        // so both directions look the value up in BenchmarkOverlayOptions instead of using
        // ObservableCollection index math.
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
