using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

// Ported from Voidstrap's UI/Elements/ContextMenu/ThemeColorItem.cs, retargeted at
// PhasmaStrap.Utility.AppColorTheme (see that file for why it isn't named CustomTheme).
namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public sealed class ThemeColorItem : INotifyPropertyChanged
    {
        private Color _color;

        public string Key { get; }

        public string Label { get; }

        public string Group { get; }

        public Color Color => _color;

        public Brush Swatch => new SolidColorBrush(_color);

        public string Hex
        {
            get => AppColorTheme.ToHex(_color);
            set
            {
                if (AppColorTheme.TryParseColor(value, out Color c) && c != _color)
                {
                    _color = c;
                    OnPropertyChanged(nameof(Swatch));
                    Changed?.Invoke();
                }
            }
        }

        public event Action? Changed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ThemeColorItem(string key, string label, Color color, string group = "")
        {
            Key = key;
            Label = label;
            _color = color;
            Group = group;
        }

        public void SetColor(Color c)
        {
            if (c == _color)
                return;
            _color = c;
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(Swatch));
            Changed?.Invoke();
        }

        public void Detach()
        {
            Changed = null;
            PropertyChanged = null;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
