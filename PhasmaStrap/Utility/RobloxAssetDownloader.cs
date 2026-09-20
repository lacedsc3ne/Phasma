using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    public sealed record AssetDownloadResult(bool Success, byte[]? Bytes, string? Error);

    public static class RobloxAssetDownloader
    {
        public static async Task<AssetDownloadResult> DownloadAssetAsync(long assetId, CancellationToken token = default)
        {
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://assetdelivery.roblox.com/v1/asset/?id={assetId}");
                req.Headers.TryAddWithoutValidation("User-Agent", $"{App.ProjectName}/{App.Version}");

                string? cookie = RobloxCookie.Get();
                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");

                using HttpResponseMessage res = await client.SendAsync(req, token);

                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return new AssetDownloadResult(false, null, "This asset needs an account that owns/can access it - sign into Roblox first.");

                if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new AssetDownloadResult(false, null, "No asset exists with that ID.");

                if (!res.IsSuccessStatusCode)
                    return new AssetDownloadResult(false, null, $"Roblox returned {(int)res.StatusCode} {res.StatusCode}.");

                byte[] bytes = await res.Content.ReadAsByteArrayAsync(token);
                return bytes.Length > 0
                    ? new AssetDownloadResult(true, bytes, null)
                    : new AssetDownloadResult(false, null, "Roblox returned an empty response.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxAssetDownloader", $"Failed to download asset {assetId}: {ex.Message}");
                return new AssetDownloadResult(false, null, ex.Message);
            }
        }
    }
}
