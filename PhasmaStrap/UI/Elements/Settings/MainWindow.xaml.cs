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
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl && (e.Key == Key.F || e.Key == Key.K))
            {
                SettingsSearchBox.Focus();
                SettingsSearchBox.SelectAll();
                e.Handled = true;
            }
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
                CloseSearchPopup();
                return;
            }

            List<Search.SettingsSearchResult> results = Search.SettingsSearchEngine.Search(query);

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

        public void LoadState()
        {
            if (_state.Left > SystemParameters.VirtualScreenWidth)
                _state.Left = 0;

            if (_state.Top > SystemParameters.VirtualScreenHeight)
                _state.Top = 0;

            if (_state.Width > 0)
                this.Width = _state.Width;

            if (_state.Height > 0)
                this.Height = _state.Height;

            if (_state.Left > 0 && _state.Top > 0)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Left = _state.Left;
                this.Top = _state.Top;
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

            if (shouldMinimizeToTray)
            {
                // nothing is discarded by hiding the window, so skip the unsaved-changes prompt below
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            if (App.FastFlags.Changed || App.PendingSettingTasks.Any())
            {
                var result = Frontend.ShowMessageBox(Strings.Menu_UnsavedChanges, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }
            
            _state.Width = this.Width;
            _state.Height = this.Height;

            _state.Top = this.Top;
            _state.Left = this.Left;

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
            WindowState = System.Windows.WindowState.Normal;
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
