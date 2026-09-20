using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhasmaStrap.Utility
{
    public class DeviceStart
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = "";

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = "";

        [JsonPropertyName("verify_url")]
        public string VerifyUrl { get; set; } = "";

        [JsonPropertyName("verify_url_complete")]
        public string VerifyUrlComplete { get; set; } = "";

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 3;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 600;
    }

    public class DevicePoll
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("account_id")]
        public string? AccountId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class AccountDetails
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = "";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("admin")]
        public bool Admin { get; set; }
    }

    public class GalleryItem
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Summary { get; set; } = "";

        public string Author { get; set; } = "";

        public int Imports { get; set; }

        public string Detail => Summary.Length > 0
            ? $"{Summary}  ·  by {Author}, {Imports} import{(Imports == 1 ? "" : "s")}"
            : $"by {Author}, {Imports} import{(Imports == 1 ? "" : "s")}";
    }

    public static class PhasmaAccount
    {
        private const string LOG_IDENT = "PhasmaAccount";

        private static string TokenPath => Path.Combine(Paths.Base, "AccountToken.txt");

        public static bool SignedIn => !string.IsNullOrEmpty(Token);

        public static string? Token
        {
            get
            {
                try
                {
                    return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : null;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException($"{LOG_IDENT}::Token", ex);
                    return null;
                }
            }
        }

        private static void StoreToken(string? token)
        {
            try
            {
                if (string.IsNullOrEmpty(token))
                {
                    if (File.Exists(TokenPath))
                        File.Delete(TokenPath);

                    return;
                }

                Directory.CreateDirectory(Paths.Base);
                File.WriteAllText(TokenPath, token);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::StoreToken", ex);
            }
        }

        public static string DisplayName =>
            !string.IsNullOrEmpty(App.State.Prop.AccountName) ? App.State.Prop.AccountName
            : !string.IsNullOrEmpty(App.State.Prop.AccountId) ? App.State.Prop.AccountId
            : "";

        public static event EventHandler? Changed;

        private static void Announce() => Changed?.Invoke(null, EventArgs.Empty);

        public static void Authorize(HttpRequestMessage request)
        {
            string? token = Token;
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        public static async Task<DeviceStart?> BeginAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/app/start");
                using var response = await App.HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not start signing in: the server answered {(int)response.StatusCode}");
                    return null;
                }

                return JsonSerializer.Deserialize<DeviceStart>(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::BeginAsync", ex);
                return null;
            }
        }

        public static async Task<DevicePoll?> PollAsync(string deviceCode, CancellationToken cancel)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/app/poll")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { device_code = deviceCode }), Encoding.UTF8, "application/json")
                };

                using var response = await App.HttpClient.SendAsync(request, cancel);
                return JsonSerializer.Deserialize<DevicePoll>(await response.Content.ReadAsStringAsync(cancel));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::PollAsync", ex);
                return null;
            }
        }

        public static async Task<bool> WaitForApprovalAsync(DeviceStart start, CancellationToken cancel)
        {
            var deadline = DateTime.UtcNow.AddSeconds(start.ExpiresIn);
            int wait = Math.Max(2, start.Interval);

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(wait), cancel);

                var poll = await PollAsync(start.DeviceCode, cancel);
                if (poll is null)
                    continue;

                if (poll.Status == "ok" && !string.IsNullOrEmpty(poll.Token))
                {
                    StoreToken(poll.Token);
                    App.State.Prop.AccountId = poll.AccountId ?? "";
                    App.State.Prop.AccountName = poll.Name ?? "";
                    App.State.Save();
                    App.Logger.WriteLine(LOG_IDENT, $"Signed in as {DisplayName}");
                    Announce();
                    await RefreshAsync();
                    return true;
                }

                if (poll.Status == "denied" || poll.Status == "expired")
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Signing in ended: {poll.Status}");
                    return false;
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "The code ran out before it was approved");
            return false;
        }

        private static async Task<bool> TryDiscordAppAsync(Action<string>? status, CancellationToken cancel)
        {
            status?.Invoke("Asking Discord...");

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            window.CancelAfter(TimeSpan.FromMinutes(2));

            string? code = await DiscordRpcAuth.RequestCodeAsync(window.Token);
            if (string.IsNullOrEmpty(code))
                return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/app/rpc")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json")
                };

                using var response = await App.HttpClient.SendAsync(request, cancel);
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"The server would not take the Discord code ({(int)response.StatusCode})");
                    return false;
                }

                var poll = JsonSerializer.Deserialize<DevicePoll>(await response.Content.ReadAsStringAsync(cancel));
                if (poll is null || string.IsNullOrEmpty(poll.Token))
                    return false;

                StoreToken(poll.Token);
                App.State.Prop.AccountId = poll.AccountId ?? "";
                App.State.Prop.AccountName = poll.Name ?? "";
                App.State.Save();
                App.Logger.WriteLine(LOG_IDENT, $"Signed in as {DisplayName} through the Discord app");
                Announce();
                await RefreshAsync();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::TryDiscordAppAsync", ex);
                return false;
            }
        }

        public static async Task<bool> SignInAsync(Action<string>? status, CancellationToken cancel)
        {
            if (await TryDiscordAppAsync(status, cancel))
                return true;

            status?.Invoke("Starting...");

            var start = await BeginAsync();
            if (start is null)
            {
                status?.Invoke("Could not reach the server. Check your connection and try again.");
                return false;
            }

            Utilities.ShellExecute(start.VerifyUrlComplete);
            status?.Invoke($"Approve the code {start.UserCode} in the browser window that just opened. Waiting...");

            bool ok = await WaitForApprovalAsync(start, cancel);
            status?.Invoke(ok ? "" : "That was not approved in time. Try again when you are ready.");
            return ok;
        }

        public static async Task<AccountDetails?> RefreshAsync()
        {
            if (!SignedIn)
                return null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{App.ServerBase}/v1/app/me");
                Authorize(request);

                using var response = await App.HttpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    App.Logger.WriteLine(LOG_IDENT, "The server no longer knows this sign-in, clearing it");
                    Forget();
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                    return null;

                var details = JsonSerializer.Deserialize<AccountDetails>(await response.Content.ReadAsStringAsync());
                if (details is null)
                    return null;

                App.State.Prop.AccountId = details.AccountId;
                App.State.Prop.AccountName = details.Name ?? "";
                App.State.Prop.AccountAvatar = details.Avatar ?? "";
                App.State.Save();
                Announce();
                return details;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::RefreshAsync", ex);
                return null;
            }
        }

        public static async Task<string?> ShortLinkAsync(string kind, string name, string code)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/share/new")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { kind, name, code }), Encoding.UTF8, "application/json")
                };

                if (SignedIn)
                    Authorize(request);

                using var response = await App.HttpClient.SendAsync(request);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                    return null;

                return document.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::ShortLinkAsync", ex);
                return null;
            }
        }

        public static Dictionary<string, string> BackupTargets() => new()
        {
            ["Settings.json"] = App.Settings.FileLocation,
            ["FastFlagProfiles.json"] = App.FlagProfiles.FileLocation,
            ["ClientAppSettings.json"] = App.FastFlags.FileLocation,
        };

        public static async Task<int> BackUpNowAsync()
        {
            var files = new Dictionary<string, string>();

            foreach (var (name, file) in BackupTargets())
            {
                if (File.Exists(file))
                    files[name] = await File.ReadAllTextAsync(file);
            }

            if (files.Count == 0)
                return 0;

            return await BackUpAsync(files) ? files.Count : -1;
        }

        public static async Task MaybeAutoBackUpAsync()
        {
            if (!App.Settings.Prop.AutoBackupEnabled || !SignedIn)
                return;

            if (DateTime.TryParse(App.State.Prop.LastAutoBackup, out var last) && DateTime.UtcNow - last.ToUniversalTime() < TimeSpan.FromHours(24))
                return;

            try
            {
                int count = await BackUpNowAsync();

                if (count > 0)
                {
                    App.State.Prop.LastAutoBackup = DateTime.UtcNow.ToString("o");
                    App.State.Save();
                    App.Logger.WriteLine(LOG_IDENT, $"Backed up {count} file(s) on its own");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::MaybeAutoBackUpAsync", ex);
            }
        }

        public static async Task<(string? Problem, string? Url)> PublishToGalleryAsync(string kind, string name, string summary, string code)
        {
            if (!SignedIn)
                return ("Sign in on the Accounts page first.", null);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/gallery/publish")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { kind, name, summary, code }), Encoding.UTF8, "application/json")
                };
                Authorize(request);

                using var response = await App.HttpClient.SendAsync(request);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                if (document.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                    return (null, document.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null);

                return (document.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                    ? reason.GetString()
                    : "The server would not take it.", null);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::PublishToGalleryAsync", ex);
                return ("Could not reach the server.", null);
            }
        }

        public static async Task<List<GalleryItem>> GalleryAsync(string kind, string query)
        {
            var found = new List<GalleryItem>();

            try
            {
                string url = $"{App.ServerBase}/v1/gallery/list?kind={Uri.EscapeDataString(kind)}&q={Uri.EscapeDataString(query ?? "")}";
                using var response = await App.HttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return found;

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!document.RootElement.TryGetProperty("items", out var items))
                    return found;

                foreach (var entry in items.EnumerateArray())
                {
                    found.Add(new GalleryItem
                    {
                        Id = entry.GetProperty("id").GetString() ?? "",
                        Name = entry.GetProperty("name").GetString() ?? "",
                        Summary = entry.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                        Author = entry.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "",
                        Imports = entry.TryGetProperty("imports", out var i) ? i.GetInt32() : 0,
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::GalleryAsync", ex);
            }

            return found;
        }

        public static string? GalleryIdIn(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:phasmastrap\.com/g/|^\s*)([a-z0-9]{4,24})\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            string id = match.Groups[1].Value.ToLowerInvariant();

            if (text.Contains("/g/", StringComparison.OrdinalIgnoreCase))
                return id;

            return id.Length == 7 && !text.Contains('-') ? id : null;
        }

        public static async Task<string?> GalleryTakeAsync(string id)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/gallery/take")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id }), Encoding.UTF8, "application/json")
                };

                using var response = await App.HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::GalleryTakeAsync", ex);
                return null;
            }
        }

        public static async Task<bool> BackUpAsync(Dictionary<string, string> files)
        {
            if (!SignedIn)
                return false;

            try
            {
                var payload = new { machine = Environment.MachineName, version = App.Version, files };

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/backup/put")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                Authorize(request);

                using var response = await App.HttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return true;

                App.Logger.WriteLine(LOG_IDENT, $"Backup was refused: the server answered {(int)response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::BackUpAsync", ex);
                return false;
            }
        }

        public static async Task<Dictionary<string, string>?> RestoreAsync()
        {
            if (!SignedIn)
                return null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{App.ServerBase}/v1/backup/get");
                Authorize(request);

                using var response = await App.HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
                    return null;

                var result = new Dictionary<string, string>();
                foreach (var entry in files.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                        result[entry.Name] = entry.Value.GetString() ?? "";
                }

                return result;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::RestoreAsync", ex);
                return null;
            }
        }

        public static async Task SignOutAsync()
        {
            string? token = Token;
            Forget();

            if (string.IsNullOrEmpty(token))
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/app/signout");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                using var response = await App.HttpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::SignOutAsync", ex);
            }
        }

        public static void Forget()
        {
            StoreToken(null);
            App.State.Prop.AccountAvatar = "";
            App.State.Prop.AccountId = "";
            App.State.Prop.AccountName = "";
            App.State.Save();
            Announce();
        }
    }
}
