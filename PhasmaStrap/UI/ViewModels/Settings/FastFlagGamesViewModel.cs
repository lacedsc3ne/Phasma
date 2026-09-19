using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class ProfileOption
    {
        public const string NewId = "__new";

        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
    }

    // a game that has a rule
    public sealed class GameRuleRow : NotifyPropertyChangedViewModel
    {
        private readonly FastFlagGamesViewModel _owner;

        public FlagGameRule Rule { get; }

        public GameRuleRow(FastFlagGamesViewModel owner, FlagGameRule rule)
        {
            _owner = owner;
            Rule = rule;
        }

        private string _title = "";
        public string Title { get => _title; set { _title = value; OnPropertyChanged(nameof(Title)); } }

        private string _subtitle = "";
        public string Subtitle { get => _subtitle; set { _subtitle = value; OnPropertyChanged(nameof(Subtitle)); } }

        private string? _icon;
        public string? IconUrl { get => _icon; set { _icon = string.IsNullOrEmpty(value) ? null : value; OnPropertyChanged(nameof(IconUrl)); } }

        public ObservableCollection<ProfileOption> Profiles => _owner.ProfileOptions;

        public string ProfileId
        {
            get => Rule.ProfileId;
            set
            {
                if (string.IsNullOrEmpty(value) || value == Rule.ProfileId)
                    return;

                Rule.ProfileId = value;
                _owner.RuleChanged();
                OnPropertyChanged(nameof(ProfileId));
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }

        public string ProfileSummary
        {
            get
            {
                FlagProfile? profile = App.FlagProfiles.Find(Rule.ProfileId);
                if (profile is null)
                    return "";
                if (profile.ChangeCount == 0)
                    return "This profile is empty - add flags to it with Edit flags.";

                var parts = new List<string>();
                if (profile.Flags.Count > 0)
                    parts.Add($"{profile.Flags.Count} flag(s) added or changed");
                if (profile.Remove.Count > 0)
                    parts.Add($"{profile.Remove.Count} of yours turned off");
                return string.Join(", ", parts);
            }
        }

        public void Refresh() => OnPropertyChanged(nameof(ProfileSummary));
    }

    // a game that can be picked in "Add a game"
    public sealed class GameChoice
    {
        public long UniverseId { get; init; }
        public long RootPlaceId { get; init; }
        public long LinkedPlaceId { get; init; }
        public string Name { get; init; } = "";
        public string? IconUrl { get; init; }
        public string Tag { get; init; } = "";
    }

    /// <summary>
    /// The FastFlag "Per-game flags" tab: which games get which FastFlag profile. Like the rest
    /// of the settings, changes are kept when the window's Save button is pressed.
    /// </summary>
    public sealed class FastFlagGamesViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<GameRuleRow> Rules { get; } = new();

        public ObservableCollection<ProfileOption> ProfileOptions { get; } = new();

        public ObservableCollection<ProfileOption> AddProfileOptions { get; } = new();

        public ObservableCollection<GameChoice> Results { get; } = new();

        public ObservableCollection<PhasmaStrap.Integrations.UniversePlace> Places { get; } = new();

        // asks the page to show the editor tab with this profile
        public event Action<string>? OpenEditorRequested;

        // place ID -> its game, for rules saved without a name (moved from the old system)
        private static readonly Dictionary<long, GameInfo> _lookedUp = new();

        public FastFlagGamesViewModel()
        {
            Reload();
            ShowRecent();
        }

        public Visibility ManagerDisabledVisibility => App.Settings.Prop.UseFastFlagManager ? Visibility.Collapsed : Visibility.Visible;

        public Visibility NoRulesVisibility => Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility TrackingOffVisibility => App.Settings.Prop.EnableActivityTracking ? Visibility.Collapsed : Visibility.Visible;

        public bool CloseRunningRoblox
        {
            get => App.Settings.Prop.FastFlagPresetCloseRunningRoblox;
            set { App.Settings.Prop.FastFlagPresetCloseRunningRoblox = value; OnPropertyChanged(nameof(CloseRunningRoblox)); }
        }

        // ------------------------------------------------------------------ the list of games

        public void Reload()
        {
            ProfileOptions.Clear();
            AddProfileOptions.Clear();
            AddProfileOptions.Add(new ProfileOption { Id = ProfileOption.NewId, Name = "A new profile for this game" });

            foreach (FlagProfile profile in App.FlagProfiles.Prop.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                ProfileOptions.Add(new ProfileOption { Id = profile.Id, Name = profile.Name });
                AddProfileOptions.Add(new ProfileOption { Id = profile.Id, Name = profile.Name });
            }

            if (!AddProfileOptions.Any(o => o.Id == _addProfileId))
                _addProfileId = ProfileOption.NewId;
            OnPropertyChanged(nameof(AddProfileId));

            Rules.Clear();
            foreach (FlagGameRule rule in App.FlagProfiles.Prop.Rules
                .OrderBy(r => r.GameName.Length > 0 ? r.GameName : "~", StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PlaceId))
            {
                var row = new GameRuleRow(this, rule);
                Describe(row);
                Rules.Add(row);
            }

            OnPropertyChanged(nameof(NoRulesVisibility));
            _ = FillMissingNamesAsync();
        }

        private static void Describe(GameRuleRow row)
        {
            FlagGameRule rule = row.Rule;
            GameInfo? known = rule.PlaceId > 0 ? _lookedUp.GetValueOrDefault(rule.PlaceId) : null;

            string game = rule.GameName.Length > 0 ? rule.GameName : known?.Name ?? (rule.UniverseId > 0 ? $"Game {rule.UniverseId}" : $"Place {rule.PlaceId}");

            row.Title = game;
            row.IconUrl = rule.IconUrl.Length > 0 ? rule.IconUrl : known?.IconUrl;

            if (rule.IsWholeGame)
                row.Subtitle = "The whole game - every place in it";
            else if (rule.PlaceId == rule.RootPlaceId || rule.PlaceId == known?.RootPlaceId)
                row.Subtitle = "Only its start place";
            else
                row.Subtitle = rule.PlaceName.Length > 0 ? $"Only the place \"{rule.PlaceName}\"" : $"Only place {rule.PlaceId}";
        }

        private async Task FillMissingNamesAsync()
        {
            foreach (GameRuleRow row in Rules.ToList())
            {
                FlagGameRule rule = row.Rule;
                if (rule.GameName.Length > 0 || rule.PlaceId <= 0)
                    continue;

                if (!_lookedUp.ContainsKey(rule.PlaceId))
                {
                    GameInfo? game = await GameLookup.FromPlaceAsync(rule.PlaceId);
                    if (game is null)
                        continue;

                    _lookedUp[rule.PlaceId] = game;
                }

                Describe(row);
            }
        }

        public void RuleChanged()
        {
            App.FlagProfiles.NotifyEdited();
        }

        public ICommand PreviewCommand => new RelayCommand<GameRuleRow>(row =>
        {
            if (row is null)
                return;

            var dialog = new Elements.Dialogs.FlagPreviewDialog(row.Title, App.FlagProfiles.Find(row.Rule.ProfileId))
            {
                Owner = Application.Current.Windows.OfType<Elements.Settings.MainWindow>().FirstOrDefault()
            };
            dialog.ShowDialog();
        });

        public ICommand EditProfileCommand => new RelayCommand<GameRuleRow>(row =>
        {
            if (row is not null)
                OpenEditorRequested?.Invoke(row.Rule.ProfileId);
        });

        public ICommand RemoveCommand => new RelayCommand<GameRuleRow>(row =>
        {
            if (row is null)
                return;

            App.FlagProfiles.Prop.Rules.Remove(row.Rule);
            Rules.Remove(row);
            OnPropertyChanged(nameof(NoRulesVisibility));
            App.FlagProfiles.NotifyEdited();

            Status = $"{row.Title} goes back to just your flags. Press Save to keep this.";
        });

        // ------------------------------------------------------------------ add a game

        private string _searchText = "";
        public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(nameof(SearchText)); } }

        private string _resultsHeader = "";
        public string ResultsHeader { get => _resultsHeader; private set { _resultsHeader = value; OnPropertyChanged(nameof(ResultsHeader)); } }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusVisibility)); } }

        public Visibility StatusVisibility => _status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private bool _busy;
        public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(nameof(IsBusy)); } }

        private CancellationTokenSource? _searchCts;

        private void ShowRecent()
        {
            Results.Clear();

            IEnumerable<IGrouping<long, PhasmaStrap.Models.PlayTimeEntry>> recent;
            try
            {
                recent = PhasmaStrap.Integrations.PlayTimeStore.GetAll().Where(e => e.UniverseId > 0).GroupBy(e => e.UniverseId).Take(12).ToList();
            }
            catch
            {
                recent = Enumerable.Empty<IGrouping<long, PhasmaStrap.Models.PlayTimeEntry>>();
            }

            foreach (var game in recent)
            {
                PhasmaStrap.Models.PlayTimeEntry latest = game.OrderByDescending(e => e.LastPlayed).First();
                Results.Add(new GameChoice
                {
                    UniverseId = game.Key,
                    RootPlaceId = latest.PlaceId,
                    Name = latest.Name.Length > 0 ? latest.Name : $"Game {game.Key}",
                    IconUrl = string.IsNullOrEmpty(latest.IconUrl) ? null : latest.IconUrl,
                    Tag = "Played " + Ago(latest.LastPlayed),
                });
            }

            ResultsHeader = Results.Count > 0 ? "Recently played - or search above" : "Search for a game above";
        }

        private static string Ago(DateTime when)
        {
            TimeSpan span = DateTime.Now - when;
            if (span.TotalHours < 1) return "just now";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 2) return "yesterday";
            return $"{(int)span.TotalDays} days ago";
        }

        public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);

        private async Task SearchAsync()
        {
            string text = SearchText.Trim();

            if (text.Length == 0)
            {
                ShowRecent();
                return;
            }

            _searchCts?.Cancel();
            var cts = _searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            IsBusy = true;
            ResultsHeader = "Searching...";
            Results.Clear();

            try
            {
                // a link or a place ID names one game exactly
                RobloxLaunchTarget? target = RobloxLinkParser.TryParse(text, out RobloxLaunchTarget parsed) ? parsed
                    : text.Contains('.') ? await RobloxLinkParser.ResolveAsync(text, cts.Token) : null;

                if (target is { Kind: RobloxLinkKind.Place, PlaceId: > 0 })
                {
                    GameInfo? game = await GameLookup.FromPlaceAsync(target.PlaceId, cts.Token);
                    if (cts.IsCancellationRequested)
                        return;

                    if (game is null)
                    {
                        ResultsHeader = $"No game found for place {target.PlaceId}.";
                        return;
                    }

                    var choice = new GameChoice { UniverseId = game.UniverseId, RootPlaceId = game.RootPlaceId, LinkedPlaceId = target.PlaceId, Name = game.Name, IconUrl = NullIfEmpty(game.IconUrl), Tag = $"From the link" };
                    Results.Add(choice);
                    ResultsHeader = "Found";
                    SelectGame(choice);
                    return;
                }

                List<GameInfo> games = await GameLookup.SearchAsync(text, cts.Token);
                if (cts.IsCancellationRequested)
                    return;

                foreach (GameInfo game in games)
                    Results.Add(new GameChoice { UniverseId = game.UniverseId, RootPlaceId = game.RootPlaceId, Name = game.Name, IconUrl = NullIfEmpty(game.IconUrl) });

                ResultsHeader = games.Count == 0 ? "No games found. Try other words, or paste the game's link." : "Pick the game";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ResultsHeader = $"Search failed: {ex.Message}";
            }
            finally
            {
                if (_searchCts == cts)
                    IsBusy = false;
            }
        }

        private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

        private GameChoice? _selected;
        public GameChoice? Selected { get => _selected; private set { _selected = value; OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(SelectedVisibility)); } }

        public Visibility SelectedVisibility => _selected is null ? Visibility.Collapsed : Visibility.Visible;

        public ICommand SelectGameCommand => new RelayCommand<GameChoice>(choice =>
        {
            if (choice is not null)
                SelectGame(choice);
        });

        private void SelectGame(GameChoice choice)
        {
            Selected = choice;
            Places.Clear();
            SelectedPlace = null;

            // a link to one particular place most likely means that place
            WholeGame = choice.LinkedPlaceId <= 0 || choice.LinkedPlaceId == choice.RootPlaceId;
            OnPropertyChanged(nameof(WholeGame));
            OnPropertyChanged(nameof(OnePlace));

            if (!WholeGame)
                _ = LoadPlacesAsync();

            UpdateExisting();
        }

        public bool WholeGame { get; set; } = true;

        public bool OnePlace
        {
            get => !WholeGame;
            set
            {
                WholeGame = !value;
                OnPropertyChanged(nameof(WholeGame));
                OnPropertyChanged(nameof(OnePlace));

                if (value && Places.Count == 0)
                    _ = LoadPlacesAsync();

                UpdateExisting();
            }
        }

        private PhasmaStrap.Integrations.UniversePlace? _selectedPlace;
        public PhasmaStrap.Integrations.UniversePlace? SelectedPlace
        {
            get => _selectedPlace;
            set { _selectedPlace = value; OnPropertyChanged(nameof(SelectedPlace)); UpdateExisting(); }
        }

        private string _placesStatus = "";
        public string PlacesStatus { get => _placesStatus; private set { _placesStatus = value; OnPropertyChanged(nameof(PlacesStatus)); } }

        private async Task LoadPlacesAsync()
        {
            GameChoice? game = Selected;
            if (game is null)
                return;

            PlacesStatus = "Loading the game's places...";

            List<PhasmaStrap.Integrations.UniversePlace> places = await PhasmaStrap.Integrations.UniversePlaces.GetPlacesAsync(game.UniverseId, game.RootPlaceId);

            if (Selected != game)
                return;

            // the API can refuse to list places; the linked/start place is still pickable
            if (places.Count == 0)
            {
                if (game.LinkedPlaceId > 0 && game.LinkedPlaceId != game.RootPlaceId)
                    places.Add(new PhasmaStrap.Integrations.UniversePlace(game.LinkedPlaceId, $"Place {game.LinkedPlaceId}", false));
                if (game.RootPlaceId > 0)
                    places.Insert(0, new PhasmaStrap.Integrations.UniversePlace(game.RootPlaceId, "Start place", true));
            }

            Places.Clear();
            foreach (PhasmaStrap.Integrations.UniversePlace place in places)
                Places.Add(place);

            SelectedPlace = Places.FirstOrDefault(p => p.PlaceId == game.LinkedPlaceId) ?? Places.FirstOrDefault();
            PlacesStatus = Places.Count > 1 ? $"{Places.Count} places" : "";
        }

        private string _addProfileId = ProfileOption.NewId;
        public string AddProfileId
        {
            get => _addProfileId;
            set { _addProfileId = string.IsNullOrEmpty(value) ? ProfileOption.NewId : value; OnPropertyChanged(nameof(AddProfileId)); }
        }

        private string _existingNote = "";
        public string ExistingNote { get => _existingNote; private set { _existingNote = value; OnPropertyChanged(nameof(ExistingNote)); OnPropertyChanged(nameof(ExistingNoteVisibility)); } }

        public Visibility ExistingNoteVisibility => _existingNote.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        private FlagGameRule? ExistingRuleForSelection()
        {
            GameChoice? game = Selected;
            if (game is null)
                return null;

            if (WholeGame)
                return App.FlagProfiles.Prop.Rules.FirstOrDefault(r => r.IsWholeGame && r.UniverseId == game.UniverseId);

            long placeId = SelectedPlace?.PlaceId ?? 0;
            return placeId > 0 ? App.FlagProfiles.Prop.Rules.FirstOrDefault(r => r.PlaceId == placeId) : null;
        }

        private void UpdateExisting()
        {
            FlagGameRule? existing = ExistingRuleForSelection();
            FlagProfile? profile = existing is null ? null : App.FlagProfiles.Find(existing.ProfileId);

            ExistingNote = profile is null ? "" : $"This already uses \"{profile.Name}\" - adding it again switches it to the profile picked below.";
        }

        public ICommand AddCommand => new RelayCommand(Add);

        private void Add()
        {
            GameChoice? game = Selected;
            if (game is null)
                return;

            long placeId = 0;
            string placeName = "";

            if (!WholeGame)
            {
                if (SelectedPlace is null)
                {
                    Status = "Pick the place first, or choose the whole game.";
                    return;
                }

                placeId = SelectedPlace.PlaceId;
                placeName = SelectedPlace.Name;
            }

            FlagProfile? profile;
            bool created = false;

            if (AddProfileId == ProfileOption.NewId)
            {
                profile = new FlagProfile { Name = FlagLayers.UniqueName(App.FlagProfiles.Prop, game.Name) };
                App.FlagProfiles.Prop.Profiles.Add(profile);
                created = true;
            }
            else
            {
                profile = App.FlagProfiles.Find(AddProfileId);
                if (profile is null)
                    return;
            }

            FlagGameRule? rule = ExistingRuleForSelection();
            if (rule is null)
            {
                rule = new FlagGameRule();
                App.FlagProfiles.Prop.Rules.Add(rule);
            }

            rule.UniverseId = game.UniverseId;
            rule.RootPlaceId = game.RootPlaceId;
            rule.PlaceId = placeId;
            rule.PlaceName = placeName;
            rule.GameName = game.Name;
            rule.IconUrl = game.IconUrl ?? "";
            rule.ProfileId = profile.Id;

            if (placeId > 0)
                GameLookup.Remember(placeId, game.UniverseId);
            if (game.RootPlaceId > 0)
                GameLookup.Remember(game.RootPlaceId, game.UniverseId);

            Selected = null;
            SearchText = "";
            ShowRecent();

            App.FlagProfiles.NotifyEdited();
            Reload();

            if (created)
            {
                Status = $"{game.Name} now has its own profile. Add the flags it should get, then press Save.";
                OpenEditorRequested?.Invoke(profile.Id);
            }
            else
            {
                Status = $"{game.Name} now uses \"{profile.Name}\". Press Save to keep this.";
            }
        }

        public ICommand CancelAddCommand => new RelayCommand(() => Selected = null);
    }
}
