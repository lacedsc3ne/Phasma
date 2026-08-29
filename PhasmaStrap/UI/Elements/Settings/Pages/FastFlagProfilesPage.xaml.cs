using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.UI.Elements.Dialogs;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagProfilesPage.xaml
    /// </summary>
    public partial class FastFlagProfilesPage
    {
        // same reasoning as FastFlagEditorPage - a DataGrid (plus a ListBox driving which dictionary it's bound to)
        // is a codebehind-only affair, mvvm buys nothing here

        private sealed class PlaceAssignment
        {
            public string PlaceId { get; init; } = "";
            public string ProfileName { get; init; } = "";
            public string Display => $"{PlaceId} → {ProfileName}";
        }

        private readonly ObservableCollection<string> _profileNames = new();
        private readonly ObservableCollection<FastFlag> _profileFlags = new();
        private readonly ObservableCollection<PlaceAssignment> _placeAssignments = new();

        private string? _selectedProfileName;

        public FastFlagProfilesPage()
        {
            InitializeComponent();

            ProfilesListBox.ItemsSource = _profileNames;
            FlagsDataGrid.ItemsSource = _profileFlags;
            AssignmentProfileComboBox.ItemsSource = _profileNames;
            AssignmentsItemsControl.ItemsSource = _placeAssignments;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e) => ReloadAll();

        private void ReloadAll()
        {
            ReloadProfileNames();
            ReloadAssignments();
            ReloadSelectedProfileFlags();
        }

        private void ReloadProfileNames()
        {
            string? selected = _selectedProfileName;

            _profileNames.Clear();

            foreach (string name in App.Settings.Prop.FastFlagProfiles.Keys.OrderBy(x => x))
                _profileNames.Add(name);

            if (selected is not null && _profileNames.Contains(selected))
            {
                ProfilesListBox.SelectedItem = selected;
            }
            else
            {
                _selectedProfileName = null;
                ProfilesListBox.SelectedItem = null;
            }

            UpdateProfileButtonStates();
        }

        private void ReloadAssignments()
        {
            _placeAssignments.Clear();

            foreach (var pair in App.Settings.Prop.FastFlagPlaceProfiles.OrderBy(x => x.Key))
                _placeAssignments.Add(new PlaceAssignment { PlaceId = pair.Key, ProfileName = pair.Value });
        }

        private void ReloadSelectedProfileFlags()
        {
            _profileFlags.Clear();

            if (_selectedProfileName is null || !App.Settings.Prop.FastFlagProfiles.TryGetValue(_selectedProfileName, out var flags))
            {
                EditingProfileTextBlock.Text = Strings.Menu_FastFlagProfiles_NoProfileSelectedHint;
                FlagsDataGrid.IsEnabled = false;
                AddFlagButton.IsEnabled = false;
                DeleteFlagsButton.IsEnabled = false;
                return;
            }

            EditingProfileTextBlock.Text = string.Format(Strings.Menu_FastFlagProfiles_EditingProfile, _selectedProfileName);
            FlagsDataGrid.IsEnabled = true;
            AddFlagButton.IsEnabled = true;

            foreach (var pair in flags.OrderBy(x => x.Key))
            {
                _profileFlags.Add(new FastFlag
                {
                    Name = pair.Key,
                    Value = pair.Value?.ToString() ?? ""
                });
            }

            DeleteFlagsButton.IsEnabled = FlagsDataGrid.SelectedItems.Count > 0;
        }

        private void UpdateProfileButtonStates()
        {
            bool hasSelection = _selectedProfileName is not null;
            RenameProfileButton.IsEnabled = hasSelection && NewProfileNameTextBox.Text.Trim().Length > 0;
            DeleteProfileButton.IsEnabled = hasSelection;
        }

        #region Profiles

        private void NewProfileNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateProfileButtonStates();

        private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProfileName = ProfilesListBox.SelectedItem as string;
            UpdateProfileButtonStates();
            ReloadSelectedProfileFlags();
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NewProfileNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_ProfileNameEmpty, MessageBoxImage.Error);
                return;
            }

            if (App.Settings.Prop.FastFlagProfiles.ContainsKey(name))
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_ProfileNameDuplicate, MessageBoxImage.Error);
                return;
            }

            App.Settings.Prop.FastFlagProfiles[name] = new Dictionary<string, object>();
            NewProfileNameTextBox.Text = "";

            _selectedProfileName = name;
            ReloadProfileNames();
            ReloadSelectedProfileFlags();
        }

        private void RenameProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileName is null)
                return;

            string newName = NewProfileNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_ProfileNameEmpty, MessageBoxImage.Error);
                return;
            }

            if (newName == _selectedProfileName)
                return;

            if (App.Settings.Prop.FastFlagProfiles.ContainsKey(newName))
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_ProfileNameDuplicate, MessageBoxImage.Error);
                return;
            }

            var profiles = App.Settings.Prop.FastFlagProfiles;
            var flags = profiles[_selectedProfileName];
            profiles.Remove(_selectedProfileName);
            profiles[newName] = flags;

            // repoint any place assignments that used the old name
            var placeProfiles = App.Settings.Prop.FastFlagPlaceProfiles;
            foreach (string placeId in placeProfiles.Where(x => x.Value == _selectedProfileName).Select(x => x.Key).ToList())
                placeProfiles[placeId] = newName;

            NewProfileNameTextBox.Text = "";
            _selectedProfileName = newName;

            ReloadProfileNames();
            ReloadAssignments();
            ReloadSelectedProfileFlags();
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileName is null)
                return;

            var result = Frontend.ShowMessageBox(
                string.Format(Strings.Menu_FastFlagProfiles_DeleteProfileConfirmMessage, _selectedProfileName),
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo
            );

            if (result != MessageBoxResult.Yes)
                return;

            App.Settings.Prop.FastFlagProfiles.Remove(_selectedProfileName);

            // nothing should point at a profile that no longer exists
            var placeProfiles = App.Settings.Prop.FastFlagPlaceProfiles;
            foreach (string placeId in placeProfiles.Where(x => x.Value == _selectedProfileName).Select(x => x.Key).ToList())
                placeProfiles.Remove(placeId);

            _selectedProfileName = null;

            ReloadProfileNames();
            ReloadAssignments();
            ReloadSelectedProfileFlags();
        }

        #endregion

        #region Flag overrides

        private void ShowAddFlagDialog()
        {
            var dialog = new AddFastFlagDialog();
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            if (dialog.Tabs.SelectedIndex == 0)
                AddSingleFlag(dialog.FlagNameTextBox.Text.Trim(), dialog.FlagValueTextBox.Text);
            else if (dialog.Tabs.SelectedIndex == 1)
                ImportFlagsJson(dialog.JsonTextBox.Text);
        }

        private void AddSingleFlag(string name, string value)
        {
            if (_selectedProfileName is null)
                return;

            if (string.IsNullOrEmpty(name))
            {
                ShowAddFlagDialog();
                return;
            }

            var flags = App.Settings.Prop.FastFlagProfiles[_selectedProfileName];

            flags[name] = value;

            ReloadSelectedProfileFlags();
        }

        private void ImportFlagsJson(string json)
        {
            if (_selectedProfileName is null)
                return;

            Dictionary<string, object>? list;

            json = json.Trim();

            if (!json.StartsWith('{'))
                json = '{' + json;

            if (!json.EndsWith('}'))
            {
                int lastIndex = json.LastIndexOf('}');

                if (lastIndex == -1)
                    json += '}';
                else
                    json = json.Substring(0, lastIndex + 1);
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                list = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);

                if (list is null)
                    throw new Exception("JSON deserialization returned null");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(
                    string.Format(Strings.Menu_FastFlagEditor_InvalidJSON, ex.Message),
                    MessageBoxImage.Error
                );

                ShowAddFlagDialog();

                return;
            }

            var flags = App.Settings.Prop.FastFlagProfiles[_selectedProfileName];

            foreach (var pair in list)
            {
                if (pair.Value is null)
                    continue;

                if (string.IsNullOrEmpty(pair.Key))
                    continue;

                flags[pair.Key] = pair.Value.ToString()!;
            }

            ReloadSelectedProfileFlags();
        }

        private void AddFlagButton_Click(object sender, RoutedEventArgs e) => ShowAddFlagDialog();

        private void DeleteFlagsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileName is null)
                return;

            var flags = App.Settings.Prop.FastFlagProfiles[_selectedProfileName];
            var toRemove = new List<FastFlag>();

            foreach (FastFlag entry in FlagsDataGrid.SelectedItems)
                toRemove.Add(entry);

            foreach (FastFlag entry in toRemove)
            {
                flags.Remove(entry.Name);
                _profileFlags.Remove(entry);
            }

            DeleteFlagsButton.IsEnabled = FlagsDataGrid.SelectedItems.Count > 0;
        }

        private void FlagsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteFlagsButton.IsEnabled = FlagsDataGrid.SelectedItems.Count > 0;
        }

        private void FlagsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_selectedProfileName is null)
                return;

            if (e.Row.DataContext is not FastFlag entry)
                return;

            if (e.EditingElement is not TextBox textbox)
                return;

            var flags = App.Settings.Prop.FastFlagProfiles[_selectedProfileName];

            switch (e.Column.Header)
            {
                case "Name":
                    string oldName = entry.Name;
                    string newName = textbox.Text.Trim();

                    if (newName == oldName)
                        return;

                    if (string.IsNullOrEmpty(newName) || flags.ContainsKey(newName))
                    {
                        e.Cancel = true;
                        textbox.Text = oldName;
                        return;
                    }

                    flags.Remove(oldName);
                    flags[newName] = entry.Value;
                    entry.Name = newName;

                    break;

                case "Value":
                    flags[entry.Name] = textbox.Text;
                    break;
            }
        }

        #endregion

        #region Place assignments

        private void AddAssignmentButton_Click(object sender, RoutedEventArgs e)
        {
            string placeId = PlaceIdTextBox.Text.Trim();
            string? profileName = AssignmentProfileComboBox.SelectedItem as string;

            if (!long.TryParse(placeId, out long parsed) || parsed <= 0)
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_InvalidPlaceId, MessageBoxImage.Error);
                return;
            }

            if (profileName is null)
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_NoProfilesAvailable, MessageBoxImage.Error);
                return;
            }

            App.Settings.Prop.FastFlagPlaceProfiles[placeId] = profileName;
            PlaceIdTextBox.Text = "";

            ReloadAssignments();
        }

        private void RemoveAssignmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not PlaceAssignment assignment)
                return;

            App.Settings.Prop.FastFlagPlaceProfiles.Remove(assignment.PlaceId);
            ReloadAssignments();
        }

        #endregion
    }
}
