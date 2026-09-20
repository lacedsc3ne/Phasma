using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public sealed class ThemeColorItem : INotifyPropertyChanged
    {
        private Color _color;

        public string Key { get; }

        public string Label { get; }

        public string Group { get; }

        public Color Color => _color;

        public bool AllowAlpha { get; }

        public double OpacityPercent
        {
            get => Math.Round(_color.A / 255.0 * 100);
            set
            {
                byte alpha = (byte)Math.Round(Math.Clamp(value, 0, 100) / 100.0 * 255);
                if (alpha != _color.A)
                    SetColor(Color.FromArgb(alpha, _color.R, _color.G, _color.B));
            }
        }

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
                    OnPropertyChanged(nameof(OpacityPercent));
                    Changed?.Invoke();
                }
            }
        }

        public event Action? Changed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ThemeColorItem(string key, string label, Color color, string group = "", bool allowAlpha = false)
        {
            AllowAlpha = allowAlpha;
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
            OnPropertyChanged(nameof(OpacityPercent));
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
