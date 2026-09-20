#if DEBUG_UPDATER
#warning "Automatic updater debugging is enabled"
#endif

using System.ComponentModel;
using System.Data;
using System.Web;
using System.Windows;
using PhasmaStrap.Integrations;
using System.Windows.Forms;
using System.Windows.Shell;

using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

using System.Collections.Concurrent;
using System.Net.Http.Headers;

using PhasmaStrap.AppData;
using PhasmaStrap.RobloxInterfaces;
using PhasmaStrap.UI.Elements.Bootstrapper.Base;

using ICSharpCode.SharpZipLib.Zip;

namespace PhasmaStrap
{
    public class Bootstrapper
    {
        #region Properties
        private const int ProgressBarMaximum = 10000;

        private const double TaskbarProgressMaximumWpf = 1;
        private const int TaskbarProgressMaximumWinForms = WinFormsDialogBase.TaskbarProgressMaximum;

        private const string AppSettings =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<Settings>\r\n" +
            "	<ContentFolder>content</ContentFolder>\r\n" +
            "	<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
            "</Settings>\r\n";

        private readonly FastZipEvents _fastZipEvents = new();
        private readonly CancellationTokenSource _cancelTokenSource = new();

        private IAppData AppData = default!;
        private LaunchMode _launchMode;

        private string _launchCommandLine = App.LaunchSettings.RobloxLaunchArgs;
        private Version? _latestVersion = null;
        private string _latestVersionGuid = null!;
        private string _latestVersionDirectory = null!;
        private PackageManifest _versionPackageManifest = null!;

        private bool _usingKeptVersion;
        private string _versionManifestText = "";
        private bool _channelFetched = false;

        private bool _isInstalling = false;
        private double _progressIncrement;
        private double _taskbarProgressIncrement;
        private double _taskbarProgressMaximum;
        private long _totalDownloadedBytes = 0;
        private bool _packageExtractionSuccess = true;

        private SemaphoreSlim? _downloadRequestThrottle;

        private bool _mustUpgrade => App.LaunchSettings.ForceFlag.Active || App.State.Prop.ForceReinstall || String.IsNullOrEmpty(AppData.DistributionState.VersionGuid) || !File.Exists(AppData.ExecutablePath);
        private bool _noConnection = false;

        private AsyncMutex? _mutex;

        private int _appPid = 0;

        public IBootstrapperDialog? Dialog = null;

        public bool IsStudioLaunch => _launchMode != LaunchMode.Player;

        public string MutexName => $"{MutexNamePrefix}-{_launchMode}";
        public string BackgroundUpdaterMutexName => $"PhasmaStrap-BackgroundUpdater-{_launchMode}";

        public string MutexNamePrefix { get; set; } = "PhasmaStrap-Bootstrapper";
        public bool QuitIfMutexExists { get; set; } = false;
        #endregion

        #region Core
        public Bootstrapper(LaunchMode launchMode)
        {
            _launchMode = launchMode;

            _fastZipEvents.FileFailure += (_, e) =>
            {
                if (!e.Name.EndsWith(".ttf"))
                    throw e.Exception;

                App.Logger.WriteLine("FastZipEvents::OnFileFailure", $"Failed to extract {e.Name}");
                _packageExtractionSuccess = false;
            };
            _fastZipEvents.DirectoryFailure += (_, e) => throw e.Exception;
            _fastZipEvents.ProcessFile += (_, e) => e.ContinueRunning = !_cancelTokenSource.IsCancellationRequested;

            SetupAppData();
        }

        private void SetupAppData()
        {
            AppData = IsStudioLaunch ? new RobloxStudioData() : new RobloxPlayerData();
            Deployment.BinaryType = AppData.BinaryType;
        }

        private void SetStatus(string message)
        {
            App.Logger.WriteLine("Bootstrapper::SetStatus", message);

            message = message.Replace("{product}", AppData.ProductName);

            if (Dialog is not null)
                Dialog.Message = message;
        }

        private void UpdateProgressBar()
        {
            if (Dialog is null)
                return;

            if (System.Windows.Application.Current is not null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(UpdateProgressBar));
                return;
            }

            int progressValue = (int)Math.Floor(_progressIncrement * _totalDownloadedBytes);

            progressValue = Math.Clamp(progressValue, 0, ProgressBarMaximum);

            Dialog.ProgressValue = progressValue;

            double taskbarProgressValue = _taskbarProgressIncrement * _totalDownloadedBytes;
            taskbarProgressValue = Math.Clamp(taskbarProgressValue, 0, _taskbarProgressMaximum);

            Dialog.TaskbarProgressValue = taskbarProgressValue;
        }

        private void HandleConnectionError(Exception exception)
        {
            const string LOG_IDENT = "Bootstrapper::HandleConnectionError";

            _noConnection = true;

            App.Logger.WriteLine(LOG_IDENT, "Connectivity check failed");
            App.Logger.WriteException(LOG_IDENT, exception);

            string message = Strings.Dialog_Connectivity_BadConnection;

            if (exception is AggregateException)
                exception = exception.InnerException!;

            if (exception is HttpRequestException && exception.InnerException is null)
                message = String.Format(Strings.Dialog_Connectivity_RobloxDown, "[status.roblox.com](https://status.roblox.com)");

            if (_mustUpgrade)
                message += $"\n\n{Strings.Dialog_Connectivity_RobloxUpgradeNeeded}\n\n{Strings.Dialog_Connectivity_TryAgainLater}";
            else
                message += $"\n\n{Strings.Dialog_Connectivity_RobloxUpgradeSkip}";

            Frontend.ShowConnectivityDialog(
                String.Format(Strings.Dialog_Connectivity_UnableToConnect, "Roblox"),
                message,
                _mustUpgrade ? MessageBoxImage.Error : MessageBoxImage.Warning,
                exception);

            if (_mustUpgrade)
                App.Terminate(ErrorCode.ERROR_CANCELLED);
        }

        public async Task Run()
        {
            const string LOG_IDENT = "Bootstrapper::Run";

            App.Logger.WriteLine(LOG_IDENT, "Running bootstrapper");

            if (Dialog is not null)
                Dialog.CancelEnabled = true;

            SetStatus(Strings.Bootstrapper_Status_Connecting);

            var connectionResult = await Deployment.InitializeConnectivity();

            App.Logger.WriteLine(LOG_IDENT, "Connectivity check finished");

            if (connectionResult is not null)
                HandleConnectionError(connectionResult);

#if (!DEBUG || DEBUG_UPDATER) && !QA_BUILD
            if (App.Settings.Prop.CheckForUpdates && !App.LaunchSettings.UpgradeFlag.Active)
            {
                bool updatePresent = await CheckForUpdates();

                if (updatePresent)
                    return;
            }
#endif

            App.AssertWindowsOSVersion();

            if (_launchMode == LaunchMode.Unknown)
            {
                await SafeGetLatestVersionInfo();

                if (_launchMode == LaunchMode.Unknown)
                    throw new ApplicationException("Failed to deduce launch type");
            }

            bool mutexExists = Utilities.DoesMutexExist(MutexName);

            if (mutexExists)
            {
                if (!QuitIfMutexExists)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{MutexName} mutex exists, waiting...");
                    SetStatus(Strings.Bootstrapper_Status_WaitingOtherInstances);
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{MutexName} mutex exists, exiting!");
                    return;
                }
            }

            await using var mutex = new AsyncMutex(false, MutexName);
            await mutex.AcquireAsync(_cancelTokenSource.Token);

            _mutex = mutex;

            if (mutexExists)
            {
                App.Settings.Load();
                App.State.Load();
                AppData.DistributionStateManager.Load();
            }

            await SafeGetLatestVersionInfo();

            CleanupVersionsFolder();

            bool allModificationsApplied = true;

            if (!_noConnection)
            {
                if (_usingKeptVersion && AppData.DistributionState.VersionGuid != _latestVersionGuid)
                    SwitchToKeptVersion();

                if (AppData.DistributionState.VersionGuid != _latestVersionGuid || _mustUpgrade)
                {
                    bool backgroundUpdaterMutexOpen = !App.LaunchSettings.BackgroundUpdaterFlag.Active && Utilities.DoesMutexExist(BackgroundUpdaterMutexName);

                    App.Logger.WriteLine(LOG_IDENT, $"Background updater running: {backgroundUpdaterMutexOpen}");

                    if (backgroundUpdaterMutexOpen && _mustUpgrade)
                    {
                        Utilities.KillBackgroundUpdater();
                        backgroundUpdaterMutexOpen = false;
                    }

                    if (!backgroundUpdaterMutexOpen)
                    {
                        if (IsEligibleForBackgroundUpdate())
                            StartBackgroundUpdater();
                        else
                            await UpgradeRoblox();
                    }
                }

                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                allModificationsApplied = await ApplyModifications();

                if (App.Settings.Prop.NetworkingProxyEnabled)
                {
                    try
                    {
                        Networking.AssetProxyCA.PatchRobloxTrustBundles();

                        if (!Networking.AssetProxyCA.IsRobloxTrustBundlePatched())
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Roblox's trust bundle does not contain the proxy certificate after patching - spoofers will not work this session");
                            UI.NotificationCenter.Notify("Proxy certificate not accepted", "Roblox's certificate bundle could not be patched, so Asset Warp and the spoofers won't work this session. Check the Networking page.", UI.NotificationCategory.General, kind: UI.NotificationKindId.ProxyCertificate);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not patch Roblox's trust bundle: {ex.Message}");
                    }
                }
            }

            if (IsStudioLaunch)
                WindowsRegistry.RegisterStudio();
            else
                WindowsRegistry.RegisterPlayer();

            if (_launchMode != LaunchMode.Player)
                await mutex.ReleaseAsync();

            if (!App.LaunchSettings.NoLaunchFlag.Active && !_cancelTokenSource.IsCancellationRequested)
            {
                if (!App.LaunchSettings.QuietFlag.Active)
                {
                    if (!_packageExtractionSuccess)
                        Frontend.ShowBalloonTip(Strings.Bootstrapper_ExtractionFailed_Title, Strings.Bootstrapper_ExtractionFailed_Message, ToolTipIcon.Warning);
                    else if (!allModificationsApplied)
                        Frontend.ShowBalloonTip(Strings.Bootstrapper_ModificationsFailed_Title, Strings.Bootstrapper_ModificationsFailed_Message, ToolTipIcon.Warning);
                }

                if (_launchMode == LaunchMode.Player)
                {
                    if (App.Settings.Prop.MatchmakerEnabled)
                        await TryApplyMatchmakingAsync();

                    LaunchGame game = App.Settings.Prop.UseFastFlagManager ? await ResolveLaunchGameAsync() : LaunchGame.Unknown;
                    Utility.FlagProfile? profile = game.Known ? Utility.FlagLayers.ProfileFor(App.FlagProfiles.Prop, game.PlaceId, game.UniverseId) : null;

                    if (!game.Known && App.Settings.Prop.UseFastFlagManager)
                        Utility.FlagProfileSession.NoteLaunchWithoutGame(App.FlagProfiles.Prop);

                    var wanted = Utility.FlagProfileSession.Wanted.Of(profile);

                    bool startsNewClient = await Utility.FlagProfileSession.PrepareLaunchAsync(wanted, game.Known);

                    bool flagsWritten = await WriteLaunchFlagsAsync(profile);

                    if (startsNewClient || flagsWritten)
                        Utility.FlagProfileSession.RecordLaunch(flagsWritten ? wanted : Utility.FlagProfileSession.Wanted.None);

                    if (App.Settings.Prop.AssetWarpEnabled && App.Settings.Prop.AssetWarpPreloadEnabled)
                    {
                        _ = Networking.AssetPreloadCache.PreloadAvatarAsync();
                        _ = Networking.AssetPreloadCache.PreloadRecentGamesAsync(
                            Integrations.PlayTimeStore.GetAll().OrderByDescending(e => e.LastPlayed).Select(e => e.UniverseId).Where(id => id > 0));
                    }
                }

                StartRoblox();

                if (App.Settings.Prop.DisableRobloxCrashHandler)
                    _ = DisableCrashHandlerIfNeeded();
            }

            await mutex.ReleaseAsync();

            Dialog?.CloseBootstrapper();
        }

        private RegistryKey GetChannelRegistryKey() => Registry.CurrentUser.CreateSubKey($"SOFTWARE\\ROBLOX Corporation\\Environments\\{AppData.RegistryName}\\Channel");

        private string? GetCurrentChannelFromArgs()
        {
            const string LOG_IDENT = "Bootstrapper::GetCurrentChannelFromArgs";

            if (App.LaunchSettings.ChannelFlag.Active && !string.IsNullOrEmpty(App.LaunchSettings.ChannelFlag.Data))
            {
                App.Logger.WriteLine(LOG_IDENT, "Got from channel arg");
                return App.LaunchSettings.ChannelFlag.Data.ToLowerInvariant();
            }

            Match match = Regex.Match(
                App.LaunchSettings.RobloxLaunchArgs,
                "channel:([a-zA-Z0-9-_]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

            if (match.Groups.Count == 2)
            {
                App.Logger.WriteLine(LOG_IDENT, "Got from launch URI");
                return match.Groups[1].Value.ToLowerInvariant();
            }

            if (_launchMode != LaunchMode.Unknown)
            {
                using RegistryKey key = GetChannelRegistryKey();
                if (key.GetValue("www.roblox.com") is string value && !String.IsNullOrEmpty(value))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Got from registry ({AppData.RegistryName})");
                    return value;
                }
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, "Skipping registry check, unknown launch");
            }

            if (App.Settings.Prop.ChannelChangeMode != Enums.ChannelChangeMode.Ignore
                && !String.IsNullOrEmpty(App.Settings.Prop.RobloxChannel))
            {
                App.Logger.WriteLine(LOG_IDENT, "Got from saved channel setting");
                return App.Settings.Prop.RobloxChannel.ToLowerInvariant();
            }

            App.Logger.WriteLine(LOG_IDENT, "Could not find channel");
            return null;
        }

        private void FetchCurrentChannel()
        {
            const string LOG_IDENT = "Bootstrapper::FetchCurrentChannel";

            if (_channelFetched)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Channel has already been fetched");
                return;
            }

            string? channel = GetCurrentChannelFromArgs();

            if (!String.IsNullOrEmpty(channel))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Got channel as {channel}");

                Deployment.Channel = channel;

                if (!Deployment.IsDefaultChannel)
                    App.SendStat("robloxChannel", channel);
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not get channel, defaulting to {Deployment.DefaultChannel}");

                Deployment.Channel = Deployment.DefaultChannel;
            }

            _channelFetched = true;
        }

        private void UpdateChannelRegistry()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\ROBLOX Corporation\\Environments\\{AppData.RegistryName}\\Channel");
            key.SetValueSafe("www.roblox.com", Deployment.IsDefaultChannel ? "" : Deployment.Channel);
        }

        private async Task GetLatestVersionInfo()
        {
            const string LOG_IDENT = "Bootstrapper::GetLatestVersionInfo";

            FetchCurrentChannel();

            string? newVersionGuid = null;
            Version? newVersion = null;

            if (!App.LaunchSettings.VersionFlag.Active || string.IsNullOrEmpty(App.LaunchSettings.VersionFlag.Data))
            {
                ClientVersion clientVersion;

                try
                {
                    clientVersion = await Deployment.GetInfo();
                }
                catch (InvalidChannelException ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Resetting channel from {Deployment.Channel} because {ex.StatusCode}");

                    Deployment.Channel = Deployment.DefaultChannel;
                    clientVersion = await Deployment.GetInfo();
                }

                UpdateChannelRegistry();

                newVersionGuid = clientVersion.VersionGuid;
                newVersion = Utilities.ParseVersionSafe(clientVersion.Version);

                if (_launchMode == LaunchMode.Player)
                {
                    Utility.RobloxVersions.RecordLatest(newVersionGuid, clientVersion.Version);

                    string? chosen = Utility.RobloxVersions.Choose(newVersionGuid, AppData.DistributionState.VersionGuid, out string? why);

                    if (why is not null)
                        App.Logger.WriteLine(LOG_IDENT, $"Version manager: {why}");

                    if (chosen is not null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Version manager: using {chosen} instead of the latest ({newVersionGuid})");
                        newVersionGuid = chosen;
                        newVersion = Utilities.ParseVersionSafe(Utility.RobloxVersions.FileVersionOf(chosen));
                        _usingKeptVersion = true;
                    }
                }
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Version set to {App.LaunchSettings.VersionFlag.Data} from arguments");
                newVersionGuid = App.LaunchSettings.VersionFlag.Data;
            }

            if (newVersionGuid != _latestVersionGuid)
            {
                _latestVersionGuid = newVersionGuid!;
                _latestVersion = newVersion;

                _latestVersionDirectory = Path.Combine(Paths.Versions, _latestVersionGuid);

                string? pkgManifestData = _usingKeptVersion ? Utility.RobloxVersions.ReadManifestCopy(_latestVersionGuid) : null;

                if (pkgManifestData is null)
                {
                    string pkgManifestUrl = Deployment.GetLocation($"/{_latestVersionGuid}-rbxPkgManifest.txt");
                    pkgManifestData = await App.HttpClient.GetStringAsync(pkgManifestUrl);

                    Utility.RobloxVersions.SaveManifestCopy(_latestVersionGuid, pkgManifestData);
                }

                _versionManifestText = pkgManifestData;
                _versionPackageManifest = new(pkgManifestData);
            }

            if (_launchMode == LaunchMode.Unknown)
            {
                if (_versionPackageManifest.Count != 0)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Identifying launch mode from package manifest");

                    bool isPlayer = _versionPackageManifest.Exists(x => x.Name == "RobloxApp.zip");
                    App.Logger.WriteLine(LOG_IDENT, $"isPlayer: {isPlayer}");

                    _launchMode = isPlayer ? LaunchMode.Player : LaunchMode.Studio;

                    SetupAppData();

                    UpdateChannelRegistry();
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not identify launch mode as package manifest is empty");
                }
            }
        }

        private async Task SafeGetLatestVersionInfo()
        {
            if (!_noConnection)
            {
                try
                {
                    await GetLatestVersionInfo();
                }
                catch (Exception ex)
                {
                    HandleConnectionError(ex);
                }
            }
        }

        private void SwitchToKeptVersion()
        {
            const string LOG_IDENT = "Bootstrapper::SwitchToKeptVersion";

            App.Logger.WriteLine(LOG_IDENT, $"Switching from {AppData.DistributionState.VersionGuid} to {_latestVersionGuid}, which is already on disk");

            AppData.DistributionState.VersionGuid = _latestVersionGuid;
            AppData.DistributionState.PackageHashes.Clear();

            foreach (var package in _versionPackageManifest)
                AppData.DistributionState.PackageHashes.Add(package.Name, package.Signature);

            AppData.DistributionStateManager.Save();
        }

        private bool IsEligibleForBackgroundUpdate()
        {
            const string LOG_IDENT = "Bootstrapper::IsEligibleForBackgroundUpdate";

            if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Is the background updater process");
                return false;
            }

            if (!App.Settings.Prop.BackgroundUpdatesEnabled)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Background updates disabled");
                return false;
            }

            if (_mustUpgrade)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Must upgrade is true");
                return false;
            }

            const long minimumFreeSpace = 5_000_000_000;
            long space = Filesystem.GetFreeDiskSpace(Paths.Base);
            if (space < minimumFreeSpace)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not eligible: User has {space} free space, at least {minimumFreeSpace} is required");
                return false;
            }

            if (_latestVersion == default)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Latest version is undefined");
                return false;
            }

            Version? currentVersion = Utilities.GetRobloxVersion(AppData);
            if (currentVersion == default)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Current version is undefined");
                return false;
            }

            if (currentVersion.Minor > _latestVersion.Minor)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not eligible: Downgrade");
                return false;
            }

            int diff = _latestVersion.Minor - currentVersion.Minor;
            if (diff == 0 || diff == 1)
            {
                App.Logger.WriteLine(LOG_IDENT, "Eligible");
                return true;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Not eligible: Major version diff is {diff}");
                return false;
            }
        }

        private async Task TryApplyMatchmakingAsync()
        {
            const string LOG_IDENT = "Bootstrapper::TryApplyMatchmakingAsync";

            try
            {
                Match uriMatch = Regex.Match(_launchCommandLine, @"roblox(?:-player)?://experiences/start\?([^\s""]+)", RegexOptions.IgnoreCase);
                if (!uriMatch.Success)
                {
                    await TryApplyLegacyTicketMatchmakingAsync();
                    return;
                }

                string query = uriMatch.Groups[1].Value;
                var queryParams = HttpUtility.ParseQueryString(query);

                if (!string.IsNullOrEmpty(queryParams["gameInstanceId"]))
                    return;

                if (!long.TryParse(queryParams["placeId"], out long placeId) || placeId <= 0)
                    return;

                if (App.Settings.Prop.MatchmakerExcludedPlaces.Contains(placeId.ToString()))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Place {placeId} is on the matchmaker exclusion list, skipping");
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Matchmaker is on, looking for a better server for place {placeId}");

                int maxCandidates = Matchmaker.ResolveEffectiveCandidateCount();
                MatchmakerCandidate? winner = await Matchmaker.PickBestJobIdAsync(placeId, maxCandidates: maxCandidates);

                if (winner is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No better server found, letting Roblox assign one normally");
                    return;
                }

                queryParams["gameInstanceId"] = winner.JobId;
                string newQuery = queryParams.ToString() ?? query;
                _launchCommandLine = _launchCommandLine[..uriMatch.Groups[1].Index] + newQuery + _launchCommandLine[(uriMatch.Groups[1].Index + uriMatch.Groups[1].Length)..];

                App.Logger.WriteLine(LOG_IDENT, $"Redirecting to {winner.DatacenterName} (about {winner.EstimatedPingMs}ms), JobId {winner.JobId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Matchmaking failed, launching normally: {ex.Message}");
            }
        }

        private async Task TryApplyLegacyTicketMatchmakingAsync()
        {
            const string LOG_IDENT = "Bootstrapper::TryApplyLegacyTicketMatchmakingAsync";

            Match ticketMatch = Regex.Match(_launchCommandLine, @"placelauncherurl:([^\s""+]+)", RegexOptions.IgnoreCase);
            if (!ticketMatch.Success)
                return;

            string decodedUrl = HttpUtility.UrlDecode(ticketMatch.Groups[1].Value);

            int queryIndex = decodedUrl.IndexOf('?');
            if (queryIndex < 0)
                return;

            var queryParams = HttpUtility.ParseQueryString(decodedUrl[(queryIndex + 1)..]);

            if (!long.TryParse(queryParams["placeId"], out long placeId) || placeId <= 0)
                return;

            if (App.Settings.Prop.MatchmakerExcludedPlaces.Contains(placeId.ToString()))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Place {placeId} is on the matchmaker exclusion list, skipping");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Matchmaker is on, looking for a better server for place {placeId} (legacy ticket launch)");

            int maxCandidates = Matchmaker.ResolveEffectiveCandidateCount();
            MatchmakerCandidate? winner = await Matchmaker.PickBestJobIdAsync(placeId, maxCandidates: maxCandidates);

            if (winner is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "No better server found, letting Roblox assign one normally");
                return;
            }

            queryParams["request"] = "RequestGameJob";
            queryParams["gameId"] = winner.JobId;

            string newUrl = decodedUrl[..queryIndex] + "?" + queryParams;
            string newEncodedUrl = HttpUtility.UrlEncode(newUrl);

            _launchCommandLine = _launchCommandLine[..ticketMatch.Groups[1].Index] + newEncodedUrl + _launchCommandLine[(ticketMatch.Groups[1].Index + ticketMatch.Groups[1].Length)..];

            App.Logger.WriteLine(LOG_IDENT, $"Redirecting to {winner.DatacenterName} (about {winner.EstimatedPingMs}ms), JobId {winner.JobId}");
        }

        private long? TryResolveLaunchPlaceId()
        {
            Match uriMatch = Regex.Match(_launchCommandLine, @"roblox(?:-player)?://experiences/start\?([^\s""]+)", RegexOptions.IgnoreCase);
            if (uriMatch.Success)
            {
                var queryParams = HttpUtility.ParseQueryString(uriMatch.Groups[1].Value);

                if (long.TryParse(queryParams["placeId"], out long placeId) && placeId > 0)
                    return placeId;

                return null;
            }

            Match ticketMatch = Regex.Match(_launchCommandLine, @"placelauncherurl:([^\s""+]+)", RegexOptions.IgnoreCase);
            if (!ticketMatch.Success)
                return null;

            string decodedUrl = HttpUtility.UrlDecode(ticketMatch.Groups[1].Value);

            int queryIndex = decodedUrl.IndexOf('?');
            if (queryIndex < 0)
                return null;

            var ticketQueryParams = HttpUtility.ParseQueryString(decodedUrl[(queryIndex + 1)..]);

            if (long.TryParse(ticketQueryParams["placeId"], out long ticketPlaceId) && ticketPlaceId > 0)
                return ticketPlaceId;

            return null;
        }

        private readonly record struct LaunchGame(long PlaceId, long UniverseId)
        {
            public static readonly LaunchGame Unknown = new(0, 0);
            public bool Known => PlaceId > 0;
        }

        private async Task<LaunchGame> ResolveLaunchGameAsync()
        {
            const string LOG_IDENT = "Bootstrapper::ResolveLaunchGameAsync";

            Utility.FlagProfileData profiles = App.FlagProfiles.Prop;
            if (profiles.Rules.Count == 0)
                return new LaunchGame(TryResolveLaunchPlaceId() ?? 0, 0);

            long placeId = TryResolveLaunchPlaceId() ?? 0;
            long universeId = 0;

            if (placeId == 0)
            {
                long? userId = TryResolveFollowedUserId();
                if (userId is not null)
                {
                    var where = await Utility.GameLookup.WhereIsUserAsync(userId.Value, TimeSpan.FromSeconds(5));
                    if (where is not null)
                    {
                        (placeId, universeId) = where.Value;
                        App.Logger.WriteLine(LOG_IDENT, $"Following user {userId}, who is in place {placeId}");
                    }
                    else
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Following user {userId}, but where they are playing isn't visible - launching with your own flags");
                    }
                }
            }

            if (placeId > 0 && universeId == 0 && Utility.FlagLayers.NeedsUniverse(profiles, placeId))
                universeId = await Utility.GameLookup.UniverseOfAsync(placeId, TimeSpan.FromSeconds(4)) ?? 0;

            return new LaunchGame(placeId, universeId);
        }

        private long? TryResolveFollowedUserId()
        {
            Match uriMatch = Regex.Match(_launchCommandLine, @"roblox(?:-player)?://experiences/start\?([^\s""]+)", RegexOptions.IgnoreCase);
            if (uriMatch.Success)
            {
                var queryParams = HttpUtility.ParseQueryString(uriMatch.Groups[1].Value);
                return long.TryParse(queryParams["userId"], out long userId) && userId > 0 ? userId : null;
            }

            Match ticketMatch = Regex.Match(_launchCommandLine, @"placelauncherurl:([^\s""+]+)", RegexOptions.IgnoreCase);
            if (!ticketMatch.Success)
                return null;

            string decodedUrl = HttpUtility.UrlDecode(ticketMatch.Groups[1].Value);
            int queryIndex = decodedUrl.IndexOf('?');
            if (queryIndex < 0)
                return null;

            var ticketQuery = HttpUtility.ParseQueryString(decodedUrl[(queryIndex + 1)..]);
            if (!string.Equals(ticketQuery["request"], "RequestFollowUser", StringComparison.OrdinalIgnoreCase))
                return null;

            return long.TryParse(ticketQuery["userId"], out long followed) && followed > 0 ? followed : null;
        }

        private async Task<bool> WriteLaunchFlagsAsync(Utility.FlagProfile? profile)
        {
            const string LOG_IDENT = "Bootstrapper::WriteLaunchFlagsAsync";

            if (!App.Settings.Prop.UseFastFlagManager || !ModsTargetThisLaunch)
                return false;

            if (string.IsNullOrEmpty(_latestVersionDirectory) || !Directory.Exists(_latestVersionDirectory))
                return false;

            try
            {
                string filePath = Path.Combine(_latestVersionDirectory, "ClientSettings", "ClientAppSettings.json");

                Dictionary<string, string> flags = Utility.FlagLayers.Compose(App.FastFlags.Prop, profile);
                string contents = JsonSerializer.Serialize(flags, new JsonSerializerOptions { WriteIndented = true });

                if (!File.Exists(filePath) || await File.ReadAllTextAsync(filePath) != contents)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    Filesystem.AssertReadOnly(filePath);
                    await File.WriteAllTextAsync(filePath, contents);
                    Filesystem.AssertReadOnly(filePath);
                }

                if (profile is null)
                    App.Logger.WriteLine(LOG_IDENT, $"Launching with your {flags.Count} flag(s), no game profile");
                else
                    App.Logger.WriteLine(LOG_IDENT, $"Launching with profile '{profile.Name}' ({profile.Flags.Count} added/changed, {profile.Remove.Count} turned off) - {flags.Count} flag(s) in total");

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write this launch's flags: {ex.Message}");
                return false;
            }
        }

        private void StartRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::StartRoblox";

            SetStatus(Strings.Bootstrapper_Status_Starting);

            var startInfo = new ProcessStartInfo()
            {
                FileName = AppData.ExecutablePath,
                Arguments = _launchCommandLine,
                WorkingDirectory = AppData.Directory
            };

            if (_launchMode == LaunchMode.Player && ShouldRunAsAdmin())
            {
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
            }
            else if (_launchMode == LaunchMode.StudioAuth)
            {
                Process.Start(startInfo);
                return;
            }

            string? logFileName = null;

            string rbxDir = Path.Combine(Paths.LocalAppData, "Roblox");
            if (!Directory.Exists(rbxDir))
                Directory.CreateDirectory(rbxDir);

            string rbxLogDir = Path.Combine(rbxDir, "logs");
            if (!Directory.Exists(rbxLogDir))
                Directory.CreateDirectory(rbxLogDir);

            var logWatcher = new FileSystemWatcher()
            {
                Path = rbxLogDir,
                Filter = "*.log",
                EnableRaisingEvents = true
            };

            var logCreatedEvent = new AutoResetEvent(false);

            logWatcher.Created += (_, e) =>
            {
                logWatcher.EnableRaisingEvents = false;
                logFileName = e.FullPath;
                logCreatedEvent.Set();
            };

            try
            {
                using var process = Process.Start(startInfo)!;
                _appPid = process.Id;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return;
            }
            catch (Exception)
            {
                File.Delete(AppData.ExecutablePath);
                throw;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Started Roblox (PID {_appPid}), waiting for log file");

            logCreatedEvent.WaitOne(TimeSpan.FromSeconds(15));

            if (String.IsNullOrEmpty(logFileName))
            {
                App.Logger.WriteLine(LOG_IDENT, "Unable to identify log file");
                Frontend.ShowPlayerErrorDialog();
                return;
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"Got log file as {logFileName}");
            }

            _mutex?.ReleaseAsync();

            if (IsStudioLaunch)
                return;

            if (App.Settings.Prop.EnableActivityTracking || App.LaunchSettings.TestModeFlag.Active)
            {
                using var ipl = new InterProcessLock("Watcher", TimeSpan.FromSeconds(5));

                var watcherData = new WatcherData
                {
                    ProcessId = _appPid,
                    LogFile = logFileName,
                    AutoclosePids = new List<int>()
                };

                string watcherDataArg = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(watcherData)));

                string args = $"-watcher \"{watcherDataArg}\"";

                if (App.LaunchSettings.TestModeFlag.Active)
                    args += " -testmode";

                if (ipl.IsAcquired)
                    Process.Start(Paths.Process, args);
            }

            Thread.Sleep(1000);
        }

        private async Task DisableCrashHandlerIfNeeded()
        {
            const string LOG_IDENT = "Bootstrapper::DisableCrashHandlerIfNeeded";

            try
            {
                await Task.Delay(800);

                foreach (var process in Process.GetProcessesByName("RobloxCrashHandler"))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(1000))
                                process.Kill();

                            App.Logger.WriteLine(LOG_IDENT, $"Terminated RobloxCrashHandler {process.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to close RobloxCrashHandler {process.Id}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private bool ShouldRunAsAdmin()
        {
            foreach (var root in WindowsRegistry.Roots)
            {
                using var key = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");

                if (key is null)
                    continue;

                string? flags = (string?)key.GetValue(AppData.ExecutablePath);

                if (flags is not null && flags.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public void Cancel()
        {
            const string LOG_IDENT = "Bootstrapper::Cancel";

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Cancelling launch...");

            _cancelTokenSource.Cancel();

            if (Dialog is not null)
                Dialog.CancelEnabled = false;

            if (_isInstalling)
            {
                try
                {
                    if (Directory.Exists(_latestVersionDirectory))
                        Directory.Delete(_latestVersionDirectory, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fully clean up installation!");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            else if (_appPid != 0)
            {
                try
                {
                    using var process = Process.GetProcessById(_appPid);
                    process.Kill();
                }
                catch (Exception) { }
            }

            Dialog?.CloseBootstrapper();

            App.SoftTerminate(ErrorCode.ERROR_CANCELLED);
        }
#endregion

        #region App Install
        private async Task<bool> CheckForUpdates()
        {
            const string LOG_IDENT = "Bootstrapper::CheckForUpdates";

            using (var probe = new InterProcessLock("AutoUpdater"))
            {
                if (!probe.IsAcquired)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Another PhasmaStrap is already updating, skipping the update check");
                    return false;
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Checking for updates...");

#if !DEBUG_UPDATER
            var releaseInfo = await App.GetLatestRelease();

            if (releaseInfo is null)
                return false;

            var versionComparison = Utilities.CompareVersions(App.Version, releaseInfo.TagName);

            if (versionComparison == VersionComparison.Equal || versionComparison == VersionComparison.GreaterThan)
            {
                App.Logger.WriteLine(LOG_IDENT, "No updates found");
                return false;
            }

            if (Dialog is not null)
                Dialog.CancelEnabled = false;

            string version = releaseInfo.TagName;
#else
            string version = App.Version;
#endif

            SetStatus(Strings.Bootstrapper_Status_UpgradingPhasmaStrap);

            try
            {
#if DEBUG_UPDATER
                string downloadLocation = Path.Combine(Paths.TempUpdates, "PhasmaStrap.exe");

                Directory.CreateDirectory(Paths.TempUpdates);

                File.Copy(Paths.Process, downloadLocation, true);
#else
                var asset = releaseInfo.Assets!.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Release {releaseInfo.TagName} has no .exe to download");

                string downloadLocation = Path.Combine(Paths.TempUpdates, asset.Name);

                Directory.CreateDirectory(Paths.TempUpdates);

                if (File.Exists(downloadLocation) && !IsCompleteDownload(downloadLocation, asset))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Discarding an incomplete earlier download of {asset.Name}");
                    File.Delete(downloadLocation);
                }

                if (!File.Exists(downloadLocation))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Downloading {releaseInfo.TagName} ({asset.Size / 1024 / 1024} MB)...");

                    string partial = downloadLocation + ".part";

                    using (var response = await App.HttpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        await using var fileStream = new FileStream(partial, FileMode.Create, FileAccess.Write);
                        await response.Content.CopyToAsync(fileStream);
                    }

                    if (!IsCompleteDownload(partial, asset))
                    {
                        File.Delete(partial);
                        throw new InvalidDataException($"The download of {asset.Name} was incomplete or corrupted");
                    }

                    File.Move(partial, downloadLocation, true);
                }
#endif

                App.Logger.WriteLine(LOG_IDENT, $"Starting {version}...");

                ProcessStartInfo startInfo = new()
                {
                    FileName = downloadLocation,
                };

                startInfo.ArgumentList.Add("-upgrade");

                foreach (string arg in App.LaunchSettings.Args)
                    startInfo.ArgumentList.Add(arg);

                if (_launchMode == LaunchMode.Player && !startInfo.ArgumentList.Contains("-player"))
                    startInfo.ArgumentList.Add("-player");
                else if (_launchMode == LaunchMode.Studio && !startInfo.ArgumentList.Contains("-studio"))
                    startInfo.ArgumentList.Add("-studio");

                App.Settings.Save();

                new InterProcessLock("AutoUpdater");

                Process.Start(startInfo);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the auto-updater");
                App.Logger.WriteException(LOG_IDENT, ex);

                Frontend.ShowMessageBox(
                    string.Format(Strings.Bootstrapper_AutoUpdateFailed, version),
                    MessageBoxImage.Information
                );

                Utilities.ShellExecute(App.ProjectDownloadLink);
            }

            return false;
        }

        private static bool IsCompleteDownload(string path, GithubReleaseAsset asset)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || (asset.Size > 0 && info.Length != asset.Size))
                return false;

            if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(path);
                using var sha = System.Security.Cryptography.SHA256.Create();
                string hash = Convert.ToHexString(sha.ComputeHash(stream));
                return string.Equals(hash, digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        #endregion

        #region Roblox Install
        private static bool TryDeleteRobloxInDirectory(string dir)
        {
            string clientPath = Path.Combine(dir, "RobloxPlayerBeta.exe");
            if (!File.Exists(clientPath))
            {
                clientPath = Path.Combine(dir, "RobloxStudioBeta.exe");
                if (!File.Exists(clientPath))
                    return true;
            }

            try
            {
                File.Delete(clientPath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void CleanupVersionsFolder()
        {
            const string LOG_IDENT = "Bootstrapper::CleanupVersionsFolder";

            if (App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                App.Logger.WriteLine(LOG_IDENT, "Background updater tried to cleanup, stopping!");
                return;
            }

            if (!Directory.Exists(Paths.Versions))
            {
                App.Logger.WriteLine(LOG_IDENT, "Versions directory does not exist, skipping cleanup.");
                return;
            }

            HashSet<string> keep = Utility.RobloxVersions.FoldersToKeep(App.PlayerState.Prop.VersionGuid);

            foreach (string dir in Directory.GetDirectories(Paths.Versions))
            {
                string dirName = Path.GetFileName(dir);

                if (dirName != App.PlayerState.Prop.VersionGuid && dirName != App.StudioState.Prop.VersionGuid && !keep.Contains(dirName))
                {
                    if (!TryDeleteRobloxInDirectory(dir))
                        continue;

                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {dir}");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }
        }

        private void MigrateCompatibilityFlags()
        {
            const string LOG_IDENT = "Bootstrapper::MigrateCompatibilityFlags";

            string oldClientLocation = Path.Combine(Paths.Versions, AppData.DistributionState.VersionGuid, AppData.ExecutableName);
            string newClientLocation = Path.Combine(_latestVersionDirectory, AppData.ExecutableName);

            using RegistryKey appFlagsKey = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
            string? appFlags = appFlagsKey.GetValue(oldClientLocation) as string;

            if (appFlags is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Migrating app compatibility flags from {oldClientLocation} to {newClientLocation}...");
                appFlagsKey.SetValueSafe(newClientLocation, appFlags);
                appFlagsKey.DeleteValueSafe(oldClientLocation);
            }
        }

        private void KillRobloxInstances()
        {
            const string LOG_IDENT = "Bootstrapper::KillRobloxInstances";

            List<Process> processes = new List<Process>();
            processes.AddRange(Process.GetProcessesByName(AppData.ProcessName));
            processes.AddRange(Process.GetProcessesByName("RobloxCrashHandler"));

            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process {process.Id}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }

        private async Task GracefullyCloseRobloxInstances()
        {
            const string LOG_IDENT = "Bootstrapper::GracefullyCloseRobloxInstances";

            while (true)
            {
                Process[] processes = Process.GetProcessesByName(AppData.ProcessName);
                if (processes.Length == 0)
                    break;

                foreach (Process process in processes)
                {
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to close process {process.Id}");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }

                try
                {
                    await Task.Delay(1000, _cancelTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task UpgradeRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::UpgradeRoblox";

            Directory.CreateDirectory(Paths.Base);
            Directory.CreateDirectory(Paths.Downloads);
            Directory.CreateDirectory(Paths.Versions);

            _isInstalling = true;

            if (!App.LaunchSettings.BackgroundUpdaterFlag.Active)
            {
                SetStatus(Strings.Bootstrapper_Status_ShuttingDown);

                if (IsStudioLaunch)
                    await GracefullyCloseRobloxInstances();
                else
                    KillRobloxInstances();

                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                if (Directory.Exists(_latestVersionDirectory))
                {
                    try
                    {
                        Directory.Delete(_latestVersionDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to delete the latest version directory");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }

            if (String.IsNullOrEmpty(AppData.DistributionState.VersionGuid))
                SetStatus(Strings.Bootstrapper_Status_Installing);
            else
                SetStatus(Strings.Bootstrapper_Status_Upgrading);

            Directory.CreateDirectory(_latestVersionDirectory);

            var cachedPackageHashes = Directory.GetFiles(Paths.Downloads).Select(x => Path.GetFileName(x));

            int totalSizeRequired = 0;

            totalSizeRequired += _versionPackageManifest.Where(x => !cachedPackageHashes.Contains(x.Signature)).Sum(x => x.PackedSize);
            totalSizeRequired += _versionPackageManifest.Sum(x => x.Size);

            if (Filesystem.GetFreeDiskSpace(Paths.Base) < totalSizeRequired)
            {
                Frontend.ShowMessageBox(Strings.Bootstrapper_NotEnoughSpace, MessageBoxImage.Error);
                App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                return;
            }

            if (Dialog is not null)
            {
                Dialog.ProgressStyle = ProgressBarStyle.Continuous;
                Dialog.TaskbarProgressState = TaskbarItemProgressState.Normal;

                Dialog.ProgressMaximum = ProgressBarMaximum;

                int totalPackedSize = _versionPackageManifest.Sum(package => package.PackedSize);
                _progressIncrement = (double)ProgressBarMaximum / totalPackedSize;

                if (Dialog is WinFormsDialogBase)
                    _taskbarProgressMaximum = (double)TaskbarProgressMaximumWinForms;
                else
                    _taskbarProgressMaximum = (double)TaskbarProgressMaximumWpf;

                _taskbarProgressIncrement = _taskbarProgressMaximum / (double)totalPackedSize;
            }

            var extractionTasks = new ConcurrentBag<Task>();

            int packageConcurrency = DownloadConfiguration.NormalizeConcurrent(App.Settings.Prop.MaxConcurrentDownloads);
            int downloadSegments = DownloadConfiguration.NormalizeSegments(App.Settings.Prop.MaxDownloadSegments);

            _downloadRequestThrottle = new SemaphoreSlim(Math.Clamp(packageConcurrency * downloadSegments, 1, 32));

            try
            {
                if (packageConcurrency <= 1)
                {
                    foreach (var package in _versionPackageManifest)
                    {
                        if (_cancelTokenSource.IsCancellationRequested)
                            return;

                        await DownloadPackage(package);

                        if (package.Name == "WebView2RuntimeInstaller.zip")
                            continue;

                        extractionTasks.Add(Task.Run(() => ExtractPackage(package), _cancelTokenSource.Token));
                    }
                }
                else
                {
                    using var packageThrottle = new SemaphoreSlim(packageConcurrency);

                    var downloadTasks = _versionPackageManifest.Select(async package =>
                    {
                        await packageThrottle.WaitAsync(_cancelTokenSource.Token);
                        try
                        {
                            if (_cancelTokenSource.IsCancellationRequested)
                                return;

                            await DownloadPackage(package);

                            if (package.Name == "WebView2RuntimeInstaller.zip")
                                return;

                            extractionTasks.Add(Task.Run(() => ExtractPackage(package), _cancelTokenSource.Token));
                        }
                        finally
                        {
                            packageThrottle.Release();
                        }
                    });

                    await Task.WhenAll(downloadTasks);
                }
            }
            finally
            {
                _downloadRequestThrottle.Dispose();
                _downloadRequestThrottle = null;
            }

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            if (Dialog is not null)
            {
                Dialog.ProgressStyle = ProgressBarStyle.Marquee;
                Dialog.TaskbarProgressState = TaskbarItemProgressState.Indeterminate;
                SetStatus(Strings.Bootstrapper_Status_Configuring);
            }

            await Task.WhenAll(extractionTasks);

            App.Logger.WriteLine(LOG_IDENT, "Writing AppSettings.xml...");
            await File.WriteAllTextAsync(Path.Combine(_latestVersionDirectory, "AppSettings.xml"), AppSettings);

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            if (App.State.Prop.PromptWebView2Install)
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                using var hkcuKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");

                if (hklmKey is not null || hkcuKey is not null)
                {
                    App.State.Prop.PromptWebView2Install = true;
                }
                else
                {
                    var result = Frontend.ShowMessageBox(Strings.Bootstrapper_WebView2NotFound, MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.Yes);

                    if (result != MessageBoxResult.Yes)
                    {
                        App.State.Prop.PromptWebView2Install = false;
                    }
                    else
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Installing WebView2 runtime...");

                        var package = _versionPackageManifest.Find(x => x.Name == "WebView2RuntimeInstaller.zip");

                        if (package is null)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Aborted runtime install because package does not exist, has WebView2 been added in this Roblox version yet?");
                            return;
                        }

                        string baseDirectory = Path.Combine(_latestVersionDirectory, AppData.PackageDirectoryMap[package.Name]);

                        ExtractPackage(package);

                        SetStatus(Strings.Bootstrapper_Status_InstallingWebView2);

                        var startInfo = new ProcessStartInfo()
                        {
                            WorkingDirectory = baseDirectory,
                            FileName = Path.Combine(baseDirectory, "MicrosoftEdgeWebview2Setup.exe"),
                            Arguments = "/silent /install"
                        };

                        await Process.Start(startInfo)!.WaitForExitAsync();

                        App.Logger.WriteLine(LOG_IDENT, "Finished installing runtime");

                        Directory.Delete(baseDirectory, true);
                    }
                }
            }

            MigrateCompatibilityFlags();

            string previousVersionGuid = AppData.DistributionState.VersionGuid;
            AppData.DistributionState.VersionGuid = _latestVersionGuid;

            if (_launchMode == LaunchMode.Player)
            {
                Utility.RobloxVersions.RecordInstall(_latestVersionGuid, _versionManifestText, previousVersionGuid);

                if (App.Settings.Prop.RobloxCheckFlagsAfterUpdate && !string.IsNullOrEmpty(previousVersionGuid))
                    Utility.RobloxVersions.CheckFlagsAfterInstall(_latestVersionGuid, previousVersionGuid);
            }

            AppData.DistributionState.PackageHashes.Clear();

            foreach (var package in _versionPackageManifest)
                AppData.DistributionState.PackageHashes.Add(package.Name, package.Signature);

            CleanupVersionsFolder();

            var allPackageHashes = new List<string>();

            allPackageHashes.AddRange(App.PlayerState.Prop.PackageHashes.Values);
            allPackageHashes.AddRange(App.StudioState.Prop.PackageHashes.Values);

            if (!App.Settings.Prop.DebugDisableVersionPackageCleanup)
            {
                foreach (string hash in cachedPackageHashes)
                {
                    if (!allPackageHashes.Contains(hash))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Deleting unused package {hash}");

                        try
                        {
                            File.Delete(Path.Combine(Paths.Downloads, hash));
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {hash}!");
                            App.Logger.WriteException(LOG_IDENT, ex);
                        }
                    }
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Registering approximate program size...");

            int distributionSize = _versionPackageManifest.Sum(x => x.Size + x.PackedSize) / 1024;

            AppData.DistributionState.Size = distributionSize;

            int totalSize = App.PlayerState.Prop.Size + App.PlayerState.Prop.Size;

            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("EstimatedSize", totalSize);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Registered as {totalSize} KB");

            App.State.Prop.ForceReinstall = false;

            App.State.Save();
            AppData.DistributionStateManager.Save();

            _isInstalling = false;
        }

        private void StartBackgroundUpdater()
        {
            const string LOG_IDENT = "Bootstrapper::StartBackgroundUpdater";

            if (Utilities.DoesMutexExist(BackgroundUpdaterMutexName))
            {
                App.Logger.WriteLine(LOG_IDENT, "Background updater already running");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Starting background updater");

            Process.Start(Paths.Process, $"-backgroundupdater {_launchMode}");
        }

        private bool ModsTargetThisLaunch => App.Settings.Prop.ModApplyTarget switch
        {
            ModApplyTarget.Player => !IsStudioLaunch,
            ModApplyTarget.Studio => IsStudioLaunch,
            _ => true
        };

        private async Task<bool> ApplyModifications()
        {
            const string LOG_IDENT = "Bootstrapper::ApplyModifications";

            bool success = true;

            SetStatus(Strings.Bootstrapper_Status_ApplyingModifications);

            if (!ModsTargetThisLaunch)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Skipping mod application - ModApplyTarget is {App.Settings.Prop.ModApplyTarget} and this is a {(IsStudioLaunch ? "Studio" : "Player")} launch");
                return true;
            }

            App.Logger.WriteLine(LOG_IDENT, "Checking file mods...");

            File.Delete(Path.Combine(Paths.Base, "ModManifest.txt"));

            List<string> modFolderFiles = new();

            Directory.CreateDirectory(Paths.Modifications);

            try
            {
                var previousManagedManifest = new HashSet<string>(AppData.DistributionState.ManagedModManifest, StringComparer.OrdinalIgnoreCase);
                var currentManagedManifest = new List<string>();
                var currentManagedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                ManagedModScanResult managedScan = ManagedModStore.ScanEnabledFiles();

                foreach (ManagedModFile file in managedScan.Files)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                        return true;

                    if (file.Relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string destination = Path.Combine(Paths.Modifications, file.Relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    CloudFiles.Hydrate(file.Source);
                    File.Copy(file.Source, destination, true);

                    currentManagedSet.Add(file.Relative);
                    currentManagedManifest.Add(file.Relative);
                }

                foreach (string stalePath in previousManagedManifest)
                {
                    if (currentManagedSet.Contains(stalePath))
                        continue;

                    string staleFile = Path.Combine(Paths.Modifications, stalePath);
                    if (!File.Exists(staleFile))
                        continue;

                    try
                    {
                        Filesystem.AssertReadOnly(staleFile);
                        File.Delete(staleFile);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not remove stale managed mod file {stalePath}: {ex.Message}");
                    }
                }

                foreach ((string id, string message) in managedScan.Failures)
                    App.Logger.WriteLine(LOG_IDENT, $"Managed mod {id[..8]} could not be indexed: {message}");

                AppData.DistributionState.ManagedModManifest = currentManagedManifest;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Managed mods could not be applied: " + ex.Message);
            }

            string modFontFamiliesFolder = Path.Combine(Paths.Modifications, "content\\fonts\\families");

            if (File.Exists(Paths.CustomFont))
            {
                App.Logger.WriteLine(LOG_IDENT, "Begin font check");

                Directory.CreateDirectory(modFontFamiliesFolder);

                const string path = "rbxasset://fonts/CustomFont.ttf";

                string contentFolder = Path.Combine(_latestVersionDirectory, "content");
                Directory.CreateDirectory(contentFolder);

                string fontsFolder = Path.Combine(contentFolder, "fonts");
                Directory.CreateDirectory(fontsFolder);

                string familiesFolder = Path.Combine(fontsFolder, "families");
                Directory.CreateDirectory(familiesFolder);

                foreach (string jsonFilePath in Directory.GetFiles(familiesFolder))
                {
                    string jsonFilename = Path.GetFileName(jsonFilePath);
                    string modFilepath = Path.Combine(modFontFamiliesFolder, jsonFilename);

                    if (File.Exists(modFilepath))
                        continue;

                    App.Logger.WriteLine(LOG_IDENT, $"Setting font for {jsonFilename}");

                    var fontFamilyData = JsonSerializer.Deserialize<FontFamily>(File.ReadAllText(jsonFilePath));

                    if (fontFamilyData is null)
                        continue;

                    bool shouldWrite = false;

                    foreach (var fontFace in fontFamilyData.Faces)
                    {
                        if (fontFace.AssetId != path)
                        {
                            fontFace.AssetId = path;
                            shouldWrite = true;
                        }
                    }

                    if (shouldWrite)
                        File.WriteAllText(modFilepath, JsonSerializer.Serialize(fontFamilyData, new JsonSerializerOptions { WriteIndented = true }));
                }

                App.Logger.WriteLine(LOG_IDENT, "End font check");
            }
            else if (Directory.Exists(modFontFamiliesFolder))
            {
                Directory.Delete(modFontFamiliesFolder, true);
            }

            foreach (string file in Directory.GetFiles(Paths.Modifications, "*.*", SearchOption.AllDirectories))
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return true;

                string relativeFile = file.Substring(Paths.Modifications.Length + 1);

                if (relativeFile == "README.txt")
                {
                    File.Delete(file);
                    continue;
                }

                if (!App.Settings.Prop.UseFastFlagManager && String.Equals(relativeFile, "ClientSettings\\ClientAppSettings.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (relativeFile.EndsWith(".lock"))
                    continue;

                modFolderFiles.Add(relativeFile);

                string fileModFolder = Path.Combine(Paths.Modifications, relativeFile);
                string fileVersionFolder = Path.Combine(_latestVersionDirectory, relativeFile);

                if (File.Exists(fileVersionFolder) && MD5Hash.FromFile(fileModFolder) == MD5Hash.FromFile(fileVersionFolder))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{relativeFile} already exists in the version folder, and is a match");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fileVersionFolder)!);

                CloudFiles.Hydrate(fileModFolder);

                Filesystem.AssertReadOnly(fileVersionFolder);
                try
                {
                    File.Copy(fileModFolder, fileVersionFolder, true);
                    Filesystem.AssertReadOnly(fileVersionFolder);
                    App.Logger.WriteLine(LOG_IDENT, $"{relativeFile} has been copied to the version folder");
                }
                catch (Exception ex) when (CloudFiles.IsCloudFailure(ex, fileModFolder) || CloudFiles.IsCloudFailure(ex, fileVersionFolder))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to apply modification ({relativeFile}) because a OneDrive/cloud-synced file was not available locally");
                    App.Logger.WriteException(LOG_IDENT, ex);
                    success = false;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to apply modification ({relativeFile})");
                    App.Logger.WriteException(LOG_IDENT, ex);
                    success = false;
                }
            }

            if (_launchMode == LaunchMode.Player && App.Settings.Prop.TopBarPhasmaLogo)
            {
                Utility.TopBarLogoPatcher.Log ??= message => App.Logger.WriteLine("TopBarLogoPatcher", message);

                List<string> logoFiles = Utility.TopBarLogoPatcher.Apply(_latestVersionDirectory, () =>
                    System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/PhasmaStrapLogo.png")).Stream);

                foreach (string logoFile in logoFiles)
                {
                    if (!modFolderFiles.Contains(logoFile, StringComparer.OrdinalIgnoreCase))
                        modFolderFiles.Add(logoFile);
                }
            }

            var fileRestoreMap = new Dictionary<string, List<string>>();

            foreach (string fileLocation in AppData.DistributionState.ModManifest)
            {
                if (modFolderFiles.Contains(fileLocation))
                    continue;

                var packageMapEntry = AppData.PackageDirectoryMap.SingleOrDefault(x => !String.IsNullOrEmpty(x.Value) && fileLocation.StartsWith(x.Value));
                string packageName = packageMapEntry.Key;

                if (String.IsNullOrEmpty(packageName))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{fileLocation} was removed as a mod but does not belong to a package");

                    string versionFileLocation = Path.Combine(_latestVersionDirectory, fileLocation);

                    if (File.Exists(versionFileLocation))
                        File.Delete(versionFileLocation);

                    continue;
                }

                string fileName = fileLocation.Substring(packageMapEntry.Value.Length);

                if (!fileRestoreMap.ContainsKey(packageName))
                    fileRestoreMap[packageName] = new();

                fileRestoreMap[packageName].Add(fileName);

                App.Logger.WriteLine(LOG_IDENT, $"{fileLocation} was removed as a mod, restoring from {packageName}");
            }

            foreach (var entry in fileRestoreMap)
            {
                var package = _versionPackageManifest.Find(x => x.Name == entry.Key);

                if (package is not null)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                        return true;

                    await DownloadPackage(package);
                    ExtractPackage(package, entry.Value);
                }
            }

            if (App.LaunchSettings.BackgroundUpdaterFlag.Active || !AppData.DistributionStateManager.HasFileOnDiskChanged())
            {
                AppData.DistributionState.ModManifest = modFolderFiles;
                AppData.DistributionStateManager.Save();
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"{AppData.DistributionStateManager.ClassName} disk mismatch, not saving ModManifest");
            }

            App.Logger.WriteLine(LOG_IDENT, $"Finished checking file mods");

            if (!success)
                App.Logger.WriteLine(LOG_IDENT, "Failed to apply all modifications");

            return success;
        }

        private async Task DownloadPackage(Package package)
        {
            string LOG_IDENT = $"Bootstrapper::DownloadPackage.{package.Name}";

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            Directory.CreateDirectory(Paths.Downloads);

            string packageUrl = Deployment.GetLocation($"/{_latestVersionGuid}-{package.Name}");
            string robloxPackageLocation = Path.Combine(Paths.LocalAppData, "Roblox", "Downloads", package.Signature);

            if (File.Exists(package.DownloadPath))
            {
                var file = new FileInfo(package.DownloadPath);

                string calculatedMD5 = MD5Hash.FromFile(package.DownloadPath);

                if (calculatedMD5 != package.Signature)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is corrupted ({calculatedMD5} != {package.Signature})! Deleting and re-downloading...");
                    file.Delete();
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is already downloaded, skipping...");

                    Interlocked.Add(ref _totalDownloadedBytes, package.PackedSize);
                    UpdateProgressBar();

                    return;
                }
            }
            else if (File.Exists(robloxPackageLocation))
            {
                App.Logger.WriteLine(LOG_IDENT, $"Found existing copy at '{robloxPackageLocation}'! Copying to Downloads folder...");
                File.Copy(robloxPackageLocation, package.DownloadPath);

                Interlocked.Add(ref _totalDownloadedBytes, package.PackedSize);
                UpdateProgressBar();

                return;
            }

            if (File.Exists(package.DownloadPath))
                return;

            const int maxTries = 5;

            App.Logger.WriteLine(LOG_IDENT, "Downloading...");

            int bufferSize = DownloadConfiguration.NormalizeBufferKb(App.Settings.Prop.DownloadBufferKb) * 1024;
            int segmentCount = DownloadConfiguration.NormalizeSegments(App.Settings.Prop.MaxDownloadSegments);

            for (int i = 1; i <= maxTries; i++)
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                long totalBytesRead = 0;

                try
                {
                    bool downloadedSegmented = segmentCount > 1 &&
                        await TryDownloadPackageSegmented(package, packageUrl, segmentCount, bufferSize, n => Interlocked.Add(ref totalBytesRead, n));

                    if (!downloadedSegmented)
                    {
                        Interlocked.Exchange(ref totalBytesRead, 0);
                        totalBytesRead = await DownloadPackageSingleStream(package, packageUrl, bufferSize);
                    }

                    string hash = MD5Hash.FromFile(package.DownloadPath);

                    if (hash != package.Signature)
                        throw new ChecksumFailedException($"Failed to verify download of {packageUrl}\n\nExpected hash: {package.Signature}\nGot hash: {hash}");

                    App.Logger.WriteLine(LOG_IDENT, $"Finished downloading! ({totalBytesRead} bytes total, {(downloadedSegmented ? $"{segmentCount} segments" : "single stream")})");
                    break;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"An exception occurred after downloading {totalBytesRead} bytes. ({i}/{maxTries})");
                    App.Logger.WriteException(LOG_IDENT, ex);

                    if (ex.GetType() == typeof(ChecksumFailedException))
                    {
                        App.SendStat("packageDownloadState", "httpFail");

                        Frontend.ShowConnectivityDialog(
                            Strings.Dialog_Connectivity_UnableToDownload,
                            String.Format(Strings.Dialog_Connectivity_UnableToDownloadReason, "[https://bloxstraplabs.com/wiki/help/bloxstrap-cannot-download-roblox/](https://bloxstraplabs.com/wiki/help/bloxstrap-cannot-download-roblox/)"),
                            MessageBoxImage.Error,
                            ex
                        );

                        App.Terminate(ErrorCode.ERROR_CANCELLED);
                    }
                    else if (i >= maxTries)
                        throw;

                    if (File.Exists(package.DownloadPath))
                        File.Delete(package.DownloadPath);

                    Interlocked.Add(ref _totalDownloadedBytes, -totalBytesRead);
                    UpdateProgressBar();

                    if (ex.GetType() == typeof(IOException) && !packageUrl.StartsWith("http://"))
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Retrying download over HTTP...");
                        packageUrl = packageUrl.Replace("https://", "http://");
                    }
                }
            }
        }

        private async Task<long> DownloadPackageSingleStream(Package package, string packageUrl, int bufferSize)
        {
            long totalBytesRead = 0;
            var buffer = new byte[bufferSize];

            var response = await App.HttpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, _cancelTokenSource.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(_cancelTokenSource.Token);

            using (var fileStream = new FileStream(package.DownloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete))
            {
                while (true)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                    {
                        stream.Close();
                        fileStream.Close();
                        return totalBytesRead;
                    }

                    int bytesRead = await stream.ReadAsync(buffer, _cancelTokenSource.Token);

                    if (bytesRead == 0)
                        break;

                    totalBytesRead += bytesRead;

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _cancelTokenSource.Token);

                    Interlocked.Add(ref _totalDownloadedBytes, bytesRead);
                    UpdateProgressBar();
                }
            }

            return totalBytesRead;
        }

        private async Task<bool> TryDownloadPackageSegmented(Package package, string packageUrl, int segmentCount, int bufferSize, Action<long> onBytesRead)
        {
            string LOG_IDENT = $"Bootstrapper::DownloadPackageSegmented.{package.Name}";

            long totalLength;

            await AcquireDownloadSlot();
            try
            {
                using var probeRequest = new HttpRequestMessage(HttpMethod.Get, packageUrl);
                probeRequest.Headers.Range = new RangeHeaderValue(0, 0);

                using var probeResponse = await App.HttpClient.SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead, _cancelTokenSource.Token);

                if (probeResponse.StatusCode != HttpStatusCode.PartialContent || probeResponse.Content.Headers.ContentRange?.Length is not long length || length <= 0)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Server did not respond to a ranged request, falling back to a single-stream download for this package");
                    return false;
                }

                totalLength = length;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Ranged request probe failed, falling back to a single-stream download for this package");
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
            finally
            {
                ReleaseDownloadSlot();
            }

            if (totalLength < DownloadConfiguration.MinSegmentablePackageSize)
                return false;

            long segmentSize = (long)Math.Ceiling(totalLength / (double)segmentCount);

            using (var presizeStream = new FileStream(package.DownloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.Delete))
                presizeStream.SetLength(totalLength);

            using (SafeFileHandle handle = File.OpenHandle(package.DownloadPath, FileMode.Open, FileAccess.Write, FileShare.Delete))
            {
                var segmentTasks = new List<Task>();

                for (long start = 0; start < totalLength; start += segmentSize)
                {
                    long segmentStart = start;
                    long segmentEnd = Math.Min(start + segmentSize - 1, totalLength - 1);

                    segmentTasks.Add(DownloadSegment(handle, packageUrl, segmentStart, segmentEnd, bufferSize, onBytesRead));
                }

                await Task.WhenAll(segmentTasks);
            }

            return true;
        }

        private async Task DownloadSegment(SafeFileHandle handle, string packageUrl, long start, long end, int bufferSize, Action<long> onBytesRead)
        {
            await AcquireDownloadSlot();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, packageUrl);
                request.Headers.Range = new RangeHeaderValue(start, end);

                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancelTokenSource.Token);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(_cancelTokenSource.Token);

                var buffer = new byte[bufferSize];
                long offset = start;

                while (true)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                        return;

                    int bytesRead = await stream.ReadAsync(buffer, _cancelTokenSource.Token);

                    if (bytesRead == 0)
                        break;

                    await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, bytesRead), offset, _cancelTokenSource.Token);
                    offset += bytesRead;

                    onBytesRead(bytesRead);
                    Interlocked.Add(ref _totalDownloadedBytes, bytesRead);
                    UpdateProgressBar();
                }
            }
            finally
            {
                ReleaseDownloadSlot();
            }
        }

        private Task AcquireDownloadSlot() =>
            _downloadRequestThrottle?.WaitAsync(_cancelTokenSource.Token) ?? Task.CompletedTask;

        private void ReleaseDownloadSlot() =>
            _downloadRequestThrottle?.Release();

        private void ExtractPackage(Package package, List<string>? files = null)
        {
            const string LOG_IDENT = "Bootstrapper::ExtractPackage";

            string? packageDir = AppData.PackageDirectoryMap.GetValueOrDefault(package.Name);

            if (packageDir is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"WARNING: {package.Name} was not found in the package map!");
                return;
            }

            string packageFolder = Path.Combine(_latestVersionDirectory, packageDir);
            string? fileFilter = null;

            if (files is not null)
            {
                var regexList = new List<string>();

                foreach (string file in files)
                    regexList.Add("^" + file.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + "$");

                fileFilter = String.Join(';', regexList);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Extracting {package.Name}...");

            var fastZip = new FastZip(_fastZipEvents);
            fastZip.RestoreDateTimeOnExtract = false;
            fastZip.RestoreAttributesOnExtract = false;

            fastZip.ExtractZip(package.DownloadPath, packageFolder, fileFilter);

            App.Logger.WriteLine(LOG_IDENT, $"Finished extracting {package.Name}");
        }
        #endregion
    }
}
