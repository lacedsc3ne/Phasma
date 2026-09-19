using System.Windows;

using Windows.Win32;
using Windows.Win32.Foundation;

using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.Enums;

namespace PhasmaStrap
{
    public static class LaunchHandler
    {
        public static void ProcessNextAction(NextAction action, bool isUnfinishedInstall = false)
        {
            const string LOG_IDENT = "LaunchHandler::ProcessNextAction";

            switch (action)
            {
                case NextAction.LaunchSettings:
                    App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                    LaunchSettings();
                    break;

                case NextAction.LaunchRoblox:
                    App.Logger.WriteLine(LOG_IDENT, "Opening Roblox");
                    LaunchRoblox(LaunchMode.Player);
                    break;

                case NextAction.LaunchRobloxStudio:
                    App.Logger.WriteLine(LOG_IDENT, "Opening Roblox Studio");
                    LaunchRoblox(LaunchMode.Studio);
                    break;

                default:
                    App.Logger.WriteLine(LOG_IDENT, "Closing");
                    App.Terminate(isUnfinishedInstall ? ErrorCode.ERROR_INSTALL_USEREXIT : ErrorCode.ERROR_SUCCESS);
                    break;
            }
        }

        public static void ProcessLaunchArgs()
        {
            const string LOG_IDENT = "LaunchHandler::ProcessLaunchArgs";

            // this order is specific

            if (App.LaunchSettings.UninstallFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening uninstaller");
                LaunchUninstaller();
            }
            else if (App.LaunchSettings.SwitchAccountFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Switching account");
                LaunchAccountSwitch(App.LaunchSettings.SwitchAccountFlag.Data);
            }
            else if (App.LaunchSettings.EditClipFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening clip editor");
                LaunchClipEditor(App.LaunchSettings.EditClipFlag.Data);
            }
            else if (App.LaunchSettings.MenuFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening settings");
                LaunchSettings();
            }
            else if (App.LaunchSettings.WatcherFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening watcher");
                LaunchWatcher();
            }
            else if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening background updater");
                LaunchBackgroundUpdater();
            }
            else if (App.LaunchSettings.RobloxLaunchMode != LaunchMode.None)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Opening bootstrapper ({App.LaunchSettings.RobloxLaunchMode})");
                LaunchRoblox(App.LaunchSettings.RobloxLaunchMode);
            }
            else if (!App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Opening menu");
                LaunchMenu();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Closing - quiet flag active");
                App.Terminate();
            }
        }

        public static void LaunchInstaller()
        {
            using var interlock = new InterProcessLock("Installer");

            if (!interlock.IsAcquired)
            {
                Frontend.ShowMessageBox(Strings.Dialog_AlreadyRunning_Installer, MessageBoxImage.Stop);
                App.Terminate();
                return;
            }

            if (App.LaunchSettings.UninstallFlag.Active)
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_FirstRunUninstall, MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INVALID_FUNCTION);
                return;
            }

            if (App.LaunchSettings.QuietFlag.Active)
            {
                var installer = new Installer();

                if (!installer.CheckInstallLocation())
                    App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);

                installer.DoInstall();

                interlock.Dispose();

                ProcessLaunchArgs();
            }
            else
            {
#if QA_BUILD
                Frontend.ShowMessageBox("You are about to install a QA build of PhasmaStrap. The red window border indicates that this is a QA build.\n\nQA builds are handled completely separately of your standard installation, like a virtual environment.", MessageBoxImage.Information);
#endif

                new LanguageSelectorDialog().ShowDialog();

                var installer = new UI.Elements.Installer.MainWindow();
                installer.ShowDialog();

                interlock.Dispose();

                ProcessNextAction(installer.CloseAction, !installer.Finished);
            }

        }

        public static void LaunchUninstaller()
        {
            using var interlock = new InterProcessLock("Uninstaller");

            if (!interlock.IsAcquired)
            {
                Frontend.ShowMessageBox(Strings.Dialog_AlreadyRunning_Uninstaller, MessageBoxImage.Stop);
                App.Terminate();
                return;
            }

            bool confirmed = false;
            bool keepData = true;

            if (App.LaunchSettings.QuietFlag.Active)
            {
                confirmed = true;
            }
            else
            {
                var dialog = new UninstallerDialog();
                dialog.ShowDialog();

                confirmed = dialog.Confirmed;
                keepData = dialog.KeepData;
            }

            if (!confirmed)
            {
                App.Terminate();
                return;
            }

            Installer.DoUninstall(keepData);

            Frontend.ShowMessageBox(Strings.Bootstrapper_SuccessfullyUninstalled, MessageBoxImage.Information);

            App.Terminate();
        }

        public static void LaunchSettings()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchSettings";

            using var interlock = new InterProcessLock("Settings");

            // UI-test hook: a hidden background window may open next to the user's real one
            bool uiTest = Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1";

            if (interlock.IsAcquired || uiTest)
            {
                bool showAlreadyRunningWarning = Process.GetProcessesByName(App.ProjectName).Length > 1;

                // UI-test hook: open the colour theme editor on its own, in the background
                if (Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1" && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_PAGE") == "ColorThemeEditor")
                {
                    var editor = new UI.Elements.ContextMenu.AppColorThemeEditor();
                    ApplyUiTestBackground(editor);
                    editor.ShowDialog();
                    App.Terminate();
                    return;
                }

                // UI-test hook: the launch (bootstrapper) dialog on its own, in the background
                if (Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1" && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_PAGE") == "BootstrapperDialog")
                {
                    var dialog = new UI.Elements.Bootstrapper.FluentDialog(false) { Message = "Connecting to Roblox...", ProgressValue = 42 };
                    ApplyUiTestBackground(dialog);
                    dialog.ShowDialog();
                    App.Terminate();
                    return;
                }

                var window = new UI.Elements.Settings.MainWindow(showAlreadyRunningWarning);
                if (ApplyUiTestBackground(window) && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_PAGE") is string pageName)
                {
                    // UI-test hook: open straight on a page (class name, e.g. "CapturePage"), since a
                    // background window can't be clicked through the navigation bar
                    window.Loaded += (_, _) =>
                    {
                        Type? page = typeof(UI.Elements.Settings.MainWindow).Assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == pageName && t.Namespace == "PhasmaStrap.UI.Elements.Settings.Pages");
                        if (page is not null)
                            window.Dispatcher.BeginInvoke(new Action(() => window.Navigate(page)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                        // PHASMASTRAP_UITEST_SCROLL=<0-100>: scroll the page that far down once it has
                        // loaded (a background window gets no mouse wheel either)
                        if (double.TryParse(Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_SCROLL"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                        {
                            // PHASMASTRAP_UITEST_SCROLL_DELAY=<seconds>: leave time to expand something first
                            if (!double.TryParse(Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_SCROLL_DELAY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delay))
                                delay = 2.5;

                            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(delay, 0.5, 120)) };
                            timer.Tick += (_, _) =>
                            {
                                timer.Stop();
                                UiTestScroll(window, percent);
                            };
                            timer.Start();
                        }
                    };
                }

                // typically we'd use Show(), but we need to block to ensure IPL stays in scope
                window.ShowDialog();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Found an already existing menu window");

                var process = Utilities.GetProcessesSafe().Where(x => x.MainWindowTitle == Strings.Menu_Title).FirstOrDefault();

                if (process is not null)
                    PInvoke.SetForegroundWindow((HWND)process.MainWindowHandle);

                App.Terminate();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        // scrolls the tallest scrollable area of the window (the page, not the navigation bar)
        private static void UiTestScroll(System.Windows.DependencyObject root, double percent)
        {
            System.Windows.Controls.ScrollViewer? best = null;

            void Walk(System.Windows.DependencyObject node)
            {
                if (node is System.Windows.Controls.ScrollViewer viewer && viewer.ScrollableHeight > 0
                    && (best is null || viewer.ActualHeight * viewer.ActualWidth > best.ActualHeight * best.ActualWidth))
                    best = viewer;

                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
            }

            Walk(root);
            best?.ScrollToVerticalOffset(best.ScrollableHeight * Math.Clamp(percent, 0, 100) / 100.0);
        }

        private static bool ApplyUiTestBackground(System.Windows.Window window)
        {
            if (Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") != "1")
                return false;

            window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            window.Left = 40;
            window.Top = 40;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.SourceInitialized += (_, _) =>
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                SetWindowPos(hwnd, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE | NOMOVE | NOACTIVATE */);
            };

            return true;
        }

        // Started by the tray menu after the user picked an account and confirmed. Runs in its own
        // process because the Watcher (which owns the tray) goes away when Roblox closes.
        public static void LaunchAccountSwitch(string? data)
        {
            const string LOG_IDENT = "LaunchHandler::LaunchAccountSwitch";

            if (!long.TryParse(data, out long userId))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not a user ID: '{data}'");
                App.Terminate();
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    Utility.AccountQuickSwitch.Log ??= message => App.Logger.WriteLine("AccountQuickSwitch", message);

                    // ask nicely first so Roblox can flush its state, then insist
                    foreach (string name in new[] { App.RobloxPlayerAppName, App.RobloxStudioAppName })
                    {
                        foreach (Process process in Process.GetProcessesByName(name))
                        {
                            try { process.CloseMainWindow(); } catch { }
                            process.Dispose();
                        }
                    }

                    bool StillRunning() => new[] { App.RobloxPlayerAppName, App.RobloxStudioAppName }
                        .Any(name => { Process[] found = Process.GetProcessesByName(name); foreach (Process p in found) p.Dispose(); return found.Length > 0; });

                    for (int i = 0; i < 20 && StillRunning(); i++)
                        await Task.Delay(250);

                    foreach (Process process in Process.GetProcessesByName(App.RobloxPlayerAppName))
                    {
                        try { process.Kill(); } catch { }
                        process.Dispose();
                    }

                    for (int i = 0; i < 40 && StillRunning(); i++)
                        await Task.Delay(250);

                    if (StillRunning())
                        throw new InvalidOperationException("Roblox (or Roblox Studio) is still running. Close it and switch again.");

                    await Utility.AccountQuickSwitch.SwitchAsync(Paths.AccountBackups, Integrations.RobloxCookie.LiveCookiesDatPath, userId);

                    App.Logger.WriteLine(LOG_IDENT, "Login switched - starting Roblox");
                    Process.Start(Paths.Process, "-player");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                    App.Current.Dispatcher.Invoke(() => Frontend.ShowMessageBox($"Could not switch accounts: {ex.Message}", System.Windows.MessageBoxImage.Warning));
                }
                finally
                {
                    App.Current.Dispatcher.Invoke(() => App.Terminate());
                }
            });
        }

        public static void LaunchClipEditor(string? path)
        {
            const string LOG_IDENT = "LaunchHandler::LaunchClipEditor";

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                App.Logger.WriteLine(LOG_IDENT, $"No such clip: '{path}'");
                App.Terminate();
                return;
            }

            var window = new UI.Elements.Dialogs.ClipEditorWindow(path);

            // UI-test hook: open at the very bottom of the z-order without taking focus, so the
            // window can be driven (UI Automation) and captured (PrintWindow) without disturbing
            // whatever is on screen - e.g. a fullscreen game. It has to stay on-screen: a window
            // parked at negative coordinates is never composed, so PrintWindow returns nothing.
            if (!ApplyUiTestBackground(window))
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            window.ShowDialog();
            App.Terminate();
        }

        public static void LaunchMenu()
        {
            var dialog = new LaunchMenuDialog();
            dialog.ShowDialog();

            ProcessNextAction(dialog.CloseAction);
        }

        public static void LaunchRoblox(LaunchMode launchMode)
        {
            const string LOG_IDENT = "LaunchHandler::LaunchRoblox";

            if (launchMode == LaunchMode.None)
                throw new InvalidOperationException("No Roblox launch mode set");

            if (!File.Exists(Path.Combine(Paths.System, "mfplat.dll")))
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_WMFNotFound, MessageBoxImage.Error);

                if (!App.LaunchSettings.QuietFlag.Active)
                    Utilities.ShellExecute("https://support.microsoft.com/en-us/topic/media-feature-pack-list-for-windows-n-editions-c1c6fffa-d052-8338-7a79-a4bb980a700a");

                App.Terminate(ErrorCode.ERROR_FILE_NOT_FOUND);
            }

            if (App.Settings.Prop.ConfirmLaunches && launchMode != LaunchMode.Studio && Mutex.TryOpenExisting("ROBLOX_singletonMutex", out var _))
            {
                // this currently doesn't work very well since it relies on checking the existence of the singleton mutex
                // which often hangs around for a few seconds after the window closes
                // it would be better to have this rely on the activity tracker when we implement IPC in the planned refactoring

                var result = Frontend.ShowMessageBox(Strings.Bootstrapper_ConfirmLaunch, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                {
                    App.Terminate();
                    return;
                }
            }

            // start bootstrapper and show the bootstrapper modal if we're not running silently
            App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper");
            App.Bootstrapper = new Bootstrapper(launchMode);
            IBootstrapperDialog? dialog = null;

            if (!App.LaunchSettings.QuietFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper dialog");
                dialog = App.Settings.Prop.BootstrapperStyle.GetNew();
                App.Bootstrapper.Dialog = dialog;
                dialog.Bootstrapper = App.Bootstrapper;
            }

            Task.Run(App.Bootstrapper.Run).ContinueWith(t =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Bootstrapper task has finished");

                if (t.IsFaulted)
                {
                    App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the bootstrapper");

                    if (t.Exception is not null)
                        App.FinalizeExceptionHandling(t.Exception);
                }

                App.Terminate();
            });

            dialog?.ShowBootstrapper();

            App.Logger.WriteLine(LOG_IDENT, "Exiting");
        }

        public static void LaunchWatcher()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchWatcher";

            // this whole topology is a bit confusing, bear with me:
            // main thread: strictly UI only, handles showing of the notification area icon, context menu, server details dialog
            // - server information task: queries server location, invoked if either the explorer notification is shown or the server details dialog is opened
            // - discord rpc thread: handles rpc connection with discord
            //    - discord rich presence tasks: handles querying and displaying of game information, invoked on activity watcher events
            // - watcher task: runs activity watcher + waiting for roblox to close, terminates when it has

            var watcher = new Watcher();

            Task.Run(watcher.Run).ContinueWith(t => 
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher task has finished");

                watcher.Dispose();

                if (t.IsFaulted)
                {
                    App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the watcher");

                    if (t.Exception is not null)
                        App.FinalizeExceptionHandling(t.Exception);
                }

                if (App.Settings.Prop.CleanerOptions != Enums.CleanerOptions.Never)
                    Cleaner.DoCleaning();

                App.Terminate();
            });
        }

        public static void LaunchBackgroundUpdater()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchBackgroundUpdater";

            // Activate some LaunchFlags we need
            App.LaunchSettings.QuietFlag.Active = true;
            App.LaunchSettings.NoLaunchFlag.Active = true;

            if (!Enum.TryParse(App.LaunchSettings.BackgroundUpdaterFlag.Data, out LaunchMode launchMode))
                throw new ApplicationException($"Invalid launch mode arg ({App.LaunchSettings.BackgroundUpdaterFlag.Data})");

            if (launchMode != LaunchMode.Player && launchMode != LaunchMode.Studio)
                throw new ApplicationException($"Unsupported launch mode {launchMode} provided");

            App.Logger.WriteLine(LOG_IDENT, "Initializing bootstrapper");
            App.Bootstrapper = new Bootstrapper(launchMode)
            {
                MutexNamePrefix = "PhasmaStrap-BackgroundUpdater",
                QuitIfMutexExists = true
            };

            CancellationTokenSource cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Started event waiter");
                using (EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset, "PhasmaStrap-BackgroundUpdaterKillEvent"))
                    handle.WaitOne();

                App.Logger.WriteLine(LOG_IDENT, "Received close event, killing it all!");
                App.Bootstrapper.Cancel();
            }, cts.Token);

            Task.Run(App.Bootstrapper.Run).ContinueWith(t =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Bootstrapper task has finished");
                cts.Cancel(); // stop event waiter

                if (t.IsFaulted)
                {
                    App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the bootstrapper");

                    if (t.Exception is not null)
                        App.FinalizeExceptionHandling(t.Exception);
                }

                App.Terminate();
            });

            App.Logger.WriteLine(LOG_IDENT, "Exiting");
        }
    }
}
