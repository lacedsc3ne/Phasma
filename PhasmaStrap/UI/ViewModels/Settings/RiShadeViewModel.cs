using PhasmaStrap.Models.Persistable;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class RiShadeViewModel : NotifyPropertyChangedViewModel
    {
        private RiShadeSettings Prop => App.Settings.Prop.RiShade;

        public bool RiShadeEnabled
        {
            get => App.Settings.Prop.RiShadeEnabled;
            set => App.Settings.Prop.RiShadeEnabled = value;
        }

        public IEnumerable<string> RenderScaleNames { get; } = RiShadeSettings.RenderScaleNames;
        public int RenderScaleIndex
        {
            get => Prop.RenderScaleIndex;
            set => Prop.RenderScaleIndex = value;
        }

        public IEnumerable<string> PresetNames { get; } = RiShadeSettings.PresetNames;

        private string _selectedPreset = RiShadeSettings.PresetNames[0];
        public string SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                _selectedPreset = value;
                if (string.IsNullOrEmpty(value))
                    return;
                if (value == "Vanilla (off)")
                    App.Settings.Prop.RiShade = new RiShadeSettings();
                else
                    RiShadeSettings.ApplyPreset(Prop, value);
                RefreshAll();
            }
        }

        private void RefreshAll() => OnPropertyChanged(string.Empty);

        // color grade
        public bool GradeEnabled { get => Prop.GradeEnabled; set => Prop.GradeEnabled = value; }
        public float Brightness { get => Prop.Brightness; set => Prop.Brightness = value; }
        public float Gamma { get => Prop.Gamma; set => Prop.Gamma = value; }
        public float HueShift { get => Prop.HueShift; set => Prop.HueShift = value; }

        // tonemap
        public bool TonemapEnabled { get => Prop.TonemapEnabled; set => Prop.TonemapEnabled = value; }
        public IEnumerable<string> TonemapNames { get; } = RiShadeSettings.TonemapNames;
        public int TonemapMode { get => Prop.TonemapMode; set => Prop.TonemapMode = value; }
        public float TonemapExposure { get => Prop.TonemapExposure; set => Prop.TonemapExposure = value; }
        public float TonemapWhitepoint { get => Prop.TonemapWhitepoint; set => Prop.TonemapWhitepoint = value; }

        // vignette
        public bool VignetteEnabled { get => Prop.VignetteEnabled; set => Prop.VignetteEnabled = value; }
        public float VignetteStrength { get => Prop.VignetteStrength; set => Prop.VignetteStrength = value; }

        // sharpen
        public bool SharpenEnabled { get => Prop.SharpenEnabled; set => Prop.SharpenEnabled = value; }
        public float SharpenStrength { get => Prop.SharpenStrength; set => Prop.SharpenStrength = value; }

        // bloom
        public bool BloomEnabled { get => Prop.BloomEnabled; set => Prop.BloomEnabled = value; }
        public float BloomStrength { get => Prop.BloomStrength; set => Prop.BloomStrength = value; }
        public float BloomThreshold { get => Prop.BloomThreshold; set => Prop.BloomThreshold = value; }

        // chromatic aberration
        public bool ChromaEnabled { get => Prop.ChromaEnabled; set => Prop.ChromaEnabled = value; }
        public float ChromaStrength { get => Prop.ChromaStrength; set => Prop.ChromaStrength = value; }

        // film grain
        public bool GrainEnabled { get => Prop.GrainEnabled; set => Prop.GrainEnabled = value; }
        public float GrainStrength { get => Prop.GrainStrength; set => Prop.GrainStrength = value; }

        // clarity / debanding / ambient glow
        public float ClarityStrength { get => Prop.ClarityStrength; set => Prop.ClarityStrength = value; }
        public bool DebandEnabled { get => Prop.DebandEnabled; set => Prop.DebandEnabled = value; }
        public float DebandStrength { get => Prop.DebandStrength; set => Prop.DebandStrength = value; }
        public float AmbientStrength { get => Prop.AmbientStrength; set => Prop.AmbientStrength = value; }
    }
}
