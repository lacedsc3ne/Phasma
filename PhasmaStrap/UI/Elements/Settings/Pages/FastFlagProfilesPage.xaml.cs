using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.Models;
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

        // same three options/order as FastFlagsViewModel.EngineScopeModeOptions, kept in sync manually
        // since this page is codebehind-only and has no viewmodel to share the constant with
        public static readonly string[] ScopeModeOptions =
        {
            "Apply everywhere",
            "Only apply to listed games",
            "Apply everywhere except listed games",
        };

        private readonly ObservableCollection<string> _profileNames = new();
        private readonly ObservableCollection<FastFlag> _profileFlags = new();
        private readonly ObservableCollection<string> _scopePlaces = new();

        private string? _selectedProfileName;

        public FastFlagProfilesPage()
        {
            InitializeComponent();

            ProfilesListBox.ItemsSource = _profileNames;
            FlagsDataGrid.ItemsSource = _profileFlags;
            ScopePlacesItemsControl.ItemsSource = _scopePlaces;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e) => ReloadAll();

        private void ReloadAll()
        {
            ReloadProfileNames();
            ReloadSelectedProfileFlags();
            ReloadScopeSection();
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

        private void ReloadSelectedProfileFlags()
        {
            _profileFlags.Clear();

            if (_selectedProfileName is null || !App.Settings.Prop.FastFlagProfiles.TryGetValue(_selectedProfileName, out var flags))
            {
                EditingProfileTextBlock.Text = Strings.Menu_FastFlagProfiles_NoProfileSelectedHint;
                AddFlagButton.IsEnabled = false;
                DeleteFlagsButton.IsEnabled = false;
                return;
            }

            EditingProfileTextBlock.Text = string.Format(Strings.Menu_FastFlagProfiles_EditingProfile, _selectedProfileName);
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

        private FastFlagProfileScope? GetSelectedScope()
        {
            if (_selectedProfileName is null)
                return null;

            var scopes = App.Settings.Prop.FastFlagProfileScopes;

            if (!scopes.TryGetValue(_selectedProfileName, out FastFlagProfileScope? scope))
            {
                scope = new FastFlagProfileScope();
                scopes[_selectedProfileName] = scope;
            }

            return scope;
        }

        private void ReloadScopeSection()
        {
            _scopePlaces.Clear();

            bool hasSelection = _selectedProfileName is not null;
            ScopeModeComboBox.IsEnabled = hasSelection;
            ScopePlaceIdTextBox.IsEnabled = hasSelection;
            AddScopePlaceButton.IsEnabled = hasSelection;

            if (!hasSelection)
            {
                ScopeDescriptionTextBlock.Text = Strings.Menu_FastFlagProfiles_NoProfileSelectedHint;
                ScopeModeComboBox.SelectedIndex = -1;
                return;
            }

            App.Settings.Prop.FastFlagProfileScopes.TryGetValue(_selectedProfileName!, out FastFlagProfileScope? scope);
            scope ??= new FastFlagProfileScope();

            ScopeDescriptionTextBlock.Text = string.Format(Strings.Menu_FastFlagProfiles_ScopeDescription_Selected, _selectedProfileName);

            ScopeModeComboBox.SelectionChanged -= ScopeModeComboBox_SelectionChanged;
            ScopeModeComboBox.SelectedIndex = scope.Mode switch
            {
                EngineSettingsScopeMode.OnlyListedPlaces => 1,
                EngineSettingsScopeMode.AllExceptListedPlaces => 2,
                _ => 0,
            };
            ScopeModeComboBox.SelectionChanged += ScopeModeComboBox_SelectionChanged;

            foreach (string placeId in scope.Places)
                _scopePlaces.Add(placeId);
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
            ReloadScopeSection();
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
            ReloadScopeSection();
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

            // repoint this profile's own scope entry to the new name
            var scopes = App.Settings.Prop.FastFlagProfileScopes;
            if (scopes.TryGetValue(_selectedProfileName, out FastFlagProfileScope? scope))
            {
                scopes.Remove(_selectedProfileName);
                scopes[newName] = scope;
            }

            NewProfileNameTextBox.Text = "";
            _selectedProfileName = newName;

            ReloadProfileNames();
            ReloadSelectedProfileFlags();
            ReloadScopeSection();
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

            // nothing should keep a scope for a profile that no longer exists
            App.Settings.Prop.FastFlagProfileScopes.Remove(_selectedProfileName);

            _selectedProfileName = null;

            ReloadProfileNames();
            ReloadSelectedProfileFlags();
            ReloadScopeSection();
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

        #region Per-profile scope

        private void ScopeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FastFlagProfileScope? scope = GetSelectedScope();
            if (scope is null)
                return;

            scope.Mode = ScopeModeComboBox.SelectedIndex switch
            {
                1 => EngineSettingsScopeMode.OnlyListedPlaces,
                2 => EngineSettingsScopeMode.AllExceptListedPlaces,
                _ => EngineSettingsScopeMode.All,
            };
        }

        private void AddScopePlaceButton_Click(object sender, RoutedEventArgs e)
        {
            FastFlagProfileScope? scope = GetSelectedScope();
            if (scope is null)
                return;

            string id = ScopePlaceIdTextBox.Text.Trim();

            if (!long.TryParse(id, out _) || id.Length == 0)
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagProfiles_InvalidPlaceId, MessageBoxImage.Error);
                return;
            }

            if (scope.Places.Contains(id))
                return;

            scope.Places.Add(id);
            _scopePlaces.Add(id);
            ScopePlaceIdTextBox.Text = "";
        }

        private void RemoveScopePlaceButton_Click(object sender, RoutedEventArgs e)
        {
            FastFlagProfileScope? scope = GetSelectedScope();
            if (scope is null)
                return;

            if (sender is not FrameworkElement element || element.Tag is not string placeId)
                return;

            scope.Places.Remove(placeId);
            _scopePlaces.Remove(placeId);
        }

        #endregion
    }
}
