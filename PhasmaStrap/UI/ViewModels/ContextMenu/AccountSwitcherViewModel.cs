using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.ContextMenu;

namespace PhasmaStrap.UI.ViewModels.ContextMenu
{
    /// <summary>
    /// One account saved in the switcher's library. <see cref="DatFile"/> names the backup file
    /// (under <see cref="Paths.AccountBackups"/>) holding a full copy of that account's
    /// RobloxCookies.dat, exactly as Roblox's own client wrote it (see RobloxCookie.cs's security
    /// note for why copying it verbatim is already secure).
    /// </summary>
    public sealed class SwitcherAccount : INotifyPropertyChanged
    {
        private string _note = "";
        private DateTime _lastUsedUtc;
        private string? _avatarUrl;
        private bool _isCurrent;

        public long UserId { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DatFile { get; set; } = "";
        public DateTime AddedUtc { get; set; }

        public string Note
        {
            get => _note;
            set { if (_note != (value ?? "")) { _note = value ?? ""; Raise(nameof(Note)); NoteChanged?.Invoke(this); } }
        }

        public DateTime LastUsedUtc
        {
            get => _lastUsedUtc;
            set { _lastUsedUtc = value; Raise(nameof(LastUsedUtc)); Raise(nameof(LastUsedDisplay)); }
        }

        [JsonIgnore]
        public string? AvatarUrl
        {
            get => _avatarUrl;
            set { string? v = string.IsNullOrEmpty(value) ? null : value; if (_avatarUrl != v) { _avatarUrl = v; Raise(nameof(AvatarUrl)); } }
        }

        [JsonIgnore]
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; Raise(nameof(IsCurrent)); Raise(nameof(CurrentBadgeVisibility)); } }
        }

        public event Action<SwitcherAccount>? NoteChanged;

        [JsonIgnore]
        public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

        [JsonIgnore]
        public string Subtitle => $"@{Username}  ·  ID {UserId}";

        [JsonIgnore]
        public string LastUsedDisplay => LastUsedUtc == default ? "Never switched to" : $"Last used {LastUsedUtc.ToLocalTime():g}";

        [JsonIgnore]
        public Visibility CurrentBadgeVisibility => _isCurrent ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Backs the Roblox account switcher (Integrations &gt; Roblox &gt; Account Switcher &gt; View).
    ///
    /// How this actually works end to end:
    ///  - Roblox's own client keeps the logged-in session in one file, RobloxCookies.dat, whose
    ///    cookie payload is DPAPI-protected under the current Windows account
    ///    (see RobloxCookie.LiveCookiesDatPath / RobloxCookie.cs's header comment).
    ///  - "Add this account" verifies you're actually signed in (calls the authenticated
    ///    users.roblox.com API through RobloxCookie.GetAccountAsync) then copies that live dat file
    ///    byte-for-byte into Paths.AccountBackups as this account's saved login - no re-encryption
    ///    needed, since the copy is already DPAPI-protected exactly like the original.
    ///  - "Switch" does the reverse: copies a saved backup back over the live dat file (after backing
    ///    up whatever was live, in case anything goes wrong). Both directions require Roblox to be
    ///    fully closed, since it holds the file open while running.
    ///  - There is deliberately no hook anywhere in Bootstrapper.cs/LaunchHandler.cs: once a switch
    ///    completes, the live dat file already holds the selected account's cookie, so the very next
    ///    time you press Play (through the normal launch path), Roblox reads that file itself and
    ///    signs in as whichever account is currently live. The shared file on disk *is* the
    ///    integration point.
    ///  - "Import by cookie" is the one path that doesn't start from a real Roblox login on this PC -
    ///    you paste a .ROBLOSECURITY value you already own (e.g. from another machine). It's verified
    ///    the same way (authenticated API call) and then spliced into a synthesized dat file using an
    ///    existing dat as a template (RobloxCookie.SynthesizeDatWithCookie), since building the dat's
    ///    container format from scratch isn't something we can safely reproduce.
    /// </summary>
    public sealed class AccountSwitcherViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private sealed class StoredAccount
        {
            public long UserId { get; set; }
            public string Username { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Note { get; set; } = "";
            public string DatFile { get; set; } = "";
            public DateTime AddedUtc { get; set; }
            public DateTime LastUsedUtc { get; set; }
        }

        private const string LOG_IDENT = "AccountSwitcher";

        private readonly string _folder = Paths.AccountBackups;
        private readonly string _metaPath = Path.Combine(Paths.AccountBackups, "accounts.json");
        private readonly string _liveCookiePath = RobloxCookie.LiveCookiesDatPath;

        private readonly SemaphoreSlim _opLock = new(1, 1);
        private bool _disposed;
        private bool _busy;

        private string _status = "Ready";
        private string _searchText = "";
        private string _newCookieText = "";
        private bool _importVisible;

        private string _currentTitle = "Not signed in";
        private string _currentSubtitle = "Sign into Roblox to add this account";
        private string? _currentAvatarUrl;
        private bool _isLoggedIn;
        private long _currentUserId;
        private bool _currentInLibrary;

        public ObservableCollection<SwitcherAccount> Accounts { get; } = new();
        public ObservableCollection<SwitcherAccount> FilteredAccounts { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand AddCurrentCommand { get; }
        public ICommand SwitchCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ToggleImportCommand { get; }
        public ICommand ImportCookieCommand { get; }
        public ICommand CopyUserIdCommand { get; }

        public AccountSwitcherViewModel()
        {
            Directory.CreateDirectory(_folder);

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            AddCurrentCommand = new AsyncRelayCommand(AddCurrentAsync);
            SwitchCommand = new AsyncRelayCommand<SwitcherAccount?>(SwitchAsync);
            DeleteCommand = new RelayCommand<SwitcherAccount?>(Delete);
            LogoutCommand = new AsyncRelayCommand(LogoutAsync);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            ToggleImportCommand = new RelayCommand(() => ImportVisible = !ImportVisible);
            ImportCookieCommand = new AsyncRelayCommand(ImportByCookieAsync);
            CopyUserIdCommand = new RelayCommand<SwitcherAccount?>(CopyUserId);

            _ = RefreshAsync();
        }

        public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public string SearchText
        {
            get => _searchText;
            set { if (_searchText != (value ?? "")) { _searchText = value ?? ""; OnPropertyChanged(nameof(SearchText)); ApplyFilter(); } }
        }

        public string NewCookieText { get => _newCookieText; set { _newCookieText = value ?? ""; OnPropertyChanged(nameof(NewCookieText)); } }

        public bool ImportVisible
        {
            get => _importVisible;
            set { if (_importVisible != value) { _importVisible = value; OnPropertyChanged(nameof(ImportVisible)); OnPropertyChanged(nameof(ImportVisibility)); } }
        }

        public Visibility ImportVisibility => _importVisible ? Visibility.Visible : Visibility.Collapsed;

        public string CurrentTitle { get => _currentTitle; private set { _currentTitle = value; OnPropertyChanged(nameof(CurrentTitle)); } }
        public string CurrentSubtitle { get => _currentSubtitle; private set { _currentSubtitle = value; OnPropertyChanged(nameof(CurrentSubtitle)); } }
        public string? CurrentAvatarUrl { get => _currentAvatarUrl; private set { _currentAvatarUrl = value; OnPropertyChanged(nameof(CurrentAvatarUrl)); } }

        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            private set { _isLoggedIn = value; OnPropertyChanged(nameof(IsLoggedIn)); OnPropertyChanged(nameof(LoggedInVisibility)); OnPropertyChanged(nameof(AddCurrentEnabled)); }
        }

        public Visibility LoggedInVisibility => _isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        public bool AddCurrentEnabled => _isLoggedIn && !_currentInLibrary && !_busy;

        public string EmptyStateTitle => Accounts.Count == 0 ? "No saved accounts yet" : "No accounts match your search";
        public string EmptyStateSubtitle => Accounts.Count == 0
            ? "Sign into Roblox, then click \"Add this account\" to save it here for one-click switching."
            : "Try a different name, note, or user ID.";
        public Visibility EmptyStateVisibility => FilteredAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private async Task RefreshAsync()
        {
            try
            {
                Status = "Loading accounts...";
                LoadMeta();
                ApplyFilter();

                var current = await RobloxCookie.GetAccountAsync().ConfigureAwait(true);

                if (current != null)
                {
                    _currentUserId = current.UserId;
                    IsLoggedIn = true;
                    CurrentTitle = string.IsNullOrWhiteSpace(current.DisplayName) ? current.Username : current.DisplayName;
                    CurrentSubtitle = $"@{current.Username}  ·  ID {current.UserId}";
                    _currentInLibrary = Accounts.Any(a => a.UserId == current.UserId);
                    OnPropertyChanged(nameof(AddCurrentEnabled));
                }
                else
                {
                    _currentUserId = 0;
                    IsLoggedIn = false;
                    CurrentTitle = "Not signed in";
                    CurrentSubtitle = "Sign into Roblox to add this account";
                    _currentInLibrary = false;
                }

                foreach (SwitcherAccount a in Accounts)
                    a.IsCurrent = a.UserId != 0 && a.UserId == _currentUserId;

                await FetchAvatarsSafeAsync().ConfigureAwait(true);

                Status = Accounts.Count == 0 ? "No saved accounts yet." : $"{Accounts.Count} account(s) saved.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                App.Logger.WriteLine(LOG_IDENT, $"RefreshAsync failed: {ex.Message}");
            }
        }

        private async Task FetchAvatarsAsync()
        {
            var ids = new List<long>();

            if (_currentUserId != 0)
                ids.Add(_currentUserId);

            foreach (SwitcherAccount a in Accounts)
                if (a.UserId != 0 && !ids.Contains(a.UserId))
                    ids.Add(a.UserId);

            if (ids.Count == 0)
                return;

            var map = new Dictionary<long, string>();

            for (int i = 0; i < ids.Count; i += 100)
            {
                List<long> chunk = ids.GetRange(i, Math.Min(100, ids.Count - i));
                string url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={string.Join(",", chunk)}&size=150x150&format=Png&isCircular=false";

                using JsonDocument doc = JsonDocument.Parse(await App.HttpClient.GetStringAsync(url).ConfigureAwait(true));

                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement el in data.EnumerateArray())
                    {
                        long id = el.TryGetProperty("targetId", out var t) && t.TryGetInt64(out var l) ? l : 0;
                        string img = el.TryGetProperty("imageUrl", out var iu) ? (iu.GetString() ?? "") : "";

                        if (id != 0 && !string.IsNullOrEmpty(img))
                            map[id] = img;
                    }
                }
            }

            if (_currentUserId != 0 && map.TryGetValue(_currentUserId, out string? curImg))
                CurrentAvatarUrl = curImg;

            foreach (SwitcherAccount a in Accounts)
                if (map.TryGetValue(a.UserId, out string? img))
                    a.AvatarUrl = img;
        }

        private async Task FetchAvatarsSafeAsync()
        {
            if (_disposed)
                return;

            try
            {
                await FetchAvatarsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Avatar fetch failed: {ex.Message}");
            }
        }

        private async Task AddCurrentAsync()
        {
            if (_busy)
                return;

            if (IsRobloxRunning())
            {
                Frontend.ShowMessageBox("Close all Roblox windows before adding the current account.", MessageBoxImage.Warning);
                return;
            }

            _busy = true;
            OnPropertyChanged(nameof(AddCurrentEnabled));
            await _opLock.WaitAsync().ConfigureAwait(true);

            try
            {
                string? cookie = RobloxCookie.Get();

                if (string.IsNullOrEmpty(cookie))
                {
                    Frontend.ShowMessageBox("You are not signed into Roblox on this PC.", MessageBoxImage.Warning);
                    return;
                }

                var account = await RobloxCookie.GetAccountAsync(cookie).ConfigureAwait(true);

                if (account == null)
                {
                    Frontend.ShowMessageBox("Could not verify the current Roblox account. The cookie may be expired.", MessageBoxImage.Warning);
                    return;
                }

                if (!File.Exists(_liveCookiePath))
                {
                    Frontend.ShowMessageBox("RobloxCookies.dat was not found, so this account cannot be saved.", MessageBoxImage.Warning);
                    return;
                }

                SwitcherAccount? existing = Accounts.FirstOrDefault(a => a.UserId == account.UserId);
                string datName = existing?.DatFile ?? $"acc_{account.UserId}_{Guid.NewGuid():N}"[..8] + ".dat";
                string datPath = Path.Combine(_folder, datName);

                await CopyWithRetryAsync(_liveCookiePath, datPath, overwrite: true).ConfigureAwait(true);

                if (existing != null)
                {
                    existing.Username = account.Username;
                    existing.DisplayName = account.DisplayName;
                    existing.LastUsedUtc = DateTime.UtcNow;
                    Status = $"Updated saved account: {account.Username}";
                }
                else
                {
                    SwitcherAccount item = CreateAccount(account.UserId, account.Username, account.DisplayName, "", datName, DateTime.UtcNow, DateTime.UtcNow);
                    Accounts.Insert(0, item);
                    Status = $"Added account: {account.Username}";
                }

                _currentInLibrary = true;
                SaveMeta();
                ApplyFilter();

                foreach (SwitcherAccount a in Accounts)
                    a.IsCurrent = a.UserId == account.UserId;

                await FetchAvatarsSafeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Status = $"Add failed: {ex.Message}";
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                _opLock.Release();
                _busy = false;
                OnPropertyChanged(nameof(AddCurrentEnabled));
            }
        }

        private string ResolveTemplate() => File.Exists(_liveCookiePath)
            ? _liveCookiePath
            : Accounts.Select(a => Path.Combine(_folder, a.DatFile)).FirstOrDefault(File.Exists) ?? "";

        /// <summary>
        /// Verifies one pasted cookie and, if valid, saves it as a library account. Shared by both
        /// the single "Import by cookie" flow and bulk import - trims surrounding whitespace/quotes
        /// too, since a cookie value copied out of a JSON export or some browser extensions often
        /// comes wrapped in quote characters that would otherwise make an otherwise-valid cookie
        /// look "invalid".
        /// </summary>
        private async Task<(bool Success, string? Username, string? Error)> ImportOneCookieAsync(string rawCookie, string template)
        {
            string cookie = rawCookie.Trim().Trim('"', '\'', ' ');

            if (string.IsNullOrEmpty(cookie))
                return (false, null, "Empty");

            var account = await RobloxCookie.GetAccountAsync(cookie).ConfigureAwait(true);

            if (account == null)
                return (false, null, "Invalid or expired cookie");

            SwitcherAccount? existing = Accounts.FirstOrDefault(a => a.UserId == account.UserId);
            string datName = existing?.DatFile ?? $"acc_{account.UserId}_{Guid.NewGuid():N}"[..8] + ".dat";
            string datPath = Path.Combine(_folder, datName);

            if (!RobloxCookie.SynthesizeDatWithCookie(template, cookie, datPath))
                return (false, account.Username, "Could not build a login file from this cookie");

            if (existing != null)
            {
                existing.Username = account.Username;
                existing.DisplayName = account.DisplayName;
            }
            else
            {
                Accounts.Insert(0, CreateAccount(account.UserId, account.Username, account.DisplayName, "", datName, DateTime.UtcNow, default));
            }

            return (true, account.Username, null);
        }

        private async Task ImportByCookieAsync()
        {
            if (_busy)
                return;

            if (string.IsNullOrWhiteSpace(_newCookieText))
            {
                Frontend.ShowMessageBox("Paste a .ROBLOSECURITY cookie value first.", MessageBoxImage.Warning);
                return;
            }

            string template = ResolveTemplate();

            if (string.IsNullOrEmpty(template))
            {
                Frontend.ShowMessageBox("Sign into any Roblox account once (or add the current account) before importing by cookie. PhasmaStrap needs an existing login as a template.", MessageBoxImage.Warning);
                return;
            }

            _busy = true;
            OnPropertyChanged(nameof(AddCurrentEnabled));
            await _opLock.WaitAsync().ConfigureAwait(true);

            try
            {
                Status = "Verifying cookie...";
                (bool success, string? username, string? error) = await ImportOneCookieAsync(_newCookieText, template).ConfigureAwait(true);

                if (!success)
                {
                    Status = error ?? "Import failed.";
                    Frontend.ShowMessageBox(error ?? "Import failed.", MessageBoxImage.Warning);
                    return;
                }

                NewCookieText = "";
                ImportVisible = false;
                SaveMeta();
                ApplyFilter();
                Status = $"Imported account: {username}";

                await FetchAvatarsSafeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Status = $"Import failed: {ex.Message}";
            }
            finally
            {
                _opLock.Release();
                _busy = false;
                OnPropertyChanged(nameof(AddCurrentEnabled));
            }
        }

        // --- bulk import: one cookie per line, each verified/saved the same way as the single
        // import above. Meant for moving a whole library of accounts over at once rather than
        // one-at-a-time. ---

        private string _bulkCookieText = "";

        public string BulkCookieText
        {
            get => _bulkCookieText;
            set { _bulkCookieText = value ?? ""; OnPropertyChanged(nameof(BulkCookieText)); }
        }

        private bool _bulkImportVisible;

        public bool BulkImportVisible
        {
            get => _bulkImportVisible;
            set { if (_bulkImportVisible != value) { _bulkImportVisible = value; OnPropertyChanged(nameof(BulkImportVisible)); OnPropertyChanged(nameof(BulkImportVisibility)); } }
        }

        public Visibility BulkImportVisibility => _bulkImportVisible ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ToggleBulkImportCommand => new RelayCommand(() => BulkImportVisible = !BulkImportVisible);
        public ICommand BulkImportCookiesCommand => new AsyncRelayCommand(BulkImportCookiesAsync);

        private async Task BulkImportCookiesAsync()
        {
            if (_busy)
                return;

            string[] lines = _bulkCookieText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (lines.Length == 0)
            {
                Frontend.ShowMessageBox("Paste at least one .ROBLOSECURITY cookie value, one per line.", MessageBoxImage.Warning);
                return;
            }

            string template = ResolveTemplate();

            if (string.IsNullOrEmpty(template))
            {
                Frontend.ShowMessageBox("Sign into any Roblox account once (or add the current account) before importing by cookie. PhasmaStrap needs an existing login as a template.", MessageBoxImage.Warning);
                return;
            }

            _busy = true;
            OnPropertyChanged(nameof(AddCurrentEnabled));
            await _opLock.WaitAsync().ConfigureAwait(true);

            int succeeded = 0;
            var failures = new List<string>();

            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    Status = $"Importing {i + 1} of {lines.Length}...";

                    (bool success, string? username, string? error) = await ImportOneCookieAsync(lines[i], template).ConfigureAwait(true);

                    if (success)
                        succeeded++;
                    else
                        failures.Add($"Line {i + 1}{(username is not null ? $" ({username})" : "")}: {error}");
                }

                SaveMeta();
                ApplyFilter();
                BulkCookieText = "";
                Status = failures.Count == 0
                    ? $"Imported {succeeded} account(s)."
                    : $"Imported {succeeded} of {lines.Length} - {failures.Count} failed.";

                if (failures.Count > 0)
                    Frontend.ShowMessageBox("Some cookies could not be imported:\n\n" + string.Join("\n", failures), MessageBoxImage.Warning);
                else
                    BulkImportVisible = false;

                await FetchAvatarsSafeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Status = $"Bulk import failed: {ex.Message}";
            }
            finally
            {
                _opLock.Release();
                _busy = false;
                OnPropertyChanged(nameof(AddCurrentEnabled));
            }
        }

        private async Task SwitchAsync(SwitcherAccount? account)
        {
            if (account == null)
                return;

            if (IsRobloxRunning())
            {
                Frontend.ShowMessageBox("Close all Roblox windows before switching accounts.", MessageBoxImage.Warning);
                return;
            }

            string datPath = Path.Combine(_folder, account.DatFile);

            if (!File.Exists(datPath))
            {
                Frontend.ShowMessageBox("The saved login for this account is missing. Remove it and add the account again.", MessageBoxImage.Warning);
                return;
            }

            await _opLock.WaitAsync().ConfigureAwait(true);
            bool backedUp = false;

            try
            {
                Status = $"Switching to {account.Username}...";

                if (File.Exists(_liveCookiePath))
                {
                    string safety = Path.Combine(_folder, "_previous_session.dat");
                    await CopyWithRetryAsync(_liveCookiePath, safety, overwrite: true).ConfigureAwait(true);
                    backedUp = true;
                }

                await ReplaceLiveCookieAsync(datPath).ConfigureAwait(true);

                account.LastUsedUtc = DateTime.UtcNow;
                SaveMeta();
                ApplyFilter();

                foreach (SwitcherAccount a in Accounts)
                    a.IsCurrent = a == account;

                _currentUserId = account.UserId;
                _currentInLibrary = true;
                CurrentTitle = account.Title;
                CurrentSubtitle = account.Subtitle;
                CurrentAvatarUrl = account.AvatarUrl;
                IsLoggedIn = true;
                Status = $"Signed in as {account.Username}. Launch Roblox to play on this account.";

                await FetchAvatarsSafeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                if (backedUp && !File.Exists(_liveCookiePath))
                {
                    try
                    {
                        await ReplaceLiveCookieAsync(Path.Combine(_folder, "_previous_session.dat")).ConfigureAwait(true);
                    }
                    catch
                    {
                        // best-effort rollback only
                    }
                }

                Status = $"Switch failed: {ex.Message}";
                Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                _opLock.Release();
            }
        }

        private void Delete(SwitcherAccount? account)
        {
            if (account == null)
                return;

            if (Frontend.ShowMessageBox($"Remove the saved account {account.Username}?\nThis only deletes PhasmaStrap's saved login, not the Roblox account.", MessageBoxImage.Question, MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                string datPath = Path.Combine(_folder, account.DatFile);

                if (File.Exists(datPath))
                    File.Delete(datPath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Delete dat failed: {ex.Message}");
            }

            account.NoteChanged -= OnNoteChanged;
            Accounts.Remove(account);

            if (_currentInLibrary && account.UserId == _currentUserId)
            {
                _currentInLibrary = false;
                OnPropertyChanged(nameof(AddCurrentEnabled));
            }

            SaveMeta();
            ApplyFilter();
            Status = $"Removed: {account.Username}";
        }

        private async Task LogoutAsync()
        {
            if (IsRobloxRunning())
            {
                Frontend.ShowMessageBox("Close all Roblox windows before signing out.", MessageBoxImage.Warning);
                return;
            }

            await _opLock.WaitAsync().ConfigureAwait(true);

            try
            {
                if (File.Exists(_liveCookiePath))
                {
                    File.Delete(_liveCookiePath);
                    _currentUserId = 0;
                    _currentInLibrary = false;
                    IsLoggedIn = false;
                    CurrentTitle = "Not signed in";
                    CurrentSubtitle = "Sign into Roblox to add this account";
                    CurrentAvatarUrl = null;

                    foreach (SwitcherAccount a in Accounts)
                        a.IsCurrent = false;

                    Status = "Signed out of Roblox.";
                }
                else
                {
                    Status = "No active Roblox login found.";
                }
            }
            catch (Exception ex)
            {
                Status = $"Sign out failed: {ex.Message}";
            }
            finally
            {
                _opLock.Release();
            }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(_folder);
                Process.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Status = $"Failed to open folder: {ex.Message}";
            }
        }

        private void CopyUserId(SwitcherAccount? account)
        {
            if (account == null)
                return;

            try
            {
                Clipboard.SetText(account.UserId.ToString());
                Status = $"Copied user ID {account.UserId}";
            }
            catch (Exception ex)
            {
                Status = $"Could not copy the user ID: {ex.Message}";
            }
        }

        private SwitcherAccount CreateAccount(long userId, string username, string displayName, string note, string datFile, DateTime addedUtc, DateTime lastUsedUtc)
        {
            var item = new SwitcherAccount
            {
                UserId = userId,
                Username = username,
                DisplayName = displayName,
                Note = note,
                DatFile = datFile,
                AddedUtc = addedUtc,
                LastUsedUtc = lastUsedUtc
            };

            item.NoteChanged += OnNoteChanged;
            return item;
        }

        private void OnNoteChanged(SwitcherAccount account) => SaveMeta();

        private void ApplyFilter()
        {
            string q = _searchText.Trim();
            IEnumerable<SwitcherAccount> ordered = Accounts.OrderByDescending(a => a.LastUsedUtc);

            IEnumerable<SwitcherAccount> src = string.IsNullOrEmpty(q)
                ? ordered
                : ordered.Where(a =>
                    a.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    a.Note.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    a.UserId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase));

            FilteredAccounts.Clear();

            foreach (SwitcherAccount a in src)
                FilteredAccounts.Add(a);

            OnPropertyChanged(nameof(EmptyStateVisibility));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateSubtitle));
        }

        private void LoadMeta()
        {
            foreach (SwitcherAccount a in Accounts)
                a.NoteChanged -= OnNoteChanged;

            Accounts.Clear();

            try
            {
                if (!File.Exists(_metaPath))
                    return;

                var list = JsonSerializer.Deserialize<List<StoredAccount>>(File.ReadAllText(_metaPath)) ?? new();

                foreach (StoredAccount s in list.OrderByDescending(s => s.LastUsedUtc))
                {
                    if (string.IsNullOrEmpty(s.DatFile) || !File.Exists(Path.Combine(_folder, s.DatFile)))
                        continue;

                    Accounts.Add(CreateAccount(s.UserId, s.Username, s.DisplayName, s.Note, s.DatFile, s.AddedUtc, s.LastUsedUtc));
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"LoadMeta failed: {ex.Message}");
            }
        }

        private void SaveMeta()
        {
            try
            {
                var list = Accounts.Select(a => new StoredAccount
                {
                    UserId = a.UserId,
                    Username = a.Username,
                    DisplayName = a.DisplayName,
                    Note = a.Note,
                    DatFile = a.DatFile,
                    AddedUtc = a.AddedUtc,
                    LastUsedUtc = a.LastUsedUtc
                }).ToList();

                Directory.CreateDirectory(_folder);
                File.WriteAllText(_metaPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"SaveMeta failed: {ex.Message}");
            }
        }

        private static bool IsRobloxRunning()
        {
            foreach (string name in new[] { App.RobloxPlayerAppName, App.RobloxStudioAppName })
            {
                Process[] processes = Array.Empty<Process>();

                try
                {
                    processes = Process.GetProcessesByName(name);

                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                                return true;
                        }
                        catch
                        {
                            // ignore - process may have exited between enumeration and check
                        }
                    }
                }
                catch
                {
                    // ignore - best-effort check
                }
                finally
                {
                    foreach (Process process in processes)
                        process.Dispose();
                }
            }

            return false;
        }

        private static async Task CopyWithRetryAsync(string src, string dest, bool overwrite = false, int retries = 5)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var d = new FileStream(dest, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await s.CopyToAsync(d).ConfigureAwait(false);
                    return;
                }
                catch (IOException) when (i < retries - 1)
                {
                    await Task.Delay(150).ConfigureAwait(false);
                }
            }

            throw new IOException("Could not access the Roblox cookie file. Make sure Roblox is fully closed.");
        }

        private async Task ReplaceLiveCookieAsync(string source)
        {
            string tmp = _liveCookiePath + ".tmp";

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await CopyWithRetryAsync(source, tmp, overwrite: true).ConfigureAwait(false);

                    if (File.Exists(_liveCookiePath))
                        File.Replace(tmp, _liveCookiePath, null);
                    else
                        File.Move(tmp, _liveCookiePath);

                    return;
                }
                catch (IOException) when (i < 4)
                {
                    await Task.Delay(150).ConfigureAwait(false);
                }
            }

            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch
            {
                // best-effort cleanup only
            }

            throw new IOException("Could not write the Roblox cookie file. Make sure Roblox is fully closed.");
        }

        // --- login with a real embedded browser (BrowserLoginWindow, WebView2) instead of pasting
        // a raw cookie value - the cookie is read straight out of that window's own isolated
        // browser session after a successful login, then saved through the same
        // verify-and-synthesize-a-dat path as the manual cookie import above. ---

        public ICommand LoginWithBrowserCommand => new AsyncRelayCommand(LoginWithBrowserAsync);

        private async Task LoginWithBrowserAsync()
        {
            if (_busy)
                return;

            string template = ResolveTemplate();

            if (string.IsNullOrEmpty(template))
            {
                Frontend.ShowMessageBox("Sign into any Roblox account once (or add the current account) before using this. PhasmaStrap needs an existing login as a template.", MessageBoxImage.Warning);
                return;
            }

            string? cookie = await BrowserLoginWindow.ShowAndWaitForCookieAsync(Application.Current?.MainWindow).ConfigureAwait(true);

            if (string.IsNullOrEmpty(cookie))
                return;

            _busy = true;
            OnPropertyChanged(nameof(AddCurrentEnabled));
            await _opLock.WaitAsync().ConfigureAwait(true);

            try
            {
                Status = "Verifying account...";
                (bool success, string? username, string? error) = await ImportOneCookieAsync(cookie, template).ConfigureAwait(true);

                if (!success)
                {
                    Status = error ?? "Could not add this account.";
                    Frontend.ShowMessageBox(error ?? "Could not add this account.", MessageBoxImage.Warning);
                    return;
                }

                SaveMeta();
                ApplyFilter();
                Status = $"Added account: {username}";

                await FetchAvatarsSafeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Status = $"Login failed: {ex.Message}";
            }
            finally
            {
                _opLock.Release();
                _busy = false;
                OnPropertyChanged(nameof(AddCurrentEnabled));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _opLock.Dispose();

                foreach (SwitcherAccount a in Accounts)
                    a.NoteChanged -= OnNoteChanged;
            }
            catch
            {
                // best-effort cleanup only
            }

            GC.SuppressFinalize(this);
        }
    }
}
