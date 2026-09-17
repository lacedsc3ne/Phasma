using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.Models;
using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Edits the flags inside a saved FastFlag preset (Fast Flag Editor > Presets). Works on a
    /// copy; the preset file is only rewritten on Save.
    /// </summary>
    public partial class PresetEditorDialog : WpfUiWindow
    {
        private readonly FastFlagSnapshot _snapshot;
        private readonly ObservableCollection<FastFlag> _flags = new();

        public bool Saved { get; private set; }

        public PresetEditorDialog(FastFlagSnapshot snapshot)
        {
            _snapshot = snapshot;

            InitializeComponent();

            HeaderText.Text = snapshot.Name;
            RootTitleBar.Title = $"Edit preset - {snapshot.Name}";

            foreach (var kv in snapshot.Flags.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                _flags.Add(new FastFlag { Name = kv.Key, Value = kv.Value?.ToString() ?? "" });

            FlagsGrid.ItemsSource = _flags;
            UpdateStatus();
        }

        private void UpdateStatus() => StatusText.Text = $"{_flags.Count} flag(s)";

        private void FlagsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteButton.IsEnabled = FlagsGrid.SelectedItems.Count > 0;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NewNameBox.Text.Trim();
            string value = NewValueBox.Text.Trim();

            if (name.Length == 0)
            {
                StatusText.Text = "Enter a flag name first.";
                return;
            }

            FastFlag? existing = _flags.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.Value = value;
                FlagsGrid.Items.Refresh();
            }
            else
            {
                _flags.Add(new FastFlag { Name = name, Value = value });
            }

            NewNameBox.Text = "";
            NewValueBox.Text = "";
            UpdateStatus();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (FastFlag flag in FlagsGrid.SelectedItems.OfType<FastFlag>().ToList())
                _flags.Remove(flag);

            UpdateStatus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // commit any cell still being edited
            FlagsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var flags = new List<KeyValuePair<string, object>>();
            foreach (FastFlag flag in _flags)
            {
                string name = (flag.Name ?? "").Trim();
                if (name.Length == 0)
                    continue;

                flags.Add(new KeyValuePair<string, object>(name, (flag.Value ?? "").Trim()));
            }

            try
            {
                FastFlagSnapshotManager.Save(_snapshot.Name, flags);
                Saved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PresetEditorDialog::Save", ex);
                StatusText.Text = $"Could not save: {ex.Message}";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
