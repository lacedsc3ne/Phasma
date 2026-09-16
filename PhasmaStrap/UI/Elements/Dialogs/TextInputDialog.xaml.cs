using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// A minimal "enter a name" prompt, used by the Mod Management and Custom Cursor Set
    /// managers on ModsPage to ask for a new/renamed item's display name.
    /// </summary>
    public partial class TextInputDialog : WpfUiWindow
    {
        public bool Confirmed { get; private set; } = false;

        public string Value { get; private set; } = "";

        public TextInputDialog(string title, string prompt, string initialValue = "")
        {
            InitializeComponent();

            Title = title;
            TitleBarElement.Title = title;
            PromptText.Text = prompt;
            ValueBox.Text = initialValue;

            Loaded += (_, _) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        private void Accept()
        {
            Value = ValueBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(Value))
                return;

            Confirmed = true;
            Close();
        }

        private void OnOkButtonClicked(object sender, RoutedEventArgs e) => Accept();

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Accept();
        }
    }
}
