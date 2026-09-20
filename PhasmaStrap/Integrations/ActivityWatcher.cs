namespace PhasmaStrap.Integrations
{
    public class ActivityWatcher : IDisposable
    {
        private const string GameMessageEntry                = "[FLog::CreatorOutput] [BloxstrapRPC]";
        private const string GameJoiningEntry                = "[FLog::Output] ! Joining game";

        private const string GameTeleportingEntry            = "[FLog::UgcExperienceController] UgcExperienceController: doTeleport: joinScriptUrl";
        private const string GameJoiningUniverseEntry        = "[FLog::GameJoinLoadTime] Report game_join_loadtime:";
        private const string GameJoiningUDMUXEntry           = "[FLog::Network] UDMUX Address = ";
        private const string GameJoinedEntry                 = "[FLog::Network] Replicator created: ";
        private const string GameDisconnectedEntry           = "[FLog::Network] Time to disconnect replication data:";
        private const string GameLeavingEntry                = "[FLog::SingleSurfaceApp] leaveUGCGameInternal";

        private const string GameJoiningEntryPattern         = @"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at ([0-9\.]+)";
        private const string GameJoinReferralPattern         = @"referral_page:([^,]+)";
        private const string GameTeleportJoinTypePattern     = @"JoinTypeId""%3a(\d+)%2c";
        private const string GameJoiningUniversePattern      = @"universeid:([0-9]+).*userid:([0-9]+)";
        private const string GameJoiningUDMUXPattern         = @"UDMUX Address = ([0-9\.]+), Port = [0-9]+ \| RCC Server Address = ([0-9\.]+), Port = [0-9]+";
        private const string GameJoinedEntryPattern          = @"serverId: ([0-9\.]+)\|[0-9]+";
        private const string GameMessageEntryPattern         = @"\[BloxstrapRPC\] (.*)";

        private int _logEntriesRead = 0;
        private bool _teleportMarker = false;
        private bool _reservedTeleportMarker = false;

        private ActivityData? _pendingJoin;

        public event EventHandler<string>? OnLogEntry;
        public event EventHandler? OnGameJoin;
        public event EventHandler? OnGameLeave;
        public event EventHandler? OnLogOpen;
        public event EventHandler? OnAppClose;
        public event EventHandler<Message>? OnRPCMessage;

        private DateTime LastRPCRequest;

        public string LogLocation = null!;

        public bool InGame = false;

        public ActivityData Data { get; private set; } = new();

        public List<ActivityData> History = new();

        public bool IsDisposed = false;

        public ActivityWatcher(string? logFile = null)
        {
            if (!String.IsNullOrEmpty(logFile))
                LogLocation = logFile;
        }

        public async void Start()
        {
            const string LOG_IDENT = "ActivityWatcher::Start";

            FileInfo logFileInfo;

            if (String.IsNullOrEmpty(LogLocation))
            {
                string logDirectory = Path.Combine(Paths.LocalAppData, "Roblox\\logs");

                if (!Directory.Exists(logDirectory))
                    return;

                App.Logger.WriteLine(LOG_IDENT, "Opening Roblox log file...");

                while (true)
                {
                    logFileInfo = new DirectoryInfo(logDirectory)
                        .GetFiles()
                        .Where(x => x.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) && x.CreationTime <= DateTime.Now)
                        .OrderByDescending(x => x.CreationTime)
                        .First();

                    if (logFileInfo.CreationTime.AddSeconds(15) > DateTime.Now)
                        break;

                    App.Logger.WriteLine(LOG_IDENT, $"Could not find recent enough log file, waiting... (newest is {logFileInfo.Name})");
                    await Task.Delay(1000);
                }

                LogLocation = logFileInfo.FullName;
            }
            else
            {
                logFileInfo = new FileInfo(LogLocation);
            }

            OnLogOpen?.Invoke(this, EventArgs.Empty);

            var logFileStream = logFileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            App.Logger.WriteLine(LOG_IDENT, $"Opened {LogLocation}");

            using var streamReader = new StreamReader(logFileStream);

            while (!IsDisposed)
            {
                string? log = await streamReader.ReadLineAsync();

                if (log is null)
                    await Task.Delay(1000);
                else
                    ReadLogEntry(log);
            }
        }

        private void ReadLogEntry(string entry)
        {
            const string LOG_IDENT = "ActivityWatcher::ReadLogEntry";

            OnLogEntry?.Invoke(this, entry);

            _logEntriesRead += 1;

            if (_logEntriesRead <= 1000 && _logEntriesRead % 50 == 0)
                App.Logger.WriteLine(LOG_IDENT, $"Read {_logEntriesRead} log entries");
            else if (_logEntriesRead % 100 == 0)
                App.Logger.WriteLine(LOG_IDENT, $"Read {_logEntriesRead} log entries");

            int logMessageIdx = entry.IndexOf(' ');
            if (logMessageIdx == -1)
            {
                return;
            }

            string logMessage = entry[(logMessageIdx + 1)..];

            if (logMessage.StartsWith(GameLeavingEntry))
            {
                App.Logger.WriteLine(LOG_IDENT, "User is back into the desktop app");

                OnAppClose?.Invoke(this, EventArgs.Empty);

                _pendingJoin = null;

                if (Data.PlaceId != 0 && !InGame)
                {
                    App.Logger.WriteLine(LOG_IDENT, "User appears to be leaving from a cancelled/errored join");
                    Data = new();
                }

                return;
            }

            if (!InGame && Data.PlaceId == 0)
            {
                if (logMessage.StartsWith(GameJoiningEntry))
                {
                    ActivityData? joining = ParseJoining(logMessage);
                    if (joining is null)
                        return;

                    InGame = false;
                    Data = joining;

                    App.Logger.WriteLine(LOG_IDENT, $"Joining Game ({Data})");
                }
            }
            else if (!InGame && Data.PlaceId != 0)
            {
                if (logMessage.StartsWith(GameJoiningUniverseEntry))
                {
                    ApplyUniverse(Data, logMessage, previous: History.FirstOrDefault());
                }
                else if (logMessage.StartsWith(GameJoiningUDMUXEntry))
                {
                    ApplyUdmux(Data, logMessage);
                }
                else if (logMessage.StartsWith(GameJoiningEntry))
                {
                    ActivityData? joining = ParseJoining(logMessage);
                    if (joining is not null && joining.JobId != Data.JobId)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Join to ({Data}) was superseded");
                        Data = joining;
                        App.Logger.WriteLine(LOG_IDENT, $"Joining Game ({Data})");
                    }
                }
                else if (logMessage.StartsWith(GameJoinedEntry))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Joined Game ({Data})");

                    InGame = true;
                    Data.TimeJoined = DateTime.Now;

                    OnGameJoin?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (InGame && Data.PlaceId != 0)
            {
                if (logMessage.StartsWith(GameDisconnectedEntry))
                {
                    LeaveCurrentGame(LOG_IDENT);

                    if (_pendingJoin is not null)
                    {
                        Data = _pendingJoin;
                        _pendingJoin = null;
                        App.Logger.WriteLine(LOG_IDENT, $"Joining Game ({Data}) - announced before the previous server disconnected");
                    }
                }
                else if (logMessage.StartsWith(GameJoiningEntry))
                {
                    ActivityData? joining = ParseJoining(logMessage);
                    if (joining is not null && joining.JobId != Data.JobId)
                    {
                        _pendingJoin = joining;
                        App.Logger.WriteLine(LOG_IDENT, $"Next server announced while still connected ({joining})");
                    }
                }
                else if (_pendingJoin is not null && logMessage.StartsWith(GameJoiningUniverseEntry))
                {
                    ApplyUniverse(_pendingJoin, logMessage, previous: Data);
                }
                else if (_pendingJoin is not null && logMessage.StartsWith(GameJoiningUDMUXEntry))
                {
                    ApplyUdmux(_pendingJoin, logMessage);
                }
                else if (_pendingJoin is not null && logMessage.StartsWith(GameJoinedEntry))
                {
                    LeaveCurrentGame(LOG_IDENT);

                    Data = _pendingJoin;
                    _pendingJoin = null;

                    App.Logger.WriteLine(LOG_IDENT, $"Joined Game ({Data})");

                    InGame = true;
                    Data.TimeJoined = DateTime.Now;

                    OnGameJoin?.Invoke(this, EventArgs.Empty);
                }
                else if (logMessage.StartsWith(GameTeleportingEntry))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Initiating teleport to server ({Data})");
                    _teleportMarker = true;

                    var joinTypeMatch = Regex.Match(logMessage, GameTeleportJoinTypePattern);
                    if (joinTypeMatch.Success && int.TryParse(joinTypeMatch.Groups[1].Value, out int joinTypeId))
                    {
                        var joinType = (ServerSessionJoinType)joinTypeId;
                        App.Logger.WriteLine(LOG_IDENT, $"Teleport JoinTypeId: {joinTypeId}");

                        if (joinType is ServerSessionJoinType.NewGamePrivateGame or ServerSessionJoinType.SpecificPrivateGame)
                        {
                            _reservedTeleportMarker = true;
                            App.Logger.WriteLine(LOG_IDENT, "Detected reserved server teleport");
                        }
                    }
                }
                else if (logMessage.StartsWith(GameMessageEntry))
                {
                    var match = Regex.Match(logMessage, GameMessageEntryPattern);

                    if (match.Groups.Count != 2)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to assert format for RPC message entry");
                        App.Logger.WriteLine(LOG_IDENT, logMessage);
                        return;
                    }

                    string messagePlain = match.Groups[1].Value;
                    Message? message;

                    App.Logger.WriteLine(LOG_IDENT, $"Received message: '{messagePlain}'");

                    if ((DateTime.Now - LastRPCRequest).TotalSeconds <= 1)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Dropping message as ratelimit has been hit");
                        return;
                    }

                    try
                    {
                        message = JsonSerializer.Deserialize<Message>(messagePlain);
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (message is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    if (string.IsNullOrEmpty(message.Command))
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (Command is empty)");
                        return;
                    }

                    if (message.Command == "SetLaunchData")
                    {
                        string? data;

                        try
                        {
                            data = message.Data.Deserialize<string>();
                        }
                        catch (Exception)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                            return;
                        }

                        if (data is null)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                            return;
                        }

                        if (data.Length > 200)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Data cannot be longer than 200 characters");
                            return;
                        }

                        Data.RPCLaunchData = data;
                    }

                    OnRPCMessage?.Invoke(this, message);

                    LastRPCRequest = DateTime.Now;
                }
            }
        }

        private void LeaveCurrentGame(string logIdent)
        {
            App.Logger.WriteLine(logIdent, $"Disconnected from Game ({Data})");

            Data.TimeLeft = DateTime.Now;
            History.Insert(0, Data);

            InGame = false;
            Data = new();

            OnGameLeave?.Invoke(this, EventArgs.Empty);
        }

        private ActivityData? ParseJoining(string logMessage)
        {
            const string LOG_IDENT = "ActivityWatcher::ParseJoining";

            Match match = Regex.Match(logMessage, GameJoiningEntryPattern);

            if (match.Groups.Count != 4)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to assert format for game join entry");
                App.Logger.WriteLine(LOG_IDENT, logMessage);
                return null;
            }

            var data = new ActivityData
            {
                PlaceId = long.Parse(match.Groups[2].Value),
                JobId = match.Groups[1].Value,
                MachineAddress = match.Groups[3].Value,
            };

            if (App.Settings.Prop.ShowServerDetails && data.MachineAddressValid)
                _ = data.QueryServerLocation();

            if (_teleportMarker)
            {
                data.IsTeleport = true;
                _teleportMarker = false;
            }

            if (_reservedTeleportMarker)
            {
                data.ServerType = ServerType.Reserved;
                _reservedTeleportMarker = false;
            }

            return data;
        }

        private static void ApplyUniverse(ActivityData data, string logMessage, ActivityData? previous)
        {
            const string LOG_IDENT = "ActivityWatcher::ApplyUniverse";

            var match = Regex.Match(logMessage, GameJoiningUniversePattern);

            if (match.Groups.Count != 3)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to assert format for game join universe entry");
                App.Logger.WriteLine(LOG_IDENT, logMessage);
                return;
            }

            data.UniverseId = Int64.Parse(match.Groups[1].Value);
            data.UserId = Int64.Parse(match.Groups[2].Value);

            var loadTimeMatch = Regex.Match(logMessage, GameJoinReferralPattern);

            if (loadTimeMatch.Groups.Count == 2)
            {
                string referral = loadTimeMatch.Groups[1].Value;

                if (referral.Contains("RequestPrivateGame", StringComparison.OrdinalIgnoreCase) || referral.Contains("GameDetailPageJSHybridEvent", StringComparison.OrdinalIgnoreCase))
                    data.ServerType = ServerType.Private;
            }

            if (previous is not null && data.UniverseId == previous.UniverseId && data.IsTeleport)
                data.RootActivity = previous.RootActivity ?? previous;
        }

        private static void ApplyUdmux(ActivityData data, string logMessage)
        {
            const string LOG_IDENT = "ActivityWatcher::ApplyUdmux";

            var match = Regex.Match(logMessage, GameJoiningUDMUXPattern);

            if (match.Groups.Count != 3 || match.Groups[2].Value != data.MachineAddress)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to assert format for game join UDMUX entry");
                App.Logger.WriteLine(LOG_IDENT, logMessage);
                return;
            }

            data.MachineAddress = match.Groups[1].Value;

            if (App.Settings.Prop.ShowServerDetails)
                _ = data.QueryServerLocation();

            App.Logger.WriteLine(LOG_IDENT, $"Server is UDMUX protected ({data})");
        }

        public void Dispose()
        {
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
