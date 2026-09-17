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
    //
    // Response shape (apis.roblox.com/user-profile-api/v1/user/profiles/get-profiles):
    //   { "profileDetails": [ { "userId": 123, "names": { "username": "...", "displayName": "...",
    //     "combinedName": "...", ... }, "isVerified": false }, ... ] }
    // - the name fields are NESTED under "names" and the badge is "isVerified"; the previous
    // version of this file looked for top-level "username"/"hasVerifiedBadge" keys that this
    // endpoint doesn't return, so it never rewrote anything.
    public static class UsernameSpoofer
    {
        private const string LOG_IDENT = "UsernameSpoofer";

        public const string Host = "apis.roblox.com";

        private static readonly string[] ProfileFragments =
        {
            "/user-profile-api/v1/user/profiles/get-profiles",
            "/v1/user/profiles/get-profiles",
        };

        private static readonly string[] NameKeys = { "username", "displayName", "combinedName", "inExperienceCombinedName", "contactName", "platformName", "alias" };

        // a name box left empty with "apply ingame" on means "hide the name" - Roblox collapses a
        // truly empty string back to the real name client-side, a zero-width space doesn't
        private const string EmptyNameSentinel = "​";

        private static long? _cachedSelfId;
        private static string? _cachedSelfName;
        private static DateTime _selfIdExpiresUtc = DateTime.MinValue;
        private static int _selfIdRefreshing;

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!ProfileFragments.Any(f => request.Path.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return null;

            if (response.Body.Length == 0 || response.StatusCode != 200)
                return null;

            var prop = App.Settings.Prop;

            bool othersName = prop.SpoofOthersApplyIngame;
            bool othersVerified = prop.SpoofOthersVerified;
            bool selfName = prop.SpoofSelfApplyIngame;
            bool selfVerified = prop.SpoofSelfVerified;
            string uniformName = (prop.UsernameSpoofName ?? "").Trim();

            bool selfOthersActive = othersName || othersVerified || selfName || selfVerified;

            if (!selfOthersActive && uniformName.Length == 0)
                return null;

            try
            {
                JsonNode? root = JsonNode.Parse(response.Body);
                if (root is not JsonObject rootObject || rootObject["profileDetails"] is not JsonArray profiles)
                    return null;

                long? selfId = selfOthersActive ? TryGetCachedSelfId() : null;
                string selfUsername = selfOthersActive ? (TryGetCachedSelfName() ?? "") : "";
                int changed = 0;

                foreach (JsonNode? node in profiles)
                {
                    if (node is not JsonObject profile)
                        continue;

                    if (!selfOthersActive)
                    {
                        changed += SetNameFields(profile, uniformName);
                        continue;
                    }

                    bool isSelf = IsOwnProfile(profile, selfId, selfUsername);

                    if (isSelf)
                    {
                        if (selfName)
                            changed += SetNameFields(profile, prop.SpoofSelfName?.Trim() ?? "");
                        if (selfVerified)
                            changed += SetVerifiedBadge(profile);
                    }
                    else
                    {
                        if (othersName)
                            changed += SetNameFields(profile, prop.SpoofOthersName?.Trim() ?? "");
                        if (othersVerified)
                            changed += SetVerifiedBadge(profile);
                    }
                }

                if (changed == 0)
                    return null;

                App.Logger.WriteLine(LOG_IDENT, $"Rewrote {changed} name/badge field(s) across {profiles.Count} profile(s), this client only");
                return JsonSerializer.SerializeToUtf8Bytes(rootObject);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not rewrite profile response: {ex.Message}");
                return null;
            }
        }

        private static bool IsOwnProfile(JsonObject profile, long? selfId, string selfUsername)
        {
            if (selfId.HasValue && TryReadInt64(profile["userId"], out long profileUserId))
                return profileUserId == selfId.Value;

            // older shape used "id"
            if (selfId.HasValue && TryReadInt64(profile["id"], out long legacyId))
                return legacyId == selfId.Value;

            if (selfUsername.Length == 0 || profile["names"] is not JsonObject names)
                return false;

            return names["username"] is JsonValue value && value.TryGetValue(out string? username)
                && string.Equals(username, selfUsername, StringComparison.Ordinal);
        }

        private static int SetNameFields(JsonObject profile, string name)
        {
            if (profile["names"] is not JsonObject names)
            {
                names = new JsonObject();
                profile["names"] = names;
            }

            string effective = name.Length == 0 ? EmptyNameSentinel : name;
            int count = 0;

            foreach (string key in NameKeys)
            {
                bool present = names[key] is JsonValue value && value.TryGetValue(out string? current) && current == effective;
                if (present)
                    continue;

                names[key] = effective;
                count++;
            }

            // some older/alternate shapes put the fields at the top level too
            foreach (string key in NameKeys)
            {
                if (profile[key] is JsonValue)
                {
                    profile[key] = effective;
                    count++;
                }
            }

            return count;
        }

        private static int SetVerifiedBadge(JsonObject profile)
        {
            int count = 0;

            if (profile["isVerified"] is not JsonValue isVerified || !isVerified.TryGetValue(out bool current) || !current)
            {
                profile["isVerified"] = true;
                count++;
            }

            if (profile["hasVerifiedBadge"] is JsonValue legacy && legacy.TryGetValue(out bool legacyValue) && !legacyValue)
            {
                profile["hasVerifiedBadge"] = true;
                count++;
            }

            return count;
        }

        internal static bool TryReadInt64(JsonNode? node, out long value)
        {
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue(out long number))
                {
                    value = number;
                    return true;
                }

                if (jsonValue.TryGetValue(out string? text) && long.TryParse(text, out number))
                {
                    value = number;
                    return true;
                }
            }

            value = 0;
            return false;
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

        // called when the proxy is (re)enabled so the identity is already resolved by the time
        // the first profile lookup comes through, instead of that one going out un-rewritten
        public static void WarmUpIdentity() => RefreshSelfIdentity();

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
                        else
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Could not resolve the signed-in account (no valid .ROBLOSECURITY cookie found) - self/others spoofing can't tell your profile apart from others' until one is available");
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
