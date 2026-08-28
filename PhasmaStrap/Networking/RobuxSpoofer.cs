using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    // rewrites the balance shown on your own screen only - the real Robux balance held
    // by Roblox's servers is completely unaffected, since this only rewrites the response
    // your own client receives, never anything sent to Roblox. Ported from Voidstrap.
    public static class RobuxSpoofer
    {
        private const string LOG_IDENT = "RobuxSpoofer";

        public const string Host = "economy.roblox.com";

        private static readonly string[] BalanceFields = { "robux", "balance", "credit" };

        public static bool TryGetAmount(out long amount)
        {
            amount = 0;
            string raw = (App.Settings.Prop.RobuxSpoofAmount ?? "").Trim();

            if (raw.Length == 0)
                return false;

            raw = raw.Replace(",", "").Replace(" ", "");
            return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount) && amount >= 0;
        }

        public static byte[]? ProcessResponse(ProxiedRequest request, ProxiedResponse response)
        {
            if (!request.Path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) || !request.Path.Contains("/currency", StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Body.Length == 0 || !TryGetAmount(out long amount))
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

            int replaced = Replace(parsed, amount);
            if (replaced == 0)
                return null;

            App.Logger.WriteLine(LOG_IDENT, $"Showed a balance of {amount} on this client only, the real balance is unchanged");
            return Encoding.UTF8.GetBytes(parsed.ToJsonString());
        }

        private static int Replace(JsonNode node, long amount)
        {
            int count = 0;

            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item is not null)
                        count += Replace(item, amount);
                }
                return count;
            }

            if (node is not JsonObject entry)
                return 0;

            var targets = new List<string>();

            foreach (KeyValuePair<string, JsonNode?> pair in entry)
            {
                if (pair.Value is JsonValue value && Array.Exists(BalanceFields, field => field.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)) && value.TryGetValue(out long _))
                    targets.Add(pair.Key);
                else if (pair.Value is not null)
                    count += Replace(pair.Value, amount);
            }

            foreach (string key in targets)
            {
                entry[key] = amount;
                count++;
            }

            return count;
        }
    }
}
