using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PhasmaStrap.Utility
{
    public static class DiscordRpcAuth
    {
        private const string LOG_IDENT = "DiscordRpcAuth";
        private const int Handshake = 0;
        private const int Frame = 1;

        private static async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken cancel)
        {
            for (int index = 0; index < 10; index++)
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut, PipeOptions.Asynchronous);

                try
                {
                    await pipe.ConnectAsync(400, cancel);
                    return pipe;
                }
                catch (OperationCanceledException)
                {
                    pipe.Dispose();
                    throw;
                }
                catch (Exception)
                {
                    pipe.Dispose();
                }
            }

            return null;
        }

        private static async Task WriteAsync(NamedPipeClientStream pipe, int opcode, string payload, CancellationToken cancel)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            byte[] head = new byte[8];

            BitConverter.TryWriteBytes(head.AsSpan(0, 4), opcode);
            BitConverter.TryWriteBytes(head.AsSpan(4, 4), body.Length);

            await pipe.WriteAsync(head, cancel);
            await pipe.WriteAsync(body, cancel);
            await pipe.FlushAsync(cancel);
        }

        private static async Task<(int Opcode, string Payload)?> ReadAsync(NamedPipeClientStream pipe, CancellationToken cancel)
        {
            byte[] head = new byte[8];

            int read = 0;
            while (read < head.Length)
            {
                int got = await pipe.ReadAsync(head.AsMemory(read, head.Length - read), cancel);
                if (got == 0)
                    return null;

                read += got;
            }

            int opcode = BitConverter.ToInt32(head, 0);
            int length = BitConverter.ToInt32(head, 4);

            if (length < 0 || length > 1024 * 256)
                return null;

            byte[] body = new byte[length];
            read = 0;
            while (read < length)
            {
                int got = await pipe.ReadAsync(body.AsMemory(read, length - read), cancel);
                if (got == 0)
                    return null;

                read += got;
            }

            return (opcode, Encoding.UTF8.GetString(body));
        }

        public static async Task<string?> RequestCodeAsync(CancellationToken cancel)
        {
            NamedPipeClientStream? pipe = null;

            try
            {
                pipe = await ConnectAsync(cancel);

                if (pipe is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Discord is not running on this PC");
                    return null;
                }

                string clientId = Integrations.DiscordRichPresence.PhasmaStrapApplicationId;

                await WriteAsync(pipe, Handshake, JsonSerializer.Serialize(new { v = 1, client_id = clientId }), cancel);

                var ready = await ReadAsync(pipe, cancel);
                if (ready is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Discord closed the connection during the handshake");
                    return null;
                }

                string nonce = Guid.NewGuid().ToString();
                string request = JsonSerializer.Serialize(new
                {
                    cmd = "AUTHORIZE",
                    args = new { client_id = clientId, scopes = new[] { "identify" } },
                    nonce,
                });

                await WriteAsync(pipe, Frame, request, cancel);

                while (true)
                {
                    var message = await ReadAsync(pipe, cancel);
                    if (message is null)
                        return null;

                    using var document = JsonDocument.Parse(message.Value.Payload);
                    var root = document.RootElement;

                    if (!root.TryGetProperty("nonce", out var got) || got.GetString() != nonce)
                        continue;

                    if (root.TryGetProperty("evt", out var evt) && evt.GetString() == "ERROR")
                    {
                        string reason = root.TryGetProperty("data", out var errorData) && errorData.TryGetProperty("message", out var text)
                            ? text.GetString() ?? "no reason given"
                            : "no reason given";

                        App.Logger.WriteLine(LOG_IDENT, $"Discord turned the request down: {reason}");
                        return null;
                    }

                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("code", out var code))
                        return code.GetString();

                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Gave up waiting for Discord");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::RequestCodeAsync", ex);
                return null;
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }
}
