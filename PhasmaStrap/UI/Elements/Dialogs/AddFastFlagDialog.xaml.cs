using Microsoft.Win32;
using System.Windows;
using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Interaction logic for AddFastFlagDialog.xaml
    /// </summary>
    public partial class AddFastFlagDialog
    {
        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public AddFastFlagDialog()
        {
            InitializeComponent();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                // the combined entry comes first so it's the DEFAULT filter (index 1) - otherwise
                // Windows' file picker only shows *.json until the user manually switches the dropdown,
                // which is exactly the friction a .txt-sharing FastFlag config runs into
                Filter = $"{Strings.FileTypes_FastFlagFiles}|*.json;*.txt|{Strings.FileTypes_JSONFiles}|*.json|{Strings.FileTypes_TextFiles}|*.txt"
            };

            if (dialog.ShowDialog() != true)
                return;

            JsonTextBox.Text = File.ReadAllText(dialog.FileName);
        }

        private void PresetValuesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FFlagPresetsDialog { Owner = this };

            dialog.ShowDialog();

            if (dialog.Result == MessageBoxResult.OK && !string.IsNullOrEmpty(dialog.SelectedValue))
                FlagValueTextBox.Text = dialog.SelectedValue;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }
    }
}
