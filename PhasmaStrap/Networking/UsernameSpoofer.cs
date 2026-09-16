using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    // rewrites display-name/verified-badge fields in Roblox's profile-lookup response, changing
    // what name/badge shows for whoever that profile lookup was about. Two independent paths:
    //  - the older, simpler Settings.UsernameSpoofName (NetworkingPage) rewrites every profile in
    //    a response uniformly, no self/others distinction.
    //  - the newer Settings.Spoof{Self,Others}* fields (AssetWarp tab, matching Voidstrap's
    //    Client Spoofer) distinguish "your own profile" from other players', each independently
    //    gated by its own "apply ingame" toggle - a name typed in the box does nothing until that
    //    toggle is on, matching Voidstrap's own control descriptions. Self/others wins over the
    //    uniform field when either is actively configured; the uniform field is the fallback.
    public static class UsernameSpoofer
    {
        private const string LOG_IDENT = "UsernameSpoofer";

        public const string Host = "apis.roblox.com";

        private const string ProfileFragment = "/user-profile-api/v1/user/profiles/get-profiles";

        private static readonly string[] NameKeys = { "username", "displayName", "combinedName", "inExperienceCombinedName", "contactName", "platformName", "alias" };

        private static long? _cachedSelfId;
        private static string? _cachedSelfName;
        private static DateTime _selfIdExpiresUtc = DateTime.MinValue;
        private static int _selfIdRefreshing;

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!request.Path.Contains(ProfileFragment, StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Body.Length == 0)
                return null;

            bool othersActive = App.Settings.Prop.SpoofOthersApplyIngame && !string.IsNullOrWhiteSpace(App.Settings.Prop.SpoofOthersName);
            bool selfActive = App.Settings.Prop.SpoofSelfApplyIngame && !string.IsNullOrWhiteSpace(App.Settings.Prop.SpoofSelfName);
            string uniformName = (App.Settings.Prop.UsernameSpoofName ?? "").Trim();

            if (!othersActive && !selfActive && uniformName.Length == 0)
                return null;

            try
            {
                JsonNode? root = JsonNode.Parse(response.Body);
                if (root is not JsonObject rootObject || rootObject["profileDetails"] is not JsonArray profiles)
                    return null;

                long? selfId = (othersActive || selfActive) ? TryGetCachedSelfId() : null;
                int changed = 0;

                foreach (JsonNode? node in profiles)
                {
                    if (node is not JsonObject profile)
                        continue;

                    bool isSelf = selfId.HasValue && profile["id"] is JsonValue idValue && idValue.TryGetValue(out long profileId) && profileId == selfId.Value;

                    if (isSelf && selfActive)
                    {
                        changed += SetNameFields(profile, App.Settings.Prop.SpoofSelfName.Trim());
                        if (App.Settings.Prop.SpoofSelfVerified)
                            changed += SetVerifiedBadge(profile, true);
                    }
                    else if (!isSelf && othersActive)
                    {
                        changed += SetNameFields(profile, App.Settings.Prop.SpoofOthersName.Trim());
                        if (App.Settings.Prop.SpoofOthersVerified)
                            changed += SetVerifiedBadge(profile, true);
                    }
                    else if (!othersActive && !selfActive && uniformName.Length > 0)
                    {
                        changed += SetNameFields(profile, uniformName);
                    }
                }

                if (changed == 0)
                    return null;

                App.Logger.WriteLine(LOG_IDENT, $"Rewrote {changed} name/badge field(s) on this client only");
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

        private static int SetVerifiedBadge(JsonObject profile, bool verified)
        {
            if (profile["hasVerifiedBadge"] is not JsonValue)
                return 0;

            profile["hasVerifiedBadge"] = verified;
            return 1;
        }

        // synchronous read of whatever's cached, kicking off a background refresh if stale/missing
        // - the proxy's response pipeline is synchronous, so this never blocks on the network. The
        // first response after (re)enabling the spoofer may go out un-rewritten while the identity
        // is still resolving; every one after that uses the freshly cached ID. Public so
        // GameCreatorSpoofer can reuse the same cached identity instead of resolving it twice.
        public static long? TryGetCachedSelfId()
        {
            if (_cachedSelfId.HasValue && DateTime.UtcNow < _selfIdExpiresUtc)
                return _cachedSelfId;

            RefreshSelfIdentity();
            return _cachedSelfId;
        }

        public static string? TryGetCachedSelfName()
        {
            if (_cachedSelfId.HasValue && DateTime.UtcNow < _selfIdExpiresUtc)
                return _cachedSelfName;

            RefreshSelfIdentity();
            return _cachedSelfName;
        }

        private static void RefreshSelfIdentity()
        {
            if (Interlocked.CompareExchange(ref _selfIdRefreshing, 1, 0) == 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Integrations.RobloxCookie.RobloxAccount? account = await Integrations.RobloxCookie.GetAccountAsync().ConfigureAwait(false);
                        if (account is not null)
                        {
                            _cachedSelfId = account.UserId;
                            _cachedSelfName = account.Username;
                            _selfIdExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _selfIdRefreshing, 0);
                    }
                });
            }
        }
    }
}
