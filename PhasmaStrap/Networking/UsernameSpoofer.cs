using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
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
