using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;

using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    // One row of the flag list
    public sealed class FlagRow : INotifyPropertyChanged
    {
        private string _name = "";
        private string _value = "";
        private string _note = "";
        private string _tone = "";

        public string Name { get => _name; set { _name = value; Changed(nameof(Name)); } }
        public string Value { get => _value; set { _value = value; Changed(nameof(Value)); } }
        public string Note { get => _note; set { _note = value; Changed(nameof(Note)); } }

        // "", "Problem", "Added", "Changed", "Off" - colours the note
        public string Tone { get => _tone; set { _tone = value; Changed(nameof(Tone)); } }

        // a flag the profile turns off (it has no value of its own)
        public bool IsTurnedOff { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Tells the editor which profile to show next time it loads (the Per-game flags tab's
    // "Edit profile" button)
    public static class FlagEditorRequest
    {
        public static string? ProfileToOpen { get; set; }
    }

    /// <summary>
    /// The FastFlag editor. Edits either "your flags" (App.FastFlags, every game) or one FastFlag
    /// profile (App.FlagProfiles, only the games it is given to). Every change waits for the
    /// settings window's Save button, like the rest of the settings.
    /// </summary>
    public partial class FastFlagEditorPage
    {
        // a datagrid is a code-behind thing, so this page is too

        private sealed class ScopeItem
        {
            public string? ProfileId { get; init; }
            public string Display { get; init; } = "";
        }

        private readonly ObservableCollection<FlagRow> _rows = new();

        private bool _showQuickFlags = false;
        private string _searchFilter = "";
        private bool _refreshingScopes;

        // null = your flags
        private string? _profileId;

        private FlagProfile? Profile => App.FlagProfiles.Find(_profileId);

        private bool EditingProfile => Profile is not null;

        // flags set by the Quick settings toggles; hidden from "your flags" unless asked for
        private static readonly HashSet<string> QuickFlagNames = new(FastFlagManager.PresetFlags.Values, StringComparer.Ordinal);

        // space the flag list always gets, even when everything above it is tall
        private const double MinimumListHeight = 320;

        public FastFlagEditorPage()
        {
            InitializeComponent();
            DataGrid.ItemsSource = _rows;
        }

        // ------------------------------------------------------------------ layout

        private void PageScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutHeight();

        private void RootLayout_LayoutUpdated(object? sender, EventArgs e) => UpdateLayoutHeight();

        // Fill the visible area when there is room (the list takes the rest); when the sections
        // above are taller than that, grow past it so the page scrolls instead of clipping.
        private void UpdateLayoutHeight()
        {
            if (PageScroller is null || RootLayout is null || RootLayout.RowDefinitions.Count == 0)
                return;

            double above = 0;
            for (int i = 0; i < RootLayout.RowDefinitions.Count - 1; i++)
                above += RootLayout.RowDefinitions[i].ActualHeight;

            double available = PageScroller.ViewportHeight - RootLayout.Margin.Top - RootLayout.Margin.Bottom;
            double wanted = Math.Max(available, above + MinimumListHeight);

            if (wanted <= 0 || double.IsNaN(wanted) || double.IsInfinity(wanted))
                return;

            // LayoutUpdated fires constantly; only touch Height when it really changes, or this loops
            if (double.IsNaN(RootLayout.Height) || Math.Abs(RootLayout.Height - wanted) > 0.5)
                RootLayout.Height = wanted;
        }

        // ------------------------------------------------------------------ page lifetime

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (FlagEditorRequest.ProfileToOpen is string requested)
            {
                FlagEditorRequest.ProfileToOpen = null;
                if (App.FlagProfiles.Find(requested) is not null)
                    _profileId = requested;
            }

            App.FlagProfiles.Edited -= OnProfilesEdited;
            App.FlagProfiles.Edited += OnProfilesEdited;

            ManagerDisabledBar.Visibility = App.Settings.Prop.UseFastFlagManager ? Visibility.Collapsed : Visibility.Visible;

            RefreshScopes();
            ReloadList();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => App.FlagProfiles.Edited -= OnProfilesEdited;

        // the Per-game flags tab changed something (a new profile, a rule) - keep up
        private void OnProfilesEdited(object? sender, EventArgs e)
        {
            if (!EditingProfile)
                _profileId = null;

            RefreshScopes();
            ReloadList();
        }

        private void MarkProfilesEdited()
        {
            App.FlagProfiles.Edited -= OnProfilesEdited;
            App.FlagProfiles.NotifyEdited();
            App.FlagProfiles.Edited += OnProfilesEdited;
        }

        // ------------------------------------------------------------------ scope (what is being edited)

        private void RefreshScopes()
        {
            _refreshingScopes = true;

            var items = new List<ScopeItem> { new() { ProfileId = null, Display = "Your flags - every game" } };
            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                items.Add(new ScopeItem { ProfileId = profile.Id, Display = $"Profile: {profile.Name}" });

            ScopeBox.ItemsSource = items;
            ScopeBox.SelectedItem = items.FirstOrDefault(i => i.ProfileId == _profileId) ?? items[0];

            _refreshingScopes = false;

            UpdateScopeUi();
        }

        private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingScopes || ScopeBox.SelectedItem is not ScopeItem item)
                return;

            _profileId = item.ProfileId;
            UpdateScopeUi();
            ClearSearch(false);
            ReloadList();
        }

        private void UpdateScopeUi()
        {
            FlagProfile? profile = Profile;

            TurnOffButton.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
            TogglePresetsButton.Visibility = profile is null ? Visibility.Visible : Visibility.Collapsed;

            if (profile is null)
            {
                ScopeText.Text = "These flags apply to every game you launch through PhasmaStrap. A game with a profile gets these plus the profile's changes.";
                UsedByPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ScopeText.Text = $"\"{profile.Name}\" only applies to the games it is given to. Those games start with your flags, plus the flags here. Flags marked \"turned off\" are taken away from your flags for those games.";

            List<string> games = App.FlagProfiles.RulesUsing(profile.Id).Select(DescribeRule).ToList();
            UsedByText.Text = games.Count == 0
                ? "No game uses this profile yet."
                : "Used by: " + string.Join(", ", games);
            UsedByPanel.Visibility = Visibility.Visible;
        }

        public static string DescribeRule(FlagGameRule rule)
        {
            string game = rule.GameName.Length > 0 ? rule.GameName : rule.UniverseId > 0 ? $"game {rule.UniverseId}" : "";

            if (rule.IsWholeGame)
                return game;

            string place = rule.PlaceName.Length > 0 ? rule.PlaceName : $"place {rule.PlaceId}";
            return game.Length > 0 && !string.Equals(game, place, StringComparison.OrdinalIgnoreCase) ? $"{game} ({place} only)" : $"{place} only";
        }

        private void GoToGames_Click(object sender, RoutedEventArgs e) => FastFlagSettingsPage.SelectTab(this, "FastFlagGamesPage");

        // ------------------------------------------------------------------ profile actions

        private Window? Owner => Window.GetWindow(this);

        private string? AskName(string title, string prompt, string initial)
        {
            var dialog = new TextInputDialog(title, prompt, initial) { Owner = Owner };
            dialog.ShowDialog();
            return dialog.Confirmed ? dialog.Value : null;
        }

        private FlagProfile? CreateProfile(string? suggestedName = null)
        {
            string? name = AskName("New profile", "Name the profile (for example the game it is for, or what it does):", suggestedName ?? "");
            if (name is null)
                return null;

            var profile = new FlagProfile { Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, name) };
            App.FlagProfiles.Prop.Profiles.Add(profile);
            MarkProfilesEdited();
            return profile;
        }

        private void SwitchTo(string? profileId)
        {
            _profileId = profileId;
            RefreshScopes();
            ClearSearch(false);
            ReloadList();
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = CreateProfile();
            if (profile is not null)
                SwitchTo(profile.Id);
        }

        private void ProfileMenu_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = ProfileMenuButton, Placement = PlacementMode.Bottom };

            menu.Items.Add(MenuItem("Rename", profile is not null, () => RenameProfile(profile!)));
            menu.Items.Add(MenuItem("Duplicate", profile is not null, () => DuplicateProfile(profile!)));
            menu.Items.Add(MenuItem("Copy share code", profile is not null, () => CopyShareCode(profile!)));
            menu.Items.Add(MenuItem("Add a profile from a share code", true, PasteShareCode));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Delete this profile", profile is not null, () => DeleteProfile(profile!)));

            menu.IsOpen = true;
        }

        private static MenuItem MenuItem(string header, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        private void RenameProfile(FlagProfile profile)
        {
            string? name = AskName("Rename profile", "New name:", profile.Name);
            if (name is null)
                return;

            profile.Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, name, profile.Id);
            MarkProfilesEdited();
            RefreshScopes();
        }

        private void DuplicateProfile(FlagProfile profile)
        {
            FlagProfile copy = profile.Copy();
            copy.Id = FlagProfile.NewId();
            copy.Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, profile.Name + " copy");

            App.FlagProfiles.Prop.Profiles.Add(copy);
            MarkProfilesEdited();
            SwitchTo(copy.Id);
        }

        private void CopyShareCode(FlagProfile profile)
        {
            if (profile.ChangeCount == 0)
            {
                Frontend.ShowMessageBox("This profile is empty, so there is nothing to share yet.", MessageBoxImage.Information);
                return;
            }

            ClipboardShare.CopyText(FlagLayers.ToShareCode(profile));
            Frontend.ShowMessageBox($"The share code for \"{profile.Name}\" is on your clipboard. Anyone can add it with Profile options > Add a profile from a share code.\n\nIt holds the flags only - not which games use it.", MessageBoxImage.Information);
        }

        private void PasteShareCode()
        {
            string clipboard = "";
            try { clipboard = Clipboard.GetText(); } catch { }

            string? code = AskName("Add a profile from a share code", "Paste the share code (it starts with PHF1-):", clipboard.Trim().StartsWith("PHF1-", StringComparison.OrdinalIgnoreCase) ? clipboard.Trim() : "");
            if (code is null)
                return;

            FlagProfile? profile = FlagLayers.FromShareCode(code);
            if (profile is null)
            {
                Frontend.ShowMessageBox("That isn't a working profile share code. Check that the whole code was copied.", MessageBoxImage.Warning);
                return;
            }

            List<string> bad = profile.Flags.Where(f => FlagValidation.Problem(f.Key, f.Value) is not null).Select(f => f.Key).ToList();
            foreach (string key in bad)
                profile.Flags.Remove(key);

            profile.Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, profile.Name.Length > 0 ? profile.Name : "Shared profile");
            App.FlagProfiles.Prop.Profiles.Add(profile);
            MarkProfilesEdited();
            SwitchTo(profile.Id);

            string message = $"Added \"{profile.Name}\" with {profile.Flags.Count} flag(s)" + (profile.Remove.Count > 0 ? $" and {profile.Remove.Count} turned off" : "") + ". Give it to a game on the Per-game flags tab, then press Save.";
            if (bad.Count > 0)
                message += $"\n\n{bad.Count} flag(s) in the code were not valid and were left out: {string.Join(", ", bad.Take(10))}";

            Frontend.ShowMessageBox(message, MessageBoxImage.Information);
        }

        private void DeleteProfile(FlagProfile profile)
        {
            int games = App.FlagProfiles.RulesUsing(profile.Id).Count();
            string question = games == 0
                ? $"Delete the profile \"{profile.Name}\"?"
                : $"Delete the profile \"{profile.Name}\"? {games} game(s) use it and will go back to just your flags.";

            if (Frontend.ShowMessageBox(question, MessageBoxImage.Question, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            App.FlagProfiles.Prop.Profiles.Remove(profile);
            App.FlagProfiles.Prop.Rules.RemoveAll(r => r.ProfileId == profile.Id);
            MarkProfilesEdited();
            SwitchTo(null);
        }

        // ------------------------------------------------------------------ the list

        private void ReloadList()
        {
            string? selectedName = (DataGrid.SelectedItem as FlagRow)?.Name;

            _rows.Clear();

            FlagProfile? profile = Profile;

            if (profile is null)
            {
                foreach (var (name, raw) in App.FastFlags.Prop.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    bool quick = QuickFlagNames.Contains(name);
                    if ((quick && !_showQuickFlags) || !MatchesSearch(name))
                        continue;

                    string value = raw?.ToString() ?? "";
                    string? problem = FlagValidation.Problem(name, value);

                    _rows.Add(new FlagRow
                    {
                        Name = name,
                        Value = value,
                        Note = problem ?? (quick ? "Set by Quick settings" : ""),
                        Tone = problem is null ? "" : "Problem",
                    });
                }
            }
            else
            {
                foreach (var (name, value) in profile.Flags.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!MatchesSearch(name))
                        continue;

                    _rows.Add(ProfileRow(name, value));
                }

                foreach (string name in profile.Remove.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (!MatchesSearch(name) || profile.Flags.ContainsKey(name))
                        continue;

                    string? yours = App.FastFlags.GetValue(name);
                    _rows.Add(new FlagRow
                    {
                        Name = name,
                        Value = "",
                        IsTurnedOff = true,
                        Note = yours is null ? "Turned off (it isn't in your flags right now)" : $"Turned off for these games (yours is {yours})",
                        Tone = "Off",
                    });
                }
            }

            UpdateEmptyText(profile);

            if (selectedName is null)
                return;

            FlagRow? again = _rows.FirstOrDefault(r => r.Name == selectedName);
            if (again is not null)
            {
                DataGrid.SelectedItem = again;
                DataGrid.ScrollIntoView(again);
            }
        }

        private void UpdateEmptyText(FlagProfile? profile)
        {
            string text = "";

            if (_rows.Count == 0)
            {
                if (_searchFilter.Length > 0)
                    text = "No flags match your search.";
                else if (profile is not null)
                    text = "This profile is empty. Add flags with Add new or Search Database, or pick flags of yours this game shouldn't get with Turn off one of your flags. You can also right-click flags under \"Your flags\" to copy them here.";
                else if (App.FastFlags.Prop.Count > 0 && !_showQuickFlags)
                    text = "Only flags from the Quick settings toggles so far - Show Quick settings flags lists them. Add your own with Add new or Search Database.";
                else
                    text = "No flags yet. Add one with Add new or Search Database.";
            }

            EmptyText.Text = text;
            EmptyText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static FlagRow ProfileRow(string name, string value)
        {
            string? problem = FlagValidation.Problem(name, value);
            string? yours = App.FastFlags.GetValue(name);

            return new FlagRow
            {
                Name = name,
                Value = value,
                Note = problem ?? (yours is null ? "Added for these games" : yours == value ? "Same as your flags" : $"Changes your value ({yours})"),
                Tone = problem is not null ? "Problem" : yours is null ? "Added" : "Changed",
            };
        }

        private bool MatchesSearch(string name) => _searchFilter.Length == 0 || name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);

        private void ClearSearch(bool refresh = true)
        {
            SearchTextBox.Text = "";
            _searchFilter = "";

            if (refresh)
                ReloadList();
        }

        private void Select(string name)
        {
            FlagRow? row = _rows.FirstOrDefault(r => r.Name == name);
            if (row is null)
                return;

            DataGrid.SelectedItem = row;
            DataGrid.ScrollIntoView(row);
        }

        // ------------------------------------------------------------------ changing flags

        private bool ScopeHas(string name)
        {
            FlagProfile? profile = Profile;
            return profile is null ? App.FastFlags.GetValue(name) is not null : profile.Flags.ContainsKey(name);
        }

        private void SetInScope(string name, string value)
        {
            FlagProfile? profile = Profile;

            if (profile is null)
            {
                App.FastFlags.SetValue(name, value);
                return;
            }

            profile.Flags[name] = value;
            profile.Remove.Remove(name);
            MarkProfilesEdited();
        }

        private void RemoveFromScope(string name)
        {
            FlagProfile? profile = Profile;

            if (profile is null)
            {
                App.FastFlags.SetValue(name, null);
                return;
            }

            profile.Flags.Remove(name);
            profile.Remove.Remove(name);
            MarkProfilesEdited();
        }

        private void ShowAddDialog()
        {
            var dialog = new AddFastFlagDialog { Owner = Owner };
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            if (dialog.Tabs.SelectedIndex == 0)
                AddSingle(dialog.FlagNameTextBox.Text.Trim(), dialog.FlagValueTextBox.Text.Trim());
            else if (dialog.Tabs.SelectedIndex == 1)
                ImportJSON(dialog.JsonTextBox.Text);
        }

        // also the search database's "add" callback
        private void AddSingle(string name, string value)
        {
            if (ScopeHas(name))
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);

                if (!EditingProfile && QuickFlagNames.Contains(name) && !_showQuickFlags)
                {
                    TogglePresetsButton.IsChecked = true;
                    _showQuickFlags = true;
                }

                ClearSearch();
                Select(name);
                return;
            }

            string? problem = FlagValidation.Problem(name, value);
            if (problem is not null)
            {
                Frontend.ShowMessageBox($"{name}\n\n{problem}", MessageBoxImage.Warning);
                return;
            }

            SetInScope(name, value);

            if (!MatchesSearch(name))
                ClearSearch(false);

            ReloadList();
            Select(name);
            UpdateScopeUi();
        }

        private void ImportJSON(string json)
        {
            Dictionary<string, object>? list;

            json = json.Trim();

            // autocorrect where possible
            if (!json.StartsWith('{'))
                json = '{' + json;

            if (!json.EndsWith('}'))
            {
                int lastIndex = json.LastIndexOf('}');
                json = lastIndex == -1 ? json + '}' : json[..(lastIndex + 1)];
            }

            try
            {
                var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                list = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options) ?? throw new Exception("JSON deserialization returned null");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(string.Format(Strings.Menu_FastFlagEditor_InvalidJSON, ex.Message), MessageBoxImage.Error);
                ShowAddDialog();
                return;
            }

            if (list.Count > 16)
            {
                if (Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_LargeConfig, MessageBoxImage.Warning, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
            }

            List<string> conflicting = list.Keys.Where(ScopeHas).ToList();
            bool overwrite = false;

            if (conflicting.Count > 0)
            {
                string message = string.Format(Strings.Menu_FastFlagEditor_ConflictingImport, conflicting.Count, string.Join(", ", conflicting.Take(25)));
                if (conflicting.Count > 25)
                    message += "...";

                overwrite = Frontend.ShowMessageBox(message, MessageBoxImage.Question, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            }

            var skipped = new List<string>();
            int added = 0;

            foreach (var (key, raw) in list)
            {
                string? value = raw?.ToString();
                if (value is null)
                    continue;

                if (ScopeHas(key) && !overwrite)
                    continue;

                if (FlagValidation.Problem(key, value) is string problem)
                {
                    skipped.Add($"{key}: {problem}");
                    continue;
                }

                SetInScope(key, value);
                added++;
            }

            ClearSearch();
            UpdateScopeUi();

            if (skipped.Count > 0)
            {
                Frontend.ShowMessageBox(
                    $"Added {added} flag(s). {skipped.Count} were left out because they are not valid:\n\n" + string.Join("\n", skipped.Take(15)) + (skipped.Count > 15 ? "\n..." : ""),
                    MessageBoxImage.Warning);
            }
        }

        private void DataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            // a turned-off flag has no value; typing one turns it into a changed value instead
            if (e.Row.DataContext is FlagRow { IsTurnedOff: true } row && e.Column.DisplayIndex == 1)
                row.Value = App.FastFlags.GetValue(row.Name) ?? "";
        }

        private void DataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Row.DataContext is not FlagRow row || e.EditingElement is not TextBox textbox)
                return;

            string text = textbox.Text.Trim();

            if (e.Column.DisplayIndex == 0)
            {
                string oldName = row.Name;
                if (text == oldName)
                    return;

                string value = row.IsTurnedOff ? "" : row.Value;

                if (ScopeHas(text))
                {
                    Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);
                    e.Cancel = true;
                    textbox.Text = oldName;
                    return;
                }

                if (FlagValidation.NameProblem(text) is string nameProblem)
                {
                    Frontend.ShowMessageBox(nameProblem, MessageBoxImage.Warning);
                    e.Cancel = true;
                    textbox.Text = oldName;
                    return;
                }

                FlagProfile? profile = Profile;
                if (row.IsTurnedOff && profile is not null)
                {
                    profile.Remove.Remove(oldName);
                    if (!profile.Remove.Contains(text))
                        profile.Remove.Add(text);
                    MarkProfilesEdited();
                }
                else
                {
                    RemoveFromScope(oldName);
                    SetInScope(text, value);
                }

                row.Name = text;
            }
            else if (e.Column.DisplayIndex == 1)
            {
                if (FlagValidation.Problem(row.Name, text) is string problem)
                {
                    Frontend.ShowMessageBox($"{row.Name}\n\n{problem}", MessageBoxImage.Warning);
                    e.Cancel = true;
                    textbox.Text = row.IsTurnedOff ? "" : row.Value;
                    return;
                }

                SetInScope(row.Name, text);
                row.Value = text;
                row.IsTurnedOff = false;
            }

            // notes depend on the new name/value - refresh once the edit has been committed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ReloadList();
                UpdateScopeUi();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => ShowAddDialog();

        private void SearchDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FFlagSearchDialog(AddSingle) { Owner = Owner };
            dialog.ShowDialog();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (FlagRow row in DataGrid.SelectedItems.OfType<FlagRow>().ToList())
            {
                _rows.Remove(row);
                RemoveFromScope(row.Name);
            }

            UpdateScopeUi();
            UpdateEmptyText(Profile);
        }

        private void TurnOffButton_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            if (profile is null)
                return;

            var candidates = App.FastFlags.Prop
                .Where(f => !profile.Remove.Contains(f.Key) && !profile.Flags.ContainsKey(f.Key))
                .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                .Select(f => (f.Key, f.Value?.ToString() ?? ""))
                .ToList();

            if (candidates.Count == 0)
            {
                Frontend.ShowMessageBox("There is nothing left to turn off - every one of your flags is already changed or turned off in this profile.", MessageBoxImage.Information);
                return;
            }

            var dialog = new FlagPickerDialog("Turn off flags for this profile",
                $"Pick flags from your own list that games using \"{profile.Name}\" should NOT get. Ctrl+click picks several.",
                candidates)
            { Owner = Owner };

            if (dialog.ShowDialog() != true || dialog.Picked.Count == 0)
                return;

            foreach (string name in dialog.Picked)
            {
                if (!profile.Remove.Contains(name))
                    profile.Remove.Add(name);
            }

            MarkProfilesEdited();
            ClearSearch();
            UpdateScopeUi();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;

            _showQuickFlags = button.IsChecked ?? false;
            ReloadList();
        }

        // ------------------------------------------------------------------ right-click menu

        private void DataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            List<FlagRow> rows = DataGrid.SelectedItems.OfType<FlagRow>().ToList();
            if (rows.Count == 0 || DataGrid.ContextMenu is not System.Windows.Controls.ContextMenu menu)
            {
                e.Handled = true;
                return;
            }

            menu.Items.Clear();

            FlagProfile? current = Profile;
            List<FlagProfile> others = App.FlagProfiles.Prop.Profiles.Where(p => p.Id != current?.Id).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            string what = rows.Count == 1 ? "this flag" : $"these {rows.Count} flags";

            if (current is null)
            {
                menu.Items.Add(ProfileSubmenu($"Copy {what} to a profile", others, profile => CopyRows(rows, profile, move: false)));
                menu.Items.Add(ProfileSubmenu($"Move {what} to a profile", others, profile => CopyRows(rows, profile, move: true)));
                menu.Items.Add(ProfileSubmenu($"Turn {what} off in a profile", others, profile => TurnOffIn(rows, profile)));
            }
            else
            {
                List<FlagRow> valued = rows.Where(r => !r.IsTurnedOff).ToList();
                menu.Items.Add(MenuItem($"Copy {what} to your flags", valued.Count > 0, () => CopyRows(valued, null, move: false)));
                menu.Items.Add(MenuItem($"Move {what} to your flags", valued.Count > 0, () => CopyRows(valued, null, move: true)));
                menu.Items.Add(ProfileSubmenu($"Copy {what} to another profile", others, profile => CopyRows(rows, profile, move: false)));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Copy as JSON", true, () =>
            {
                var dictionary = rows.Where(r => !r.IsTurnedOff).ToDictionary(r => r.Name, r => (object)r.Value);
                ClipboardShare.CopyText(JsonSerializer.Serialize(dictionary, new JsonSerializerOptions { WriteIndented = true }));
            }));
        }

        private MenuItem ProfileSubmenu(string header, List<FlagProfile> profiles, Action<FlagProfile> action)
        {
            var parent = new MenuItem { Header = header };

            foreach (FlagProfile profile in profiles)
            {
                FlagProfile target = profile;
                parent.Items.Add(MenuItem(target.Name, true, () => action(target)));
            }

            if (profiles.Count > 0)
                parent.Items.Add(new Separator());

            parent.Items.Add(MenuItem("New profile...", true, () =>
            {
                FlagProfile? created = CreateProfile();
                if (created is not null)
                    action(created);
            }));

            return parent;
        }

        // copies (or moves) rows from the current scope into a profile, or into your flags (null)
        private void CopyRows(List<FlagRow> rows, FlagProfile? target, bool move)
        {
            FlagProfile? source = Profile;

            foreach (FlagRow row in rows)
            {
                if (target is null)
                {
                    if (!row.IsTurnedOff)
                        App.FastFlags.SetValue(row.Name, row.Value);
                }
                else if (row.IsTurnedOff)
                {
                    if (!target.Remove.Contains(row.Name))
                        target.Remove.Add(row.Name);
                }
                else
                {
                    target.Flags[row.Name] = row.Value;
                    target.Remove.Remove(row.Name);
                }

                if (move)
                {
                    if (source is null)
                        App.FastFlags.SetValue(row.Name, null);
                    else
                    {
                        source.Flags.Remove(row.Name);
                        source.Remove.Remove(row.Name);
                    }
                }
            }

            MarkProfilesEdited();
            ReloadList();
            UpdateScopeUi();

            string where = target is null ? "your flags" : $"\"{target.Name}\"";
            string verb = move ? "Moved" : "Copied";
            string hint = target is not null && !App.FlagProfiles.RulesUsing(target.Id).Any() ? " No game uses that profile yet - give it one on the Per-game flags tab." : "";
            ShowStatus($"{verb} {rows.Count} flag(s) to {where}.{hint}");
        }

        private void TurnOffIn(List<FlagRow> rows, FlagProfile target)
        {
            foreach (FlagRow row in rows)
            {
                if (!target.Remove.Contains(row.Name))
                    target.Remove.Add(row.Name);
                target.Flags.Remove(row.Name);
            }

            MarkProfilesEdited();
            ShowStatus($"Games using \"{target.Name}\" won't get {(rows.Count == 1 ? rows[0].Name : $"these {rows.Count} flags")}.");
        }

        private void ShowStatus(string message) => Frontend.ShowMessageBox(message, MessageBoxImage.Information);

        // ------------------------------------------------------------------ export

        private static readonly Regex _groupPrefixRegex = new("^[A-Z]+[a-z]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private void ExportJSONButton_Click(object sender, RoutedEventArgs e)
        {
            FlagProfile? profile = Profile;
            Dictionary<string, object> flags = profile is null
                ? App.FastFlags.Prop
                : profile.Flags.ToDictionary(f => f.Key, f => (object)f.Value);

            if (flags.Count == 0)
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_NoFlagsToCopy, MessageBoxImage.Information);
                return;
            }

            var dialog = new CopyFlagsDialog(flags.Count) { Owner = Owner };
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            var options = new JsonSerializerOptions { WriteIndented = true };

            string payload = dialog.SelectedFormat switch
            {
                CopyFlagsFormat.GroupedJson => BuildGroupedJson(flags),
                CopyFlagsFormat.Base64 => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(flags, options))),
                _ => JsonSerializer.Serialize(flags, options)
            };

            Clipboard.SetDataObject(payload);

            string message = Strings.Menu_FastFlagEditor_JsonCopiedToClipboard;
            if (profile is not null && profile.Remove.Count > 0)
                message += $"\n\nThe {profile.Remove.Count} flag(s) this profile turns off can't be written as JSON. Use Profile options > Copy share code to share the whole profile.";

            Frontend.ShowMessageBox(message, MessageBoxImage.Information);
        }

        private static string BuildGroupedJson(Dictionary<string, object> prop)
        {
            var groups = prop
                .GroupBy(kvp =>
                {
                    var match = _groupPrefixRegex.Match(kvp.Key);
                    return match.Success ? match.Value : "Other";
                })
                .OrderBy(g => g.Key)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("{");

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                sb.Append("  // ").AppendLine(groups[groupIndex].Key);

                var entries = groups[groupIndex].OrderBy(kvp => kvp.Key).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    bool last = i == entries.Count - 1 && groupIndex == groups.Count - 1;
                    sb.Append("  \"").Append(entries[i].Key).Append("\": ").Append(JsonSerializer.Serialize(entries[i].Value));
                    sb.AppendLine(last ? "" : ",");
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ search

        private readonly System.Windows.Threading.DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private bool _searchDebounceHooked;

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textbox)
                return;

            _searchFilter = textbox.Text;

            if (!_searchDebounceHooked)
            {
                _searchDebounce.Tick += (_, _) =>
                {
                    _searchDebounce.Stop();
                    ReloadList();
                };
                _searchDebounceHooked = true;
            }

            _searchDebounce.Stop();
            _searchDebounce.Start();
        }
    }
}
