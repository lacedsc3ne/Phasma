using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class GBSEditorPage
    {
        private readonly GBSEditorViewModel _viewModel = new();

        public GBSEditorPage()
        {
            InitializeComponent();
            DataContext = _viewModel;
            DataGrid.ItemsSource = _viewModel.Entries;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e) => _viewModel.Load();

        private void Page_Unloaded(object sender, RoutedEventArgs e) => _viewModel.FlushPending();

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NewNameTextBox.Text.Trim();
            string type = NewTypeTextBox.Text.Trim();
            string value = NewValueTextBox.Text;

            if (string.IsNullOrWhiteSpace(name))
            {
                Frontend.ShowMessageBox(Strings.Menu_GBSEditor_InvalidName, MessageBoxImage.Information);
                return;
            }

            if (_viewModel.NameExists(name))
            {
                Frontend.ShowMessageBox(Strings.Menu_GBSEditor_AlreadyExists, MessageBoxImage.Information);
                return;
            }

            if (!_viewModel.Add(name, string.IsNullOrWhiteSpace(type) ? "token" : type, value))
            {
                Frontend.ShowMessageBox(Strings.Menu_GBSEditor_AlreadyExists, MessageBoxImage.Information);
                return;
            }

            NewNameTextBox.Text = "";
            NewTypeTextBox.Text = "token";
            NewValueTextBox.Text = "";
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = new List<GBSEntry>();

            foreach (GBSEntry entry in DataGrid.SelectedItems)
                selected.Add(entry);

            foreach (GBSEntry entry in selected)
                _viewModel.Remove(entry);
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.DataContext is not GBSEntry entry)
                return;

            if (e.EditingElement is not TextBox textbox)
                return;

            if (e.Column.Header is not string header)
                return;

            if (header == Strings.Common_Name)
            {
                string newName = textbox.Text.Trim();

                if (string.IsNullOrWhiteSpace(newName))
                {
                    e.Cancel = true;
                    return;
                }

                if (!string.Equals(newName, entry.Name, StringComparison.Ordinal) && _viewModel.NameExists(newName))
                {
                    Frontend.ShowMessageBox(Strings.Menu_GBSEditor_AlreadyExists, MessageBoxImage.Information);
                    e.Cancel = true;
                    textbox.Text = entry.Name;
                    return;
                }
            }

            Dispatcher.BeginInvoke(new Action(() => _viewModel.Persist()));
        }
    }
}
