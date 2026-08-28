using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    // companion to AssetWarpPolicy: blanks out thumbnail images (menu/lobby artwork - game
    // icons, avatar headshots shown outside of an actual game) when any of the image-ish
    // AssetWarp categories are enabled, so the app shell stops fetching decorative artwork
    // through the proxy too. This only ever touches the JSON thumbnail-lookup response body
    // for this one endpoint; it never blocks or rewrites anything in-game. Ported from
    // Voidstrap.Integrations.AssetProxy.AppShellStripper.
    public static class AssetWarpThumbnailPolicy
    {
        private const string LOG_IDENT = "AssetWarpThumbnailPolicy";

        public const string Host = "thumbnails.roblox.com";

        public static bool IsEnabled =>
            App.Settings.Prop.AssetWarpEnabled &&
            (App.Settings.Prop.AssetWarpDisableAllImages ||
             App.Settings.Prop.AssetWarpDisableAllTextures ||
             App.Settings.Prop.AssetWarpDisableAllDecals);

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!IsEnabled)
                return null;

            if (!request.Path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Body.Length == 0)
                return null;

            if (!request.Headers.TryGetValue("User-Agent", out string? userAgent) ||
                string.IsNullOrEmpty(userAgent) ||
                !userAgent.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                return null;

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(response.Body);
            }
            catch (Exception)
            {
                return null;
            }

            if (parsed is null)
                return null;

            int blanked = Blank(parsed);
            if (blanked == 0)
                return null;

            App.Logger.WriteLine(LOG_IDENT, $"Blanked {blanked} thumbnail(s) outside of a game");
            return Encoding.UTF8.GetBytes(parsed.ToJsonString());
        }

        private static int Blank(JsonNode node)
        {
            int count = 0;

            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item is not null)
                        count += Blank(item);
                }

                return count;
            }

            if (node is not JsonObject entry)
                return 0;

            if (entry.ContainsKey("imageUrl") && entry.ContainsKey("state"))
            {
                entry["imageUrl"] = "";
                entry["state"] = "Blocked";
                return 1;
            }

            foreach (KeyValuePair<string, JsonNode?> pair in entry)
            {
                if (pair.Value is not null)
                    count += Blank(pair.Value);
            }

            return count;
        }
    }
}