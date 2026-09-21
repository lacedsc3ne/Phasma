namespace PhasmaStrap.Utility
{
    public static class RobloxLaunch
    {
        private const string LOG_IDENT = "RobloxLaunch";

        public static string DeepLink(long placeId, string? jobId = null, string? accessCode = null, string? linkCode = null, string? launchData = null)
        {
            var uri = new StringBuilder($"roblox://experiences/start?placeId={placeId}");

            if (!string.IsNullOrEmpty(accessCode))
                uri.Append($"&accessCode={Uri.EscapeDataString(accessCode)}");
            else if (!string.IsNullOrEmpty(linkCode))
                uri.Append($"&linkCode={Uri.EscapeDataString(linkCode)}");
            else if (!string.IsNullOrEmpty(jobId))
                uri.Append($"&gameInstanceId={jobId}");

            if (!string.IsNullOrEmpty(launchData))
                uri.Append($"&launchData={Uri.EscapeDataString(launchData)}");

            return uri.ToString();
        }

        public static bool Launch(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return false;

            try
            {
                Process.Start(Paths.Process, $"-player \"{uri}\"");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not launch {uri}: {ex.Message}");
                return false;
            }
        }

        public static bool Join(long placeId, string? jobId = null) => Launch(DeepLink(placeId, jobId));
    }
}
