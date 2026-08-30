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

        public GBSEditorViewModel()
        {
            ReloadCommand = new RelayCommand(Load);
            Load();
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
