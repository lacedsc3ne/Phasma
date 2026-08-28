using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PhasmaStrap.Integrations
{
    // reads (read-only) the .ROBLOSECURITY session cookie Roblox's own client already keeps
    // on this machine, DPAPI-encrypted under your Windows account. Used only so the
    // matchmaker can call the same authenticated APIs the real client uses to join a server -
    // this never writes, swaps, or exports the cookie anywhere. Ported from Voidstrap
    // (trimmed down from its version, which also backs a separate account-switching feature
    // this fork doesn't have).
    public static class RobloxCookie
    {
        private const string LOG_IDENT = "RobloxCookie";

        private static readonly Regex WarningRegex = new(@"(_\|WARNING:-DO-NOT-SHARE[^\s;,""']+)", RegexOptions.Compiled);
        private static readonly Regex NamedRegex = new(@"\.ROBLOSECURITY[\s=]+([^\s;,""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string CookiesDatPath => Path.Combine(Paths.LocalAppData, "Roblox", "LocalStorage", "RobloxCookies.dat");

        public static string? Get()
        {
            string? fromDat = ReadFromDat();
            if (!string.IsNullOrEmpty(fromDat))
                return fromDat;

            return ReadFromRegistry();
        }

        private static string? ReadFromDat()
        {
            try
            {
                if (!File.Exists(CookiesDatPath))
                    return null;

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CookiesDatPath));
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
                App.Logger.WriteLine(LOG_IDENT, $"RobloxCookies.dat read failed: {ex.Message}");
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
