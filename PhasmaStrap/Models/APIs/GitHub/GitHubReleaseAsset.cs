public class GithubReleaseAsset
{
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    // optional integrity metadata GitHub includes on some release assets - not every
    // caller needs these, so they're left nullable/default rather than required
    [JsonPropertyName("digest")]
    public string? Digest { get; set; } = null;

    [JsonPropertyName("size")]
    public long Size { get; set; } = 0;

    [JsonPropertyName("state")]
    public string? State { get; set; } = null;
}