using System.Net.Http;

namespace PhasmaStrap.Utility
{
    public sealed class AccountNotice
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("detail")]
        public string Detail { get; set; } = "";

        [JsonPropertyName("link")]
        public string Link { get; set; } = "";

        [JsonPropertyName("read")]
        public bool Read { get; set; }
    }

    public sealed class AccountNoticeList
    {
        [JsonPropertyName("items")]
        public List<AccountNotice> Items { get; set; } = new();

        [JsonPropertyName("unread")]
        public int Unread { get; set; }
    }

    public static class AccountNotices
    {
        private const string LOG_IDENT = "AccountNotices";

        private static CancellationTokenSource? _cts;

        public static void Start()
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private static async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (PhasmaAccount.SignedIn)
                        await CheckAsync();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Check failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public static async Task CheckAsync()
        {
            AccountNoticeList? list = await FetchAsync();

            if (list is null || list.Unread == 0)
                return;

            var shown = new List<string>();

            foreach (AccountNotice notice in list.Items.Where(item => !item.Read))
            {
                NotificationCenter.Notify(
                    notice.Title,
                    notice.Detail,
                    NotificationCategory.General,
                    durationSeconds: 8,
                    kind: NotificationKindId.GalleryReview);

                shown.Add(notice.Id);
            }

            if (shown.Count > 0)
                await MarkReadAsync(shown);
        }

        private static async Task<AccountNoticeList?> FetchAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{App.ServerBase}/v1/app/notices");
                PhasmaAccount.Authorize(request);

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return null;

                return JsonSerializer.Deserialize<AccountNoticeList>(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read your notices: {ex.Message}");
                return null;
            }
        }

        private static async Task MarkReadAsync(List<string> ids)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/app/notices/read");
                PhasmaAccount.Authorize(request);
                request.Content = new StringContent(JsonSerializer.Serialize(new { ids }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    App.Logger.WriteLine(LOG_IDENT, $"Marking read answered {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not mark your notices read: {ex.Message}");
            }
        }
    }
}
