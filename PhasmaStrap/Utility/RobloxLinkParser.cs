using System.Web;

namespace PhasmaStrap.Utility
{
    public enum RobloxLinkKind
    {
        Unknown,
        Place,
        ShareLink,
        DeepLink,
    }

    public sealed class RobloxLaunchTarget
    {
        public RobloxLinkKind Kind { get; init; }
        public long PlaceId { get; init; }
        public string? LinkCode { get; init; }
        public string? ShareCode { get; init; }
        public string? ShareType { get; init; }
        public string? RawUri { get; init; }

        public bool IsPrivateServer => !string.IsNullOrEmpty(LinkCode) || string.Equals(ShareType, "Server", StringComparison.OrdinalIgnoreCase);

        public string ToDeepLink()
        {
            switch (Kind)
            {
                case RobloxLinkKind.Place:
                    return string.IsNullOrEmpty(LinkCode)
                        ? $"roblox://experiences/start?placeId={PlaceId}"
                        : $"roblox://experiences/start?placeId={PlaceId}&linkCode={Uri.EscapeDataString(LinkCode)}";
                case RobloxLinkKind.ShareLink:
                    return $"roblox://navigation/share_links?code={Uri.EscapeDataString(ShareCode ?? "")}&type={Uri.EscapeDataString(ShareType ?? "Server")}";
                case RobloxLinkKind.DeepLink:
                    return RawUri ?? "";
                default:
                    return "";
            }
        }

        public string Describe()
        {
            return Kind switch
            {
                RobloxLinkKind.Place when !string.IsNullOrEmpty(LinkCode) => $"Private server for place {PlaceId}",
                RobloxLinkKind.Place => $"Place {PlaceId}",
                RobloxLinkKind.ShareLink => $"Share link ({ShareType ?? "Server"})",
                RobloxLinkKind.DeepLink => "Roblox deep link",
                _ => "Unrecognised link",
            };
        }
    }

    public static class RobloxLinkParser
    {
        private const string LOG_IDENT = "RobloxLinkParser";

        public static bool TryParse(string? input, out RobloxLaunchTarget target)
        {
            target = new RobloxLaunchTarget { Kind = RobloxLinkKind.Unknown };

            string text = (input ?? "").Trim().Trim('"');
            if (text.Length == 0)
                return false;

            if (text.StartsWith("roblox://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
            {
                target = new RobloxLaunchTarget { Kind = RobloxLinkKind.DeepLink, RawUri = text };
                return true;
            }

            if (long.TryParse(text, out long bareId) && bareId > 0)
            {
                target = new RobloxLaunchTarget { Kind = RobloxLinkKind.Place, PlaceId = bareId };
                return true;
            }

            if (!text.Contains("://", StringComparison.Ordinal))
                text = "https://" + text;

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
                return false;

            string host = uri.Host.ToLowerInvariant();
            if (!host.EndsWith("roblox.com", StringComparison.Ordinal) && host != "ro.blox.com")
                return false;

            var query = HttpUtility.ParseQueryString(uri.Query);

            if (uri.AbsolutePath.StartsWith("/share", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(query["code"]))
            {
                target = new RobloxLaunchTarget { Kind = RobloxLinkKind.ShareLink, ShareCode = query["code"], ShareType = query["type"] ?? "Server" };
                return true;
            }

            if (long.TryParse(query["placeId"], out long queryPlace) && queryPlace > 0)
            {
                target = new RobloxLaunchTarget { Kind = RobloxLinkKind.Place, PlaceId = queryPlace, LinkCode = query["linkCode"] ?? query["privateServerLinkCode"] };
                return true;
            }

            string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("games", StringComparison.OrdinalIgnoreCase) && long.TryParse(segments[i + 1], out long placeId) && placeId > 0)
                {
                    target = new RobloxLaunchTarget { Kind = RobloxLinkKind.Place, PlaceId = placeId, LinkCode = query["privateServerLinkCode"] ?? query["linkCode"] };
                    return true;
                }
            }

            return false;
        }

        public static async Task<RobloxLaunchTarget?> ResolveAsync(string? input, CancellationToken ct = default)
        {
            if (TryParse(input, out RobloxLaunchTarget direct))
                return direct;

            string text = (input ?? "").Trim();
            if (!text.Contains("://", StringComparison.Ordinal))
                text = "https://" + text;

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
                return null;

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"{App.ProjectName}/{App.Version}");

                Uri current = uri;
                for (int hop = 0; hop < 5; hop++)
                {
                    using HttpResponseMessage response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
                    Uri? location = response.Headers.Location;

                    if (location is null)
                        break;

                    if (!location.IsAbsoluteUri)
                        location = new Uri(current, location);

                    if (TryParse(location.ToString(), out RobloxLaunchTarget resolved))
                        return resolved;

                    current = location;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not resolve '{input}': {ex.Message}");
            }

            return null;
        }
    }
}
