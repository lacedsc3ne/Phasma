namespace PhasmaStrap.Utility
{
    internal static class Http
    {
        /// <summary>
        /// Gets and deserializes a JSON API response to the specified object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="url"></param>
        /// <exception cref="HttpRequestException"></exception>
        /// <exception cref="JsonException"></exception>
        public static async Task<T> GetJson<T>(string url)
        {
            var request = await App.HttpClient.GetAsync(url);

            request.EnsureSuccessStatusCode();

            string json = await request.Content.ReadAsStringAsync();
            
            return JsonSerializer.Deserialize<T>(json)!;
        }

        /// <summary>
        /// Reads the content of an HTTP response as a string, refusing to buffer more than <paramref name="maxBytes"/>.
        /// Used by network-facing integrations (e.g. GameChat) so a malicious or misbehaving endpoint can't exhaust memory.
        /// </summary>
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
    }
}
