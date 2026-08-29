using System.Net.WebSockets;

namespace PhasmaStrap.Integrations.GameChat
{
    public class GameChatMessage
    {
        public long Id;
        public long SenderId;
        public string Type = "";
        public string Sender = "";
        public string Target = "";
        public string Text = "";
        public bool IsTo;
        public JsonElement Scores;
        public bool HasScores;
    }

    public class GameChatRejection
    {
        public string Reason = "";
        public string Target = "";
    }

    public enum GameChatBugResult
    {
        Ok,
        RateLimited,
        NotConnected,
        Failed
    }

    /// <summary>
    /// Talks to an optional, user-configured chat relay server (Settings > Game Chat > Server URL).
    /// PhasmaStrap does not operate a hosted chat backend of its own (unlike Voidstrap's original
    /// GameChatClient, which always relayed through Voidstrap's own website API) - without a server
    /// configured this class simply reports "not connected" and never makes network calls.
    /// </summary>
    public class GameChatClient : IDisposable
    {
        private const string Tag = "GameChatClient";
        private const int MaxHttpResponseBytes = 1024 * 1024;
        private const int MaxIncomingMessageBytes = 1024 * 1024;
        private const int MinPollMs = 5000;
        private const int MaxPollMs = 60000;
        private const int HiddenPollMs = 60000;

        private CancellationTokenSource _cts = new();
        private string? _token;
        private long _since;
        private readonly HashSet<long> _seen = new();
        private readonly Queue<long> _seenOrder = new();
        private readonly object _sync = new();
        private readonly object _resetLock = new();
        private readonly SemaphoreSlim _socketSendLock = new(1, 1);
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private ClientWebSocket? _socket;
        private int _connectionGeneration;
        private bool _disposed;

        public string ChannelId { get; set; } = "global";
        public string Name { get; private set; } = "";
        public long OwnRobloxId { get; set; }
        public bool Connected => _token != null;
        public bool SlowPoll { get; set; }

        public event EventHandler<string>? OnSystemMessage;
        public event EventHandler<GameChatMessage>? OnMessage;
        public event EventHandler<GameChatRejection>? OnRejected;

        private static string? BaseUrl
        {
            get
            {
                string configured = App.Settings.Prop.GameChatServerUrl?.Trim() ?? "";
                return string.IsNullOrEmpty(configured) ? null : configured.TrimEnd('/') + "/api/chat";
            }
        }

        private void EmitSystem(string text) => OnSystemMessage?.Invoke(this, text);

        private static StringContent JsonBody(object payload) =>
            new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        private async Task ConnectAsync(CancellationToken ct, bool announce = true)
        {
            if (_disposed)
                return;

            string? baseUrl = BaseUrl;
            if (baseUrl == null)
            {
                EmitSystem(GameChatStrings.NoServerConfigured);
                return;
            }

            int generation = Volatile.Read(ref _connectionGeneration);

            try
            {
                _token = null;
                if (announce)
                    EmitSystem(string.Format(GameChatStrings.ConnectingToServer, ChannelId));

                using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
                request.Content = JsonBody(new { action = "join", channelId = ChannelId, name = OwnRobloxId > 0 ? OwnRobloxId.ToString() : "" });
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new Exception("HTTP " + (int)response.StatusCode);

                using var doc = JsonDocument.Parse(await Http.ReadStringBoundedAsync(response.Content, MaxHttpResponseBytes, ct).ConfigureAwait(false));
                var root = doc.RootElement;
                string? token = root.TryGetProperty("token", out var t) ? t.GetString() : null;
                if (ct.IsCancellationRequested || generation != Volatile.Read(ref _connectionGeneration) || string.IsNullOrEmpty(token))
                    return;

                _token = token;
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                _since = root.TryGetProperty("serial", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;

                lock (_sync)
                {
                    _seen.Clear();
                    _seenOrder.Clear();
                }

                if (announce)
                    EmitSystem(GameChatStrings.ConnectedSuccessfully);

                _ = ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    EmitSystem(string.Format(GameChatStrings.ConnectionFailed, ex.Message));
            }
        }

        public Task RestartAsync(bool announce = true)
        {
            if (_disposed)
                return Task.CompletedTask;
            return Task.Run(() => RestartCoreAsync(announce));
        }

        private async Task RestartCoreAsync(bool announce)
        {
            if (_disposed)
                return;
            (int generation, CancellationToken token) = ResetConnection();
            try
            {
                await _connectionLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_disposed || generation != Volatile.Read(ref _connectionGeneration))
                        return;
                    await ConnectAsync(token, announce).ConfigureAwait(false);
                }
                finally
                {
                    _connectionLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Stop()
        {
            if (_disposed)
                return;
            ResetConnection();
        }

        private (int Generation, CancellationToken Token) ResetConnection()
        {
            lock (_resetLock)
            {
                if (_disposed)
                    return (Volatile.Read(ref _connectionGeneration), new CancellationToken(true));

                CancellationTokenSource old;
                CancellationToken token;
                int generation;
                lock (_sync)
                {
                    generation = Interlocked.Increment(ref _connectionGeneration);
                    old = _cts;
                    _cts = new CancellationTokenSource();
                    token = _cts.Token;
                    _socket = null;
                }
                try
                {
                    old.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                old.Dispose();
                _token = null;
                return (generation, token);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            int retryDelay = MinPollMs;
            try
            {
                while (!ct.IsCancellationRequested && _token != null)
                {
                    ClientWebSocket? connectedSocket = null;
                    try
                    {
                        using var socket = new ClientWebSocket();
                        socket.Options.SetRequestHeader("x-chat-token", _token);
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        string socketUrl = (BaseUrl ?? throw new InvalidOperationException())
                            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase) + "/socket";
                        await socket.ConnectAsync(new Uri(socketUrl), ct);
                        connectedSocket = socket;
                        lock (_sync)
                            _socket = socket;
                        retryDelay = MinPollMs;
                        await ReadSocketAsync(socket, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(Tag, "Chat connection error: " + ex.Message);
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            if (ReferenceEquals(_socket, connectedSocket))
                                _socket = null;
                        }
                    }

                    if (ct.IsCancellationRequested || _token == null)
                        return;

                    await PollOnceAsync(ct);
                    await Task.Delay(SlowPoll ? HiddenPollMs : retryDelay, ct);
                    retryDelay = Math.Min(MaxPollMs, retryDelay * 2);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                EmitSystem(string.Format(GameChatStrings.ReceiveError, ex.Message));
            }
        }

        private async Task ReadSocketAsync(ClientWebSocket socket, CancellationToken ct)
        {
            byte[] buffer = new byte[8192];
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    if (result.MessageType != WebSocketMessageType.Text)
                        break;
                    if (message.Length + result.Count > MaxIncomingMessageBytes)
                        throw new InvalidDataException("Chat message exceeded the size limit");
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text || message.Length == 0)
                    continue;

                message.Position = 0;
                using var doc = await JsonDocument.ParseAsync(message, cancellationToken: ct);
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var eventName) && eventName.GetString() == "rejected")
                {
                    OnRejected?.Invoke(this, new GameChatRejection
                    {
                        Reason = ReadString(root, "reason", 64),
                        Target = ReadString(root, "target", 64),
                    });
                    continue;
                }
                DispatchMessage(root);
            }
        }

        private async Task<bool> SendSocketAsync(object payload, CancellationToken ct)
        {
            ClientWebSocket? socket;
            lock (_sync)
                socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
                return false;

            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await _socketSendLock.WaitAsync(ct);
            try
            {
                if (socket.State != WebSocketState.Open)
                    return false;
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                return true;
            }
            catch (WebSocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
            finally
            {
                _socketSendLock.Release();
            }
        }

        private async Task PollOnceAsync(CancellationToken ct)
        {
            string? token = _token;
            string? baseUrl = BaseUrl;
            if (token == null || baseUrl == null)
                return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/poll?since=" + _since);
                request.Headers.Add("x-chat-token", token);
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _ = RestartAsync(false);
                    return;
                }
                if (!response.IsSuccessStatusCode)
                    return;

                using var doc = JsonDocument.Parse(await Http.ReadStringBoundedAsync(response.Content, MaxHttpResponseBytes, ct).ConfigureAwait(false));
                var root = doc.RootElement;
                if (root.TryGetProperty("serial", out var serial) && serial.ValueKind == JsonValueKind.Number)
                    _since = Math.Max(_since, serial.GetInt64());
                if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                    return;
                foreach (var item in messages.EnumerateArray())
                    DispatchMessage(item);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(Tag, "Chat recovery error: " + ex.Message);
            }
        }

        private void DispatchMessage(JsonElement m)
        {
            long id = m.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
            if (id <= 0)
                return;
            _since = Math.Max(_since, id);
            if (!MarkSeen(id))
                return;

            var msg = new GameChatMessage
            {
                Id = id,
                SenderId = m.TryGetProperty("senderId", out var sid) && sid.ValueKind == JsonValueKind.Number ? sid.GetInt64() : 0,
                Type = ReadString(m, "type", 24),
                Sender = ReadString(m, "sender", 64),
                Target = ReadString(m, "target", 64),
                Text = ReadString(m, "text", 4000),
                IsTo = m.TryGetProperty("isTo", out var it) && it.ValueKind == JsonValueKind.True,
            };
            if (msg.Type == "whisper" && msg.Sender == Name)
                msg.IsTo = true;
            if (m.TryGetProperty("attributeScores", out var sc) && sc.ValueKind == JsonValueKind.Object)
            {
                msg.Scores = sc.Clone();
                msg.HasScores = true;
            }
            OnMessage?.Invoke(this, msg);
        }

        private static string ReadString(JsonElement element, string name, int maxLength)
        {
            string value = element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? ""
                : "";
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private bool MarkSeen(long id)
        {
            lock (_sync)
            {
                if (!_seen.Add(id))
                    return false;
                _seenOrder.Enqueue(id);
                while (_seenOrder.Count > 2000)
                    _seen.Remove(_seenOrder.Dequeue());
                return true;
            }
        }

        private async Task<bool> HandleSendResponse(HttpResponseMessage response, string localEchoType, string target, string text, CancellationToken token)
        {
            string body = await Http.ReadStringBoundedAsync(response.Content, MaxHttpResponseBytes, token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var status) && status.GetString() == "rejected")
            {
                OnRejected?.Invoke(this, new GameChatRejection
                {
                    Reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                    Target = root.TryGetProperty("target", out var tg) ? tg.GetString() ?? "" : "",
                });
                return false;
            }

            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                EmitSystem(GameChatStrings.NotConnected);
                return false;
            }

            long id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
            string echoText = root.TryGetProperty("text", out var txEl) && txEl.ValueKind == JsonValueKind.String ? (txEl.GetString() ?? text) : text;
            var msg = new GameChatMessage
            {
                Id = id,
                SenderId = OwnRobloxId,
                Type = localEchoType,
                Sender = Name,
                Target = target,
                Text = echoText,
                IsTo = localEchoType == "whisper",
            };
            if (root.TryGetProperty("attributeScores", out var sc) && sc.ValueKind == JsonValueKind.Object)
            {
                msg.Scores = sc.Clone();
                msg.HasScores = true;
            }
            if (id > 0)
                MarkSeen(id);
            _since = Math.Max(_since, id);
            OnMessage?.Invoke(this, msg);
            return true;
        }

        public async Task SendMessageAsync(string text)
        {
            if (_token == null || BaseUrl == null)
            {
                EmitSystem(_token == null && BaseUrl == null ? GameChatStrings.NoServerConfigured : GameChatStrings.NotConnected);
                return;
            }

            try
            {
                if (await SendSocketAsync(new { action = "message", text }, _cts.Token))
                    return;
                using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
                request.Headers.Add("x-chat-token", _token);
                request.Content = JsonBody(new { action = "message", text });
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    EmitSystem(GameChatStrings.NotConnected);
                    return;
                }
                await HandleSendResponse(response, "message", "", text, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                EmitSystem(GameChatStrings.SendTimedOut);
            }
            catch (Exception ex)
            {
                EmitSystem(string.Format(GameChatStrings.SendError, ex.Message));
            }
        }

        public async Task SendWhisperAsync(string target, string text)
        {
            if (_token == null || BaseUrl == null)
            {
                EmitSystem(_token == null && BaseUrl == null ? GameChatStrings.NoServerConfigured : GameChatStrings.NotConnected);
                return;
            }

            try
            {
                if (await SendSocketAsync(new { action = "whisper", target, text }, _cts.Token))
                    return;
                using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
                request.Headers.Add("x-chat-token", _token);
                request.Content = JsonBody(new { action = "whisper", target, text });
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    EmitSystem(GameChatStrings.NotConnected);
                    return;
                }
                await HandleSendResponse(response, "whisper", target, text, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                EmitSystem(GameChatStrings.SendTimedOut);
            }
            catch (Exception ex)
            {
                EmitSystem(string.Format(GameChatStrings.SendError, ex.Message));
            }
        }

        public async Task SendEchoAsync(string text)
        {
            if (_token == null || BaseUrl == null)
            {
                EmitSystem(_token == null && BaseUrl == null ? GameChatStrings.NoServerConfigured : GameChatStrings.NotConnected);
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
                request.Headers.Add("x-chat-token", _token);
                request.Content = JsonBody(new { action = "echo", text });
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    EmitSystem(GameChatStrings.NotConnected);
                    return;
                }
                string body = await Http.ReadStringBoundedAsync(response.Content, MaxHttpResponseBytes, _cts.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var status) && status.GetString() == "rejected")
                {
                    string reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                    EmitSystem(reason switch
                    {
                        "moderation" => GameChatStrings.MessageRejectedModeration,
                        "queue_full" => GameChatStrings.MessageRejectedQueueFull,
                        "api_error" => GameChatStrings.MessageRejectedApiError,
                        _ => GameChatStrings.MessageRejectedUnknown,
                    });
                    return;
                }

                if (root.TryGetProperty("text", out var echoed))
                    EmitSystem(string.Format(GameChatStrings.EchoResponse, echoed.GetString() ?? ""));
            }
            catch (TaskCanceledException)
            {
                EmitSystem(GameChatStrings.RequestTimedOut);
            }
            catch (Exception ex)
            {
                EmitSystem(string.Format(GameChatStrings.ConnectionError, ex.Message));
            }
        }

        public async Task<GameChatBugResult> SendBugAsync(string text)
        {
            if (_token == null || BaseUrl == null)
                return GameChatBugResult.NotConnected;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
                request.Headers.Add("x-chat-token", _token);
                request.Content = JsonBody(new { action = "bug", text });
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return GameChatBugResult.RateLimited;
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return GameChatBugResult.NotConnected;
                if (!response.IsSuccessStatusCode)
                    return GameChatBugResult.Failed;
                return GameChatBugResult.Ok;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(Tag, "Bug report failed: " + ex.Message);
                return GameChatBugResult.Failed;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            lock (_resetLock)
            {
                CancellationTokenSource cts;
                lock (_sync)
                {
                    cts = _cts;
                    _token = null;
                    _socket = null;
                    _seen.Clear();
                    _seenOrder.Clear();
                }
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                cts.Dispose();
            }
            OnSystemMessage = null;
            OnMessage = null;
            OnRejected = null;
            GC.SuppressFinalize(this);
        }
    }
}
