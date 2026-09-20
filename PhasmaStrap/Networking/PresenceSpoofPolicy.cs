using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    public enum PresenceSpoofMode
    {
        Off,
        Online,
        Studio,
        Offline
    }

    public static class PresenceSpoofPolicy
    {
        public const string Host = "apis.roblox.com";

        private const string PulseFragment = "/user-heartbeats-api/pulse";

        private const string ActionReportFragment = "/user-heartbeats-api/action-report";

        public static byte[]? TransformRequest(ProxiedRequest request)
        {
            PresenceSpoofMode mode = App.Settings.Prop.PresenceSpoofMode;

            if (mode is not (PresenceSpoofMode.Online or PresenceSpoofMode.Studio))
                return null;

            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!request.Path.Contains(PulseFragment, StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                JsonObject root;
                if (request.Body.Length == 0)
                {
                    root = new JsonObject();
                }
                else if (JsonNode.Parse(request.Body) is JsonObject parsed)
                {
                    root = parsed;
                }
                else
                {
                    return null;
                }

                (string containerKey, JsonObject? session) = FindObject(root, "SessionInfo");
                if (session is null)
                {
                    containerKey = "SessionInfo";
                    session = new JsonObject();
                    root[containerKey] = session;
                }

                bool lowerCamel = containerKey.Length > 0 && char.IsLower(containerKey[0]);
                string clientType = mode == PresenceSpoofMode.Studio ? "Studio" : "Player";
                string location = mode == PresenceSpoofMode.Studio ? "Studio" : "Website";

                SetString(session, "ClientType", lowerCamel, clientType);
                SetString(session, "Location", lowerCamel, location);

                return JsonSerializer.SerializeToUtf8Bytes(root);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ProxiedResponse? TryServeFromCache(ProxiedRequest request)
        {
            PresenceSpoofMode mode = App.Settings.Prop.PresenceSpoofMode;

            if (mode == PresenceSpoofMode.Off)
                return null;

            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return null;

            bool actionReport = request.Path.Contains(ActionReportFragment, StringComparison.OrdinalIgnoreCase);
            bool pulse = request.Path.Contains(PulseFragment, StringComparison.OrdinalIgnoreCase);

            if (!actionReport && !(mode == PresenceSpoofMode.Offline && pulse))
                return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/json"
            };

            return new ProxiedResponse(200, "OK", headers, Encoding.UTF8.GetBytes("{}"));
        }

        private static (string Key, JsonObject? Value) FindObject(JsonObject value, string name)
        {
            foreach ((string key, JsonNode? node) in value)
            {
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (key, node as JsonObject);
            }

            return (name, null);
        }

        private static void SetString(JsonObject value, string name, bool lowerCamel, string replacement)
        {
            string? existingKey = null;
            foreach ((string key, JsonNode? _) in value)
            {
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    existingKey = key;
                    break;
                }
            }

            string keyName = existingKey ?? (lowerCamel ? char.ToLowerInvariant(name[0]) + name[1..] : name);
            value[keyName] = replacement;
        }
    }
}
