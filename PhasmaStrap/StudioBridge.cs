using PhasmaStrap.Models;

namespace PhasmaStrap
{
    // local HTTP bridge the Studio companion plugin (PhasmaStrapStudio.lua) talks to,
    // so the plugin can report what you're working on in Studio and receive the app's
    // branding palette back. Ported from Voidstrap, without its TCP-listener fallback path.
    public static class StudioBridge
    {
        private const string LOG_IDENT = "StudioBridge";

        public const int Port = 40404;

        private static readonly object Sync = new();

        private static HttpListener? _listener;

        private static CancellationTokenSource? _cts;

        private static StudioState? _latest;

        public static bool IsRunning
        {
            get
            {
                lock (Sync)
                    return _listener is not null && _listener.IsListening;
            }
        }

        public static StudioState? GetFreshState(TimeSpan maxAge)
        {
            lock (Sync)
            {
                if (_latest is null)
                    return null;

                if (DateTime.UtcNow - _latest.ReceivedUtc > maxAge)
                    return null;

                return _latest;
            }
        }

        public static void Start()
        {
            lock (Sync)
            {
                if (_listener is not null)
                    return;

                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                    listener.Start();
                    _listener = listener;
                    _cts = new CancellationTokenSource();
                    _ = Task.Run(() => LoopAsync(listener, _cts.Token));
                    App.Logger.WriteLine(LOG_IDENT, $"Listening on 127.0.0.1:{Port}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to start: {ex.Message}");
                    _listener = null;
                }
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                try
                {
                    _cts?.Cancel();
                    _listener?.Stop();
                    _listener?.Close();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to stop cleanly: {ex.Message}");
                }
                finally
                {
                    _listener = null;
                    _cts = null;
                }
            }
        }

        private static async Task LoopAsync(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(context), token);
            }
        }

        private static async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/rpc")
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                string body = await reader.ReadToEndAsync();

                if (body.Length > 65536)
                {
                    context.Response.StatusCode = 413;
                    context.Response.Close();
                    return;
                }

                try
                {
                    var payload = JsonSerializer.Deserialize<JsonElement>(body);

                    var state = new StudioState
                    {
                        Sharing = payload.TryGetProperty("sharing", out var sharing) && sharing.GetBoolean(),
                        Place = GetString(payload, "place"),
                        PlaceId = GetInt64(payload, "placeId"),
                        UniverseId = GetInt64(payload, "universeId"),
                        Creator = GetString(payload, "creator"),
                        Script = GetString(payload, "script"),
                        ScriptLines = (int)GetInt64(payload, "scriptLines"),
                        Mode = GetString(payload, "mode"),
                        Selection = (int)GetInt64(payload, "selection"),
                        SelectionClass = GetString(payload, "selectionClass"),
                        Custom = GetString(payload, "custom"),
                        ReceivedUtc = DateTime.UtcNow
                    };

                    lock (Sync)
                        _latest = state;
                }
                catch (JsonException)
                {
                    // malformed payload from the plugin - ignore this tick, keep the server alive
                }

                using JsonDocument paletteDoc = JsonDocument.Parse(Integrations.StudioTheme.GetPaletteJson());

                string response = JsonSerializer.Serialize(new
                {
                    version = App.Version,
                    palette = paletteDoc.RootElement
                });

                byte[] bytes = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Request handling failed: {ex.Message}");

                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // response may already be closed/disposed, nothing more to do
                }
            }
        }

        private static string GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        private static long GetInt64(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.TryGetInt64(out long result)
                ? result
                : 0;
    }
}
