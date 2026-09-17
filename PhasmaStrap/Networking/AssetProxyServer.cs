using System.IO.Compression;
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

        // hostname -> optional request transform, optional response transform, optional cache
        // short-circuit (checked before ever contacting the real server - if it returns a
        // response, that's used directly and ForwardToUpstreamAsync is skipped entirely; this is
        // what actually makes AssetWarp's "Preloading" serve cached assets without a network
        // round-trip, rather than just rewriting a response that was already fetched)
        public static readonly Dictionary<string, (Func<ProxiedRequest, byte[]?>? RequestTransform, Func<ProxiedRequest, ProxiedResponse, byte[]?>? ResponseTransform, Func<ProxiedRequest, ProxiedResponse?>? TryServeFromCache)> InterceptedHosts
            = new(StringComparer.OrdinalIgnoreCase);

        // headers that describe THIS hop's connection rather than the request itself; forwarding
        // them verbatim would either be meaningless upstream or (Accept-Encoding, Expect)
        // actively break the body handling below
        private static readonly string[] HopByHopRequestHeaders =
        {
            "Connection", "Proxy-Connection", "Keep-Alive", "Transfer-Encoding", "Content-Length", "Expect", "Accept-Encoding", "Upgrade",
        };

        private static TcpListener? _listener;

        private static CancellationTokenSource? _cts;

        private static readonly object Sync = new();

        // another PhasmaStrap process (the settings window vs. the game-session watcher) may
        // already be hosting the proxy on 443 - that's normal, not an error, so only the first
        // "port busy" is logged and the keeper loop keeps retrying quietly until it frees up
        private static bool _loggedPortBusy;

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
                    _loggedPortBusy = false;
                    App.Logger.WriteLine(LOG_IDENT, $"Listening on 127.0.0.1:{Port}");
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    if (!_loggedPortBusy)
                    {
                        _loggedPortBusy = true;
                        App.Logger.WriteLine(LOG_IDENT, $"Port {Port} is already in use (another PhasmaStrap process is probably hosting the proxy) - will keep retrying quietly");
                    }

                    _listener = null;
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
                client.NoDelay = true;

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

                ProxiedRequest? request = await ReadRequestAsync(new RawHttpReader(sslStream), sslStream, sniHost, token);
                if (request is null)
                    return;

                var (requestTransform, responseTransform, tryServeFromCache) = InterceptedHosts[sniHost];

                if (requestTransform is not null)
                {
                    byte[]? transformed = requestTransform(request);
                    if (transformed is not null)
                        request = request with { Body = transformed };
                }

                ProxiedResponse? response = tryServeFromCache?.Invoke(request);
                bool servedFromCache = response is not null;

                if (response is null)
                {
                    response = await ForwardToUpstreamAsync(request, token);
                    if (response is null)
                    {
                        await WriteSimpleResponseAsync(sslStream, 502, "Bad Gateway", token);
                        return;
                    }
                }

                if (!servedFromCache && responseTransform is not null)
                {
                    byte[]? transformed = responseTransform(request, response);
                    if (transformed is not null)
                        response = response with { Body = transformed };
                }

                ProxyTrafficLog.Record(request.Host, request.Method, request.Path, response.StatusCode, servedFromCache, response.Body.Length);

                await WriteResponseAsync(sslStream, response, request.Method, token);
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
            private readonly byte[] _buffer = new byte[16384];
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

                return offset == length ? result : result[..offset];
            }

            public async Task<byte[]> ReadToEndAsync(CancellationToken token)
            {
                using var output = new MemoryStream();

                int available = _bufferLen - _bufferPos;
                if (available > 0)
                {
                    output.Write(_buffer, _bufferPos, available);
                    _bufferPos = _bufferLen;
                }

                var chunk = new byte[16384];
                while (true)
                {
                    int read = await _stream.ReadAsync(chunk.AsMemory(0, chunk.Length), token);
                    if (read == 0)
                        break;

                    output.Write(chunk, 0, read);
                }

                return output.ToArray();
            }

            // "Transfer-Encoding: chunked" - a sequence of <hex length>\r\n<bytes>\r\n, terminated
            // by a zero-length chunk and optional trailer headers. Roblox's APIs frequently reply
            // this way, and libcurl uses it for streamed POST bodies, so a proxy that only
            // understands Content-Length sees those as empty and never rewrites them
            public async Task<byte[]> ReadChunkedAsync(CancellationToken token)
            {
                using var output = new MemoryStream();

                while (true)
                {
                    string? sizeLine = await ReadLineAsync(token);
                    if (sizeLine is null)
                        break;

                    sizeLine = sizeLine.Trim();
                    if (sizeLine.Length == 0)
                        continue;

                    int semicolon = sizeLine.IndexOf(';');
                    if (semicolon >= 0)
                        sizeLine = sizeLine[..semicolon].Trim();

                    if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.HexNumber, null, out int size) || size < 0)
                        throw new IOException($"Malformed chunk size '{sizeLine}'");

                    if (size == 0)
                    {
                        // trailers, up to the blank line
                        while (!string.IsNullOrEmpty(await ReadLineAsync(token))) { }
                        break;
                    }

                    byte[] chunk = await ReadExactAsync(size, token);
                    output.Write(chunk, 0, chunk.Length);

                    // CRLF after the chunk data
                    await ReadLineAsync(token);
                }

                return output.ToArray();
            }
        }

        private static async Task<ProxiedRequest?> ReadRequestAsync(RawHttpReader reader, Stream clientStream, string sniHost, CancellationToken token)
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

            // libcurl sends larger POST bodies with "Expect: 100-continue" and then WAITS for the
            // interim response before transmitting the body; without answering it, the request
            // hangs until curl's timeout and the body never arrives
            if (headers.TryGetValue("Expect", out string? expect) && expect.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
            {
                await clientStream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), token);
                await clientStream.FlushAsync(token);
            }

            byte[] body = await ReadBodyAsync(reader, headers, token);

            string host = headers.TryGetValue("Host", out string? hostHeader) ? hostHeader : sniHost;
            return new ProxiedRequest(host, method, path, headers, body);
        }

        private static async Task<byte[]> ReadBodyAsync(RawHttpReader reader, Dictionary<string, string> headers, CancellationToken token)
        {
            if (headers.TryGetValue("Transfer-Encoding", out string? transfer) && transfer.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                return await reader.ReadChunkedAsync(token);

            if (headers.TryGetValue("Content-Length", out string? lengthHeader) && int.TryParse(lengthHeader, out int length))
                return length > 0 ? await reader.ReadExactAsync(length, token) : Array.Empty<byte>();

            return Array.Empty<byte>();
        }

        private static async Task<ProxiedResponse?> ForwardToUpstreamAsync(ProxiedRequest request, CancellationToken token)
        {
            string? ip = await DohResolver.ResolveAsync(request.Host, token);
            if (ip is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not resolve real address for {request.Host}");
                return null;
            }

            using var upstream = new TcpClient { NoDelay = true };
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
                if (HopByHopRequestHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                    continue;

                requestBuilder.Append($"{header.Key}: {header.Value}\r\n");
            }

            // ask for an uncompressed body so the JSON transforms can actually read it; if the
            // server compresses anyway, DecodeBody below handles the common encodings
            requestBuilder.Append("Accept-Encoding: identity\r\n");
            requestBuilder.Append($"Content-Length: {request.Body.Length}\r\n");
            requestBuilder.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(requestBuilder.ToString());
            await upstreamSsl.WriteAsync(headerBytes, token);
            if (request.Body.Length > 0)
                await upstreamSsl.WriteAsync(request.Body, token);
            await upstreamSsl.FlushAsync(token);

            var reader = new RawHttpReader(upstreamSsl);

            int statusCode;
            string statusText;
            Dictionary<string, string> headers;

            // skip any 1xx interim responses (100 Continue, 103 Early Hints) to reach the real one
            while (true)
            {
                string? statusLine = await reader.ReadLineAsync(token);
                if (string.IsNullOrEmpty(statusLine))
                    return null;

                string[] statusParts = statusLine.Split(' ', 3);
                statusCode = statusParts.Length > 1 && int.TryParse(statusParts[1], out int code) ? code : 502;
                statusText = statusParts.Length > 2 ? statusParts[2] : "";

                headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                        continue;

                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }

                if (statusCode >= 200)
                    break;
            }

            byte[] body;
            bool bodyless = request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || statusCode == 204 || statusCode == 304;

            if (bodyless)
                body = Array.Empty<byte>();
            else if (headers.TryGetValue("Transfer-Encoding", out string? transfer) && transfer.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                body = await reader.ReadChunkedAsync(token);
            else if (headers.TryGetValue("Content-Length", out string? lengthHeader) && int.TryParse(lengthHeader, out int length))
                body = length > 0 ? await reader.ReadExactAsync(length, token) : Array.Empty<byte>();
            else
                body = await reader.ReadToEndAsync(token); // "Connection: close" delimits the body

            body = DecodeBody(headers, body);

            return new ProxiedResponse(statusCode, statusText, headers, body);
        }

        // inflates gzip/deflate/br bodies in place so the transforms see plain JSON, and drops
        // the Content-Encoding header so the client isn't told the (now plain) body is compressed
        private static byte[] DecodeBody(Dictionary<string, string> headers, byte[] body)
        {
            if (body.Length == 0 || !headers.TryGetValue("Content-Encoding", out string? encoding))
                return body;

            encoding = encoding.Trim().ToLowerInvariant();

            try
            {
                using var input = new MemoryStream(body);
                using var output = new MemoryStream();

                Stream? decoder = encoding switch
                {
                    "gzip" or "x-gzip" => new GZipStream(input, CompressionMode.Decompress),
                    "deflate" => new DeflateStream(input, CompressionMode.Decompress),
                    "br" => new BrotliStream(input, CompressionMode.Decompress),
                    "identity" or "" => null,
                    _ => null,
                };

                if (decoder is null)
                {
                    if (encoding is "identity" or "")
                        headers.Remove("Content-Encoding");
                    return body;
                }

                using (decoder)
                    decoder.CopyTo(output);

                headers.Remove("Content-Encoding");
                return output.ToArray();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not decode '{encoding}' body ({body.Length} bytes), passing through: {ex.Message}");
                return body;
            }
        }

        private static async Task WriteResponseAsync(Stream stream, ProxiedResponse response, string requestMethod, CancellationToken token)
        {
            bool bodyless = requestMethod.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || response.StatusCode == 204 || response.StatusCode == 304;

            var builder = new StringBuilder();
            builder.Append($"HTTP/1.1 {response.StatusCode} {response.StatusText}\r\n");
            foreach (var header in response.Headers)
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase))
                    continue;

                builder.Append($"{header.Key}: {header.Value}\r\n");
            }

            if (!bodyless || response.StatusCode == 304)
                builder.Append($"Content-Length: {response.Body.Length}\r\n");

            builder.Append("Connection: close\r\n\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), token);
            if (!bodyless && response.Body.Length > 0)
                await stream.WriteAsync(response.Body, token);
            await stream.FlushAsync(token);
        }

        private static async Task WriteSimpleResponseAsync(Stream stream, int statusCode, string statusText, CancellationToken token)
        {
            byte[] bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} {statusText}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(bytes, token);
            await stream.FlushAsync(token);
        }
    }
}
