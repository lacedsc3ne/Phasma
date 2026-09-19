using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

using Wpf.Ui.Common;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Controls.Navigation;
using Wpf.Ui.Mvvm.Contracts;

using PhasmaStrap.UI.ViewModels.Settings;

using NavigationItem = Wpf.Ui.Controls.NavigationItem;

namespace PhasmaStrap.UI.Elements.Settings
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INavigationWindow
    {
        private Models.Persistable.WindowState _state => App.State.Prop.SettingsWindow;

        private System.Windows.Forms.NotifyIcon? _trayIcon;

        private bool _exitRequested = false;

        public MainWindow(bool showAlreadyRunningWarning)
        {
            var viewModel = new MainWindowViewModel();

            viewModel.RequestSaveNoticeEvent += (_, _) => SettingsSavedSnackbar.Show();
            viewModel.RequestCloseWindowEvent += (_, _) => Close();

            DataContext = viewModel;
            
            InitializeComponent();

            App.Logger.WriteLine("MainWindow", "Initializing settings window");

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningSnackbar();

            LoadState();

            // gamepad navigation only makes sense while this window is open, so it's
            // started/stopped here rather than from App::OnStartup
            if (App.Settings.Prop.ControllerNavigationEnabled)
                ControllerService.Initialize();

            InitializeSettingsSearch();
            InitializePinning();
        }

        #region Settings search

        private readonly System.Windows.Threading.DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(60) };

        private bool _suppressSearchClose;

        private void InitializeSettingsSearch()
        {
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                RunSettingsSearch();
            };

            // warm the index on a background thread so the first keystroke is instant
            _ = Task.Run(() => Search.SettingsSearchIndex.Entries);

            PreviewKeyDown += MainWindow_SearchShortcut;
            Deactivated += (_, _) => CloseSearchPopup();
            LocationChanged += (_, _) => CloseSearchPopup();
            SizeChanged += (_, _) => CloseSearchPopup();
        }

        private void MainWindow_SearchShortcut(object sender, KeyEventArgs e)
        {
            // a hotkey capture box on the Hotkeys page must see every key, including Ctrl+F
            if (Keyboard.FocusedElement is FrameworkElement focused && focused.Tag is HotkeyRow)
                return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool inTextInput = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

            bool searchShortcut = (ctrl && (e.Key == Key.F || e.Key == Key.K))
                || (!inTextInput && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.OemQuestion);

            if (searchShortcut)
            {
                SettingsSearchBox.Focus();
                SettingsSearchBox.SelectAll();
                e.Handled = true;
            }
        }

        // --- recently opened results: shown when the box is focused while empty, and boosted in ranking ---

        private static string RecentKey(Search.SettingsSearchEntry entry) => $"{entry.Kind}|{entry.PageType.Name}|{entry.Tab}|{entry.Section}|{entry.Group}|{entry.Header}";

        private static void RememberRecent(Search.SettingsSearchEntry entry)
        {
            try
            {
                var recents = App.State.Prop.RecentSettingsSearches;
                string key = RecentKey(entry);
                recents.Remove(key);
                recents.Insert(0, key);
                while (recents.Count > 8)
                    recents.RemoveAt(recents.Count - 1);
                App.State.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Could not save recent searches: {ex.Message}");
            }
        }

        private static List<Search.SettingsSearchResult> RecentResults()
        {
            var results = new List<Search.SettingsSearchResult>();

            // two entries can legitimately share a key (e.g. identical action buttons in
            // different rows) - first one wins, never throw
            var byKey = new Dictionary<string, Search.SettingsSearchEntry>(StringComparer.Ordinal);
            foreach (Search.SettingsSearchEntry entry in Search.SettingsSearchIndex.Entries)
                byKey.TryAdd(RecentKey(entry), entry);

            foreach (string key in App.State.Prop.RecentSettingsSearches)
            {
                if (byKey.TryGetValue(key, out var entry))
                    results.Add(new Search.SettingsSearchResult(entry, 0));
            }

            return results;
        }

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void RunSettingsSearch()
        {
            string query = SettingsSearchBox.Text ?? "";

            if (query.Trim().Length == 0)
            {
                ShowRecentSearches();
                return;
            }

            List<Search.SettingsSearchResult> results = Search.SettingsSearchEngine.Search(query);

            // things you've opened before float up a little
            if (results.Count > 1 && App.State.Prop.RecentSettingsSearches.Count > 0)
            {
                var recent = new HashSet<string>(App.State.Prop.RecentSettingsSearches, StringComparer.Ordinal);
                results = results
                    .Select(r => recent.Contains(RecentKey(r.Entry)) ? new Search.SettingsSearchResult(r.Entry, r.Score + 35) : r)
                    .OrderByDescending(r => r.Score)
                    .ToList();
            }

            SettingsSearchResults.ItemsSource = results;
            SettingsSearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
            SettingsSearchFooter.Text = results.Count switch
            {
                0 => "No matching settings",
                1 => "1 result  ·  Enter to open",
                _ => $"{results.Count} results  ·  ↑↓ to move, Enter to open",
            };

            SettingsSearchPopup.IsOpen = true;
        }

        private void ShowRecentSearches()
        {
            List<Search.SettingsSearchResult> recents = RecentResults();
            if (recents.Count == 0 || !SettingsSearchBox.IsKeyboardFocusWithin)
            {
                CloseSearchPopup();
                return;
            }

            SettingsSearchResults.ItemsSource = recents;
            SettingsSearchResults.SelectedIndex = -1;
            SettingsSearchFooter.Text = "Recently opened  ·  type to search everything";
            SettingsSearchPopup.IsOpen = true;
        }

        private void CloseSearchPopup()
        {
            if (_suppressSearchClose)
                return;

            SettingsSearchPopup.IsOpen = false;
        }

        private void SettingsSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if ((SettingsSearchBox.Text ?? "").Trim().Length > 0 && SettingsSearchResults.Items.Count > 0)
                SettingsSearchPopup.IsOpen = true;
            else if ((SettingsSearchBox.Text ?? "").Trim().Length == 0)
                ShowRecentSearches();
        }

        private void SettingsSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // focus moving into the popup (clicking a result) must not close it before the click lands
            if (e.NewFocus is DependencyObject target && IsInsidePopup(target))
                return;

            CloseSearchPopup();
        }

        private bool IsInsidePopup(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                if (ReferenceEquals(current, SettingsSearchPopup.Child))
                    return true;

                current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        private void SettingsSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int count = SettingsSearchResults.Items.Count;

            switch (e.Key)
            {
                case Key.Down when count > 0:
                    if (!SettingsSearchPopup.IsOpen)
                        SettingsSearchPopup.IsOpen = true;
                    SettingsSearchResults.SelectedIndex = Math.Min(SettingsSearchResults.SelectedIndex + 1, count - 1);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Up when count > 0:
                    SettingsSearchResults.SelectedIndex = Math.Max(SettingsSearchResults.SelectedIndex - 1, 0);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.PageDown when count > 0:
                    SettingsSearchResults.SelectedIndex = Math.Min(SettingsSearchResults.SelectedIndex + 8, count - 1);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.PageUp when count > 0:
                    SettingsSearchResults.SelectedIndex = Math.Max(SettingsSearchResults.SelectedIndex - 8, 0);
                    SettingsSearchResults.ScrollIntoView(SettingsSearchResults.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (_searchDebounce.IsEnabled)
                    {
                        _searchDebounce.Stop();
                        RunSettingsSearch();
                    }

                    if (SettingsSearchResults.SelectedItem is Search.SettingsSearchResult selected)
                        ActivateSearchResult(selected);
                    else if (SettingsSearchResults.Items.Count > 0 && SettingsSearchResults.Items[0] is Search.SettingsSearchResult first)
                        ActivateSearchResult(first);

                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (SettingsSearchPopup.IsOpen)
                        CloseSearchPopup();
                    else
                        SettingsSearchBox.Text = "";
                    e.Handled = true;
                    break;
            }
        }

        private void SettingsSearchResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                DependencyObject? current = source;
                while (current is not null && current is not ListBoxItem)
                    current = System.Windows.Media.VisualTreeHelper.GetParent(current);

                if (current is ListBoxItem item && item.DataContext is Search.SettingsSearchResult result)
                {
                    ActivateSearchResult(result);
                    e.Handled = true;
                }
            }
        }

        private void ActivateSearchResult(Search.SettingsSearchResult result)
        {
            RememberRecent(result.Entry);
            _suppressSearchClose = true;
            try
            {
                SettingsSearchPopup.IsOpen = false;
                SettingsSearchBox.Text = "";
                SettingsSearchResults.ItemsSource = null;
            }
            finally
            {
                _suppressSearchClose = false;
            }

            Search.SettingsSearchNavigator.Reveal(RootNavigation, RootFrame, result.Entry);
        }

        #endregion Settings search

        #region Pinned nav items

        private readonly List<NavigationItem> _pinnedNavItems = new();

        private void InitializePinning()
        {
            // Attach a right-click "pin"/"unpin" menu to every real page item declared in XAML.
            // The dynamically-created pinned duplicates get the same menu attached as they're built.
            foreach (var control in RootNavigation.Items)
            {
                if (control is not NavigationItem navItem || string.IsNullOrEmpty(navItem.PageTag) || navItem.PageTag == "fastflageditor")
                    continue;

                AttachPinContextMenu(navItem);
            }

            RebuildPinnedGroup();
        }

        private void AttachPinContextMenu(NavigationItem navItem)
        {
            var pinMenuItem = new MenuItem();
            var menu = new System.Windows.Controls.ContextMenu();

            menu.Opened += (_, _) =>
                pinMenuItem.Header = App.Settings.Prop.PinnedNavItems.Contains(navItem.PageTag) ? Strings.Menu_Settings_UnpinFromTop : Strings.Menu_Settings_PinToTop;

            pinMenuItem.Click += (_, _) => TogglePinned(navItem.PageTag);

            menu.Items.Add(pinMenuItem);
            navItem.ContextMenu = menu;
        }

        private void TogglePinned(string pageTag)
        {
            var pinned = App.Settings.Prop.PinnedNavItems;

            if (!pinned.Remove(pageTag))
                pinned.Add(pageTag);

            App.Settings.Save();

            RebuildPinnedGroup();
        }

        private void RebuildPinnedGroup()
        {
            // Only ever remove the NavigationItem duplicates here, never PinnedHeader itself - see the
            // comment on PinnedHeader in MainWindow.xaml for why removing a header is unsafe.
            foreach (var item in _pinnedNavItems)
                RootNavigation.Items.Remove(item);

            _pinnedNavItems.Clear();

            var pinnedTags = App.Settings.Prop.PinnedNavItems;

            PinnedHeader.Visibility = pinnedTags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            int insertAt = RootNavigation.Items.IndexOf(PinnedHeader) + 1;

            foreach (string tag in pinnedTags)
            {
                NavigationItem? source = null;

                foreach (var control in RootNavigation.Items)
                {
                    if (control is NavigationItem candidate && candidate.PageTag == tag && !_pinnedNavItems.Contains(candidate))
                    {
                        source = candidate;
                        break;
                    }
                }

                if (source is null)
                    continue;

                var pinnedItem = new NavigationItem
                {
                    Content = source.Content,
                    Icon = source.Icon,
                    PageType = source.PageType,
                    PageTag = source.PageTag,
                };

                AttachPinContextMenu(pinnedItem);

                RootNavigation.Items.Insert(insertAt++, pinnedItem);
                _pinnedNavItems.Add(pinnedItem);
            }
        }

        #endregion Pinned nav items

        // ---- window placement. It lives in its own file, written only by this window: State.json
        // is also saved by the launcher and the background updater, which wrote back the size
        // they had read at startup - so the window "sometimes" came back at an old size. It is
        // saved as it changes (not only on a real close - closing to the tray, logging off or a
        // crash used to lose it), and it remembers being maximized.

        private static string PlacementPath => Path.Combine(Paths.Base, "SettingsWindow.json");

        private static bool IsUiTest => Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1";

        private System.Windows.Threading.DispatcherTimer? _placementTimer;

        private Models.Persistable.WindowState ReadPlacement()
        {
            try
            {
                if (File.Exists(PlacementPath))
                    return JsonSerializer.Deserialize<Models.Persistable.WindowState>(File.ReadAllText(PlacementPath)) ?? new();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Window placement unreadable: {ex.Message}");
            }

            // first run with this file: what State.json had
            return new Models.Persistable.WindowState { Width = _state.Width, Height = _state.Height, Left = _state.Left, Top = _state.Top };
        }

        public void LoadState()
        {
            Models.Persistable.WindowState placement = ReadPlacement();

            if (placement.Width >= MinWidth && placement.Height >= MinHeight && IsOnAScreen(placement.Left, placement.Top, placement.Width, placement.Height))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = placement.Left;
                Top = placement.Top;
                Width = placement.Width;
                Height = placement.Height;
            }
            else if (placement.Width >= MinWidth && placement.Height >= MinHeight)
            {
                // the monitor it was on is gone: keep the size, centre it
                Width = Math.Min(placement.Width, SystemParameters.WorkArea.Width);
                Height = Math.Min(placement.Height, SystemParameters.WorkArea.Height);
            }

            // maximized only once the window is up: made maximized before it's shown, WPF's
            // custom window frame (WindowChrome) loses its resize edges after un-maximizing
            if (placement.Maximized)
            {
                void MaximizeOnce(object? sender, EventArgs e)
                {
                    ContentRendered -= MaximizeOnce;
                    Dispatcher.BeginInvoke(() => WindowState = System.Windows.WindowState.Maximized, System.Windows.Threading.DispatcherPriority.Background);
                }
                ContentRendered += MaximizeOnce;
            }

            _placementTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _placementTimer.Tick += (_, _) =>
            {
                _placementTimer.Stop();
                SavePlacement();
            };

            void Changed(object? sender, EventArgs e)
            {
                if (!IsLoaded)
                    return;
                _placementTimer.Stop();
                _placementTimer.Start();
            }

            SizeChanged += Changed;
            LocationChanged += Changed;
            StateChanged += Changed;

            if (Application.Current is not null)
                Application.Current.SessionEnding += (_, _) => SavePlacement();
        }

        // at least 120 x 80 of it on one of the screens, in this window's units
        private static bool IsOnAScreen(double left, double top, double width, double height)
        {
            try
            {
                double scale;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    scale = g.DpiX / 96.0;

                foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var area = screen.WorkingArea;
                    double l = area.Left / scale, t = area.Top / scale, r = area.Right / scale, b = area.Bottom / scale;
                    double overlapW = Math.Min(left + width, r) - Math.Max(left, l);
                    double overlapH = Math.Min(top + height, b) - Math.Max(top, t);
                    if (overlapW >= 120 && overlapH >= 80)
                        return true;
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        private void SavePlacement()
        {
            // UI tests run a second copy of this window - it must not move the real one
            if (IsUiTest || WindowState == System.Windows.WindowState.Minimized && !IsVisible)
                return;

            try
            {
                Rect bounds = WindowState == System.Windows.WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
                if (bounds.IsEmpty || bounds.Width < 100 || bounds.Height < 100)
                    return;

                var placement = new Models.Persistable.WindowState
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Maximized = WindowState == System.Windows.WindowState.Maximized,
                };

                Directory.CreateDirectory(Paths.Base);
                File.WriteAllText(PlacementPath, JsonSerializer.Serialize(placement));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow", $"Window placement not saved: {ex.Message}");
            }
        }

        private async void ShowAlreadyRunningSnackbar()
        {
            await Task.Delay(500); // wait for everything to finish loading
            AlreadyRunningSnackbar.Show();
        }

        #region INavigationWindow methods

        public Frame GetFrame() => RootFrame;

        public INavigation GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        private Storyboard? _mist;

        // the drifting mist is two large blurred ellipses animated forever - a real per-frame
        // compositing cost, so it only runs while the window is actually the active one
        private void MainWindow_MistLoaded(object sender, RoutedEventArgs e)
        {
            _mist = (Storyboard)Resources["MistDrift"];
            _mist.Begin(this, true);

            Activated += (_, _) => { try { _mist?.Resume(this); } catch (Exception) { } };
            Deactivated += (_, _) => { try { _mist?.Pause(this); } catch (Exception) { } };
            StateChanged += (_, _) =>
            {
                try
                {
                    if (WindowState == System.Windows.WindowState.Minimized)
                        _mist?.Pause(this);
                    else if (IsActive)
                        _mist?.Resume(this);
                }
                catch (Exception) { }
            };
        }

        private void WpfUiWindow_Closing(object sender, CancelEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;

            bool shouldMinimizeToTray = App.Settings.Prop.MinimizeToTrayOnClose
                && !_exitRequested
                && !viewModel.RestartAfterClose
                && !viewModel.LaunchAfterClose
                && !App.LaunchSettings.TestModeFlag.Active;

            SavePlacement();

            if (shouldMinimizeToTray)
            {
                // nothing is discarded by hiding the window, so skip the unsaved-changes prompt below
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            if (App.FastFlags.Changed || App.FlagProfiles.Changed || App.PendingSettingTasks.Any())
            {
                var result = Frontend.ShowMessageBox(Strings.Menu_UnsavedChanges, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }
            
            App.State.Save();
        }

        private void MinimizeToTray()
        {
            Hide();
            try { _mist?.Pause(this); } catch (Exception) { }

            if (_trayIcon is not null)
                return;

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = Properties.Resources.IconPhasmaStrap,
                Text = App.ProjectName,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add(Strings.Common_Open, null, (_, _) => RestoreFromTray());
            contextMenu.Items.Add(Strings.Common_Exit, null, (_, _) => ExitFromTray());
            _trayIcon.ContextMenuStrip = contextMenu;
        }

        private void RestoreFromTray()
        {
            Show();
            // keep it maximized if it was
            if (WindowState == System.Windows.WindowState.Minimized)
                WindowState = ReadPlacement().Maximized ? System.Windows.WindowState.Maximized : System.Windows.WindowState.Normal;
            Activate();

            _trayIcon?.Dispose();
            _trayIcon = null;
        }

        private void ExitFromTray()
        {
            _exitRequested = true;

            _trayIcon?.Dispose();
            _trayIcon = null;

            Close();
        }

        private void WpfUiWindow_Closed(object sender, EventArgs e)
        {
            try { _mist?.Stop(this); } catch (Exception) { }
            _searchDebounce.Stop();
            _trayIcon?.Dispose();

            ControllerService.Shutdown();

            var viewModel = (MainWindowViewModel)DataContext;

            if (viewModel.RestartAfterClose)
            {
                // spawn a detached helper that waits a moment before relaunching - LaunchHandler's
                // "Settings" InterProcessLock is still held by this process until it fully exits,
                // so starting the new instance immediately would just see the lock taken and back
                // off instead of opening a fresh window
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 1 /nobreak >nul & start \"\" \"{Paths.Process}\" -settings",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                App.SoftTerminate();
                return;
            }

            if (App.LaunchSettings.TestModeFlag.Active || viewModel.LaunchAfterClose)
                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            else
                App.SoftTerminate();
        }
    }
}
