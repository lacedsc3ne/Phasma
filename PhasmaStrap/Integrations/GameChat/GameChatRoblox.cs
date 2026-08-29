using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Integrations.GameChat
{
    /// <summary>
    /// Roblox-facing helpers for the game chat overlay (headshots only).
    /// Voidstrap's original GameChatRoblox also resolved "Voidstrap profile" data (banners, badges,
    /// friend requests, avatar borders) from Voidstrap's own website API. That API doesn't exist for
    /// PhasmaStrap, so this port only keeps what can be served directly from Roblox's public endpoints.
    /// </summary>
    public static class GameChatRoblox
    {
        private const string Tag = "GameChatRoblox";
        private const int MaxImageBytes = 4_000_000;
        private const int MaxProfileCacheEntries = 256;

        private static readonly ConcurrentDictionary<long, Task<ImageSource?>> _headshotCache = new();

        public static Task<ImageSource?> GetHeadshotAsync(long userId)
        {
            if (userId <= 0)
                return Task.FromResult<ImageSource?>(null);

            Task<ImageSource?> task = _headshotCache.GetOrAdd(userId, FetchHeadshotAsync);
            TrimCache(_headshotCache);
            return task;
        }

        private static async Task<ImageSource?> FetchHeadshotAsync(long userId)
        {
            try
            {
                string url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=48x48&format=Png&isCircular=true";
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                using HttpResponseMessage response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                string body = await Http.ReadStringBoundedAsync(response.Content, MaxImageBytes).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(body);

                string? imageUrl = null;
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("imageUrl", out var iu) && iu.ValueKind == JsonValueKind.String)
                            imageUrl = iu.GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(imageUrl))
                    return null;

                return await DownloadImageAsync(imageUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(Tag, "Headshot fetch failed: " + ex.Message);
                return null;
            }
        }

        private static async Task<ImageSource?> DownloadImageAsync(string imageUrl)
        {
            try
            {
                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    return null;

                using HttpRequestMessage request = new(HttpMethod.Get, uri);
                using HttpResponseMessage response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
                    return null;

                return DecodeImage(bytes, 96);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Decodes an image from bytes with a bounded decode size, never throwing on malformed input.
        /// </summary>
        private static ImageSource? DecodeImage(byte[] bytes, int decodePixelWidth)
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                    bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static void TrimCache(ConcurrentDictionary<long, Task<ImageSource?>> cache)
        {
            if (cache.Count <= MaxProfileCacheEntries)
                return;
            foreach (long key in cache.Keys)
            {
                if (cache.Count <= MaxProfileCacheEntries)
                    break;
                cache.TryRemove(key, out _);
            }
        }
    }
}
