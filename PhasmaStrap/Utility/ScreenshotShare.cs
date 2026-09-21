using System.Net.Http;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Utility
{
    public static class ScreenshotShare
    {
        private const string LOG_IDENT = "ScreenshotShare";
        private const int MaxSide = 1920;
        private const long MaxBytes = 2 * 1024 * 1024;

        public static byte[]? Prepare(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);

                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];

                double scale = Math.Min(1.0, Math.Min((double)MaxSide / frame.PixelWidth, (double)MaxSide / frame.PixelHeight));

                BitmapSource sized = scale < 1.0
                    ? new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale))
                    : frame;

                foreach (int quality in new[] { 88, 75, 60, 45 })
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(BitmapFrame.Create(sized));

                    using var output = new MemoryStream();
                    encoder.Save(output);

                    if (output.Length <= MaxBytes)
                        return output.ToArray();
                }

                App.Logger.WriteLine(LOG_IDENT, "That screenshot would not fit under the size limit");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not prepare {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> ShareAsync(string path, string name, string summary)
        {
            if (!PhasmaAccount.SignedIn)
                return "Sign in to your PhasmaStrap account first.";

            byte[]? bytes = Prepare(path);

            if (bytes is null)
                return "That screenshot could not be read, or it is too large even after shrinking.";

            try
            {
                string query = $"?name={Uri.EscapeDataString(name ?? "")}&summary={Uri.EscapeDataString(summary ?? "")}";

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/shots/new{query}");
                PhasmaAccount.Authorize(request);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Shared {Path.GetFileName(path)} ({bytes.Length} bytes)");
                    return null;
                }

                App.Logger.WriteLine(LOG_IDENT, $"The gallery refused it ({(int)response.StatusCode}): {text}");

                try
                {
                    using JsonDocument document = JsonDocument.Parse(text);
                    return document.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() : "The gallery would not take it.";
                }
                catch
                {
                    return "The gallery would not take it.";
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Sharing failed: {ex.Message}");
                return ex.Message;
            }
        }
    }
}
