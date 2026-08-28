using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace PhasmaStrap.Networking
{
    public sealed record ProxiedRequest(string Host, string Method, string Path, Dictionary<string, string> Headers, byte[] Body);

    public sealed record ProxiedResponse(int StatusCode, string StatusText, Dictionary<string, string> Headers, byte[] Body);

    // a narrowly-scoped local TLS-terminating proxy: it only ever accepts connections for
    // the exact hostnames explicitly registered via InterceptedHosts, and it only exists to
    // let the spoofing policies below rewrite specific request/response bodies before
    // relaying to the real Roblox servers. It is not a general-purpose traffic interceptor -
    // any hostname outside the allowlist is refused.
    public static class AssetProxyServer
    {
        private const string LOG_IDENT = "AssetProxyServer";

        // must be 443: the hosts-file block only redirects the IP for the intercepted
        // hostnames, not the port, so this has to be where a real HTTPS client actually
        // connects. Windows (unlike Linux) doesn't require elevation to bind low ports, and
        // this only ever binds to loopback, so it can't be reached from outside this machine.
        public const int Port = 443;

        // hostname -> optional request transform, optional response transform
        public static readonly Dictionary<string, (Func<ProxiedRequest, byte[]?>? RequestTransform, Func<ProxiedRequest, ProxiedResponse, byte[]?>? ResponseTransform)> InterceptedHosts
            = new(StringComparer.OrdinalIgnoreCase);

        private static TcpListener? _listener;

        private static CancellationTokenSource? _cts;

        private static readonly object Sync = new();

        public static bool IsRunning
        {
            get
            {
                lock (Sync)
                    return _listener is not null;
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
                    var listener = new TcpListener(System.Net.IPAddress.Loopback, Port);
                    listener.Start();
                    _listener = listener;
                    _cts = new CancellationTokenSource();
                    _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
                    App.Logger.WriteLine(LOG_IDENT, $"Listening on 127.0.0.1:{Port}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);
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

        private static async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(token);
                }
                catch (Exception)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using TcpClient _ = client;
            string? sniHost = null;

            try
            {
                using var networkStream = client.GetStream();
                using var sslStream = new SslStream(networkStream, false);

                var serverOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (sender, hostName) =>
                    {
                        sniHost = hostName;
                        return AssetProxyCA.GetLeafCertificate(hostName ?? "unknown");
                    },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                };

                await sslStream.AuthenticateAsServerAsync(serverOptions, token);

                if (sniHost is null || !InterceptedHosts.ContainsKey(sniHost))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Refusing connection for non-allowlisted host {sniHost}");
                    return;
                }

                ProxiedRequest? request = await ReadRequestAsync(new RawHttpReader(sslStream), sniHost, token);
                if (request is null)
                    return;

                var (requestTransform, responseTransform) = InterceptedHosts[sniHost];

                if (requestTransform is not null)
                {
                    byte[]? transformed = requestTransform(request);
                    if (transformed is not null)
                        request = request with { Body = transformed };
                }

                ProxiedResponse? response = await ForwardToUpstreamAsync(request, token);
                if (response is null)
                {
                    await WriteSimpleResponseAsync(sslStream, 502, "Bad Gateway", token);
                    return;
                }

                if (responseTransform is not null)
                {
                    byte[]? transformed = responseTransform(request, response);
                    if (transformed is not null)
                        response = response with { Body = transformed };
                }

                await WriteResponseAsync(sslStream, response, token);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Connection for {sniHost ?? "unknown host"} failed: {ex.Message}");
            }
        }

        // minimal buffered reader that supports both line-based header reads and exact-length
        // body reads against the SAME underlying buffer, so bytes read ahead while looking
        // for a line ending are never lost when switching to a raw body read afterwards -
        // unlike System.IO.StreamReader, whose internal buffer isn't accessible for this
        private sealed class RawHttpReader
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[8192];
            private int _bufferLen;
            private int _bufferPos;

            public RawHttpReader(Stream stream) => _stream = stream;

            public async Task<string?> ReadLineAsync(CancellationToken token)
            {
                List<byte> line = new();

                while (true)
                {
                    if (_bufferPos >= _bufferLen)
                    {
                        _bufferLen = await _stream.ReadAsync(_buffer.AsMemory(0, _buffer.Length), token);
                        _bufferPos = 0;

                        if (_bufferLen == 0)
                            return line.Count > 0 ? Encoding.ASCII.GetString(line.ToArray()) : null;
                    }

                    byte b = _buffer[_bufferPos++];

                    if (b == (byte)'\n')
                    {
                        if (line.Count > 0 && line[^1] == (byte)'\r')
                            line.RemoveAt(line.Count - 1);

                        return Encoding.ASCII.GetString(line.ToArray());
                    }

                    line.Add(b);
                }
            }

            public async Task<byte[]> ReadExactAsync(int length, CancellationToken token)
            {
                var result = new byte[length];
                int offset = 0;

                int available = _bufferLen - _bufferPos;
                if (available > 0)
                {
                    int take = Math.Min(available, length);
                    Array.Copy(_buffer, _bufferPos, result, 0, take);
                    _bufferPos += take;
                    offset += take;
                }

                while (offset < length)
                {
                    int read = await _stream.ReadAsync(result.AsMemory(offset, length - offset), token);
                    if (read == 0)
                        break;

                    offset += read;
                }

                return result;
            }
        }

        private static async Task<ProxiedRequest?> ReadRequestAsync(RawHttpReader reader, string sniHost, CancellationToken token)
        {
            string? requestLine = await reader.ReadLineAsync(token);
            if (string.IsNullOrEmpty(requestLine))
                return null;

            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2)
                return null;

            string method = parts[0];
            string path = parts[1];

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            byte[] body = Array.Empty<byte>();
            if (headers.TryGetValue("Content-Length", out string? lengthHeader) && int.TryParse(lengthHeader, out int length) && length > 0)
                body = await reader.ReadExactAsync(length, token);

            string host = headers.TryGetValue("Host", out string? hostHeader) ? hostHeader : sniHost;
            return new ProxiedRequest(host, method, path, headers, body);
        }

        private static async Task<ProxiedResponse?> ForwardToUpstreamAsync(ProxiedRequest request, CancellationToken token)
        {
            string? ip = await DohResolver.ResolveAsync(request.Host, token);
            if (ip is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not resolve real address for {request.Host}");
                return null;
            }

            using var upstream = new TcpClient();
            await upstream.ConnectAsync(ip, 443, token);

            using var upstreamSsl = new SslStream(upstream.GetStream(), false, (sender, cert, chain, errors) => errors == SslPolicyErrors.None);
            await upstreamSsl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = request.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, token);

            var requestBuilder = new StringBuilder();
            requestBuilder.Append($"{request.Method} {request.Path} HTTP/1.1\r\n");
            foreach (var header in request.Headers)
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                requestBuilder.Append($"{header.Key}: {header.Value}\r\n");
            }
            requestBuilder.Append($"Content-Length: {request.Body.Length}\r\n");
            requestBuilder.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(requestBuilder.ToString());
            await upstreamSsl.WriteAsync(headerBytes, token);
            if (request.Body.Length > 0)
                await upstreamSsl.WriteAsync(request.Body, token);

            var reader = new RawHttpReader(upstreamSsl);

            string? statusLine = await reader.ReadLineAsync(token);
            if (string.IsNullOrEmpty(statusLine))
                return null;

            string[] statusParts = statusLine.Split(' ', 3);
            int statusCode = statusParts.Length > 1 && int.TryParse(statusParts[1], out int code) ? code : 502;
            string statusText = statusParts.Length > 2 ? statusParts[2] : "";

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            byte[] body = Array.Empty<byte>();
            if (headers.TryGetValue("Content-Length", out string? lengthHeader) && int.TryParse(lengthHeader, out int length) && length > 0)
                body = await reader.ReadExactAsync(length, token);

            return new ProxiedResponse(statusCode, statusText, headers, body);
        }

        private static async Task WriteResponseAsync(Stream stream, ProxiedResponse response, CancellationToken token)
        {
            var builder = new StringBuilder();
            builder.Append($"HTTP/1.1 {response.StatusCode} {response.StatusText}\r\n");
            foreach (var header in response.Headers)
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;

                builder.Append($"{header.Key}: {header.Value}\r\n");
            }
            builder.Append($"Content-Length: {response.Body.Length}\r\n");
            builder.Append("Connection: close\r\n\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), token);
            if (response.Body.Length > 0)
                await stream.WriteAsync(response.Body, token);
        }

        private static async Task WriteSimpleResponseAsync(Stream stream, int statusCode, string statusText, CancellationToken token)
        {
            byte[] bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} {statusText}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(bytes, token);
        }
    }
}
