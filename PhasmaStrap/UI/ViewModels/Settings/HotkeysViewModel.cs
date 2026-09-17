using System.Collections.ObjectModel;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    // A single hotkey-bindable action row. GestureText is edited directly from HotkeysPage's
    // capture box (see HotkeysPage.xaml.cs) and persists straight to
    // Settings.Prop.HotkeyBindings - there's no "Apply" step, since hotkeys only ever actually
    // run inside a Watcher session (a separate process from Settings), which reads the saved
    // bindings fresh each time it starts. See GlobalHotkeyManager.ApplyBindings.
    public sealed class HotkeyRow : NotifyPropertyChangedViewModel
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";

        public string GestureText
        {
            get => App.Settings.Prop.HotkeyBindings.TryGetValue(Id, out string? text) ? text : "";
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    App.Settings.Prop.HotkeyBindings.Remove(Id);
                else
                    App.Settings.Prop.HotkeyBindings[Id] = value;

                App.Settings.Save();
                OnPropertyChanged(nameof(GestureText));
                OnPropertyChanged(nameof(HasGesture));
            }
        }

        public bool HasGesture => !string.IsNullOrEmpty(GestureText);
    }

    public class HotkeysViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<HotkeyRow> Hotkeys { get; } = new(
            HotkeyActions.All.Select(a => new HotkeyRow { Id = a.Id, DisplayName = a.DisplayName, Description = a.Description }));
    }
}
