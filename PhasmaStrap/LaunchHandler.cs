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
            else if (App.LaunchSettings.GuardFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Starting the account guard");
                Utility.AccountGuard.RunBackground();
            }
            else if (App.LaunchSettings.DiscordJoinFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Joining a friend from Discord");
                Integrations.DiscordJoin.RunJoinHandler(App.LaunchSettings.DiscordJoinFlag.Data);
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

            bool uiTest = Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1";

            if (interlock.IsAcquired || uiTest)
            {
                bool showAlreadyRunningWarning = Process.GetProcessesByName(App.ProjectName).Length > 1;

                if (Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1" && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_PAGE") == "ColorThemeEditor")
                {
                    var editor = new UI.Elements.ContextMenu.AppColorThemeEditor();
                    ApplyUiTestBackground(editor);
                    editor.ShowDialog();
                    App.Terminate();
                    return;
                }

                if (Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1" && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_PAGE") == "CrosshairEditor")
                {
                    var crosshairEditor = new UI.Elements.Dialogs.CrosshairEditorWindow();
                    ApplyUiTestBackground(crosshairEditor);
                    crosshairEditor.ShowDialog();
                    App.Terminate();
                    return;
                }

                if (Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1" && Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_PAGE") == "ServerPicker")
                {
                    var fake = Task.Run(async () =>
                    {
                        await Task.Delay(1500);
                        Models.MatchmakerCandidate Make(string city, string country, int km, int ping, int playing) => new()
                        {
                            JobId = Guid.NewGuid().ToString(), Datacenter = new Models.RobloxDatacenter { City = city, Country = country },
                            DistanceKm = km, EstimatedPingMs = ping, Playing = playing, MaxPlayers = 30,
                        };
                        return new List<Models.MatchmakerCandidate>
                        {
                            Make("Amsterdam", "Netherlands", 170, 9, 21), Make("Frankfurt", "Germany", 320, 13, 28), Make("Frankfurt", "Germany", 320, 13, 12),
                            Make("Paris", "France", 260, 12, 30), Make("London", "United Kingdom", 330, 14, 18), Make("Warsaw", "Poland", 1160, 31, 9),
                            Make("Ashburn", "USA", 6200, 96, 25), Make("Chicago", "USA", 6650, 104, 30), Make("Dallas", "USA", 7900, 124, 17),
                        };
                    });

                    var picker = new UI.Elements.Dialogs.ServerPickerWindow(1, fake, TimeSpan.FromSeconds(120));
                    ApplyUiTestBackground(picker);
                    picker.Topmost = false;
                    picker.ShowDialog();
                    App.Logger.WriteLine(LOG_IDENT, $"Picker closed, chosen: {picker.ChosenJobId ?? "(none)"}");
                    App.Terminate();
                    return;
                }

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
                    window.Loaded += (_, _) =>
                    {
                        Type? page = typeof(UI.Elements.Settings.MainWindow).Assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == pageName && t.Namespace == "PhasmaStrap.UI.Elements.Settings.Pages");
                        if (page is not null)
                            window.Dispatcher.BeginInvoke(new Action(() => window.Navigate(page)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                        if (double.TryParse(Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_SCROLL"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                        {
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
                SetWindowPos(hwnd, new IntPtr(1) , 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 );
            };

            return true;
        }

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
                var result = Frontend.ShowMessageBox(Strings.Bootstrapper_ConfirmLaunch, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                {
                    App.Terminate();
                    return;
                }
            }

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

            var watcher = new Watcher();

            Task.Run(watcher.Run).ContinueWith(t =>
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher task has finished");

                try
                {
                    if (!Task.Run(watcher.Dispose).Wait(TimeSpan.FromSeconds(15)))
                        App.Logger.WriteLine(LOG_IDENT, "Shutdown steps did not finish in time, closing anyway");

                    if (t.IsFaulted)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the watcher");

                        if (t.Exception is not null)
                            App.FinalizeExceptionHandling(t.Exception);
                    }

                    if (App.Settings.Prop.CleanerOptions != Enums.CleanerOptions.Never)
                        Cleaner.DoCleaning();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Shutdown step threw, closing anyway: {ex.Message}");
                }
                finally
                {
                    App.Terminate();
                }
            });
        }

        public static void LaunchBackgroundUpdater()
        {
            const string LOG_IDENT = "LaunchHandler::LaunchBackgroundUpdater";

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
                cts.Cancel();

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
