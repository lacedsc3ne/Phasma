using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhasmaStrap.Models
{
    // A single row shown in FFlagSearchDialog's search/browse result grids.
    // Ported from Voidstrap's UI/Elements/Dialogs/FlagSearchResult.cs.
    public class FlagSearchResult : INotifyPropertyChanged
    {
        private string _name = "";
        private string _value = "";
        private string _source = "";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public string Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
