using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Xml.Linq;

using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>
    /// A single raw key stored in Roblox's GlobalBasicSettings file - unlike PerformanceViewModel,
    /// which only surfaces a handful of named GBSEditor.KnownProperties, this exposes every key so it
    /// can be viewed, added, edited and removed directly. Ported from Voidstrap's GBSEditorPage, but
    /// adapted from Voidstrap's curated-toggles UI into a raw editor (the FastFlagEditorPage-style
    /// counterpart to PerformancePage's curated GBS toggles), since PhasmaStrap's GBSEditor.cs backend
    /// (kept as-is) only accepts writes to its own KnownProperties allowlist.
    /// </summary>
    public sealed class GBSEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _name = "";
        private string _type = "token";
        private string _value = "";

        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }

        public string Type
        {
            get => _type;
            set { _type = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type))); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
    }

    /// <summary>
    /// Raw editor over every key in Roblox's GlobalBasicSettings_13.xml file, reusing PhasmaStrap's
    /// existing <see cref="GBSEditor"/> backend for loading/saving/read-only handling. Because
    /// GBSEditor.SetProperty refuses to write anything outside its own KnownProperties allowlist, add/
    /// edit/remove here work directly against the loaded XDocument's Properties element (both of which
    /// GBSEditor exposes publicly for exactly this kind of raw access) and then go through GBSEditor's
    /// own Save() so the same atomic-write/read-only handling is used for every key, known or not.
    /// </summary>
    public class GBSEditorViewModel : NotifyPropertyChangedViewModel
    {
        private readonly GBSEditor _gbs = new();

        public ObservableCollection<GBSEntry> Entries { get; } = new();

        public bool SettingsFileReadOnly
        {
            get
            {
                try
                {
                    return _gbs.GetReadOnly();
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                try
                {
                    _gbs.SetReadOnly(value);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("GBSEditorViewModel::SettingsFileReadOnly", ex);
                }

                OnPropertyChanged(nameof(SettingsFileReadOnly));
            }
        }

        public ICommand ReloadCommand { get; }

        public ICommand ResetToDefaultsCommand { get; }

        public GBSEditorViewModel()
        {
            ReloadCommand = new RelayCommand(Load);
            ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
            Load();
        }

        // --- curated settings, matching Voidstrap's GBSEditorPage tabs - reuses the shared GBSEditor
        // backend (same instance the raw Entries editor above already writes through), so both halves
        // of this page stay consistent. Every key here is in GBSEditor.KnownProperties.

        // Interface
        public float UITransparency
        {
            get => _gbs.GetFloat("PreferredTransparency", 1f);
            set => Write("PreferredTransparency", value, nameof(UITransparency));
        }

        public int PreferredTextSize
        {
            get => _gbs.GetInt("PreferredTextSize", 1);
            set => Write("PreferredTextSize", value, nameof(PreferredTextSize));
        }

        public bool ReducedMotion
        {
            get => _gbs.GetBool("ReducedMotion", true);
            set => Write("ReducedMotion", value, nameof(ReducedMotion));
        }

        // Roblox stores "was the hide-HUD shortcut last used", not HUD visibility directly, so this is
        // inverted the same way Voidstrap's HudVisible is.
        public bool HudVisible
        {
            get => !_gbs.GetBool("UsedHideHudShortcut");
            set => Write("UsedHideHudShortcut", !value, nameof(HudVisible));
        }

        public bool ChatVisible
        {
            get => _gbs.GetBool("ChatVisible", true);
            set => Write("ChatVisible", value, nameof(ChatVisible));
        }

        public bool PlayerNamesEnabled
        {
            get => _gbs.GetBool("PlayerNamesEnabled", true);
            set => Write("PlayerNamesEnabled", value, nameof(PlayerNamesEnabled));
        }

        public bool BadgeVisible
        {
            get => _gbs.GetBool("BadgeVisible", true);
            set => Write("BadgeVisible", value, nameof(BadgeVisible));
        }

        // Graphics
        public int FramerateCap
        {
            get => _gbs.GetInt("FramerateCap", 0);
            set => Write("FramerateCap", value, nameof(FramerateCap));
        }

        public int GraphicsQuality
        {
            get => _gbs.GetInt("SavedQualityLevel", 0);
            set => Write("SavedQualityLevel", value, nameof(GraphicsQuality));
        }

        public bool Fullscreen
        {
            get => _gbs.GetBool("Fullscreen", true);
            set => Write("Fullscreen", value, nameof(Fullscreen));
        }

        // Audio
        public float MasterVolume
        {
            get => _gbs.GetFloat("MasterVolume", 1f);
            set => Write("MasterVolume", value, nameof(MasterVolume));
        }

        public float VoiceChatVolume
        {
            get => _gbs.GetFloat("PartyVoiceVolume", 1f);
            set => Write("PartyVoiceVolume", value, nameof(VoiceChatVolume));
        }

        // Input
        public float MouseSensitivity
        {
            get => _gbs.GetFloat("MouseSensitivity", 1f);
            set => Write("MouseSensitivity", value, nameof(MouseSensitivity));
        }

        public bool CameraYInverted
        {
            get => _gbs.GetBool("CameraYInverted");
            set => Write("CameraYInverted", value, nameof(CameraYInverted));
        }

        public float GamepadSensitivity
        {
            get => _gbs.GetFloat("GamepadCameraSensitivity", 0.2f);
            set => Write("GamepadCameraSensitivity", value, nameof(GamepadSensitivity));
        }

        // Roblox stores vibration strength as a float, not a bool - HapticStrength > 0 means enabled,
        // same as Voidstrap's ControllerVibration.
        public bool ControllerVibration
        {
            get => _gbs.GetFloat("HapticStrength", 1f) > 0f;
            set => Write("HapticStrength", value ? 1f : 0f, nameof(ControllerVibration));
        }

        // VR
        public bool VREnabled
        {
            get => _gbs.GetBool("VREnabled");
            set => Write("VREnabled", value, nameof(VREnabled));
        }

        // GBSEditor.KnownProperties clamps VRComfortSetting to [0, 2] (three-value enum: Comfort/Normal/
        // Intense), so this uses 0/1/2 rather than Voidstrap's XAML tags of 1/2/3 - those would silently
        // clamp "Intense" (tag 3) down to 2 ("Normal") under the shared range both forks enforce.
        public int VRComfortSetting
        {
            get => _gbs.GetInt("VRComfortSetting", 1);
            set => Write("VRComfortSetting", value, nameof(VRComfortSetting));
        }

        public bool VignetteEnabled
        {
            get => _gbs.GetBool("VignetteEnabled", true);
            set => Write("VignetteEnabled", value, nameof(VignetteEnabled));
        }

        // Network
        // Same underlying GBS key as PerformancePage's "Show performance stats" toggle - Roblox only
        // has one stats overlay setting, Voidstrap just surfaces it again under its Network tab.
        public bool NetworkStatsVisible
        {
            get => _gbs.GetBool("PerformanceStatsVisible");
            set => Write("PerformanceStatsVisible", value, nameof(NetworkStatsVisible));
        }

        public bool ChatTranslationEnabled
        {
            get => _gbs.GetBool("ChatTranslationEnabled", true);
            set => Write("ChatTranslationEnabled", value, nameof(ChatTranslationEnabled));
        }

        // Advanced
        public bool MicroProfilerWebServerEnabled
        {
            get => _gbs.GetBool("MicroProfilerWebServerEnabled");
            set => Write("MicroProfilerWebServerEnabled", value, nameof(MicroProfilerWebServerEnabled));
        }

        public bool OnScreenProfilerEnabled
        {
            get => _gbs.GetBool("OnScreenProfilerEnabled");
            set => Write("OnScreenProfilerEnabled", value, nameof(OnScreenProfilerEnabled));
        }

        /// <summary>
        /// Writes and saves a single curated property, then keeps the raw Entries editor (which reads
        /// from a materialized snapshot of the document, not a live view) in sync so switching to the
        /// Raw Editor tab reflects what a curated tab just changed.
        /// </summary>
        private void Write(string name, object value, string propertyName)
        {
            _gbs.SetProperty(name, value);
            _gbs.Save();
            RefreshEntriesFromDocument();
            OnPropertyChanged(propertyName);
        }

        private void RefreshEntriesFromDocument()
        {
            XElement? properties = GBSEditor.FindProperties(_gbs.Document);
            if (properties is null)
                return;

            Entries.Clear();
            foreach (XElement element in properties.Elements().OrderBy(x => x.Attribute("name")?.Value, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new GBSEntry
                {
                    Name = element.Attribute("name")?.Value ?? "",
                    Type = element.Name.LocalName,
                    Value = element.Value
                });
            }
        }

        private void ResetToDefaults()
        {
            _gbs.ResetProperties();
            _gbs.Save();
            Load();
            OnPropertyChanged(string.Empty);
        }

        public void Load()
        {
            _gbs.Load();

            Entries.Clear();

            XElement? properties = GBSEditor.FindProperties(_gbs.Document);

            if (properties is null)
                return;

            foreach (XElement element in properties.Elements().OrderBy(x => x.Attribute("name")?.Value, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new GBSEntry
                {
                    Name = element.Attribute("name")?.Value ?? "",
                    Type = element.Name.LocalName,
                    Value = element.Value
                });
            }

            OnPropertyChanged(nameof(SettingsFileReadOnly));
        }

        public bool NameExists(string name) =>
            Entries.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal));

        public bool Add(string name, string type, string value)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || NameExists(name))
                return false;

            var entry = new GBSEntry { Name = name, Type = type, Value = value };
            Entries.Add(entry);

            return Persist();
        }

        public bool Remove(GBSEntry entry)
        {
            Entries.Remove(entry);
            return Persist();
        }

        /// <summary>
        /// Writes the current Entries collection into the underlying document and saves it. Called
        /// after every add/remove/edit, mirroring GBSEditor.Save()'s all-or-nothing semantics.
        /// </summary>
        public bool Persist()
        {
            XElement? properties = GBSEditor.EnsureProperties(_gbs.Document);

            if (properties is null)
                return false;

            properties.RemoveNodes();

            foreach (GBSEntry entry in Entries)
            {
                var element = new XElement(string.IsNullOrWhiteSpace(entry.Type) ? "token" : entry.Type, entry.Value);
                element.SetAttributeValue("name", entry.Name);
                properties.Add(element);
            }

            bool success = _gbs.Save();

            if (!success)
                App.Logger.WriteLine("GBSEditorViewModel::Persist", "Failed to save the Roblox settings file");

            return success;
        }
    }
}
