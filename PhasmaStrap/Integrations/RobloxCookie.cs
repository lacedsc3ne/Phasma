using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PhasmaStrap.Integrations
{
    // Reads (and, for the account switcher below, backs up/restores) the .ROBLOSECURITY session
    // cookie Roblox's own client keeps on this machine, DPAPI-encrypted under your Windows account
    // inside RobloxCookies.dat. Ported from Voidstrap.
    //
    // Security note: neither PhasmaStrap nor Voidstrap re-encrypts the cookie itself with its own
    // DPAPI call for storage. Instead, RobloxCookies.dat's "CookiesData" field is ALREADY a
    // DataProtectionScope.CurrentUser-protected blob written by Roblox's own client - so every
    // backup made by the account switcher (see AccountSwitcherViewModel) is just a byte-for-byte
    // copy of that already-encrypted container, decryptable only by the same Windows account that
    // created it. This file only calls ProtectedData.Unprotect/Protect directly when synthesizing a
    // brand new dat from a pasted cookie (SynthesizeDatWithCookie), where an existing dat is used as
    // a template and just has its cookie value swapped inside the decrypted blob before
    // re-protecting it the same way Roblox does.
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

        // dedicated client (cookies disabled) so an explicit Cookie header we set per-request for a
        // candidate/saved account is never mixed with whichever account App.HttpClient's own cookie
        // container might otherwise be tracking
        private static readonly HttpClient AuthClient = new(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private static string CookiesDatPath => Path.Combine(Paths.LocalAppData, "Roblox", "LocalStorage", "RobloxCookies.dat");

        /// <summary>
        /// Full path to Roblox's own live cookie store. Whatever's in this file when Roblox next
        /// starts is the account it launches as - this is the entire integration point the account
        /// switcher needs, no launch-path hook required.
        /// </summary>
        public static string LiveCookiesDatPath => CookiesDatPath;

        public static string? Get()
        {
            string? fromDat = ReadFromDat(CookiesDatPath);
            if (!string.IsNullOrEmpty(fromDat))
                return fromDat;

            return ReadFromRegistry();
        }

        /// <summary>
        /// Calls the authenticated users.roblox.com endpoint with the given .ROBLOSECURITY cookie to
        /// verify it's live and resolve which account it belongs to. Returns null if the cookie is
        /// missing, expired, or invalid.
        /// </summary>
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

        /// <summary>
        /// Decrypts and returns the .ROBLOSECURITY cookie stored inside an arbitrary RobloxCookies.dat
        /// file (e.g. one of the account switcher's own backups), rather than the live one.
        /// </summary>
        public static string? ExtractCookieFromDat(string datPath)
        {
            return ReadFromDat(datPath);
        }

        /// <summary>
        /// Builds a new RobloxCookies.dat at <paramref name="outputDatPath"/> by taking an existing,
        /// valid dat file as a template (its JSON shape/other fields are preserved verbatim) and
        /// swapping only the cookie value inside its decrypted "CookiesData" blob for
        /// <paramref name="newCookie"/>, then re-encrypting with the same DPAPI call Roblox itself
        /// uses (DataProtectionScope.CurrentUser). This is how a raw pasted cookie gets turned into a
        /// dat file the Roblox client will actually accept, without needing to know its full format.
        /// </summary>
        public static bool SynthesizeDatWithCookie(string templateDatPath, string newCookie, string outputDatPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newCookie) || string.IsNullOrEmpty(templateDatPath) || !File.Exists(templateDatPath))
                    return false;

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

                // verify the write actually took by reading it back
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
