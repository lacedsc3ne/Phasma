using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    public sealed record AssetDownloadResult(bool Success, byte[]? Bytes, string? Error);

    // Downloads a single asset's raw bytes by ID via Roblox's public single-asset endpoint -
    // simpler than AssetWarpPolicy's batch-resolution flow (that one exists to intercept and
    // rewrite Roblox's own in-game requests, not to fetch one specific asset on demand).
    //
    // assetdelivery.roblox.com/v1/asset/ redirects (302) to the actual CDN URL for a public
    // asset - HttpClient follows that automatically, no special handling needed there. But a
    // meaningful share of real assets (anything private, or not owned/purchased by the signed-in
    // account) come back 401 "Authentication required to access Asset" with NO cookie sent at
    // all, which is exactly the gap that made this always fail for anything but a fully public
    // asset. Sending the current .ROBLOSECURITY cookie (when signed in) fixes that for anything
    // the signed-in account actually has access to, same as everywhere else this app calls an
    // authenticated Roblox API.
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
