using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // Pick one or more flags out of a list (the FastFlag editor's "Turn off one of your flags")
    public partial class FlagPickerDialog : WpfUiWindow
    {
        public sealed record Item(string Name, string Value);

        private readonly List<Item> _all;

        public List<string> Picked { get; private set; } = new();

        public FlagPickerDialog(string title, string prompt, IEnumerable<(string Name, string Value)> flags)
        {
            InitializeComponent();

            Title = title;
            TitleBarElement.Title = title;
            PromptText.Text = prompt;

            _all = flags.Select(f => new Item(f.Name, f.Value)).ToList();
            FlagList.ItemsSource = _all;
            UpdateCount();

            Loaded += (_, _) => SearchBox.Focus();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = SearchBox.Text.Trim();
            HashSet<Item> selected = FlagList.SelectedItems.OfType<Item>().ToHashSet();

            FlagList.ItemsSource = text.Length == 0 ? _all : _all.Where(i => i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (Item item in FlagList.Items.OfType<Item>().Where(selected.Contains))
                FlagList.SelectedItems.Add(item);
        }

        private void FlagList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCount();

        private void UpdateCount()
        {
            int count = FlagList.SelectedItems.Count;
            CountText.Text = count == 0 ? $"{_all.Count} flag(s)" : $"{count} picked";
            OkButton.IsEnabled = count > 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Picked = FlagList.SelectedItems.OfType<Item>().Select(i => i.Name).ToList();
            DialogResult = true;
        }
    }
}
