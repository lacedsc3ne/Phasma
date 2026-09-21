using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;

using Microsoft.Win32;

namespace PhasmaStrap
{
    public partial class App : Application
    {
#if QA_BUILD
        public const string ProjectName = "PhasmaStrap-QA";
#else
        public const string ProjectName = "PhasmaStrap";
#endif
        public const string ProjectOwner = "lacedsc3ne";
        public const string ProjectRepository = "lacedsc3ne/PhasmaStrap";

        public const string ServerBase = "https://api.phasmastrap.com";
        public const string ProjectDownloadLink = "https://github.com/lacedsc3ne/PhasmaStrap";
        public const string ProjectHelpLink = "https://github.com/lacedsc3ne/PhasmaStrap/wiki";
        public const string ProjectDiscordLink = "https://discord.gg/x4M4cZS4p7";
        public const string ProjectDonateLink = "https://ko-fi.com/lacedscene";
        public const string ProjectSupportLink = ProjectDiscordLink;

        public const string RobloxPlayerAppName = "RobloxPlayerBeta";
        public const string RobloxStudioAppName = "RobloxStudioBeta";

        public const string UninstallKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProjectName}";

        public static LaunchSettings LaunchSettings { get; private set; } = null!;

        public static BuildMetadataAttribute BuildMetadata = Assembly.GetExecutingAssembly().GetCustomAttribute<BuildMetadataAttribute>()!;

        public static string Version = Assembly.GetExecutingAssembly().GetName().Version!.ToString()[..^2];

        public static Bootstrapper? Bootstrapper { get; set; } = null!;

        public static bool IsActionBuild => !String.IsNullOrEmpty(BuildMetadata.CommitRef);

        public static bool IsProductionBuild => IsActionBuild && BuildMetadata.CommitRef.StartsWith("tag", StringComparison.Ordinal);

        public static bool IsPlayerInstalled => App.PlayerState.IsSaved && !String.IsNullOrEmpty(App.PlayerState.Prop.VersionGuid);

        public static bool IsStudioInstalled => App.StudioState.IsSaved && !String.IsNullOrEmpty(App.StudioState.Prop.VersionGuid);

        public static readonly MD5 MD5Provider = MD5.Create();

        public static readonly Logger Logger = new();

        public static StudioRichPresence? StudioRichPresence;

        public static readonly Dictionary<string, BaseTask> PendingSettingTasks = new();

        public static readonly JsonManager<Settings> Settings = new();

        public static readonly JsonManager<State> State = new();

        public static readonly LazyJsonManager<DistributionState> PlayerState = new(nameof(PlayerState));

        public static readonly LazyJsonManager<DistributionState> StudioState = new(nameof(StudioState));

        public static readonly FastFlagManager FastFlags = new();

        public static readonly Utility.FlagProfileManager FlagProfiles = new();

        public static readonly HttpClient HttpClient = new(
            new HttpClientLoggingHandler(
                new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }
            )
        );

        private static bool _showingExceptionDialog = false;

        private static string? _webUrl = null;
        public static string WebUrl
        {
            get {
                if (_webUrl != null)
                    return _webUrl;

                string url = ConstructPhasmaStrapWebUrl();
                if (Settings.Loaded)
                    _webUrl = url;
                return url;
            }
        }

        public static void Terminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;

            Logger.WriteLine("App::Terminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");

            var failsafe = new Thread(() =>
            {
                Thread.Sleep(5000);
                Logger.WriteLine("App::Terminate", "Exit is taking too long, ending the process");
                try { Process.GetCurrentProcess().Kill(); } catch (Exception) { }
            })
            { IsBackground = true };

            failsafe.Start();

            Environment.Exit(exitCodeNum);
        }

        public static void SoftTerminate(ErrorCode exitCode = ErrorCode.ERROR_SUCCESS)
        {
            int exitCodeNum = (int)exitCode;

            Logger.WriteLine("App::SoftTerminate", $"Terminating with exit code {exitCodeNum} ({exitCode})");

            Current.Dispatcher.Invoke(() => Current.Shutdown(exitCodeNum));
        }

        void GlobalExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            Logger.WriteLine("App::GlobalExceptionHandler", "An exception occurred");

            FinalizeExceptionHandling(e.Exception);
        }

        public static void FinalizeExceptionHandling(AggregateException ex)
        {
            foreach (var innerEx in ex.InnerExceptions)
                Logger.WriteException("App::FinalizeExceptionHandling", innerEx);

            FinalizeExceptionHandling(ex.GetBaseException(), false);
        }

        public static void FinalizeExceptionHandling(Exception ex, bool log = true)
        {
            if (log)
                Logger.WriteException("App::FinalizeExceptionHandling", ex);

            if (_showingExceptionDialog)
                return;

            _showingExceptionDialog = true;

            SendLog();

            if (Bootstrapper?.Dialog != null)
            {
                if (Bootstrapper.Dialog.TaskbarProgressValue == 0)
                    Bootstrapper.Dialog.TaskbarProgressValue = 1;

                Bootstrapper.Dialog.TaskbarProgressState = TaskbarItemProgressState.Error;
            }

            Frontend.ShowExceptionDialog(ex);

            Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
        }

        public static string ConstructPhasmaStrapWebUrl()
        {
            return "api.phasmastrap.com";
        }

        public static bool CanSendLogs()
        {
            if (!Settings.Prop.DeveloperMode || Settings.Prop.WebEnvironment == WebEnvironment.Production)
                return IsProductionBuild;

            return true;
        }

        public static async Task<GithubRelease?> GetLatestRelease()
        {
            const string LOG_IDENT = "App::GetLatestRelease";

            try
            {
                var releaseInfo = await GetReleaseJson<GithubRelease>("/v1/releases/latest", "/releases/latest");

                if (releaseInfo is null || releaseInfo.Assets is null)
                {
                    Logger.WriteLine(LOG_IDENT, "Encountered invalid data");
                    return null;
                }

                return releaseInfo;
            }
            catch (Exception ex)
            {
                Logger.WriteException(LOG_IDENT, ex);
            }

            return null;
        }

        public static async Task<T> GetReleaseJson<T>(string serverPath, string githubPath)
        {
            if (ServerBase.Length > 0)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var response = await HttpClient.GetAsync(ServerBase + serverPath, timeout.Token);
                    response.EnsureSuccessStatusCode();

                    T? result = JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(timeout.Token));
                    if (result is not null)
                        return result;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("App::GetReleaseJson", $"PhasmaStrap's server didn't answer ({ex.Message}) - asking GitHub");
                }
            }

            return await Http.GetJson<T>($"https://api.github.com/repos/{ProjectRepository}{githubPath}");
        }

        public static async void SendStat(string key, string value)
        {
            if (!Settings.Prop.EnableAnalytics)
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{WebUrl}/metrics/post?key={key}&value={value}");
                Utility.PhasmaAccount.Authorize(request);
                using var response = await HttpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                Logger.WriteException("App::SendStat", ex);
            }
        }

        public static async void SendLog()
        {
            if (!Settings.Prop.EnableAnalytics || !CanSendLogs())
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{WebUrl}/metrics/post-exception")
                {
                    Content = new StringContent(Logger.AsDocument)
                };
                Utility.PhasmaAccount.Authorize(request);
                using var response = await HttpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                Logger.WriteException("App::SendLog", ex);
            }
        }

        public static void AssertWindowsOSVersion()
        {
            const string LOG_IDENT = "App::AssertWindowsOSVersion";

            int major = Environment.OSVersion.Version.Major;
            if (major < 10)
            {
                Logger.WriteLine(LOG_IDENT, $"Detected unsupported Windows version ({Environment.OSVersion.Version}).");

                if (!LaunchSettings.QuietFlag.Active)
                    Frontend.ShowMessageBox(Strings.App_OSDeprecation_Win7_81, MessageBoxImage.Error);

                Terminate(ErrorCode.ERROR_INVALID_FUNCTION);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string LOG_IDENT = "App::OnStartup";

            Locale.Initialize();

            base.OnStartup(e);

            Logger.WriteLine(LOG_IDENT, $"Starting {ProjectName} v{Version}");

            string userAgent = $"{ProjectName}/{Version}";

            if (IsActionBuild)
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from commit {BuildMetadata.CommitHash} ({BuildMetadata.CommitRef})");

                if (IsProductionBuild)
                    userAgent += $" (Production)";
                else
                    userAgent += $" (Artifact {BuildMetadata.CommitHash}, {BuildMetadata.CommitRef})";
            }
            else
            {
                Logger.WriteLine(LOG_IDENT, $"Compiled {BuildMetadata.Timestamp.ToFriendlyString()} from {BuildMetadata.Machine}");

#if QA_BUILD
                userAgent += " (QA)";
#else
                userAgent += $" (Build {Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildMetadata.Machine))})";
#endif
            }

            Logger.WriteLine(LOG_IDENT, $"OSVersion: {Environment.OSVersion}");

            Logger.WriteLine(LOG_IDENT, $"Loaded from {Paths.Process}");
            Logger.WriteLine(LOG_IDENT, $"Temp path is {Paths.Temp}");
            Logger.WriteLine(LOG_IDENT, $"WindowsStartMenu path is {Paths.WindowsStartMenu}");

            ApplicationConfiguration.Initialize();

            HttpClient.Timeout = TimeSpan.FromSeconds(30);
            HttpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

            LaunchSettings = new LaunchSettings(e.Args);

            if (LaunchSettings.ClassicRedirectFlag.Active)
            {
                bool enable = string.Equals(LaunchSettings.ClassicRedirectFlag.Data, "on", StringComparison.OrdinalIgnoreCase);
                Integrations.ClassicHostRedirect.Set(enable);
                Terminate();
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Integrations.ClassicServerManager.Stop();

            _ = Task.Run(() =>
            {
                try { Integrations.ClassicHostRedirect.CleanStaleRedirect(); }
                catch (Exception ex) { Logger.WriteLine("App::OnStartup", $"Stale classic redirect cleanup failed: {ex.Message}"); }
            });

            if (LaunchSettings.ApplyHostsFlag.Active)
            {
                Shutdown(Networking.HostsElevation.ApplyElevated(LaunchSettings.ApplyHostsFlag.Data) ? 0 : 1);
                return;
            }

            if (LaunchSettings.WriteProxyHostsFlag.Active)
            {
                Shutdown(Networking.HostsFileManager.WriteBlockElevated() ? 0 : 1);
                return;
            }

            if (LaunchSettings.RemoveProxyHostsFlag.Active)
            {
                Shutdown(Networking.HostsFileManager.RemoveBlockElevated() ? 0 : 1);
                return;
            }

            if (LaunchSettings.WriteTelemetryBlockFlag.Active)
            {
                Shutdown(Integrations.TelemetryBlocker.ApplyElevated() ? 0 : 1);
                return;
            }

            if (LaunchSettings.RemoveTelemetryBlockFlag.Active)
            {
                Shutdown(Integrations.TelemetryBlocker.RemoveElevated() ? 0 : 1);
                return;
            }

            if (LaunchSettings.PurgeStandbyFlag.Active)
            {
                Shutdown(Utility.SystemMemoryCleaner.PurgeStandbyListNow() ? 0 : 1);
                return;
            }

            using var uninstallKey = Registry.CurrentUser.OpenSubKey(UninstallKey);
            string? installLocation = null;
            bool fixInstallLocation = false;

            if (uninstallKey?.GetValue("InstallLocation") is string value)
            {
                if (Directory.Exists(value))
                {
                    installLocation = value;
                }
                else
                {
                    var match = Regex.Match(value, @"^[a-zA-Z]:\\Users\\([^\\]+)", RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        string newLocation = value.Replace(match.Value, Paths.UserProfile, StringComparison.InvariantCultureIgnoreCase);

                        if (Directory.Exists(newLocation))
                        {
                            installLocation = newLocation;
                            fixInstallLocation = true;
                        }
                    }
                }
            }

            if (installLocation is null && Directory.GetParent(Paths.Process)?.FullName is string processDir)
            {
                var files = Directory.GetFiles(processDir).Select(x => Path.GetFileName(x)).ToArray();

                if (files.Length <= 3 && files.Contains("Settings.json") && files.Contains("State.json"))
                {
                    installLocation = processDir;
                    fixInstallLocation = true;
                }
            }

            if (fixInstallLocation && installLocation is not null)
            {
                var installer = new Installer
                {
                    InstallLocation = installLocation,
                    IsImplicitInstall = true
                };

                if (installer.CheckInstallLocation())
                {
                    Logger.WriteLine(LOG_IDENT, $"Changing install location to '{installLocation}'");
                    installer.DoInstall();
                }
                else
                {
                    installLocation = null;
                }
            }

            if (installLocation is null)
            {
                Logger.Initialize(true);
                Logger.WriteLine(LOG_IDENT, "Not installed, launching the installer");
                AssertWindowsOSVersion();
                LaunchHandler.LaunchInstaller();
            }
            else
            {
                Paths.Initialize(installLocation);

                Logger.WriteLine(LOG_IDENT, "Entering main logic");

                if (Paths.Process != Paths.Application && !File.Exists(Paths.Application))
                {
                    Logger.WriteLine(LOG_IDENT, "Copying to install directory");
                    File.Copy(Paths.Process, Paths.Application);
                }

                Logger.Initialize(LaunchSettings.UninstallFlag.Active);

                if (!Logger.Initialized && !Logger.NoWriteMode)
                {
                    Logger.WriteLine(LOG_IDENT, "Possible duplicate launch detected, terminating.");
                    Terminate();
                }

                Settings.Load();
                State.Load();
                FastFlags.Load();
                FlagProfiles.Load(false);

                if (Settings.Prop.FastFlagPlacePresets.Count > 0)
                    FlagProfiles.MigrateFromPlacePresets();

                if (Settings.Prop.SmoothProgressBarsEnabled)
                    UI.SmoothProgress.Install();

                try
                {
                    UI.AppFont.Initialize();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"App font initialization failed: {ex.Message}");
                }

                try
                {
                    WindowsRegistry.RegisterPlayer();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Protocol handler registration failed: {ex.Message}");
                }

                try
                {
                    Integrations.SystemPerformanceBoost.ApplyGpuPreference();
                    Integrations.SystemPerformanceBoost.ApplyGameDvr();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"System performance boost setup failed: {ex.Message}");
                }

                try
                {
                    CpuCoreLimiter.ApplyConfiguredLimit();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"CPU core limiter startup failed: {ex.Message}");
                }

                try
                {
                    if (Settings.Prop.StudioPluginEnabled)
                    {
                        StudioBridge.Start();
                        StudioPluginInstaller.EnsureInstalled();
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Studio companion startup failed: {ex.Message}");
                }

                try
                {
                    if (Settings.Prop.StudioRichPresenceEnabled)
                        StudioRichPresence = new StudioRichPresence();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Studio Rich Presence startup failed: {ex.Message}");
                }

                try
                {
                    if (Settings.Prop.AutoCleanRam)
                        Utility.AutoRamCleaner.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Auto RAM cleaner startup failed: {ex.Message}");
                }

                try
                {
                    if (Settings.Prop.FriendActivityAlertsEnabled)
                        Utility.FriendActivityMonitor.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Friend activity monitor startup failed: {ex.Message}");
                }

                try
                {
                    if (Settings.Prop.PartyEnabled)
                        Utility.PartyBackground.StartIfWanted();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Party watcher startup failed: {ex.Message}");
                }

                try
                {
                    Utility.AccountNotices.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Account notices startup failed: {ex.Message}");
                }

                try
                {
                    if (Settings.Prop.EnableActivityTracking && Settings.Prop.WatchExternalLaunches && !LaunchSettings.WatcherFlag.Active)
                        Utility.RobloxSessionWatch.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LOG_IDENT, $"Roblox session watch startup failed: {ex.Message}");
                }

                _ = Task.Run(() =>
                {
                    try
                    {
                        Networking.NetworkingController.ReconcileOnStartup();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine(LOG_IDENT, $"Networking proxy reconciliation failed: {ex.Message}");
                    }
                });

                if (!Locale.SupportedLocales.ContainsKey(Settings.Prop.Locale))
                {
                    Settings.Prop.Locale = "nil";
                    Settings.Save();
                }

                Logger.WriteLine(LOG_IDENT, $"Developer mode: {Settings.Prop.DeveloperMode}");
                Logger.WriteLine(LOG_IDENT, $"Web environment: {Settings.Prop.WebEnvironment}");

                Locale.Set(Settings.Prop.Locale);

                if (!LaunchSettings.BypassUpdateCheck)
                    Installer.HandleUpgrade();

                Utility.SettingsHotReload.Start();

                LaunchHandler.ProcessLaunchArgs();
            }

            Logger.WriteLine(LOG_IDENT, "Startup finished");
        }
    }
}
