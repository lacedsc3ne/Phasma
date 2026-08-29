using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhasmaStrap.Models
{
    // A single row shown in FFlagSearchDialog's bulk-validation result grid.
    // Ported from Voidstrap's UI/Elements/Dialogs/FlagValidationResult.cs.
    public class FlagValidationResult : INotifyPropertyChanged
    {
        private string _name = "";
        private string _inputValue = "";
        private string _status = "";
        private string _validValue = "";
        private string _notes = "";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string InputValue
        {
            get => _inputValue;
            set { _inputValue = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string ValidValue
        {
            get => _validValue;
            set { _validValue = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
