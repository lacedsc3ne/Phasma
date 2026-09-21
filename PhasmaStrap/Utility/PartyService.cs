using System.Net.Http;

namespace PhasmaStrap.Utility
{
    public sealed class PartyMember
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("leader")]
        public bool Leader { get; set; }

        [JsonPropertyName("avatar")]
        public string Avatar { get; set; } = "";
    }

    public sealed class PartyInvite
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("from")]
        public string From { get; set; } = "";

        [JsonPropertyName("members")]
        public int Members { get; set; }
    }

    public sealed class PartyJoin
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; } = "";

        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = "";
    }

    public sealed class PartyState
    {
        [JsonPropertyName("in_party")]
        public bool InParty { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("leader")]
        public bool Leader { get; set; }

        [JsonPropertyName("leader_name")]
        public string LeaderName { get; set; } = "";

        [JsonPropertyName("members")]
        public List<PartyMember> Members { get; set; } = new();

        [JsonPropertyName("join")]
        public PartyJoin? Join { get; set; }

        [JsonPropertyName("invites")]
        public List<PartyInvite> Invites { get; set; } = new();
    }

    public static class PartyService
    {
        private const string LOG_IDENT = "PartyService";

        private static string MarkerPath => Path.Combine(Paths.Base, "Party.json");

        public static PartyState Current { get; private set; } = new();

        public static event EventHandler? Changed;

        public static event EventHandler<PartyJoin>? JoinRequested;

        public static bool InParty => Current.InParty;

        public static bool IsLeader => Current.InParty && Current.Leader;

        private static async Task<PartyState?> SendAsync(HttpMethod method, string path, object? body = null)
        {
            if (!PhasmaAccount.SignedIn)
                return null;

            try
            {
                using var request = new HttpRequestMessage(method, $"{App.ServerBase}{path}");
                PhasmaAccount.Authorize(request);

                if (body is not null)
                    request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{path} answered {(int)response.StatusCode}: {text}");
                    return null;
                }

                return JsonSerializer.Deserialize<PartyState>(text);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{path} failed: {ex.Message}");
                return null;
            }
        }

        private static void Adopt(PartyState? state)
        {
            if (state is null)
                return;

            Current = state;
            WriteMarker(state);

            Changed?.Invoke(null, EventArgs.Empty);

            if (state.Join is not null && !string.IsNullOrEmpty(state.Join.PlaceId))
                JoinRequested?.Invoke(null, state.Join);
        }

        private static void WriteMarker(PartyState state)
        {
            try
            {
                if (!state.InParty)
                {
                    if (File.Exists(MarkerPath))
                        File.Delete(MarkerPath);

                    return;
                }

                File.WriteAllText(MarkerPath, JsonSerializer.Serialize(new { in_party = true, leader = state.Leader, code = state.Code }));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not write the party marker: {ex.Message}");
            }
        }

        public static bool LeaderFromMarker()
        {
            try
            {
                if (!File.Exists(MarkerPath))
                    return false;

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(MarkerPath));
                return document.RootElement.TryGetProperty("leader", out JsonElement leader) && leader.GetBoolean();
            }
            catch
            {
                return false;
            }
        }

        public static async Task RefreshAsync() => Adopt(await SendAsync(HttpMethod.Get, "/v1/party"));

        public static async Task CreateAsync() => Adopt(await SendAsync(HttpMethod.Post, "/v1/party/create"));

        public static async Task<bool> JoinAsync(string code)
        {
            PartyState? state = await SendAsync(HttpMethod.Post, "/v1/party/join", new { code });
            Adopt(state);
            return state is not null;
        }

        public static async Task LeaveAsync() => Adopt(await SendAsync(HttpMethod.Post, "/v1/party/leave"));

        public static async Task DeclineInviteAsync(string id) => Adopt(await SendAsync(HttpMethod.Post, "/v1/party/invite/decline", new { id }));

        public static async Task<bool> InviteAsync(long robloxUserId)
        {
            if (!PhasmaAccount.SignedIn)
                return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/party/invite");
                PhasmaAccount.Authorize(request);
                request.Content = new StringContent(JsonSerializer.Serialize(new { roblox_id = robloxUserId.ToString() }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Inviting {robloxUserId} failed: {ex.Message}");
                return false;
            }
        }

        public static async Task ReportLaunchAsync(long placeId, string jobId)
        {
            if (!PhasmaAccount.SignedIn || !LeaderFromMarker())
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/party/launch");
                PhasmaAccount.Authorize(request);
                request.Content = new StringContent(JsonSerializer.Serialize(new { place_id = placeId.ToString(), job_id = jobId ?? "" }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                App.Logger.WriteLine(LOG_IDENT, response.IsSuccessStatusCode
                    ? $"Told the party to join {placeId}/{jobId}"
                    : $"The party server would not take the launch ({(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Reporting the launch failed: {ex.Message}");
            }
        }

        private static CancellationTokenSource? _cts;
        private static Task? _loop;

        public static void Start()
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _loop = null;
        }

        private static async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (PhasmaAccount.SignedIn)
                        await RefreshAsync();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Poll failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(App.Settings.Prop.PartyPollSeconds, 2, 30)), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
