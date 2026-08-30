using System.Windows;
using PhasmaStrap.Models.RobloxApi;
using DiscordRPC;

namespace PhasmaStrap.Integrations
{
    /// <summary>
    /// A read-only snapshot of the fields most recently sent to Discord, as shown by the standalone
    /// RPC Debug viewer window (UI/Elements/ContextMenu/RPCWindow.xaml). Deliberately mirrors only
    /// what's actually presented to Discord - it's built from <see cref="DiscordRPC.RichPresence"/>
    /// right before/after <see cref="DiscordRpcClient.SetPresence"/>/<see cref="DiscordRpcClient.ClearPresence"/>,
    /// not from any of the intermediate BloxstrapRPC/template state that produced it.
    /// </summary>
    public sealed class RichPresenceSnapshot
    {
        public bool IsActive { get; init; }
        public string? Details { get; init; }
        public string? State { get; init; }
        public string? LargeImageKey { get; init; }
        public string? LargeImageText { get; init; }
        public string? SmallImageKey { get; init; }
        public string? SmallImageText { get; init; }
        public DateTime? TimestampStart { get; init; }
        public DateTime? TimestampEnd { get; init; }
        public IReadOnlyList<(string Label, string Url)> Buttons { get; init; } = Array.Empty<(string, string)>();
        public DateTime CapturedAt { get; init; } = DateTime.Now;
    }

    public class DiscordRichPresence : IDisposable
    {
        private readonly DiscordRpcClient _rpcClient = new("1005469189907173486");
        private readonly ActivityWatcher _activityWatcher;
        private readonly Queue<Message> _messageQueue = new();

        private DiscordRPC.RichPresence? _currentPresence;
        private DiscordRPC.RichPresence? _originalPresence;

        private FixedSizeList<ThumbnailCacheEntry> _thumbnailCache = new FixedSizeList<ThumbnailCacheEntry>(20);

        private ulong? _smallImgBeingFetched = null;
        private ulong? _largeImgBeingFetched = null;
        private CancellationTokenSource? _fetchThumbnailsToken;

        private bool _visible = true;

        /// <summary>
        /// The last presence snapshot actually sent to (or cleared from) Discord. Updated at the end
        /// of every <see cref="UpdatePresence"/> call, which raises <see cref="PresenceChanged"/> -
        /// used by the standalone RPC Debug viewer window to observe live presence state without
        /// duplicating any of the RPC building logic above.
        /// </summary>
        public RichPresenceSnapshot CurrentSnapshot { get; private set; } = new RichPresenceSnapshot { IsActive = false };

        /// <summary>
        /// Raised (off the UI thread - marshal before touching UI state) whenever <see cref="CurrentSnapshot"/>
        /// changes.
        /// </summary>
        public event EventHandler? PresenceChanged;

        public DiscordRichPresence(ActivityWatcher activityWatcher)
        {
            const string LOG_IDENT = "DiscordRichPresence";

            _activityWatcher = activityWatcher;

            _activityWatcher.OnGameJoin += (_, _) => Task.Run(() => SetCurrentGame());
            _activityWatcher.OnGameLeave += (_, _) => Task.Run(() => SetCurrentGame());
            _activityWatcher.OnRPCMessage += (_, message) => ProcessRPCMessage(message);

            _rpcClient.OnReady += (_, e) =>
                App.Logger.WriteLine(LOG_IDENT, $"Received ready from user {e.User} ({e.User.ID})");

            _rpcClient.OnPresenceUpdate += (_, e) =>
                App.Logger.WriteLine(LOG_IDENT, "Presence updated");

            _rpcClient.OnError += (_, e) =>
                App.Logger.WriteLine(LOG_IDENT, $"An RPC error occurred - {e.Message}");

            _rpcClient.OnConnectionEstablished += (_, e) =>
                App.Logger.WriteLine(LOG_IDENT, "Established connection with Discord RPC");

            //spams log as it tries to connect every ~15 sec when discord is closed so not now
            //_rpcClient.OnConnectionFailed += (_, e) =>
            //    App.Logger.WriteLine(LOG_IDENT, "Failed to establish connection with Discord RPC");

            _rpcClient.OnClose += (_, e) =>
                App.Logger.WriteLine(LOG_IDENT, $"Lost connection to Discord RPC - {e.Reason} ({e.Code})");

            _rpcClient.Initialize();
        }

        public void ProcessRPCMessage(Message message, bool implicitUpdate = true)
        {
            const string LOG_IDENT = "DiscordRichPresence::ProcessRPCMessage";

            if (message.Command != "SetRichPresence" && message.Command != "SetLaunchData")
                return;

            if (_currentPresence is null || _originalPresence is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Presence is not set, enqueuing message");
                _messageQueue.Enqueue(message);
                return;
            }

            // a lot of repeated code here, could this somehow be cleaned up?

            if (message.Command == "SetLaunchData")
            {
                _currentPresence.Buttons = GetButtons();
            }
            else if (message.Command == "SetRichPresence")
            {
                ProcessSetRichPresence(message, implicitUpdate);
            }

            if (implicitUpdate)
                UpdatePresence();
        }

        private void AddToThumbnailCache(ulong id, string? url)
        {
            if (url != null)
                _thumbnailCache.Add(new ThumbnailCacheEntry { Id = id, Url = url });
        }

        private async Task UpdatePresenceIconsAsync(ulong? smallImg, ulong? largeImg, bool implicitUpdate, CancellationToken token)
        {
            Debug.Assert(smallImg != null || largeImg != null);

            if (smallImg != null && largeImg != null)
            {
                string?[] urls = await Thumbnails.GetThumbnailUrlsAsync(new List<ThumbnailRequest>
                {
                    new ThumbnailRequest
                    {
                        TargetId = (ulong)smallImg,
                        Type = "Asset",
                        Size = "512x512",
                        IsCircular = false
                    },
                    new ThumbnailRequest
                    {
                        TargetId = (ulong)largeImg,
                        Type = "Asset",
                        Size = "512x512",
                        IsCircular = false
                    }
                }, token);

                string? smallUrl = urls[0];
                string? largeUrl = urls[1];

                AddToThumbnailCache((ulong)smallImg, smallUrl);
                AddToThumbnailCache((ulong)largeImg, largeUrl);

                if (_currentPresence != null)
                {
                    _currentPresence.Assets.SmallImageKey = smallUrl;
                    _currentPresence.Assets.LargeImageKey = largeUrl;
                }
            }
            else if (smallImg != null)
            {
                string? url = await Thumbnails.GetThumbnailUrlAsync(new ThumbnailRequest
                {
                    TargetId = (ulong)smallImg,
                    Type = "Asset",
                    Size = "512x512",
                    IsCircular = false
                }, token);

                AddToThumbnailCache((ulong)smallImg, url);

                if (_currentPresence != null)
                    _currentPresence.Assets.SmallImageKey = url;
            }
            else if (largeImg != null)
            {
                string? url = await Thumbnails.GetThumbnailUrlAsync(new ThumbnailRequest
                {
                    TargetId = (ulong)largeImg,
                    Type = "Asset",
                    Size = "512x512",
                    IsCircular = false
                }, token);

                AddToThumbnailCache((ulong)largeImg, url);

                if (_currentPresence != null)
                    _currentPresence.Assets.LargeImageKey = url;
            }

            _smallImgBeingFetched = null;
            _largeImgBeingFetched = null;

            if (implicitUpdate)
                UpdatePresence();
        }

        private void ProcessSetRichPresence(Message message, bool implicitUpdate)
        {
            const string LOG_IDENT = "DiscordRichPresence::ProcessSetRichPresence";
            Models.BloxstrapRPC.RichPresence? presenceData;

            Debug.Assert(_currentPresence is not null);
            Debug.Assert(_originalPresence is not null);

            if (_fetchThumbnailsToken != null)
            {
                _fetchThumbnailsToken.Cancel();
                _fetchThumbnailsToken = null;
            }

            try
            {
                presenceData = message.Data.Deserialize<Models.BloxstrapRPC.RichPresence>();
            }
            catch (Exception)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                return;
            }

            if (presenceData is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                return;
            }

            if (presenceData.Details is not null)
            {
                if (presenceData.Details.Length > 128)
                    App.Logger.WriteLine(LOG_IDENT, $"Details cannot be longer than 128 characters");
                else if (presenceData.Details == "<reset>")
                    _currentPresence.Details = _originalPresence.Details;
                else
                    _currentPresence.Details = presenceData.Details;
            }

            if (presenceData.State is not null)
            {
                if (presenceData.State.Length > 128)
                    App.Logger.WriteLine(LOG_IDENT, $"State cannot be longer than 128 characters");
                else if (presenceData.State == "<reset>")
                    _currentPresence.State = _originalPresence.State;
                else
                    _currentPresence.State = presenceData.State;
            }

            if (presenceData.TimestampStart == 0)
                _currentPresence.Timestamps.Start = null;
            else if (presenceData.TimestampStart is not null)
                _currentPresence.Timestamps.StartUnixMilliseconds = presenceData.TimestampStart * 1000;

            if (presenceData.TimestampEnd == 0)
                _currentPresence.Timestamps.End = null;
            else if (presenceData.TimestampEnd is not null)
                _currentPresence.Timestamps.EndUnixMilliseconds = presenceData.TimestampEnd * 1000;

            // set these to start fetching
            ulong? smallImgFetch = null;
            ulong? largeImgFetch = null;

            // only set small image if account display is disabled, doesnt make sense to override it if it is true
            if (presenceData.SmallImage is not null && !App.Settings.Prop.ShowAccountOnRichPresence)
            {
                if (presenceData.SmallImage.Clear)
                {
                    _currentPresence.Assets.SmallImageKey = "";
                    _smallImgBeingFetched = null;
                }
                else if (presenceData.SmallImage.Reset)
                {
                    _currentPresence.Assets.SmallImageText = _originalPresence.Assets.SmallImageText;
                    _currentPresence.Assets.SmallImageKey = _originalPresence.Assets.SmallImageKey;
                    _smallImgBeingFetched = null;
                }
                else
                {
                    if (presenceData.SmallImage.AssetId is not null)
                    {
                        ThumbnailCacheEntry? entry = _thumbnailCache.FirstOrDefault(x => x.Id == presenceData.SmallImage.AssetId);

                        if (entry == null)
                        {
                            smallImgFetch = presenceData.SmallImage.AssetId;
                        }
                        else
                        {
                            _currentPresence.Assets.SmallImageKey = entry.Url;
                            _smallImgBeingFetched = null;
                        }
                    }

                    if (presenceData.SmallImage.HoverText is not null)
                        _currentPresence.Assets.SmallImageText = presenceData.SmallImage.HoverText;
                }
            }

            if (presenceData.LargeImage is not null)
            {
                if (presenceData.LargeImage.Clear)
                {
                    _currentPresence.Assets.LargeImageKey = "";
                    _largeImgBeingFetched = null;
                }
                else if (presenceData.LargeImage.Reset)
                {
                    _currentPresence.Assets.LargeImageText = _originalPresence.Assets.LargeImageText;
                    _currentPresence.Assets.LargeImageKey = _originalPresence.Assets.LargeImageKey;
                    _largeImgBeingFetched = null;
                }
                else
                {
                    if (presenceData.LargeImage.AssetId is not null)
                    {
                        ThumbnailCacheEntry? entry = _thumbnailCache.FirstOrDefault(x => x.Id == presenceData.LargeImage.AssetId);

                        if (entry == null)
                        {
                            largeImgFetch = presenceData.LargeImage.AssetId;
                        }
                        else
                        {
                            _currentPresence.Assets.LargeImageKey = entry.Url;
                            _largeImgBeingFetched = null;
                        }
                    }

                    if (presenceData.LargeImage.HoverText is not null)
                        _currentPresence.Assets.LargeImageText = presenceData.LargeImage.HoverText;
                }
            }

            if (smallImgFetch != null)
                _smallImgBeingFetched = smallImgFetch;
            if (largeImgFetch != null)
                _largeImgBeingFetched = largeImgFetch;

            if (_smallImgBeingFetched != null || _largeImgBeingFetched != null)
            {
                _fetchThumbnailsToken = new CancellationTokenSource();
                Task.Run(() => UpdatePresenceIconsAsync(_smallImgBeingFetched, _largeImgBeingFetched, implicitUpdate, _fetchThumbnailsToken.Token));
            }
        }

        public void SetVisibility(bool visible)
        {
            App.Logger.WriteLine("DiscordRichPresence::SetVisibility", $"Setting presence visibility ({visible})");

            _visible = visible;

            if (_visible)
                UpdatePresence();
            else
            {
                _rpcClient.ClearPresence();
                PublishSnapshot(null);
            }
        }

        public async Task<bool> SetCurrentGame()
        {
            const string LOG_IDENT = "DiscordRichPresence::SetCurrentGame";
            
            if (!_activityWatcher.InGame)
            {
                App.Logger.WriteLine(LOG_IDENT, "Not in game, clearing presence");

                _currentPresence = _originalPresence =  null;
                _messageQueue.Clear();

                UpdatePresence();
                return true;
            }

            string icon = "roblox";
            string smallImageText = "Roblox";
            string smallImage = "roblox";
            

            var activity = _activityWatcher.Data;
            long placeId = activity.PlaceId;

            App.Logger.WriteLine(LOG_IDENT, $"Setting presence for Place ID {placeId}");

            // preserve time spent playing if we're teleporting between places in the same universe
            var timeStarted = activity.TimeJoined;

            if (activity.RootActivity is not null)
                timeStarted = activity.RootActivity.TimeJoined;

            if (activity.UniverseDetails is null)
            {
                try
                {
                    await UniverseDetails.FetchSingle(activity.UniverseId);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
                    Frontend.ShowMessageBox($"{Strings.ActivityWatcher_RichPresenceLoadFailed}\n\n{ex.Message}", MessageBoxImage.Warning);
                    return false;
                }

                activity.UniverseDetails = UniverseDetails.LoadFromCache(activity.UniverseId);
            }

            var universeDetails = activity.UniverseDetails!;

            icon = universeDetails.Thumbnail.ImageUrl!;

            if (App.Settings.Prop.ShowAccountOnRichPresence)
            {
                var userDetails = await UserDetails.Fetch(activity.UserId);

                smallImage = userDetails.Thumbnail.ImageUrl!;
                smallImageText = $"Playing on {userDetails.Data.DisplayName} (@{userDetails.Data.Name})"; // i.e. "axell (@Axelan_se)"
            }

            if (!_activityWatcher.InGame || placeId != activity.PlaceId)
            {
                App.Logger.WriteLine(LOG_IDENT, "Aborting presence set because game activity has changed");
                return false;
            }

            string status = _activityWatcher.Data.ServerType switch
            {
                ServerType.Private => "In a private server",
                ServerType.Reserved => "In a reserved server",
                _ => $"by {universeDetails.Data.Creator.Name}" + (universeDetails.Data.Creator.HasVerifiedBadge ? " ☑️" : ""),
            };

            string universeName = universeDetails.Data.Name;

            if (universeName.Length < 2)
                universeName = $"{universeName}\x2800\x2800\x2800";

            _currentPresence = new DiscordRPC.RichPresence
            {
                Details = universeName,
                State = status,
                Timestamps = new Timestamps { Start = timeStarted.ToUniversalTime() },
                Buttons = GetButtons(),
                Assets = new Assets
                {
                    LargeImageKey = icon,
                    LargeImageText = universeDetails.Data.Name,
                    SmallImageKey = smallImage,
                    SmallImageText = smallImageText
                }
            };

            // apply the user's own rich presence template (if one is configured for this game) as the
            // new baseline, *before* snapshotting into _originalPresence. this means a game's own
            // BloxstrapRPC messages (processed below/afterwards via ProcessRPCMessage) still take full
            // priority and can override any field at runtime same as before, but "<reset>" from a game
            // now resets back to the user's templated defaults rather than the raw computed ones - the
            // template only controls the idle/default appearance, never live gameplay data a game
            // reports about itself.
            ApplyUserTemplate(placeId, activity.UniverseId, universeDetails.Data.Name, status, universeDetails.Data.Creator.Name);

            // this is used for configuration from BloxstrapRPC
            _originalPresence = _currentPresence.Clone();

            if (_messageQueue.Any())
            {
                App.Logger.WriteLine(LOG_IDENT, "Processing queued messages");
                ProcessRPCMessage(_messageQueue.Dequeue(), false);
            }
            
            UpdatePresence();

            return true;
        }

        // fills in the current presence from a matching user-authored RPCTemplate (see Models/RPCTemplate.cs),
        // substituting {gameName}, {status}, {creator}, {placeId} and {universeId} placeholder tokens.
        // does nothing if no enabled template is configured for this place ID.
        private void ApplyUserTemplate(long placeId, long universeId, string gameName, string status, string creatorName)
        {
            const string LOG_IDENT = "DiscordRichPresence::ApplyUserTemplate";

            if (_currentPresence is null)
                return;

            string placeIdString = placeId.ToString();

            RPCTemplate? template = App.Settings.Prop.RPCTemplates
                .FirstOrDefault(t => t.Enabled && t.GameID == placeIdString);

            if (template is null)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Applying user rich presence template for place ID {placeId}");

            string Substitute(string text) => text
                .Replace("{gameName}", gameName)
                .Replace("{status}", status)
                .Replace("{creator}", creatorName)
                .Replace("{placeId}", placeIdString)
                .Replace("{universeId}", universeId.ToString());

            if (!String.IsNullOrWhiteSpace(template.DetailsTemplate))
                _currentPresence.Details = Substitute(template.DetailsTemplate);

            if (!String.IsNullOrWhiteSpace(template.StateTemplate))
                _currentPresence.State = Substitute(template.StateTemplate);

            if (!String.IsNullOrWhiteSpace(template.LargeImageUrl))
                _currentPresence.Assets.LargeImageKey = template.LargeImageUrl;

            // only override the small image if account display is disabled, same rule the BloxstrapRPC
            // handler above already applies, so the two features don't fight over the same slot
            if (!String.IsNullOrWhiteSpace(template.SmallImageUrl) && !App.Settings.Prop.ShowAccountOnRichPresence)
                _currentPresence.Assets.SmallImageKey = template.SmallImageUrl;

            if (!String.IsNullOrWhiteSpace(template.ButtonLabel) && !String.IsNullOrWhiteSpace(template.ButtonUrl))
            {
                var buttons = new List<Button>
                {
                    new Button { Label = template.ButtonLabel, Url = template.ButtonUrl }
                };

                if (_currentPresence.Buttons is not null)
                    buttons.AddRange(_currentPresence.Buttons);

                // discord only allows a maximum of 2 buttons
                if (buttons.Count > 2)
                    buttons = buttons.Take(2).ToList();

                _currentPresence.Buttons = buttons.ToArray();
            }
        }

        public Button[] GetButtons()
        {
            var buttons = new List<Button>();

            var data = _activityWatcher.Data;

            if (!App.Settings.Prop.HideRPCButtons)
            {
                bool show = false;

                if (data.ServerType == ServerType.Public)
                    show = true;
                else if (data.ServerType == ServerType.Reserved && !String.IsNullOrEmpty(data.RPCLaunchData))
                    show = true;

                if (show)
                {
                    buttons.Add(new Button
                    {
                        Label = "Join server",
                        Url = data.GetInviteDeeplink()
                    });
                }
            }

            buttons.Add(new Button
            {
                Label = "See game page",
                Url = $"https://www.roblox.com/games/{data.PlaceId}"
            });

            return buttons.ToArray();
        }

        public void UpdatePresence()
        {
            const string LOG_IDENT = "DiscordRichPresence::UpdatePresence";
            
            if (_currentPresence is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Presence is empty, clearing");
                _rpcClient.ClearPresence();
                PublishSnapshot(null);
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Updating presence");

            if (_visible)
            {
                _rpcClient.SetPresence(_currentPresence);

                // fire-and-forget: send the untranslated presence immediately (above), then swap
                // in the translated fields once TranslationService resolves them, without ever
                // blocking this (synchronous) UpdatePresence caller on a network round-trip
                if (Utility.RpcTranslationService.IsEnabled)
                    _ = ApplyPresenceTranslationAsync(_currentPresence);
            }

            PublishSnapshot(_visible ? _currentPresence : null);
        }

        private void PublishSnapshot(DiscordRPC.RichPresence? presence)
        {
            CurrentSnapshot = presence is null
                ? new RichPresenceSnapshot { IsActive = false }
                : new RichPresenceSnapshot
                {
                    IsActive = true,
                    Details = presence.Details,
                    State = presence.State,
                    LargeImageKey = presence.Assets?.LargeImageKey,
                    LargeImageText = presence.Assets?.LargeImageText,
                    SmallImageKey = presence.Assets?.SmallImageKey,
                    SmallImageText = presence.Assets?.SmallImageText,
                    TimestampStart = presence.Timestamps?.Start,
                    TimestampEnd = presence.Timestamps?.End,
                    Buttons = presence.Buttons?.Select(b => (b.Label, b.Url)).ToArray() ?? Array.Empty<(string, string)>()
                };

            PresenceChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task ApplyPresenceTranslationAsync(DiscordRPC.RichPresence presence)
        {
            try
            {
                await Utility.RpcTranslationService.ApplyToPresenceAsync(presence).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DiscordRichPresence::ApplyPresenceTranslationAsync", $"Error: {ex.Message}");
                return;
            }

            // only re-send if this is still the current presence and we're still visible - avoids
            // clobbering a newer presence that was set while the translation was in flight
            if (_visible && ReferenceEquals(_currentPresence, presence))
                _rpcClient.SetPresence(presence);
        }

        public void Dispose()
        {
            App.Logger.WriteLine("DiscordRichPresence::Dispose", "Cleaning up Discord RPC and Presence");
            _rpcClient.ClearPresence();
            PublishSnapshot(null);
            _rpcClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
