using System.Collections.ObjectModel;
using System.Windows.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    // A single hotkey-bindable action row. The binding persists straight to
    // Settings.Prop.HotkeyBindings - there is no "Apply" step: hotkeys run inside the game-session
    // process, which follows Settings.json (SettingsHotReload) and re-reads the bindings live.
    // Capturing itself is driven by HotkeysPage.xaml.cs; this only holds the row's state.
    public sealed class HotkeyRow : NotifyPropertyChangedViewModel
    {
        private bool _isListening;
        private ModifierKeys _heldModifiers;
        private string _statusText = "";
        private bool _statusIsWarning;

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
                OnPropertyChanged(nameof(ButtonText));
            }
        }

        public bool HasGesture => !string.IsNullOrEmpty(GestureText);

        public bool IsListening
        {
            get => _isListening;
            set
            {
                _isListening = value;
                _heldModifiers = ModifierKeys.None;
                OnPropertyChanged(nameof(IsListening));
                OnPropertyChanged(nameof(ButtonText));
            }
        }

        // modifiers held so far while listening - shown live so a combination can be seen building up
        public ModifierKeys HeldModifiers
        {
            get => _heldModifiers;
            set
            {
                _heldModifiers = value;
                OnPropertyChanged(nameof(ButtonText));
            }
        }

        public string ButtonText
        {
            get
            {
                if (IsListening)
                {
                    if (_heldModifiers == ModifierKeys.None)
                        return "Press a key or combination...";

                    // "Ctrl+Shift+A" minus the placeholder key = "Ctrl+Shift+"
                    string prefix = HotkeyGesture.Format(_heldModifiers, Key.A);
                    return prefix[..^1] + "...";
                }

                return HasGesture ? HotkeyGesture.ToDisplay(GestureText) : "Click to set";
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        public bool HasStatus => !string.IsNullOrEmpty(_statusText);

        public bool StatusIsWarning
        {
            get => _statusIsWarning;
            private set
            {
                _statusIsWarning = value;
                OnPropertyChanged(nameof(StatusIsWarning));
            }
        }

        public void SetStatus(string text, bool warning = false)
        {
            StatusIsWarning = warning;
            StatusText = text;
        }
    }

    public class HotkeysViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<HotkeyRow> Hotkeys { get; } = new(
            HotkeyActions.All.Select(a => new HotkeyRow { Id = a.Id, DisplayName = a.DisplayName, Description = a.Description }));
    }
}
