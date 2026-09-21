using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
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

        public bool FeatureIsOn => Id switch
        {
            HotkeyActions.SaveInstantReplay => App.Settings.Prop.InstantReplayEnabled,
            HotkeyActions.ToggleOverlayFocusMode => App.Settings.Prop.OverlayHudEnabled || App.Settings.Prop.Crosshair,
            HotkeyActions.ToggleHeadsetAudio => App.Settings.Prop.HeadsetAudioEnabled,
            _ => true
        };

        public bool FeatureIsBlocking => Id switch
        {
            HotkeyActions.SaveInstantReplay => !App.Settings.Prop.InstantReplayEnabled,
            HotkeyActions.ToggleOverlayFocusMode => !App.Settings.Prop.OverlayHudEnabled && !App.Settings.Prop.Crosshair,
            _ => false
        };

        public string FeatureText => Id switch
        {
            HotkeyActions.SaveInstantReplay => App.Settings.Prop.InstantReplayEnabled
                ? "Instant Replay is on"
                : "Instant Replay is off, so this hotkey does nothing",
            HotkeyActions.ToggleOverlayFocusMode => App.Settings.Prop.OverlayHudEnabled || App.Settings.Prop.Crosshair
                ? "An overlay is on"
                : "No overlay is on, so there is nothing to hide",
            HotkeyActions.ToggleHeadsetAudio => App.Settings.Prop.HeadsetAudioEnabled
                ? "Headset boost is on"
                : "Headset boost is off",
            _ => "Always available"
        };

        public void RefreshFeature()
        {
            OnPropertyChanged(nameof(FeatureIsOn));
            OnPropertyChanged(nameof(FeatureIsBlocking));
            OnPropertyChanged(nameof(FeatureText));
        }
    }

    public sealed class HotkeyGroup
    {
        public string Name { get; init; } = "";

        public string Description { get; init; } = "";

        public ObservableCollection<HotkeyRow> Rows { get; } = new();
    }

    public sealed class KeyCap : NotifyPropertyChangedViewModel
    {
        private string _state = "Free";
        private string _caption = "";

        public string Label { get; init; } = "";

        public Key Key { get; init; }

        public ModifierKeys Modifier { get; init; }

        public double Width { get; init; } = 34;

        public string State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(nameof(State)); }
        }

        public string Caption
        {
            get => _caption;
            set { _caption = value; OnPropertyChanged(nameof(Caption)); }
        }
    }

    public sealed class KeyCapRow
    {
        public ObservableCollection<KeyCap> Keys { get; } = new();
    }

    public class HotkeysViewModel : NotifyPropertyChangedViewModel
    {
        public HotkeysViewModel()
        {
            foreach (HotkeyRow row in Hotkeys)
            {
                string name = row.Id switch
                {
                    HotkeyActions.TakeScreenshot or HotkeyActions.SaveInstantReplay => "Capture",
                    HotkeyActions.ToggleOverlayFocusMode => "Overlays",
                    _ => "Performance"
                };

                HotkeyGroup? group = Groups.FirstOrDefault(g => g.Name == name);

                if (group is null)
                {
                    group = new HotkeyGroup { Name = name, Description = GroupDescription(name) };
                    Groups.Add(group);
                }

                group.Rows.Add(row);
            }

            BuildKeyboard();
            Refresh();
        }

        public static HotkeysViewModel Shared { get; } = new();

        public ObservableCollection<HotkeyRow> Hotkeys { get; } = new(
            HotkeyActions.All.Select(a => new HotkeyRow { Id = a.Id, DisplayName = a.DisplayName, Description = a.Description }));

        public HotkeyRow? Row(string id) => Hotkeys.FirstOrDefault(r => r.Id == id);

        public ObservableCollection<HotkeyGroup> Groups { get; } = new();

        public ObservableCollection<KeyCapRow> Keyboard { get; } = new();

        public void Refresh()
        {
            foreach (HotkeyRow row in Hotkeys)
                row.RefreshFeature();

            RefreshKeyboard();
        }

        public void RefreshKeyboard()
        {
            var bound = new Dictionary<Key, List<string>>();
            ModifierKeys used = ModifierKeys.None;

            foreach (HotkeyRow row in Hotkeys)
            {
                if (!HotkeyGesture.TryParse(row.GestureText, out ModifierKeys modifiers, out Key key))
                    continue;

                used |= modifiers;

                if (!bound.TryGetValue(key, out List<string>? names))
                {
                    names = new List<string>();
                    bound[key] = names;
                }

                names.Add($"{HotkeyGesture.ToDisplay(row.GestureText)} · {row.DisplayName}");
            }

            foreach (KeyCapRow row in Keyboard)
            {
                foreach (KeyCap cap in row.Keys)
                {
                    if (cap.Modifier != ModifierKeys.None)
                    {
                        bool inUse = (used & cap.Modifier) != 0;

                        cap.State = inUse ? "Modifier" : "Free";
                        cap.Caption = inUse ? $"{cap.Label} is held by at least one hotkey" : "";
                        continue;
                    }

                    if (cap.Key != Key.None && bound.TryGetValue(cap.Key, out List<string>? names))
                    {
                        cap.State = "Bound";
                        cap.Caption = string.Join("\n", names);
                        continue;
                    }

                    cap.State = "Free";
                    cap.Caption = "";
                }
            }
        }

        private static string GroupDescription(string name) => name switch
        {
            "Capture" => "Screenshots and clips, saved to the Capture gallery.",
            "Overlays" => "The stats HUD and the crosshair drawn over Roblox.",
            _ => "Memory and audio tweaks that run while you play."
        };

        private void BuildKeyboard()
        {
            AddRow(
                Cap("Esc", Key.Escape, 42),
                Cap("F1", Key.F1), Cap("F2", Key.F2), Cap("F3", Key.F3), Cap("F4", Key.F4),
                Cap("F5", Key.F5), Cap("F6", Key.F6), Cap("F7", Key.F7), Cap("F8", Key.F8),
                Cap("F9", Key.F9), Cap("F10", Key.F10), Cap("F11", Key.F11), Cap("F12", Key.F12));

            AddRow(
                Cap("`", Key.OemTilde),
                Cap("1", Key.D1), Cap("2", Key.D2), Cap("3", Key.D3), Cap("4", Key.D4), Cap("5", Key.D5),
                Cap("6", Key.D6), Cap("7", Key.D7), Cap("8", Key.D8), Cap("9", Key.D9), Cap("0", Key.D0),
                Cap("-", Key.OemMinus), Cap("=", Key.OemPlus), Cap("Backspace", Key.Back, 78));

            AddRow(
                Cap("Tab", Key.Tab, 54),
                Cap("Q", Key.Q), Cap("W", Key.W), Cap("E", Key.E), Cap("R", Key.R), Cap("T", Key.T),
                Cap("Y", Key.Y), Cap("U", Key.U), Cap("I", Key.I), Cap("O", Key.O), Cap("P", Key.P),
                Cap("[", Key.OemOpenBrackets), Cap("]", Key.OemCloseBrackets), Cap("\\", Key.OemPipe, 46));

            AddRow(
                Cap("Caps", Key.Capital, 64),
                Cap("A", Key.A), Cap("S", Key.S), Cap("D", Key.D), Cap("F", Key.F), Cap("G", Key.G),
                Cap("H", Key.H), Cap("J", Key.J), Cap("K", Key.K), Cap("L", Key.L),
                Cap(";", Key.OemSemicolon), Cap("'", Key.OemQuotes), Cap("Enter", Key.Return, 78));

            AddRow(
                Modifier("Shift", ModifierKeys.Shift, 84),
                Cap("Z", Key.Z), Cap("X", Key.X), Cap("C", Key.C), Cap("V", Key.V), Cap("B", Key.B),
                Cap("N", Key.N), Cap("M", Key.M), Cap(",", Key.OemComma), Cap(".", Key.OemPeriod),
                Cap("/", Key.OemQuestion), Modifier("Shift", ModifierKeys.Shift, 94));

            AddRow(
                Modifier("Ctrl", ModifierKeys.Control, 54),
                Modifier("Win", ModifierKeys.Windows, 46),
                Modifier("Alt", ModifierKeys.Alt, 46),
                Cap("Space", Key.Space, 228),
                Modifier("Alt", ModifierKeys.Alt, 46),
                Modifier("Ctrl", ModifierKeys.Control, 54));
        }

        private void AddRow(params KeyCap[] keys)
        {
            var row = new KeyCapRow();

            foreach (KeyCap cap in keys)
                row.Keys.Add(cap);

            Keyboard.Add(row);
        }

        private static KeyCap Cap(string label, Key key, double width = 34) =>
            new() { Label = label, Key = key, Width = width };

        private static KeyCap Modifier(string label, ModifierKeys modifier, double width = 46) =>
            new() { Label = label, Modifier = modifier, Width = width };
    }
}
