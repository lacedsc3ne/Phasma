using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    public static class GameCreatorSpoofer
    {
        private const string LOG_IDENT = "GameCreatorSpoofer";

        public const string Host = "gamejoin.roblox.com";

        private static readonly string[] JoinFragments =
        {
            "/v1/join-game",
            "/v1/join-game-instance",
            "/v1/join-reserved-game",
            "/v1/join-play-together-game",
            "/v2/join-game",
            "/v2/join-game-instance",
            "/v2/join-reserved-game",
        };

        private static readonly (string Id, string Type)[] CreatorPairs =
        {
            ("CreatorId", "CreatorType"),
            ("CreatorId", "CreatorTypeEnum"),
            ("CreatorTargetId", "CreatorType"),
            ("CreatorTargetId", "CreatorTypeEnum"),
            ("creatorId", "creatorType"),
            ("creatorTargetId", "creatorType"),
        };

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!App.Settings.Prop.SpoofSelfGameCreator)
                return null;

            if (!JoinFragments.Any(f => request.Path.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return null;

            if (response.Body.Length == 0 || response.StatusCode != 200)
                return null;

            long? selfId = UsernameSpoofer.TryGetCachedSelfId();
            if (!selfId.HasValue || selfId.Value <= 0)
                return null;

            try
            {
                JsonNode? root = JsonNode.Parse(response.Body);
                if (root is null)
                    return null;

                int changed = RewriteCreatorFields(root, selfId.Value);
                if (changed == 0)
                    return null;

                App.Logger.WriteLine(LOG_IDENT, $"Rewrote {changed} creator field(s) in the join response, this client only");
                return JsonSerializer.SerializeToUtf8Bytes(root);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not rewrite join response: {ex.Message}");
                return null;
            }
        }

        private static int RewriteCreatorFields(JsonNode node, long userId)
        {
            if (node is JsonArray array)
            {
                int arrayChanged = 0;
                foreach (JsonNode? child in array)
                {
                    if (child is not null)
                        arrayChanged += RewriteCreatorFields(child, userId);
                }
                return arrayChanged;
            }

            if (node is not JsonObject obj)
                return 0;

            int changed = 0;

            foreach ((string idKey, string typeKey) in CreatorPairs)
            {
                if (!obj.ContainsKey(idKey) && !obj.ContainsKey(typeKey))
                    continue;

                if (obj.ContainsKey(idKey) && (!UsernameSpoofer.TryReadInt64(obj[idKey], out long currentId) || currentId != userId))
                {
                    obj[idKey] = userId;
                    changed++;
                }

                if (obj.ContainsKey(typeKey))
                {
                    JsonNode replacement = CreatorTypeValue(obj[typeKey], typeKey);
                    if (obj[typeKey]?.ToJsonString() != replacement.ToJsonString())
                    {
                        obj[typeKey] = replacement;
                        changed++;
                    }
                }
            }

            foreach ((string key, JsonNode? child) in obj.ToList())
            {
                if (child is JsonObject or JsonArray)
                {
                    changed += RewriteCreatorFields(child, userId);
                }
                else if (child is JsonValue value && value.TryGetValue(out string? text) && text is not null && text.Length > 2 && text[0] == '{' && text.Contains("Creator", StringComparison.Ordinal))
                {
                    try
                    {
                        JsonNode? inner = JsonNode.Parse(text);
                        if (inner is not null)
                        {
                            int innerChanged = RewriteCreatorFields(inner, userId);
                            if (innerChanged > 0)
                            {
                                obj[key] = inner.ToJsonString();
                                changed += innerChanged;
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return changed;
        }

        private static JsonNode CreatorTypeValue(JsonNode? value, string key)
        {
            if (value is JsonValue stringValue && stringValue.TryGetValue(out string? current) && current is not null)
                return JsonValue.Create(current.StartsWith("Enum.CreatorType.", StringComparison.Ordinal) ? "Enum.CreatorType.User" : "User")!;

            if (value is JsonValue number && (number.TryGetValue(out int _) || number.TryGetValue(out long _)))
                return JsonValue.Create(key.Equals("CreatorType", StringComparison.Ordinal) ? 0 : 1)!;

            return JsonValue.Create("User")!;
        }
    }
}
