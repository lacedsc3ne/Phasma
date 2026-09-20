using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PhasmaStrap.Integrations
{
    public static class RobloxCookie
    {
        public sealed class RobloxAccount
        {
            public long UserId { get; init; }
            public string Username { get; init; } = "";
            public string DisplayName { get; init; } = "";
        }

        private const string LOG_IDENT = "RobloxCookie";

        private static readonly Regex WarningRegex = new(@"(_\|WARNING:-DO-NOT-SHARE[^\s;,""']+)", RegexOptions.Compiled);
        private static readonly Regex NamedRegex = new(@"\.ROBLOSECURITY[\s=]+([^\s;,""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HttpClient AuthClient = new(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private static string CookiesDatPath => Path.Combine(Paths.LocalAppData, "Roblox", "LocalStorage", "RobloxCookies.dat");

        public static string LiveCookiesDatPath => CookiesDatPath;

        public static string? Get()
        {
            string? fromDat = ReadFromDat(CookiesDatPath);
            if (!string.IsNullOrEmpty(fromDat))
                return fromDat;

            return ReadFromRegistry();
        }

        public static async Task<RobloxAccount?> GetAccountAsync(string cookie, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(cookie))
                return null;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
                req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
                req.Headers.TryAddWithoutValidation("User-Agent", $"{App.ProjectName}/{App.Version}");

                using HttpResponseMessage res = await AuthClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                JsonElement root = doc.RootElement;

                long userId = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var id) ? id : 0;
                string username = root.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                string displayName = root.TryGetProperty("displayName", out var dnEl) ? (dnEl.GetString() ?? "") : "";

                if (userId == 0 || string.IsNullOrEmpty(username))
                    return null;

                return new RobloxAccount { UserId = userId, Username = username, DisplayName = displayName };
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GetAccountAsync failed: {ex.Message}");
                return null;
            }
        }

        public static Task<RobloxAccount?> GetAccountAsync(CancellationToken ct = default) => GetAccountAsync(Get() ?? "", ct);

        public static string? ExtractCookieFromDat(string datPath)
        {
            return ReadFromDat(datPath);
        }

        public static bool SynthesizeDatWithCookie(string templateDatPath, string newCookie, string outputDatPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newCookie) || string.IsNullOrEmpty(templateDatPath) || !File.Exists(templateDatPath))
                    return false;

                Utility.AccountGuard.MarkOwnAccess();

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(templateDatPath));
                if (!doc.RootElement.TryGetProperty("CookiesData", out var value))
                    return false;

                string? encoded = value.GetString();
                if (string.IsNullOrEmpty(encoded))
                    return false;

                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser);
                string blob = Encoding.UTF8.GetString(plain);
                string? oldCookie = Extract(blob);

                if (string.IsNullOrEmpty(oldCookie) || !blob.Contains(oldCookie))
                    return false;

                string newBlob = blob.Replace(oldCookie, newCookie);
                byte[] reEncrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(newBlob), null, DataProtectionScope.CurrentUser);
                string newEncoded = Convert.ToBase64String(reEncrypted);

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();

                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    {
                        if (property.NameEquals("CookiesData"))
                            writer.WriteString("CookiesData", newEncoded);
                        else
                            property.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputDatPath)!);
                File.WriteAllBytes(outputDatPath, stream.ToArray());

                return string.Equals(ExtractCookieFromDat(outputDatPath), newCookie, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"SynthesizeDatWithCookie failed: {ex.Message}");
                return false;
            }
        }

        private static string? ReadFromDat(string datPath)
        {
            try
            {
                if (!File.Exists(datPath))
                    return null;

                Utility.AccountGuard.MarkOwnAccess();

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(datPath));
                if (!doc.RootElement.TryGetProperty("CookiesData", out var value))
                    return null;

                string? encoded = value.GetString();
                if (string.IsNullOrEmpty(encoded))
                    return null;

                byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser);
                return Extract(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{Path.GetFileName(datPath)} read failed: {ex.Message}");
                return null;
            }
        }

        private static string? ReadFromRegistry()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Roblox\RobloxStudioBrowser\roblox.com");
                if (key is null)
                    return null;

                foreach (string name in key.GetValueNames())
                {
                    if (name.Equals(".ROBLOSECURITY", StringComparison.OrdinalIgnoreCase) && key.GetValue(name) is string raw && !string.IsNullOrEmpty(raw))
                    {
                        string? extracted = Extract(raw);
                        if (!string.IsNullOrEmpty(extracted))
                            return extracted;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Registry read failed: {ex.Message}");
            }

            return null;
        }

        private static string? Extract(string blob)
        {
            if (string.IsNullOrEmpty(blob))
                return null;

            Match warning = WarningRegex.Match(blob);
            if (warning.Success)
                return warning.Groups[1].Value.Trim();

            Match named = NamedRegex.Match(blob);
            return named.Success ? named.Groups[1].Value.Trim() : null;
        }
    }
}
