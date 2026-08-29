using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Enums.FlagPresets;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class FastFlagsViewModel : NotifyPropertyChangedViewModel
    {
        private Dictionary<string, object>? _preResetFlags;

        private static readonly string[] LODLevels = { "L0", "L12", "L23", "L34" };

        public bool GetFlagAsBool(string flagKey, string falseValue = "False") => App.FastFlags.GetPreset(flagKey) != falseValue;

        public void SetFlagFromBool(string flagKey, bool value, string falseValue = "False") => App.FastFlags.SetPreset(flagKey, value ? null : falseValue);

        #region Telemetry

        public bool DisableTelemetry
        {
            get => App.FastFlags.GetPreset("Telemetry.TelemetryV2Url") == "0.0.0.0";
            set
            {
                App.FastFlags.SetPreset("Telemetry.TelemetryV2Url", value ? "0.0.0.0" : null);
                App.FastFlags.SetPreset("Telemetry.Protocol", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.GraphicsQualityUsage", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.GpuVsCpuBound", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.RenderFidelity", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.RenderDistance", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.AudioPlugin", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.FmodErrors", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.SoundLength", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.AssetRequestV1", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.DeviceRAM", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.V2FrameRateMetrics", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.GlobalSkipUpdating", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.CallbackSafety", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.V2PointEncoding", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.ReplaceSeparator", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.OpenTelemetry", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.FLogTelemetry", value ? "0" : null);
                App.FastFlags.SetPreset("Telemetry.TelemetryService", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.PropertiesTelemetry", value ? "False" : null);
            }
        }

        public bool DisableWebview2Telemetry
        {
            get => App.FastFlags.GetPreset("Telemetry.Webview1") == "www.youtube-nocookie.com";
            set
            {
                App.FastFlags.SetPreset("Telemetry.Webview1", value ? "www.youtube-nocookie.com" : null);
                App.FastFlags.SetPreset("Telemetry.Webview2", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Webview3", value ? "0" : null);
                App.FastFlags.SetPreset("Telemetry.Webview4", value ? "0" : null);
                App.FastFlags.SetPreset("Telemetry.Webview5", value ? "0" : null);
                App.FastFlags.SetPreset("Telemetry.Webview6", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Webview7", value ? "False" : null);
            }
        }

        public bool DisableVoiceChatTelemetry
        {
            get => App.FastFlags.GetPreset("Telemetry.Voicechat1") == "False";
            set
            {
                App.FastFlags.SetPreset("Telemetry.Voicechat1", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat2", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat3", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat4", value ? "0" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat5", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat6", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat7", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat8", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat9", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat10", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat11", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat12", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat13", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat14", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat15", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat16", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat17", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat18", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat19", value ? "0" : null);
                App.FastFlags.SetPreset("Telemetry.Voicechat20", value ? "-1" : null);
            }
        }

        public bool BlockTencent
        {
            get => App.FastFlags.GetPreset("Telemetry.Tencent1") == "/tencent/";
            set
            {
                App.FastFlags.SetPreset("Telemetry.Tencent1", value ? "/tencent/" : null);
                App.FastFlags.SetPreset("Telemetry.Tencent2", value ? "/tencent/" : null);
                App.FastFlags.SetPreset("Telemetry.Tencent3", value ? "https://www.gov.cn" : null);
                App.FastFlags.SetPreset("Telemetry.Tencent4", value ? "https://www.gov.cn" : null);
                App.FastFlags.SetPreset("Telemetry.Tencent5", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Tencent6", value ? "False" : null);
                App.FastFlags.SetPreset("Telemetry.Tencent7", value ? "10000" : null);
            }
        }

        public bool PingBreakdown
        {
            get => App.FastFlags.GetPreset("Debug.PingBreakdown") == "True";
            set => App.FastFlags.SetPreset("Debug.PingBreakdown", value ? "True" : null);
        }

        public bool ShowChunks
        {
            get => App.FastFlags.GetPreset("Debug.Chunks") == "True";
            set => App.FastFlags.SetPreset("Debug.Chunks", value ? "True" : null);
        }

        public string? FlagState
        {
            get => App.FastFlags.GetPreset("Debug.FlagState");
            set => App.FastFlags.SetPreset("Debug.FlagState", value);
        }

        #endregion

        #region Voice / Chat

        public bool ChatBubble
        {
            get => App.FastFlags.GetPreset("UI.Chatbubble") == "False";
            set => App.FastFlags.SetPreset("UI.Chatbubble", value ? "False" : null);
        }

        public bool ChatTranslation
        {
            get => GetFlagAsBool("Menu.ChatTranslation");
            set => SetFlagFromBool("Menu.ChatTranslation", value);
        }

        #endregion

        #region Rendering

        public bool LightCulling
        {
            get => App.FastFlags.GetPreset("Rendering.GpuCulling") == "True";
            set
            {
                App.FastFlags.SetPreset("Rendering.GpuCulling", value ? "True" : null);
                App.FastFlags.SetPreset("Rendering.CpuCulling", value ? "True" : null);
            }
        }

        public bool RainbowTheme
        {
            get => App.FastFlags.GetPreset("UI.RainbowText") == "True";
            set => App.FastFlags.SetPreset("UI.RainbowText", value ? "True" : null);
        }

        public bool FRMQualityOverrideEnabled
        {
            get => App.FastFlags.GetPreset("Rendering.FRMQualityOverride") != null;
            set
            {
                if (value)
                    FRMQualityOverride = 21;
                else
                    App.FastFlags.SetPreset("Rendering.FRMQualityOverride", null);

                OnPropertyChanged(nameof(FRMQualityOverride));
                OnPropertyChanged(nameof(FRMQualityOverrideEnabled));
            }
        }

        public int FRMQualityOverride
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.FRMQualityOverride"), out var result) ? result : 21;
            set
            {
                App.FastFlags.SetPreset("Rendering.FRMQualityOverride", value);
                OnPropertyChanged(nameof(FRMQualityOverride));
            }
        }

        public bool MeshQualityEnabled
        {
            get => App.FastFlags.GetPreset("Geometry.MeshLOD.L0") != null;
            set
            {
                if (value)
                {
                    MeshQuality = 3;
                }
                else
                {
                    foreach (var level in LODLevels)
                        App.FastFlags.SetPreset($"Geometry.MeshLOD.{level}", null);

                    App.FastFlags.SetPreset("Geometry.MeshLOD.Static", null);
                }

                OnPropertyChanged(nameof(MeshQualityEnabled));
            }
        }

        public int MeshQuality
        {
            get => int.TryParse(App.FastFlags.GetPreset("Geometry.MeshLOD.L0"), out var result) ? result : 0;
            set
            {
                int baseValue = Math.Clamp(value, 0, LODLevels.Length - 1);

                for (int i = 0; i < LODLevels.Length; i++)
                    App.FastFlags.SetPreset($"Geometry.MeshLOD.{LODLevels[i]}", Math.Clamp(baseValue - i, 0, 3));

                OnPropertyChanged(nameof(MeshQuality));
                OnPropertyChanged(nameof(MeshQualityEnabled));
            }
        }

        public bool UnlimitedCameraZoom
        {
            get => App.FastFlags.GetPreset("Rendering.Camerazoom") == "2147483647";
            set => App.FastFlags.SetPreset("Rendering.Camerazoom", value ? "2147483647" : null);
        }

        public bool BGRA
        {
            get => App.FastFlags.GetPreset("Rendering.BGRA") == "True";
            set => App.FastFlags.SetPreset("Rendering.BGRA", value ? "True" : null);
        }

        public bool NewFpsSystem
        {
            get => App.FastFlags.GetPreset("Rendering.NewFpsSystem") == "True";
            set => App.FastFlags.SetPreset("Rendering.NewFpsSystem", value ? "True" : null);
        }

        public bool WorserParticles
        {
            get => App.FastFlags.GetPreset("Rendering.WorserParticles1") == "False";
            set
            {
                App.FastFlags.SetPreset("Rendering.WorserParticles1", value ? "False" : null);
                App.FastFlags.SetPreset("Rendering.WorserParticles2", value ? "False" : null);
                App.FastFlags.SetPreset("Rendering.WorserParticles3", value ? "False" : null);
                App.FastFlags.SetPreset("Rendering.WorserParticles4", value ? "False" : null);
            }
        }

        public bool LowPolyMeshes
        {
            get => App.FastFlags.GetPreset("Rendering.LowPolyMeshes1") == "0";
            set
            {
                App.FastFlags.SetPreset("Rendering.LowPolyMeshes1", value ? "0" : null);
                App.FastFlags.SetPreset("Rendering.LowPolyMeshes2", value ? "0" : null);
                App.FastFlags.SetPreset("Rendering.LowPolyMeshes3", value ? "0" : null);
                App.FastFlags.SetPreset("Rendering.LowPolyMeshes4", value ? "0" : null);
            }
        }

        public IReadOnlyDictionary<RenderingMode, string?> RenderingModes => FastFlagManager.RenderingModes;

        public RenderingMode SelectedRenderingMode
        {
            get => App.FastFlags.GetPresetEnum(RenderingModes!, "Rendering.Mode", "True");
            set
            {
                App.FastFlags.SetPresetEnum("Rendering.Mode", value.ToString(), "True");
                App.FastFlags.SetPreset("Rendering.Mode.DisableD3D11", null);
            }
        }

        public bool MoreLighting
        {
            get => App.FastFlags.GetPreset("Rendering.BrighterVisual") == "True";
            set => App.FastFlags.SetPreset("Rendering.BrighterVisual", value ? "True" : null);
        }

        public int MinGrassDistance
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.Nograss1"), out var result) ? result : 100;
            set
            {
                App.FastFlags.SetPreset("Rendering.Nograss1", value.ToString());
                OnPropertyChanged(nameof(MinGrassDistance));
            }
        }

        public int MaxGrassDistance
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.Nograss2"), out var result) ? result : 290;
            set
            {
                App.FastFlags.SetPreset("Rendering.Nograss2", value.ToString());
                OnPropertyChanged(nameof(MaxGrassDistance));
            }
        }

        public IReadOnlyDictionary<int, string> GrassMovementOptions { get; } = new Dictionary<int, string>
        {
            { 0, "No Movement" },
            { 1, "Minimal Movement" },
            { 2, "Medium Movement" },
            { 3, "High Movement" },
            { 4, "Ultra Movement" },
            { 5, "Default Movement" },
        };

        public int SelectedGrassMovementFactor
        {
            get => int.TryParse(App.FastFlags.GetPreset("Grass.Movement"), out var result) && GrassMovementOptions.ContainsKey(result) ? result : 5;
            set
            {
                App.FastFlags.SetPreset("Grass.Movement", value == 5 ? null : value.ToString());
                OnPropertyChanged(nameof(SelectedGrassMovementFactor));
            }
        }

        public IReadOnlyDictionary<InGameMenuVersion, IReadOnlyDictionary<string, string?>> IGMenuVersions => FastFlagManager.IGMenuVersions;

        public InGameMenuVersion SelectedIGMenuVersion
        {
            get
            {
                foreach (var version in IGMenuVersions)
                {
                    bool matches = true;

                    foreach (var flag in version.Value)
                    {
                        foreach (var pair in FastFlagManager.PresetFlags.Where(x => x.Key.StartsWith($"UI.Menu.Style.{flag.Key}")))
                        {
                            if (App.FastFlags.GetValue(pair.Value) != flag.Value)
                                matches = false;
                        }
                    }

                    if (matches)
                        return version.Key;
                }

                return IGMenuVersions.First().Key;
            }
            set
            {
                foreach (var pair in IGMenuVersions[value])
                    App.FastFlags.SetPreset($"UI.Menu.Style.{pair.Key}", pair.Value);
            }
        }

        public IReadOnlyDictionary<LightingMode, string?> LightingModes => FastFlagManager.LightingModes;

        public LightingMode SelectedLightingMode
        {
            get => App.FastFlags.GetPresetEnum(LightingModes!, "Rendering.Lighting", "True");
            set => App.FastFlags.SetPresetEnum("Rendering.Lighting", LightingModes[value]!, "True");
        }

        public bool FullscreenTitlebarDisabled
        {
            get => int.TryParse(App.FastFlags.GetPreset("UI.FullscreenTitlebarDelay"), out var result) && result > 5000;
            set => App.FastFlags.SetPreset("UI.FullscreenTitlebarDelay", value ? "3600000" : null);
        }

        public IReadOnlyDictionary<TextureSkipping, string?> TextureSkippings => FastFlagManager.TextureSkippingSkips;

        public TextureSkipping SelectedTextureSkipping
        {
            get => TextureSkippings.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.TextureSkipping.Skips")).Key;
            set
            {
                if (value == TextureSkipping.Noskip)
                    App.FastFlags.SetPreset("Rendering.TextureSkipping", null);
                else
                    App.FastFlags.SetPreset("Rendering.TextureSkipping.Skips", TextureSkippings[value]);
            }
        }

        public IReadOnlyDictionary<DistanceRendering, string?> DistanceRenderings => FastFlagManager.DistanceRenderings;

        public DistanceRendering SelectedDistanceRendering
        {
            get => DistanceRenderings.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.Distance.Chunks")).Key;
            set
            {
                if (value == DistanceRendering.Chunks1x)
                    App.FastFlags.SetPreset("Rendering.Distance.Chunks", null);
                else
                    App.FastFlags.SetPreset("Rendering.Distance.Chunks", DistanceRenderings[value]);
            }
        }

        public IReadOnlyDictionary<DynamicResolution, string?> DynamicResolutions => FastFlagManager.DynamicResolutions;

        public DynamicResolution SelectedDynamicResolution
        {
            get => DynamicResolutions.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.Dynamic.Resolution")).Key;
            set
            {
                if (value == DynamicResolution.Resolution2)
                    App.FastFlags.SetPreset("Rendering.Dynamic.Resolution", null);
                else
                    App.FastFlags.SetPreset("Rendering.Dynamic.Resolution", DynamicResolutions[value]);
            }
        }

        public IReadOnlyDictionary<RomarkStart, string?> RomarkStartMappings => FastFlagManager.RomarkStartMappings;

        public RomarkStart SelectedRomarkStart
        {
            get => RomarkStartMappings.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.Start.Graphic")).Key;
            set
            {
                if (value == RomarkStart.Disabled)
                    App.FastFlags.SetPreset("Rendering.Start.Graphic", null);
                else
                    App.FastFlags.SetPreset("Rendering.Start.Graphic", RomarkStartMappings[value]);
            }
        }

        public IReadOnlyDictionary<QualityLevel, string?> QualityLevels => FastFlagManager.QualityLevels;

        public QualityLevel SelectedQualityLevel
        {
            get => QualityLevels.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.FrmQuality")).Key;
            set
            {
                if (value == QualityLevel.Disabled)
                    App.FastFlags.SetPreset("Rendering.FrmQuality", null);
                else
                    App.FastFlags.SetPreset("Rendering.FrmQuality", QualityLevels[value]);
            }
        }

        public bool DisablePostFX
        {
            get => App.FastFlags.GetPreset("Rendering.DisablePostFX") == "True";
            set => App.FastFlags.SetPreset("Rendering.DisablePostFX", value ? "True" : null);
        }

        public bool TaskSchedulerAvoidingSleep
        {
            get => App.FastFlags.GetPreset("Rendering.AvoidSleep") == "True";
            set => App.FastFlags.SetPreset("Rendering.AvoidSleep", value ? "True" : null);
        }

        public bool DisablePlayerShadows
        {
            get => App.FastFlags.GetPreset("Rendering.ShadowIntensity") == "0";
            set
            {
                App.FastFlags.SetPreset("Rendering.ShadowIntensity", value ? "0" : null);
                App.FastFlags.SetPreset("Rendering.Pause.Voxelizer", value ? "True" : null);
                App.FastFlags.SetPreset("Rendering.ShadowMapBias", value ? "-1" : null);
            }
        }

        public bool RenderOcclusion
        {
            get => App.FastFlags.GetPreset("Rendering.Occlusion1") == "True";
            set
            {
                App.FastFlags.SetPreset("Rendering.Occlusion1", value ? "True" : null);
                App.FastFlags.SetPreset("Rendering.Occlusion2", value ? "True" : null);
                App.FastFlags.SetPreset("Rendering.Occlusion3", value ? "True" : null);
            }
        }

        public bool EnableGraySky
        {
            get => App.FastFlags.GetPreset("Graphic.GraySky") == "True";
            set => App.FastFlags.SetPreset("Graphic.GraySky", value ? "True" : null);
        }

        public bool WhiteSky
        {
            get => App.FastFlags.GetPreset("Graphic.WhiteSky") == "True";
            set
            {
                App.FastFlags.SetPreset("Graphic.WhiteSky", value ? "True" : null);
                App.FastFlags.SetPreset("Graphic.GraySky", value ? "True" : null);
            }
        }

        public bool RedFont
        {
            get => App.FastFlags.GetPreset("UI.RedFont") == "rbxasset://fonts/families/BuilderSans.json";
            set => App.FastFlags.SetPreset("UI.RedFont", value ? "rbxasset://fonts/families/BuilderSans.json" : null);
        }

        public bool LayeredClothing
        {
            get => App.FastFlags.GetPreset("Layered.Clothing") == "-1";
            set => App.FastFlags.SetPreset("Layered.Clothing", value ? "-1" : null);
        }

        public bool DisableTerrainTextures
        {
            get => App.FastFlags.GetPreset("Rendering.TerrainTextureQuality") == "0";
            set => App.FastFlags.SetPreset("Rendering.TerrainTextureQuality", value ? "0" : null);
        }

        public bool Prerender
        {
            get => App.FastFlags.GetPreset("Rendering.Prerender") == "True" && App.FastFlags.GetPreset("Rendering.PrerenderV2") == "True";
            set
            {
                App.FastFlags.SetPreset("Rendering.Prerender", value ? "True" : null);
                App.FastFlags.SetPreset("Rendering.PrerenderV2", value ? "True" : null);
            }
        }

        public string ForceBuggyVulkan
        {
            get => App.FastFlags.GetPreset("Rendering.ForceVulkan") ?? "Automatic";
            set => App.FastFlags.SetPreset("Rendering.ForceVulkan", value == "Automatic" ? null : value);
        }

        public string BypassVulkan
        {
            get => App.FastFlags.GetPreset("System.BypassVulkan") ?? "Automatic";
            set => App.FastFlags.SetPreset("System.BypassVulkan", value == "Automatic" ? null : value);
        }

        public bool ChromeUI
        {
            get => App.FastFlags.GetPreset("UI.Menu.ChromeUI") == "True" && App.FastFlags.GetPreset("UI.Menu.ChromeUI2") == "True";
            set
            {
                App.FastFlags.SetPreset("UI.Menu.ChromeUI", value ? "True" : null);
                App.FastFlags.SetPreset("UI.Menu.ChromeUI2", value ? "True" : null);
            }
        }

        public bool OldChromeUI
        {
            get => App.FastFlags.GetPreset("UI.OldChromeUI1") == "False";
            set
            {
                App.FastFlags.SetPreset("UI.OldChromeUI1", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI2", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI3", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI4", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI5", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI6", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI7", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI8", value ? "True" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI9", value ? "False" : null);
                App.FastFlags.SetPreset("UI.OldChromeUI10", value ? "False" : null);
            }
        }

        public IReadOnlyDictionary<Shader, string?> Shaders => FastFlagManager.Shaders;

        public Shader SelectedShaderLevel
        {
            get => Shaders.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.Shaders")).Key;
            set
            {
                if (value == Shader.Disabled)
                {
                    App.FastFlags.SetPreset("Rendering.Shaders", null);
                    App.FastFlags.SetPreset("Rendering.Shaders2", null);
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.Shaders", Shaders[value]);
                    App.FastFlags.SetPreset("Rendering.Shaders2", "21");
                }
            }
        }

        public bool ShadersEnabled
        {
            get => App.FastFlags.GetPreset("Rendering.Shaders2") == "21";
            set => App.FastFlags.SetPreset("Rendering.Shaders2", value ? "21" : "0");
        }

        public int ShadersLimit
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.Shaders"), out var result) ? result : 0;
            set
            {
                App.FastFlags.SetPreset("Rendering.Shaders", value == 0 ? null : value.ToString());

                if (value < -64000000)
                    Frontend.ShowMessageBox("Going below -64000000 is not recommended for performance.", MessageBoxImage.Exclamation);
            }
        }

        public IReadOnlyDictionary<RefreshRate, string?> RefreshRates => FastFlagManager.RefreshRates;

        public RefreshRate SelectedRefreshRate
        {
            get => RefreshRates.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("System.TargetRefreshRate1")).Key;
            set
            {
                if (value == RefreshRate.Default)
                {
                    App.FastFlags.SetPreset("System.TargetRefreshRate1", null);
                    App.FastFlags.SetPreset("System.TargetRefreshRate2", null);
                    App.FastFlags.SetPreset("System.TargetRefreshRate3", null);
                }
                else
                {
                    App.FastFlags.SetPreset("System.TargetRefreshRate1", RefreshRates[value]);
                    App.FastFlags.SetPreset("System.TargetRefreshRate2", RefreshRates[value]);
                    App.FastFlags.SetPreset("System.TargetRefreshRate3", RefreshRates[value]);
                }
            }
        }

        public bool MinimalRendering
        {
            get => App.FastFlags.GetPreset("Rendering.MinimalRendering") == "True";
            set => App.FastFlags.SetPreset("Rendering.MinimalRendering", value ? "True" : null);
        }

        public bool DisableSky
        {
            get => App.FastFlags.GetPreset("Rendering.NoFrmBloom") == "False";
            set
            {
                App.FastFlags.SetPreset("Rendering.NoFrmBloom", value ? "False" : null);
                App.FastFlags.SetPreset("Rendering.FRMRefactor", value ? "False" : null);
            }
        }

        public int FPSBufferPercentage
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.FrameRateBufferPercentage"), out var result) ? result : 0;
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                App.FastFlags.SetPreset("Rendering.FrameRateBufferPercentage", clamped >= 1 ? clamped.ToString() : null);
            }
        }

        public int FramerateLimit
        {
            get => int.TryParse(App.FastFlags.GetPreset("Rendering.Framerate"), out var result) ? result : 0;
            set
            {
                App.FastFlags.SetPreset("Rendering.Framerate", value == 0 ? null : value.ToString());

                if (value > 240)
                {
                    Frontend.ShowMessageBox("Going above 240 FPS is not recommended, as this may cause latency issues.", MessageBoxImage.Exclamation);
                    App.FastFlags.SetPreset("Rendering.LimitFramerate", "False");
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.LimitFramerate", null);
                }
            }
        }

        public bool Pseudolocalization
        {
            get => App.FastFlags.GetPreset("UI.Pseudolocalization") == "True";
            set => App.FastFlags.SetPreset("UI.Pseudolocalization", value ? "True" : null);
        }

        public bool DisplayFps
        {
            get => App.FastFlags.GetPreset("Rendering.DisplayFps") == "True";
            set => App.FastFlags.SetPreset("Rendering.DisplayFps", value ? "True" : null);
        }

        public bool GrayAvatar
        {
            get => App.FastFlags.GetPreset("Rendering.GrayAvatar") == "0";
            set => App.FastFlags.SetPreset("Rendering.GrayAvatar", value ? "0" : null);
        }

        public int? FontSize
        {
            get => int.TryParse(App.FastFlags.GetPreset("UI.FontSize"), out var result) ? result : 1;
            set => App.FastFlags.SetPreset("UI.FontSize", value == 1 ? null : value);
        }

        public int HideGUI
        {
            get => int.TryParse(App.FastFlags.GetPreset("UI.Hide"), out var result) ? result : 0;
            set => App.FastFlags.SetPreset("UI.Hide", value > 0 ? value.ToString() : null);
        }

        #endregion

        #region Networking

        public bool LessLagSpikes
        {
            get => App.FastFlags.GetPreset("Network.DefaultBps") == "796850000";
            set
            {
                App.FastFlags.SetPreset("Network.DefaultBps", value ? "796850000" : null);
                App.FastFlags.SetPreset("Network.MaxWorkCatchupMs", value ? "5" : null);
            }
        }

        public bool RobloxCore
        {
            get => App.FastFlags.GetPreset("Network.RCore1") == "20000";
            set
            {
                App.FastFlags.SetPreset("Network.RCore1", value ? "20000" : null);
                App.FastFlags.SetPreset("Network.RCore2", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.RCore3", value ? "10" : null);
                App.FastFlags.SetPreset("Network.RCore4", value ? "3000" : null);
                App.FastFlags.SetPreset("Network.RCore5", value ? "25" : null);
                App.FastFlags.SetPreset("Network.RCore6", value ? "5000" : null);
            }
        }

        public bool NoPayloadLimit
        {
            get => App.FastFlags.GetPreset("Network.Payload1") == "2147483647";
            set
            {
                App.FastFlags.SetPreset("Network.Payload1", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload2", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload3", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload4", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload5", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload6", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload7", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload8", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload9", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload10", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.Payload11", value ? "2147483647" : null);
            }
        }

        public bool EnableLargeReplicator
        {
            get => App.FastFlags.GetPreset("Network.EnableLargeReplicator") == "True";
            set
            {
                App.FastFlags.SetPreset("Network.EnableLargeReplicator", value ? "True" : null);
                App.FastFlags.SetPreset("Network.LargeReplicatorWrite", value ? "True" : null);
                App.FastFlags.SetPreset("Network.LargeReplicatorRead", value ? "True" : null);
                App.FastFlags.SetPreset("Network.SerializeRead", value ? "True" : null);
                App.FastFlags.SetPreset("Network.SerializeWrite", value ? "True" : null);
            }
        }

        public bool FasterLoading
        {
            get => App.FastFlags.GetPreset("Network.MaxAssetPreload") == "2147483647";
            set
            {
                App.FastFlags.SetPreset("Network.MaxAssetPreload", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Network.PlayerImageDefault", value ? "1" : null);
                App.FastFlags.SetPreset("Network.MeshPreloadding", value ? "True" : null);
            }
        }

        public bool BetterPacketSending
        {
            get => App.FastFlags.GetPreset("Network.BetterPacketSending1") == "0";
            set
            {
                App.FastFlags.SetPreset("Network.BetterPacketSending1", value ? "0" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending2", value ? "1" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending3", value ? "1" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending4", value ? "1" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending5", value ? "1" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending6", value ? "1047483647" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending7", value ? "5000000" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending8", value ? "1" : null);
                App.FastFlags.SetPreset("Network.BetterPacketSending9", value ? "1047483647" : null);
            }
        }

        public int MtuSize
        {
            get => int.TryParse(App.FastFlags.GetPreset("Network.Mtusize"), out var result) ? result : 0;
            set
            {
                int clamped = Math.Clamp(value, 0, 1500);
                App.FastFlags.SetPreset("Network.Mtusize", clamped >= 576 ? clamped.ToString() : null);
            }
        }

        public int BufferArrayLength
        {
            get => int.TryParse(App.FastFlags.GetPreset("Recommended.Buffer"), out var result) ? result : 0;
            set => App.FastFlags.SetPreset("Recommended.Buffer", value == 0 ? null : value.ToString());
        }

        #endregion

        #region UI / Misc

        public bool MoreSensetivityNumbers
        {
            get => App.FastFlags.GetPreset("UI.SensetivityNumbers") == "False";
            set => App.FastFlags.SetPreset("UI.SensetivityNumbers", value ? "False" : null);
        }

        public bool NoGuiBlur
        {
            get => App.FastFlags.GetPreset("UI.NoGuiBlur") == "0";
            set => App.FastFlags.SetPreset("UI.NoGuiBlur", value ? "0" : null);
        }

        public bool TextSizeChanger
        {
            get => App.FastFlags.GetPreset("UI.TextSize1") == "True";
            set
            {
                App.FastFlags.SetPreset("UI.TextSize1", value ? "True" : null);
                App.FastFlags.SetPreset("UI.TextSize2", value ? "True" : null);
            }
        }

        public bool TextureRemover
        {
            get => App.FastFlags.GetPreset("Rendering.RemoveTexture1") == "True";
            set
            {
                App.FastFlags.SetPreset("Rendering.RemoveTexture1", value ? "True" : null);
                App.FastFlags.SetPreset("Rendering.RemoveTexture2", value ? "10000" : null);
            }
        }

        public bool Threading
        {
            get => App.FastFlags.GetPreset("Hyper.Threading1") == "True";
            set => App.FastFlags.SetPreset("Hyper.Threading1", value ? "True" : null);
        }

        public bool DisableAds
        {
            get => App.FastFlags.GetPreset("UI.DisableAds1") == "False";
            set
            {
                App.FastFlags.SetPreset("UI.DisableAds1", value ? "False" : null);
                App.FastFlags.SetPreset("UI.DisableAds2", value ? "False" : null);
                App.FastFlags.SetPreset("UI.DisableAds3", value ? "False" : null);
                App.FastFlags.SetPreset("UI.DisableAds4", value ? "False" : null);
                App.FastFlags.SetPreset("UI.DisableAds5", value ? "False" : null);
                App.FastFlags.SetPreset("UI.DisableAds6", value ? "False" : null);
            }
        }

        public bool EnableDarkMode
        {
            get => App.FastFlags.GetPreset("DarkMode.BlueMode") == "False";
            set => App.FastFlags.SetPreset("DarkMode.BlueMode", value ? "False" : null);
        }

        public bool NoMoreMiddle
        {
            get => App.FastFlags.GetPreset("UI.RemoveMiddle") == "False";
            set => App.FastFlags.SetPreset("UI.RemoveMiddle", value ? "False" : null);
        }

        public bool Preload
        {
            get => App.FastFlags.GetPreset("Preload.Preload2") == "True";
            set
            {
                App.FastFlags.SetPreset("Preload.Preload2", value ? "True" : null);
                App.FastFlags.SetPreset("Preload.SoundPreload", value ? "True" : null);
                App.FastFlags.SetPreset("Preload.Texture", value ? "True" : null);
                App.FastFlags.SetPreset("Preload.TeleportPreload", value ? "True" : null);
                App.FastFlags.SetPreset("Preload.FontsPreload", value ? "True" : null);
                App.FastFlags.SetPreset("Preload.ItemPreload", value ? "True" : null);
                App.FastFlags.SetPreset("Preload.Teleport2", value ? "True" : null);
            }
        }

        public bool OptimizeCFrameUpdates
        {
            get => App.FastFlags.GetPreset("OptimizeCFrameUpdates") == "True";
            set
            {
                App.FastFlags.SetPreset("OptimizeCFrameUpdates", value ? "True" : null);
                App.FastFlags.SetPreset("OptimizeCFrameUpdatesIC", value ? "True" : null);
            }
        }

        public bool EnableCustomDisconnectError
        {
            get => App.FastFlags.GetPreset("UI.CustomDisconnectError1") == "True";
            set => App.FastFlags.SetPreset("UI.CustomDisconnectError1", value ? "True" : null);
        }

        public string? CustomDisconnectError
        {
            get => App.FastFlags.GetPreset("UI.CustomDisconnectError2");
            set => App.FastFlags.SetPreset("UI.CustomDisconnectError2", value);
        }

        public string? FakeVerify
        {
            get => App.FastFlags.GetPreset("Fake.Verify");
            set => App.FastFlags.SetPreset("Fake.Verify", value);
        }

        public string? NewCamera
        {
            get => App.FastFlags.GetPreset("Camera.Controls");
            set => App.FastFlags.SetPreset("Camera.Controls", value);
        }

        public string? ChatUI
        {
            get => App.FastFlags.GetPreset("Camera.Chat");
            set => App.FastFlags.SetPreset("Camera.Chat", value);
        }

        public bool RobloxStudioCoreUI
        {
            get => App.FastFlags.GetPreset("UI.OLDUIRobloxStudio") == "True";
            set => App.FastFlags.SetPreset("UI.OLDUIRobloxStudio", value ? "True" : null);
        }

        public bool VRToggle
        {
            get => GetFlagAsBool("Menu.VRToggles");
            set => SetFlagFromBool("Menu.VRToggles", value);
        }

        public bool SoothsayerCheck
        {
            get => GetFlagAsBool("Menu.Feedback");
            set => SetFlagFromBool("Menu.Feedback", value);
        }

        public bool LanguageSelector
        {
            get => App.FastFlags.GetPreset("Menu.LanguageSelector") != "0";
            set => SetFlagFromBool("Menu.LanguageSelector", value, "0");
        }

        public bool Haptics
        {
            get => GetFlagAsBool("Menu.Haptics");
            set => SetFlagFromBool("Menu.Haptics", value);
        }

        public bool FrameRateCap
        {
            get => GetFlagAsBool("Menu.Framerate");
            set => SetFlagFromBool("Menu.Framerate", value);
        }

        public bool MemoryProbing
        {
            get => App.FastFlags.GetPreset("Memory.Probe") == "True";
            set => App.FastFlags.SetPreset("Memory.Probe", value ? "True" : null);
        }

        public bool CacheSizeImprovement
        {
            get => App.FastFlags.GetPreset("Cache.Increase1") == "True";
            set
            {
                App.FastFlags.SetPreset("Cache.Increase1", value ? "True" : null);
                App.FastFlags.SetPreset("Cache.Increase2", value ? "False" : null);
                App.FastFlags.SetPreset("Cache.Increase3", value ? "True" : null);
                App.FastFlags.SetPreset("Cache.Increase4", value ? "1" : null);
                App.FastFlags.SetPreset("Cache.Increase5", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase6", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase7", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase8", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase9", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase10", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase11", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase12", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase13", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase14", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase15", value ? "2147483647" : null);
                App.FastFlags.SetPreset("Cache.Increase16", value ? "True" : null);
                App.FastFlags.SetPreset("Cache.Increase17", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase18", value ? "1036372536" : null);
                App.FastFlags.SetPreset("Cache.Increase19", value ? "1036372536" : null);
            }
        }

        public IReadOnlyDictionary<string, string?> CpuThreads => GetCpuThreads();

        public string SelectedCpuThreads
        {
            get => App.FastFlags.GetPreset("System.CpuCore1") ?? "Automatic";
            set
            {
                string? flagValue = CpuThreads.TryGetValue(value, out var v) ? v : null;

                App.FastFlags.SetPreset("System.CpuCore1", flagValue);
                App.FastFlags.SetPreset("System.CpuCore2", flagValue);
                App.FastFlags.SetPreset("System.CpuCore3", flagValue);
                App.FastFlags.SetPreset("System.CpuCore4", flagValue);
                App.FastFlags.SetPreset("System.CpuCore5", flagValue);
                App.FastFlags.SetPreset("System.CpuCore6", flagValue);
                App.FastFlags.SetPreset("System.CpuCore7", flagValue);
                App.FastFlags.SetPreset("System.CpuCore9", flagValue);

                if (flagValue is not null && int.TryParse(flagValue, out var result))
                {
                    int clamped = Math.Max(result - 1, 1);
                    App.FastFlags.SetPreset("System.CpuThreads", clamped.ToString());
                    App.FastFlags.SetPreset("System.CpuCore8", clamped.ToString());
                }
                else
                {
                    App.FastFlags.SetPreset("System.CpuThreads", null);
                    App.FastFlags.SetPreset("System.CpuCore8", null);
                }

                OnPropertyChanged(nameof(SelectedCpuThreads));
            }
        }

        public IReadOnlyDictionary<string, string?> CpuCoreMinThreadCount => GetCpuThreads();

        public string SelectedCpuCoreMinThreadCount
        {
            get => App.FastFlags.GetPreset("System.CpuCoreMinThreadCount") ?? "Automatic";
            set
            {
                string? flagValue = CpuCoreMinThreadCount.TryGetValue(value, out var v) ? v : null;

                if (flagValue is not null && int.TryParse(flagValue, out var result))
                    App.FastFlags.SetPreset("System.CpuCoreMinThreadCount", Math.Max(result - 1, 1).ToString());
                else
                    App.FastFlags.SetPreset("System.CpuCoreMinThreadCount", null);

                OnPropertyChanged(nameof(SelectedCpuCoreMinThreadCount));
            }
        }

        private static IReadOnlyDictionary<string, string?> GetCpuThreads()
        {
            var dictionary = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Automatic"] = null };

            int logicalProcessorCount = Environment.ProcessorCount;
            for (int i = 1; i <= logicalProcessorCount; i++)
                dictionary[i.ToString()] = i.ToString();

            return dictionary;
        }

        #endregion

        public event EventHandler? RequestPageReloadEvent;
        
        public event EventHandler? OpenFlagEditorEvent;

        private void OpenFastFlagEditor() => OpenFlagEditorEvent?.Invoke(this, EventArgs.Empty);

        public ICommand OpenFastFlagEditorCommand => new RelayCommand(OpenFastFlagEditor);

        public Visibility CanShowFastFlagEditor => App.IsStudioInstalled ? Visibility.Visible : Visibility.Collapsed;

        public bool UseFastFlagManager
        {
            get => App.Settings.Prop.UseFastFlagManager;
            set => App.Settings.Prop.UseFastFlagManager = value;
        }

        public IReadOnlyDictionary<MSAAMode, string?> MSAALevels => FastFlagManager.MSAAModes;

        public MSAAMode SelectedMSAALevel
        {
            get => MSAALevels.FirstOrDefault(x => x.Value == App.FastFlags.GetPreset("Rendering.MSAA")).Key;
            set => App.FastFlags.SetPreset("Rendering.MSAA", MSAALevels[value]);
        }

        public bool FixDisplayScaling
        {
            get => App.FastFlags.GetPreset("Rendering.DisableScaling") == "True";
            set => App.FastFlags.SetPreset("Rendering.DisableScaling", value ? "True" : null);
        }

        public IReadOnlyDictionary<TextureQuality, string?> TextureQualities => FastFlagManager.TextureQualityLevels;

        public TextureQuality SelectedTextureQuality
        {
            get => TextureQualities.Where(x => x.Value == App.FastFlags.GetPreset("Rendering.TextureQuality.Level")).FirstOrDefault().Key;
            set
            {
                if (value == TextureQuality.Default)
                {
                    App.FastFlags.SetPreset("Rendering.TextureQuality", null);
                }
                else
                {
                    App.FastFlags.SetPreset("Rendering.TextureQuality.OverrideEnabled", "True");
                    App.FastFlags.SetPreset("Rendering.TextureQuality.Level", TextureQualities[value]);
                }
            }
        }
        #region Engine presets

        // curated bundles across a handful of the toggles above, picked for unambiguous perf/
        // privacy impact - not every one of the ~83 toggles on this page, so presets can't
        // silently flip something niche/situational the user didn't expect
        public string[] EnginePresetNames { get; } = { "Default", "Privacy", "Quality", "Balanced", "Performance", "Potato" };

        // one-time apply action, not a persisted selection - the individual toggles above are the
        // source of truth, and may not match any named preset once hand-tweaked
        public string SelectedEnginePreset
        {
            get => "";
            set
            {
                switch (value)
                {
                    case "Privacy":
                        ApplyPrivacyPreset();
                        break;
                    case "Quality":
                        ApplyQualityPreset();
                        break;
                    case "Balanced":
                        ApplyBalancedPreset();
                        break;
                    case "Performance":
                        ApplyPerformancePreset();
                        break;
                    case "Potato":
                        ApplyPotatoPreset();
                        break;
                    default:
                        ApplyDefaultPreset();
                        break;
                }

                RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ApplyDefaultPreset()
        {
            DisableTelemetry = false;
            DisableWebview2Telemetry = false;
            DisableVoiceChatTelemetry = false;
            BlockTencent = false;
            LessLagSpikes = false;
            FasterLoading = false;
            BetterPacketSending = false;
            CacheSizeImprovement = false;
            OptimizeCFrameUpdates = false;
            Threading = false;
            Preload = false;
            DisablePostFX = false;
            DisablePlayerShadows = false;
            MinimalRendering = false;
            WorserParticles = false;
            LowPolyMeshes = false;
            LightCulling = false;
            DisableSky = false;
            MoreLighting = false;
            NoGuiBlur = false;
            TextureRemover = false;
            DisableTerrainTextures = false;
            OldChromeUI = false;
            Prerender = false;
        }

        // telemetry/tracking reduction only - no performance or visual tradeoffs
        private void ApplyPrivacyPreset()
        {
            ApplyDefaultPreset();

            DisableTelemetry = true;
            DisableWebview2Telemetry = true;
            DisableVoiceChatTelemetry = true;
            BlockTencent = true;
        }

        // brighter/clearer rendering, no toggles that reduce visual quality
        private void ApplyQualityPreset()
        {
            ApplyDefaultPreset();

            MoreLighting = true;
        }

        private void ApplyBalancedPreset()
        {
            ApplyDefaultPreset();

            DisableTelemetry = true;
            DisableWebview2Telemetry = true;
            DisableVoiceChatTelemetry = true;
            LessLagSpikes = true;
            FasterLoading = true;
            BetterPacketSending = true;
            CacheSizeImprovement = true;
            OptimizeCFrameUpdates = true;
            Threading = true;
            Preload = true;
        }

        private void ApplyPerformancePreset()
        {
            ApplyBalancedPreset();

            DisablePostFX = true;
            DisablePlayerShadows = true;
            MinimalRendering = true;
            WorserParticles = true;
            LowPolyMeshes = true;
            LightCulling = true;
            DisableSky = true;
        }

        // most aggressive tier - everything Performance has, plus further UI/texture cuts for
        // very low-end or integrated-graphics systems
        private void ApplyPotatoPreset()
        {
            ApplyPerformancePreset();

            NoGuiBlur = true;
            TextureRemover = true;
            DisableTerrainTextures = true;
            OldChromeUI = true;
            Prerender = true;
        }

        #endregion

        #region Per-game scope

        public string[] EngineScopeModeOptions { get; } =
        {
            "Apply everywhere",
            "Only apply to listed games",
            "Apply everywhere except listed games",
        };

        public string SelectedEngineScopeMode
        {
            get => App.Settings.Prop.EngineSettingsScope switch
            {
                Enums.EngineSettingsScopeMode.OnlyListedPlaces => EngineScopeModeOptions[1],
                Enums.EngineSettingsScopeMode.AllExceptListedPlaces => EngineScopeModeOptions[2],
                _ => EngineScopeModeOptions[0],
            };
            set
            {
                App.Settings.Prop.EngineSettingsScope = value == EngineScopeModeOptions[1]
                    ? Enums.EngineSettingsScopeMode.OnlyListedPlaces
                    : value == EngineScopeModeOptions[2]
                        ? Enums.EngineSettingsScopeMode.AllExceptListedPlaces
                        : Enums.EngineSettingsScopeMode.All;

                OnPropertyChanged(nameof(SelectedEngineScopeMode));
            }
        }

        public ObservableCollection<string> EngineScopedPlaces { get; } = new(App.Settings.Prop.EngineSettingsScopedPlaces);

        private string _engineScopePlaceId = "";

        public string EngineScopePlaceId
        {
            get => _engineScopePlaceId;
            set { _engineScopePlaceId = value; OnPropertyChanged(nameof(EngineScopePlaceId)); }
        }

        public ICommand AddEngineScopedPlaceCommand => new RelayCommand(() =>
        {
            string id = EngineScopePlaceId.Trim();

            if (!long.TryParse(id, out _) || EngineScopedPlaces.Contains(id))
                return;

            EngineScopedPlaces.Add(id);
            App.Settings.Prop.EngineSettingsScopedPlaces.Add(id);
            EngineScopePlaceId = "";
        });

        public ICommand RemoveEngineScopedPlaceCommand => new RelayCommand<string>(id =>
        {
            if (id is null)
                return;

            EngineScopedPlaces.Remove(id);
            App.Settings.Prop.EngineSettingsScopedPlaces.Remove(id);
        });

        #endregion

        public bool ResetConfiguration
        {
            get => _preResetFlags is not null;

            set
            {
                if (value)
                {
                    _preResetFlags = new(App.FastFlags.Prop);
                    App.FastFlags.Prop.Clear();
                }
                else
                {
                    App.FastFlags.Prop = _preResetFlags!;
                    _preResetFlags = null;
                }

                RequestPageReloadEvent?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
