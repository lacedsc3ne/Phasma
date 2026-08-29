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

        private readonly Dictionary<string, SettingsSearchEntry> _settingsSearchEntries = new(StringComparer.OrdinalIgnoreCase);

        private void InitializeSettingsSearch()
        {
            var items = new List<string>();

            foreach (var entry in SettingsSearchCatalog.Entries)
            {
                string display = entry.DisplayText;

                // Duplicate display text can occur if two options share the exact same header text
                // on the same page (e.g. a page-level fallback entry) - keep the first one found.
                if (_settingsSearchEntries.ContainsKey(display))
                    continue;

                _settingsSearchEntries[display] = entry;
                items.Add(display);
            }

            SettingsSearchBox.ItemsSource = items;
        }

        private void NavigateToSettingsSearchEntry(SettingsSearchEntry entry)
        {
            SettingsSearchBox.Text = "";
            SettingsSearchBox.IsSuggestionListOpen = false;

            // Simplification vs. Voidstrap's version: Voidstrap walks the target page's live visual
            // tree after navigating to scroll/highlight the exact matched control. PhasmaStrap's nav
            // framework (Wpf.Ui's NavigationFluent/Frame) doesn't expose an easy hook for that, so this
            // only navigates to the option's page - the page is short enough that finding the option
            // after landing on it is not a real burden.
            RootNavigation.Navigate(entry.PageType);
        }

        private void SettingsSearchBox_SuggestionChosen(object sender, RoutedEventArgs e)
        {
            string chosen = SettingsSearchBox.Text?.Trim() ?? "";

            if (chosen.Length == 0 || !_settingsSearchEntries.TryGetValue(chosen, out var entry))
                return;

            NavigateToSettingsSearchEntry(entry);
        }

        private void SettingsSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            string query = SettingsSearchBox.Text?.Trim() ?? "";

            if (query.Length == 0)
                return;

            SettingsSearchEntry? match = null;

            if (_settingsSearchEntries.TryGetValue(query, out var exact))
            {
                match = exact;
            }
            else if (SettingsSearchBox.FilteredItemsSource is not null)
            {
                // Fall back to whatever is currently the top result in the (already-filtered) dropdown.
                foreach (string text in SettingsSearchBox.FilteredItemsSource)
                {
                    if (_settingsSearchEntries.TryGetValue(text, out var found))
                    {
                        match = found;
                        break;
                    }
                }
            }

            if (match is null)
                return;

            e.Handled = true;
            NavigateToSettingsSearchEntry(match);
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
                pinMenuItem.Header = App.Settings.Prop.PinnedNavItems.Contains(navItem.PageTag) ? "Unpin from top" : "Pin to top";

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

        private void MainWindow_MistLoaded(object sender, RoutedEventArgs e) =>
            ((Storyboard)Resources["MistDrift"]).Begin(this);

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
