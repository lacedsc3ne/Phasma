using System.Text.Json.Nodes;

using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.Networking
{
    public static class JoinPickerPolicy
    {
        private const string LOG_IDENT = "JoinPickerPolicy";

        public const string Host = "gamejoin.roblox.com";

        private static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(25);

        public static bool IsEnabled => App.Settings.Prop.NetworkingProxyEnabled && App.Settings.Prop.JoinServerPickerEnabled;

        private static readonly object Sync = new();
        private static long _decidingPlace;
        private static Task<string?>? _deciding;
        private static (long PlaceId, string? JobId, DateTime AtUtc) _last;

        public static async Task<ProxiedResponse?> HandleAsync(ProxiedRequest request, CancellationToken token)
        {
            try
            {
                if (!IsEnabled || !request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                    return null;

                string path = request.Path.Split('?')[0].TrimEnd('/');
                if (!path.Equals("/v1/join-game", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (JsonNode.Parse(request.Body) is not JsonObject body)
                    return null;

                long placeId = body["placeId"] is JsonValue value && value.TryGetValue(out long id) ? id : 0;
                bool teleport = body["isTeleport"] is JsonValue flag && flag.TryGetValue(out bool isTeleport) && isTeleport;

                if (placeId <= 0 || teleport)
                    return null;

                string? jobId = await DecideAsync(placeId, token);
                if (string.IsNullOrEmpty(jobId))
                    return null;

                body["gameId"] = jobId;

                var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
                ProxiedRequest rewritten = request with
                {
                    Path = request.Path.Replace("/v1/join-game", "/v1/join-game-instance", StringComparison.OrdinalIgnoreCase),
                    Headers = headers,
                    Body = Encoding.UTF8.GetBytes(body.ToJsonString()),
                };

                App.Logger.WriteLine(LOG_IDENT, $"Place {placeId}: joining the chosen server {jobId}");

                ProxiedResponse? response = await AssetProxyServer.ForwardToUpstreamAsync(rewritten, token);
                if (response is null || response.StatusCode >= 400)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox refused the chosen server ({response?.StatusCode.ToString() ?? "no answer"}) - handing the join back");
                    return null;
                }

                return response;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Picker failed, the join goes through untouched: {ex.Message}");
                return null;
            }
        }

        private static Task<string?> DecideAsync(long placeId, CancellationToken token)
        {
            lock (Sync)
            {
                if (_deciding is not null && _decidingPlace == placeId && !_deciding.IsCompleted)
                    return _deciding;

                if (_last.PlaceId == placeId && (DateTime.UtcNow - _last.AtUtc).TotalSeconds < 20)
                    return Task.FromResult(_last.JobId);

                _decidingPlace = placeId;
                return _deciding = RunPickerAsync(placeId);
            }
        }

        private static async Task<string?> RunPickerAsync(long placeId)
        {
            string? chosen = null;

            try
            {
                using var deadline = new CancellationTokenSource(MaxHold);

                Task<List<MatchmakerCandidate>> search = Matchmaker.ListCandidatesAsync(placeId, 24, deadline.Token);

                var application = System.Windows.Application.Current;
                if (application is null)
                    return null;

                var picked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

                await application.Dispatcher.InvokeAsync(() =>
                {
                    var window = new UI.Elements.Dialogs.ServerPickerWindow(placeId, search, MaxHold);
                    window.Closed += (_, _) => picked.TrySetResult(window.ChosenJobId);
                    window.Show();
                });

                Task finished = await Task.WhenAny(picked.Task, Task.Delay(MaxHold + TimeSpan.FromSeconds(2)));
                chosen = finished == picked.Task ? picked.Task.Result : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Picker window failed: {ex.Message}");
            }

            lock (Sync)
                _last = (placeId, chosen, DateTime.UtcNow);

            return chosen;
        }
    }
}
