using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class GalleryPickerDialog : WpfUiWindow
    {
        private readonly string _kind;

        private bool _busy;

        public bool Confirmed { get; private set; }

        public string Code { get; private set; } = "";

        public string ChosenName { get; private set; } = "";

        public GalleryPickerDialog(string title, string prompt, string kind)
        {
            InitializeComponent();

            _kind = kind;
            Title = title;
            TitleBarElement.Title = title;
            PromptText.Text = prompt;

            Loaded += async (_, _) =>
            {
                SearchBox.Focus();
                await LoadAsync();
            };
        }

        private async Task LoadAsync()
        {
            if (_busy)
                return;

            _busy = true;
            EmptyText.Text = "Loading...";
            EmptyText.Visibility = Visibility.Visible;
            ItemsList.ItemsSource = null;

            var items = await PhasmaAccount.GalleryAsync(_kind, SearchBox.Text ?? "");

            ItemsList.ItemsSource = items;
            EmptyText.Text = (SearchBox.Text ?? "").Trim().Length > 0
                ? "Nothing matches that."
                : "Nothing published yet.";
            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (items.Count > 0)
                ItemsList.SelectedIndex = 0;

            _busy = false;
        }

        private async void Accept()
        {
            if (_busy || ItemsList.SelectedItem is not GalleryItem chosen)
                return;

            _busy = true;
            AddButton.IsEnabled = false;

            string? code = await PhasmaAccount.GalleryTakeAsync(chosen.Id);

            _busy = false;
            AddButton.IsEnabled = true;

            if (string.IsNullOrEmpty(code))
            {
                Frontend.ShowMessageBox("That one could not be fetched. It may have been removed.", MessageBoxImage.Warning);
                await LoadAsync();
                return;
            }

            Code = code;
            ChosenName = chosen.Name;
            Confirmed = true;
            Close();
        }

        private void OnAddClicked(object sender, RoutedEventArgs e) => Accept();

        private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await LoadAsync();
        }
    }
}
