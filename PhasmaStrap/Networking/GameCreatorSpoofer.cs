using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    // "Make yourself game creator" (AssetWarp's Client Spoofer section) - rewrites the creator
    // metadata in games.roblox.com's own game-details response to your own account, on this
    // client only. Shares UsernameSpoofer's identity-caching approach rather than duplicating it.
    public static class GameCreatorSpoofer
    {
        private const string LOG_IDENT = "GameCreatorSpoofer";

        public const string Host = "games.roblox.com";

        private const string GamesFragment = "/v1/games";

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!App.Settings.Prop.SpoofSelfGameCreator)
                return null;

            if (!request.Path.Contains(GamesFragment, StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Body.Length == 0)
                return null;

            try
            {
                JsonNode? root = JsonNode.Parse(response.Body);
                if (root is not JsonObject rootObject || rootObject["data"] is not JsonArray games)
                    return null;

                long? selfId = UsernameSpoofer.TryGetCachedSelfId();
                string? selfName = UsernameSpoofer.TryGetCachedSelfName();
                if (!selfId.HasValue || selfName is null)
                    return null;

                int changed = 0;
                foreach (JsonNode? node in games)
                {
                    if (node is not JsonObject game || game["creator"] is not JsonObject creator)
                        continue;

                    creator["id"] = selfId.Value;
                    creator["name"] = selfName;
                    creator["type"] = "User";
                    changed++;
                }

                if (changed == 0)
                    return null;

                App.Logger.WriteLine(LOG_IDENT, $"Rewrote creator metadata on {changed} game entr{(changed == 1 ? "y" : "ies")}, this client only");
                return Encoding.UTF8.GetBytes(root.ToJsonString());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
