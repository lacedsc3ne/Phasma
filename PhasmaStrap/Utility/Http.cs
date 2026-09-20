namespace PhasmaStrap.Utility
{
    internal static class Http
    {
        public static async Task<T> GetJson<T>(string url)
        {
            var request = await App.HttpClient.GetAsync(url);

            request.EnsureSuccessStatusCode();

            string json = await request.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<T>(json)!;
        }

        public static async Task<string> ReadStringBoundedAsync(HttpContent content, int maxBytes, CancellationToken token = default)
        {
            if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
                throw new InvalidOperationException("Response content exceeded the allowed size limit");

            await using Stream input = await content.ReadAsStreamAsync(token);
            using var output = new MemoryStream(content.Headers.ContentLength is long len && len > 0 ? (int)len : 4096);
            byte[] buffer = new byte[8192];

            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read == 0)
                    break;

                if (output.Length + read > maxBytes)
                    throw new InvalidOperationException("Response content exceeded the allowed size limit");

                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }

        public static async Task<byte[]> ReadBytesBoundedAsync(HttpContent content, int maxBytes, CancellationToken token = default)
        {
            if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
                throw new InvalidOperationException("Response content exceeded the allowed size limit");

            await using Stream input = await content.ReadAsStreamAsync(token);
            using var output = new MemoryStream(content.Headers.ContentLength is long len && len > 0 ? (int)len : 4096);
            byte[] buffer = new byte[8192];

            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read == 0)
                    break;

                if (output.Length + read > maxBytes)
                    throw new InvalidOperationException("Response content exceeded the allowed size limit");

                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }

            return output.ToArray();
        }

        public static async Task<string> GetStringBoundedAsync(HttpClient client, string url, int maxBytes, CancellationToken token = default)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            return await ReadStringBoundedAsync(response.Content, maxBytes, token);
        }
    }
}
