using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    public sealed class AssetRouteInfo
    {
        public string OriginalUrl = "";     // "" for a swapped asset that had no location of its own
        public string Key = "";             // what the content is cached under
        public long AssetId;
        public int TypeId;
        public long PlaceId;
        public string SwapPack = "";        // folder name of the pack that replaces this asset, "" = none
    }

    // How asset CONTENT gets to pass through PhasmaStrap without intercepting Roblox's CDN.
    //
    // The client never asks for an asset's bytes by ID. It POSTs a batch of IDs to
    // assetdelivery.roblox.com (/v1/assets/batch - a host the proxy already terminates) and gets a
    // signed CDN URL back per asset, which it then downloads. Rewriting those URLs in the batch
    // RESPONSE to point back at assetdelivery.roblox.com/phasma-asset/... makes the client fetch
    // the bytes from the proxy instead - same host, same certificate, no new hosts-file entries -
    // and the proxy can then serve them from a disk cache, shrink textures, or hand out a swap
    // pack's replacement. The original URL travels inside the rewritten one, so the proxy can
    // always fall back to "302, go and get it there yourself".
    //
    // CDN paths end in the content's hash (https://fts.rbxcdn.com/sc1/<32 hex>?<signature>), so
    // the hash is a permanent cache key: same hash, same bytes, in every game, forever - while the
    // signature around it expires within days.
    //
    // Pure functions, no App dependencies.
    public static class AssetRoute
    {
        public const string PathPrefix = "/phasma-asset/v1/";

        public static string ContentKey(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                string last = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
                if (last.Length is >= 32 and <= 64 && last.All(Uri.IsHexDigit))
                    return last.ToLowerInvariant();

                // not a hash-addressed URL: the address without its signature is the best there is
                return "u" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.Host + uri.AbsolutePath))).ToLowerInvariant()[..40];
            }

            return "u" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()[..40];
        }

        private static string ToBase64Url(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string FromBase64Url(string text)
        {
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        public static string BuildUrl(string host, AssetRouteInfo info)
        {
            string key = info.Key.Length > 0 ? info.Key : info.OriginalUrl.Length > 0 ? ContentKey(info.OriginalUrl) : $"swap{info.AssetId}";

            var url = new StringBuilder($"https://{host}{PathPrefix}{key}?a={info.AssetId}&t={info.TypeId}&p={info.PlaceId}");
            if (info.SwapPack.Length > 0)
                url.Append("&s=").Append(ToBase64Url(info.SwapPack));
            if (info.OriginalUrl.Length > 0)
                url.Append("&u=").Append(ToBase64Url(info.OriginalUrl));

            return url.ToString();
        }

        // null = not one of ours
        public static AssetRouteInfo? TryParse(string pathAndQuery)
        {
            if (!pathAndQuery.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                int question = pathAndQuery.IndexOf('?');
                string key = question < 0 ? pathAndQuery[PathPrefix.Length..] : pathAndQuery[PathPrefix.Length..question];
                if (key.Length == 0 || key.Length > 80 || !key.All(c => char.IsLetterOrDigit(c)))
                    return null;

                var info = new AssetRouteInfo { Key = key.ToLowerInvariant() };

                if (question >= 0)
                {
                    foreach (string pair in pathAndQuery[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int equals = pair.IndexOf('=');
                        if (equals <= 0)
                            continue;

                        string name = pair[..equals], value = pair[(equals + 1)..];
                        switch (name)
                        {
                            case "a": long.TryParse(value, out info.AssetId); break;
                            case "t": int.TryParse(value, out info.TypeId); break;
                            case "p": long.TryParse(value, out info.PlaceId); break;
                            case "s": info.SwapPack = FromBase64Url(value); break;
                            case "u": info.OriginalUrl = FromBase64Url(value); break;
                        }
                    }
                }

                // the original has to be a real https address at Roblox - this endpoint must not
                // become a way to make PhasmaStrap fetch arbitrary URLs
                if (info.OriginalUrl.Length > 0 && !IsRobloxContentUrl(info.OriginalUrl))
                    return null;

                return info.OriginalUrl.Length == 0 && info.SwapPack.Length == 0 ? null : info;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsRobloxContentUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.EndsWith(".rbxcdn.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase));

        public sealed class RewriteResult
        {
            public byte[] Body = Array.Empty<byte>();
            public int Routed, Swapped, Untouched;
        }

        // Rewrites the locations in a /v1/assets/batch response. `swapPackFor` names the pack that
        // replaces an asset ID in this place ("" = none). null = nothing to change / not understood.
        public static RewriteResult? RewriteBatch(byte[] requestBody, byte[] responseBody, string host, long placeId, bool routeEverything, Func<long, string> swapPackFor)
        {
            JsonNode? request, response;
            try
            {
                request = JsonNode.Parse(requestBody);
                response = JsonNode.Parse(responseBody);
            }
            catch
            {
                return null;
            }

            if (request is not JsonArray asked || response is not JsonArray answered)
                return null;

            // the response echoes each entry's requestId; the asset ID is only in the request
            var ids = new Dictionary<string, long>();
            foreach (JsonNode? node in asked)
            {
                if (node is not JsonObject entry)
                    continue;

                string requestId = entry["requestId"]?.ToJsonString().Trim('"') ?? "";
                if (requestId.Length > 0 && long.TryParse(entry["assetId"]?.ToJsonString().Trim('"'), out long assetId))
                    ids[requestId] = assetId;
            }

            var result = new RewriteResult();

            foreach (JsonNode? node in answered)
            {
                if (node is not JsonObject entry)
                    continue;

                string requestId = entry["requestId"]?.ToJsonString().Trim('"') ?? "";
                ids.TryGetValue(requestId, out long assetId);

                string location = entry["location"] is JsonValue value && value.TryGetValue(out string? text) ? text ?? "" : "";
                int.TryParse(entry["assetTypeId"]?.ToJsonString().Trim('"'), out int typeId);

                string pack = assetId > 0 ? swapPackFor(assetId) : "";

                if (pack.Length > 0 && location.Length > 0)
                {
                    entry["location"] = BuildUrl(host, new AssetRouteInfo { AssetId = assetId, TypeId = typeId, PlaceId = placeId, SwapPack = pack, OriginalUrl = IsRobloxContentUrl(location) ? location : "" });
                    result.Swapped++;
                }
                else if (routeEverything && location.Length > 0 && IsRobloxContentUrl(location) && !location.Contains(PathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    entry["location"] = BuildUrl(host, new AssetRouteInfo { AssetId = assetId, TypeId = typeId, PlaceId = placeId, OriginalUrl = location });
                    result.Routed++;
                }
                else
                {
                    result.Untouched++;
                }
            }

            if (result.Routed == 0 && result.Swapped == 0)
                return null;

            // written the way Roblox wrote it: '&' and '+' as themselves, not as & escapes
            result.Body = Encoding.UTF8.GetBytes(answered.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return result;
        }
    }
}
