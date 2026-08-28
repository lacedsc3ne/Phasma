using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    // rewrites display-name fields in Roblox's profile-lookup response, changing what name
    // shows in-game for whoever that profile lookup was about. Simplified from Voidstrap's
    // version: this doesn't distinguish "your own profile" from other players' profiles
    // (that needs resolving your authenticated identity first, which this doesn't do), so
    // it rewrites every profile in a given response uniformly rather than targeting self vs
    // others separately.
    public static class UsernameSpoofer
    {
        private const string LOG_IDENT = "UsernameSpoofer";

        public const string Host = "apis.roblox.com";

        private const string ProfileFragment = "/user-profile-api/v1/user/profiles/get-profiles";

        private static readonly string[] NameKeys = { "username", "displayName", "combinedName", "inExperienceCombinedName", "contactName", "platformName", "alias" };

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            string spoofName = (App.Settings.Prop.UsernameSpoofName ?? "").Trim();
            if (spoofName.Length == 0)
                return null;

            if (!request.Path.Contains(ProfileFragment, StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Body.Length == 0)
                return null;

            try
            {
                JsonNode? root = JsonNode.Parse(response.Body);
                if (root is not JsonObject rootObject || rootObject["profileDetails"] is not JsonArray profiles)
                    return null;

                int changed = 0;
                foreach (JsonNode? node in profiles)
                {
                    if (node is JsonObject profile)
                        changed += SetNameFields(profile, spoofName);
                }

                if (changed == 0)
                    return null;

                App.Logger.WriteLine(LOG_IDENT, $"Rewrote {changed} name field(s) to \"{spoofName}\" on this client only");
                return Encoding.UTF8.GetBytes(root.ToJsonString());
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int SetNameFields(JsonObject profile, string name)
        {
            int count = 0;
            foreach (string key in NameKeys)
            {
                if (profile[key] is JsonValue)
                {
                    profile[key] = name;
                    count++;
                }
            }
            return count;
        }
    }
}
