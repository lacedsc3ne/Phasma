namespace PhasmaStrap.Utility
{
    // Downloads a single asset's raw bytes by ID via Roblox's public single-asset endpoint -
    // simpler than AssetWarpPolicy's batch-resolution flow (that one exists to intercept and
    // rewrite Roblox's own in-game requests, not to fetch one specific asset on demand).
    public static class RobloxAssetDownloader
    {
        public static async Task<byte[]?> DownloadAssetAsync(long assetId, CancellationToken token = default)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            try
            {
                return await client.GetByteArrayAsync($"https://assetdelivery.roblox.com/v1/asset/?id={assetId}", token);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("RobloxAssetDownloader", $"Failed to download asset {assetId}: {ex.Message}");
                return null;
            }
        }
    }
}
