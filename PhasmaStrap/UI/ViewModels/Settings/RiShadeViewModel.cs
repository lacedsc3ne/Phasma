using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhasmaStrap.Models.Persistable;
using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class RiShadeViewModel : NotifyPropertyChangedViewModel
    {
        private RiShadeSettings Prop => App.Settings.Prop.RiShade;

        public ICommand ExportPresetCommand => new RelayCommand(ExportPreset);

        public ICommand ImportPresetCommand => new RelayCommand(ImportPreset);

        private void ExportPreset()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

            var dialog = new SaveFileDialog
            {
                FileName = $"RiShade-preset-{timestamp}.json",
                Filter = $"{Strings.FileTypes_JSONFiles}|*.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            string contents = JsonSerializer.Serialize(Prop, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(dialog.FileName, contents);
        }

        private void ImportPreset()
        {
            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.FileTypes_JSONFiles}|*.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string contents = File.ReadAllText(dialog.FileName);

                RiShadeSettings? settings = JsonSerializer.Deserialize<RiShadeSettings>(contents);

                if (settings is null)
                    throw new Exception("Deserialization returned null");

                settings.Normalize();

                App.Settings.Prop.RiShade = settings;

                RefreshAll();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(
                    string.Format(Strings.Menu_RiShade_ImportPreset_Failed, ex.Message),
                    System.Windows.MessageBoxImage.Error
                );
            }
        }

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
