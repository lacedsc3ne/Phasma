using System.Windows;
using Microsoft.Win32;

namespace PhasmaStrap
{
    internal class Installer
    {
        private const bool OpenReleaseNotes = true;

        private const string ForcedReleaseNotesVersion = "2.11.2";

        private static string DesktopShortcut => Path.Combine(Paths.Desktop, $"{App.ProjectName}.lnk");

        private static string StartMenuShortcut => Path.Combine(Paths.WindowsStartMenu, $"{App.ProjectName}.lnk");

        public string InstallLocation = Path.Combine(Paths.LocalAppData, App.ProjectName);

        public bool ExistingDataPresent => File.Exists(Path.Combine(InstallLocation, "Settings.json"));

        public bool CreateDesktopShortcuts = true;

        public bool CreateStartMenuShortcuts = true;

        public bool EnableAnalytics = true;

        public bool IsImplicitInstall = false;

        public string InstallLocationError { get; set; } = "";

        public void DoInstall()
        {
            const string LOG_IDENT = "Installer::DoInstall";

            App.Logger.WriteLine(LOG_IDENT, "Beginning installation");

            Directory.CreateDirectory(InstallLocation);

            Paths.Initialize(InstallLocation);

            if (!IsImplicitInstall)
            {
                Filesystem.AssertReadOnly(Paths.Application);

                CloudFiles.Hydrate(Paths.Application);

                try
                {
                    File.Copy(Paths.Process, Paths.Application, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not overwrite executable");
                    App.Logger.WriteException(LOG_IDENT, ex);

                    Frontend.ShowMessageBox(Strings.Installer_Install_CannotOverwrite, MessageBoxImage.Error);
                    App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                }
            }

            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("DisplayIcon", $"{Paths.Application},0");
                uninstallKey.SetValueSafe("DisplayName", App.ProjectName);

                uninstallKey.SetValueSafe("DisplayVersion", App.Version);

                if (uninstallKey.GetValue("InstallDate") is null)
                    uninstallKey.SetValueSafe("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

                uninstallKey.SetValueSafe("InstallLocation", Paths.Base);
                uninstallKey.SetValueSafe("NoRepair", 1);
                uninstallKey.SetValueSafe("Publisher", App.ProjectOwner);
                uninstallKey.SetValueSafe("ModifyPath", $"\"{Paths.Application}\" -settings");
                uninstallKey.SetValueSafe("QuietUninstallString", $"\"{Paths.Application}\" -uninstall -quiet");
                uninstallKey.SetValueSafe("UninstallString", $"\"{Paths.Application}\" -uninstall");
                uninstallKey.SetValueSafe("HelpLink", App.ProjectHelpLink);
                uninstallKey.SetValueSafe("URLInfoAbout", App.ProjectSupportLink);
                uninstallKey.SetValueSafe("URLUpdateInfo", App.ProjectDownloadLink);
            }

            WindowsRegistry.RegisterPlayer();

            if (App.IsStudioInstalled)
                WindowsRegistry.RegisterStudio();

            if (CreateDesktopShortcuts)
                Shortcut.Create(Paths.Application, "", DesktopShortcut);

            if (CreateStartMenuShortcuts)
                Shortcut.Create(Paths.Application, "", StartMenuShortcut);

            App.Settings.Load(false);
            App.State.Load(false);
            App.FastFlags.Load(false);

            App.Settings.Prop.EnableAnalytics = EnableAnalytics;

            App.Settings.Save();

            App.Logger.WriteLine(LOG_IDENT, "Installation finished");

            if (!IsImplicitInstall)
                App.SendStat("installAction", "install");
        }

        private bool ValidateLocation()
        {
            if (InstallLocation.Length <= 3)
                return false;

            if (InstallLocation.StartsWith("\\\\"))
                return false;

            if (InstallLocation.StartsWith(Path.GetTempPath(), StringComparison.InvariantCultureIgnoreCase)
                || InstallLocation.Contains("\\Temp\\", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (InstallLocation.Contains("OneDrive", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (String.Compare(Directory.GetParent(InstallLocation)?.FullName, Paths.UserProfile, StringComparison.InvariantCultureIgnoreCase) == 0)
                return false;

            if (InstallLocation.Contains("Program Files"))
                return false;

            return true;
        }

        public bool CheckInstallLocation()
        {
            if (string.IsNullOrEmpty(InstallLocation))
            {
                InstallLocationError = Strings.Menu_InstallLocation_NotSet;
            }
            else if (!ValidateLocation())
            {
                InstallLocationError = Strings.Menu_InstallLocation_CantInstall;
            }
            else
            {
                if (!IsImplicitInstall
                    && !InstallLocation.EndsWith(App.ProjectName, StringComparison.InvariantCultureIgnoreCase)
                    && Directory.Exists(InstallLocation)
                    && Directory.EnumerateFileSystemEntries(InstallLocation).Any())
                {
                    string suggestedChange = Path.Combine(InstallLocation, App.ProjectName);

                    MessageBoxResult result = Frontend.ShowMessageBox(
                        String.Format(Strings.Menu_InstallLocation_NotEmpty, suggestedChange),
                        MessageBoxImage.Warning,
                        MessageBoxButton.YesNoCancel,
                        MessageBoxResult.Yes
                    );

                    if (result == MessageBoxResult.Yes)
                        InstallLocation = suggestedChange;
                    else if (result == MessageBoxResult.Cancel || result == MessageBoxResult.None)
                        return false;
                }

                try
                {
                    string testFile = Path.Combine(InstallLocation, $"{App.ProjectName}WriteTest.txt");

                    Directory.CreateDirectory(InstallLocation);
                    File.WriteAllText(testFile, "");
                    File.Delete(testFile);
                }
                catch (UnauthorizedAccessException)
                {
                    InstallLocationError = Strings.Menu_InstallLocation_NoWritePerms;
                }
                catch (Exception ex)
                {
                    InstallLocationError = ex.Message;
                }
            }

            return String.IsNullOrEmpty(InstallLocationError);
        }

        public static void DoUninstall(bool keepData)
        {
            const string LOG_IDENT = "Installer::DoUninstall";

            var processes = new List<Process>();

            if (!String.IsNullOrEmpty(App.PlayerState.Prop.VersionGuid))
                processes.AddRange(Process.GetProcessesByName(App.RobloxPlayerAppName));

            if (App.IsStudioInstalled)
                processes.AddRange(Process.GetProcessesByName(App.RobloxStudioAppName));

            if (processes.Any())
            {
                var result = Frontend.ShowMessageBox(
                    Strings.Bootstrapper_Uninstall_RobloxRunning,
                    MessageBoxImage.Information,
                    MessageBoxButton.OKCancel,
                    MessageBoxResult.OK
                );

                if (result != MessageBoxResult.OK)
                {
                    App.Terminate(ErrorCode.ERROR_CANCELLED);
                    return;
                }

                try
                {
                    foreach (var process in processes)
                    {
                        process.Kill();
                        process.Close();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process! {ex}");
                }
            }

            string robloxFolder = Path.Combine(Paths.LocalAppData, "Roblox");
            bool playerStillInstalled = true;
            bool studioStillInstalled = true;

            using var playerKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player");
            var playerFolder = playerKey?.GetValue("InstallLocation");

            if (playerKey is null || playerFolder is not string)
            {
                playerStillInstalled = false;

                WindowsRegistry.Unregister("roblox");
                WindowsRegistry.Unregister("roblox-player");
            }
            else
            {
                string playerPath = Path.Combine((string)playerFolder, "RobloxPlayerBeta.exe");

                WindowsRegistry.RegisterPlayer(playerPath, "%1");
            }

            using var studioKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio");
            var studioFolder = studioKey?.GetValue("InstallLocation");

            if (studioKey is null || studioFolder is not string)
            {
                studioStillInstalled = false;

                WindowsRegistry.Unregister("roblox-studio");
                WindowsRegistry.Unregister("roblox-studio-auth");

                WindowsRegistry.Unregister("Roblox.Place");
                WindowsRegistry.Unregister(".rbxl");
                WindowsRegistry.Unregister(".rbxlx");
            }
            else
            {
                string studioPath = Path.Combine((string)studioFolder, "RobloxStudioBeta.exe");
                string studioLauncherPath = Path.Combine((string)studioFolder, "RobloxStudioLauncherBeta.exe");

                WindowsRegistry.RegisterStudioProtocol(studioPath, "%1");
                WindowsRegistry.RegisterStudioFileClass(studioPath, "-ide \"%1\"");
            }

            var cleanupSequence = new List<Action>
            {
                () =>
                {
                    foreach (var file in Directory.GetFiles(Paths.Desktop).Where(x => x.EndsWith("lnk")))
                    {
                        var shortcut = ShellLink.Shortcut.ReadFromFile(file);

                        if (shortcut.ExtraData.EnvironmentVariableDataBlock?.TargetUnicode == Paths.Application)
                            File.Delete(file);
                    }
                },

                () => File.Delete(StartMenuShortcut),

                () => Directory.Delete(Paths.Versions, true),
                () => Directory.Delete(Paths.Downloads, true),

                () => File.Delete(App.State.FileLocation)
            };

            if (!keepData)
            {
                cleanupSequence.AddRange(new List<Action>
                {
                    () => Directory.Delete(Paths.Modifications, true),
                    () => Directory.Delete(Paths.Logs, true),

                    () => File.Delete(App.Settings.FileLocation)
                });
            }

            bool deleteFolder = Directory.GetFiles(Paths.Base).Length <= 3;

            if (deleteFolder)
                cleanupSequence.Add(() => Directory.Delete(Paths.Base, true));

            if (!playerStillInstalled && !studioStillInstalled && Directory.Exists(robloxFolder))
                cleanupSequence.Add(() => Directory.Delete(robloxFolder, true));

            cleanupSequence.Add(() => Registry.CurrentUser.DeleteSubKey(App.UninstallKey));

            foreach (var process in cleanupSequence)
            {
                try
                {
                    process();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Encountered exception when running cleanup sequence (#{cleanupSequence.IndexOf(process)})");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            if (Directory.Exists(Paths.Base))
            {
                string deleteCommand;

                if (deleteFolder)
                    deleteCommand = $"del /Q \"{Paths.Base}\\*\" && rmdir \"{Paths.Base}\"";
                else
                    deleteCommand = $"del /Q \"{Paths.Application}\"";

                Process.Start(new ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout 5 && {deleteCommand}",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }

            App.SendStat("installAction", "uninstall");
        }

        private const string ReplacedSuffix = ".old";

        private static bool ReplaceApplication(string LOG_IDENT)
        {
            for (int i = 1; i <= 6; i++)
            {
                try
                {
                    File.Copy(Paths.Process, Paths.Application, true);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (i == 1)
                        App.Logger.WriteLine(LOG_IDENT, "The installed exe is in use, waiting a moment");

                    Thread.Sleep(500);
                }
            }

            string aside = $"{Paths.Application}.{DateTime.Now:yyyyMMddHHmmss}{ReplacedSuffix}";

            try
            {
                File.Move(Paths.Application, aside);
                App.Logger.WriteLine(LOG_IDENT, "Moved the running installed exe aside to replace it");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to update! (The installed exe could not be replaced or moved aside)");
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }

            try
            {
                File.Copy(Paths.Process, Paths.Application, true);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to update! (Could not copy the new version in)");
                App.Logger.WriteException(LOG_IDENT, ex);

                try { File.Move(aside, Paths.Application, true); }
                catch (Exception restoreEx) { App.Logger.WriteException(LOG_IDENT, restoreEx); }

                return false;
            }
        }

        private static void DeleteReplacedExecutables()
        {
            try
            {
                string? folder = Path.GetDirectoryName(Paths.Application);
                if (folder is null || !Directory.Exists(folder))
                    return;

                foreach (string file in Directory.EnumerateFiles(folder, Path.GetFileName(Paths.Application) + ".*" + ReplacedSuffix))
                {
                    try { File.Delete(file); }
                    catch (Exception) {  }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void UpgradeFailed(string? version)
        {
            Frontend.ShowMessageBox(
                string.Format(Strings.Bootstrapper_AutoUpdateFailed, version ?? "?"),
                MessageBoxImage.Warning);
        }

        private static bool IsLooseBuild(string executable)
        {
            try
            {
                string? folder = Path.GetDirectoryName(executable);
                if (string.IsNullOrEmpty(folder))
                    return false;

                string sibling = Path.Combine(folder, Path.GetFileNameWithoutExtension(executable) + ".dll");
                return File.Exists(sibling) && File.Exists(Path.Combine(folder, "PhasmaStrap.runtimeconfig.json"));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Installer::IsLooseBuild", ex);
                return false;
            }
        }

        public static void HandleUpgrade()
        {
            const string LOG_IDENT = "Installer::HandleUpgrade";

            DeleteReplacedExecutables();

            if (!File.Exists(Paths.Application) || Paths.Process == Paths.Application)
                return;

            if (IsLooseBuild(Paths.Process) && !IsLooseBuild(Paths.Application))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Refusing to replace the installed copy with a loose build from {Paths.Process}");

                Frontend.ShowMessageBox(
                    "This is a development build running from its build folder, so it was not copied over your installed PhasmaStrap.\n\n"
                    + "A loose build needs the files next to it to run, and the installed copy is a single file. Replacing one with the other leaves PhasmaStrap unable to start.\n\n"
                    + "Publish a single-file build if you want to update your install.",
                    MessageBoxImage.Warning,
                    MessageBoxButton.OK
                );

                return;
            }

            bool isAutoUpgrade = App.LaunchSettings.UpgradeFlag.Active
                || Paths.Process.StartsWith(Path.Combine(Paths.Base, "Updates"))
                || Paths.Process.StartsWith(Path.Combine(Paths.LocalAppData, "Temp"))
                || Paths.Process.StartsWith(Paths.TempUpdates);

            var existingVer = FileVersionInfo.GetVersionInfo(Paths.Application).ProductVersion;
            var currentVer = FileVersionInfo.GetVersionInfo(Paths.Process).ProductVersion;

            if (MD5Hash.FromFile(Paths.Process) == MD5Hash.FromFile(Paths.Application))
                return;

            if (currentVer is not null && existingVer is not null && Utilities.CompareVersions(currentVer, existingVer) == VersionComparison.LessThan)
            {
                var result = Frontend.ShowMessageBox(
                    Strings.InstallChecker_VersionLessThanInstalled,
                    MessageBoxImage.Question,
                    MessageBoxButton.YesNo
                );

                if (result != MessageBoxResult.Yes)
                    return;
            }

            if (!isAutoUpgrade)
            {
                var result = Frontend.ShowMessageBox(
                    Strings.InstallChecker_VersionDifferentThanInstalled,
                    MessageBoxImage.Question,
                    MessageBoxButton.YesNo
                );

                if (result != MessageBoxResult.Yes)
                    return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Doing upgrade");

            Filesystem.AssertReadOnly(Paths.Application);

            CloudFiles.Hydrate(Paths.Application);

            using (var ipl = new InterProcessLock("AutoUpdater", TimeSpan.FromSeconds(30)))
            {
                if (!ipl.IsAcquired)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to update! (Could not obtain singleton mutex)");
                    UpgradeFailed(currentVer);
                    return;
                }
            }

            if (!ReplaceApplication(LOG_IDENT))
            {
                UpgradeFailed(currentVer);
                return;
            }

            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("DisplayVersion", App.Version);

                uninstallKey.SetValueSafe("Publisher", App.ProjectOwner);
                uninstallKey.SetValueSafe("HelpLink", App.ProjectHelpLink);
                uninstallKey.SetValueSafe("URLInfoAbout", App.ProjectSupportLink);
                uninstallKey.SetValueSafe("URLUpdateInfo", App.ProjectDownloadLink);
            }

            if (existingVer is not null)
            {
                if (Utilities.CompareVersions(existingVer, "2.2.0") == VersionComparison.LessThan)
                {
                    string path = Path.Combine(Paths.Integrations, "rbxfpsunlocker");

                    try
                    {
                        if (Directory.Exists(path))
                            Directory.Delete(path, true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }

                if (Utilities.CompareVersions(existingVer, "2.3.0") == VersionComparison.LessThan)
                {
                    string injectorLocation = Path.Combine(Paths.Modifications, "dxgi.dll");
                    string configLocation = Path.Combine(Paths.Modifications, "ReShade.ini");

                    if (File.Exists(injectorLocation))
                        File.Delete(injectorLocation);

                    if (File.Exists(configLocation))
                        File.Delete(configLocation);
                }

                if (Utilities.CompareVersions(existingVer, "2.6.0") == VersionComparison.LessThan)
                {
                    if (App.Settings.Prop.UseDisableAppPatch)
                    {
                        try
                        {
                            File.Delete(Path.Combine(Paths.Modifications, "ExtraContent\\places\\Mobile.rbxl"));
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }

                        App.Settings.Prop.EnableActivityTracking = true;
                    }

                    if (App.Settings.Prop.BootstrapperStyle == BootstrapperStyle.ClassicFluentDialog)
                        App.Settings.Prop.BootstrapperStyle = BootstrapperStyle.FluentDialog;
                }

                if (Utilities.CompareVersions(existingVer, "2.8.0") == VersionComparison.LessThan)
                {
                    if (isAutoUpgrade)
                    {
                        if (App.LaunchSettings.Args.Length == 0)
                            App.LaunchSettings.RobloxLaunchMode = LaunchMode.Player;

                        string? query = App.LaunchSettings.Args.FirstOrDefault(x => x.Contains("roblox"));

                        if (query is not null)
                        {
                            App.LaunchSettings.RobloxLaunchMode = LaunchMode.Player;
                            App.LaunchSettings.RobloxLaunchArgs = query;
                        }
                    }

                    string oldDesktopPath = Path.Combine(Paths.Desktop, "Play Roblox.lnk");
                    string oldStartPath = Path.Combine(Paths.WindowsStartMenu, "PhasmaStrap");

                    if (File.Exists(oldDesktopPath))
                        File.Move(oldDesktopPath, DesktopShortcut, true);

                    if (Directory.Exists(oldStartPath))
                    {
                        try
                        {
                            Directory.Delete(oldStartPath, true);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }

                        Shortcut.Create(Paths.Application, "", StartMenuShortcut);
                    }

                    Registry.CurrentUser.DeleteSubKeyTree("Software\\PhasmaStrap", false);

                    WindowsRegistry.RegisterPlayer();
                }

                if (Utilities.CompareVersions(existingVer, "2.8.2") == VersionComparison.LessThan)
                {
                    string robloxDirectory = Path.Combine(Paths.Base, "Roblox");

                    if (Directory.Exists(robloxDirectory))
                    {
                        try
                        {
                            Directory.Delete(robloxDirectory, true);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Failed to delete the Roblox directory");
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }
                    }
                }

                if (Utilities.CompareVersions(existingVer, "2.11.0") == VersionComparison.LessThan)
                {
                    JsonManager<RobloxState> legacyRobloxState = new();

                    if (legacyRobloxState.IsSaved)
                    {
                        if (legacyRobloxState.Load(false))
                        {
                            App.PlayerState.Prop.VersionGuid = legacyRobloxState.Prop.Player.VersionGuid;
                            App.PlayerState.Prop.PackageHashes = legacyRobloxState.Prop.Player.PackageHashes;
                            App.PlayerState.Prop.Size = legacyRobloxState.Prop.Player.Size;
                            App.PlayerState.Prop.ModManifest = legacyRobloxState.Prop.ModManifest;

                            App.StudioState.Prop.VersionGuid = legacyRobloxState.Prop.Studio.VersionGuid;
                            App.StudioState.Prop.PackageHashes = legacyRobloxState.Prop.Studio.PackageHashes;
                            App.StudioState.Prop.Size = legacyRobloxState.Prop.Studio.Size;
                        }

                        legacyRobloxState.Delete();
                    }
                }

                if (Utilities.CompareVersions(existingVer, "2.11.3") == VersionComparison.LessThan)
                {
                    App.FastFlags.SetValue("FFlagDebugGraphicsPreferD3D11", null);
                    App.FastFlags.SetValue("FFlagDebugGraphicsPreferD3D11FL10", null);
                }

                App.Settings.Save();
                App.FastFlags.Save();
                App.State.Save();

                if (App.PlayerState.Loaded)
                    App.PlayerState.Save();

                if (App.StudioState.Loaded)
                    App.StudioState.Save();
            }

            if (currentVer is null)
                return;

            App.SendStat("installAction", "upgrade");

            if (isAutoUpgrade)
            {
#pragma warning disable CS0162
                if (OpenReleaseNotes)
                {
                    string releaseNoteVersion;
                    if (!string.IsNullOrEmpty(ForcedReleaseNotesVersion) && Version.TryParse(ForcedReleaseNotesVersion, out _))
                    {
                        if (!string.IsNullOrEmpty(existingVer))
                        {
                            VersionComparison compareResult = Utilities.CompareVersions(existingVer, ForcedReleaseNotesVersion);
                            if (compareResult == VersionComparison.Equal || compareResult == VersionComparison.GreaterThan)
                                return;
                        }

                        releaseNoteVersion = ForcedReleaseNotesVersion;
                    }
                    else
                    {
                        releaseNoteVersion = currentVer;
                    }

                    Utilities.ShellExecute($"https://github.com/{App.ProjectRepository}/wiki/Release-notes-for-PhasmaStrap-v{releaseNoteVersion}");
                }
#pragma warning restore CS0162
            }
            else
            {
                Frontend.ShowMessageBox(
                    string.Format(Strings.InstallChecker_Updated, currentVer),
                    MessageBoxImage.Information,
                    MessageBoxButton.OK
                );
            }
        }
    }
}
