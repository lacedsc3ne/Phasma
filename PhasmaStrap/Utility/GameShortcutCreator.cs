using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Utility
{
    internal static class GameShortcutCreator
    {
        private const string LOG_IDENT = "GameShortcutCreator";

        private static readonly Regex PlaceIdInUrl = new(@"roblox\.com/(?:[a-z-]+/)?games/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

        public static string IconCacheFolder => Path.Combine(Paths.Base, "GameShortcutIcons");

        public sealed record Result(bool Success, string Message);

        public static async Task<Result> CreateAsync(string input, string folder)
        {
            if (!TryParsePlaceId(input, out long placeId))
                return new Result(false, "Enter a valid place ID or roblox.com/games/... link.");

            try
            {
                string universeJson = await App.HttpClient.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
                using var universeDoc = JsonDocument.Parse(universeJson);
                if (!universeDoc.RootElement.TryGetProperty("universeId", out var universeValue) || !universeValue.TryGetInt64(out long universeId) || universeId <= 0)
                    return new Result(false, "Could not resolve that place to a game (it may not exist).");

                await UniverseDetails.FetchSingle(universeId);
                UniverseDetails? details = UniverseDetails.LoadFromCache(universeId);

                if (details is null || string.IsNullOrWhiteSpace(details.Thumbnail?.ImageUrl))
                    return new Result(false, "Could not fetch that game's details or icon from Roblox.");

                string name = string.IsNullOrWhiteSpace(details.Data.Name) ? $"Game {placeId}" : details.Data.Name;
                string icoPath = await EnsureIconAsync(placeId, details.Thumbnail.ImageUrl!);

                string sanitizedName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                string lnkPath = Path.Combine(folder, $"{sanitizedName}.lnk");
                string exeArgs = RobloxLaunch.DeepLink(placeId);

                ShellLink.Shortcut.CreateShortcut(Paths.Application, exeArgs, icoPath, 0).WriteToFile(lnkPath);

                App.Logger.WriteLine(LOG_IDENT, $"Created game shortcut for place {placeId} ({name}) at {lnkPath}");
                return new Result(true, $"Created a shortcut for \"{name}\".");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return new Result(false, $"Failed to create the shortcut: {ex.Message}");
            }
        }

        private static bool TryParsePlaceId(string input, out long placeId)
        {
            placeId = 0;
            input = input.Trim();

            if (input.Length == 0)
                return false;

            if (long.TryParse(input, out placeId))
                return placeId > 0;

            Match match = PlaceIdInUrl.Match(input);
            if (match.Success && long.TryParse(match.Groups[1].Value, out placeId))
                return placeId > 0;

            return false;
        }

        private static async Task<string> EnsureIconAsync(long placeId, string imageUrl)
        {
            Directory.CreateDirectory(IconCacheFolder);
            string icoPath = Path.Combine(IconCacheFolder, $"{placeId}.ico");

            if (File.Exists(icoPath) && CountsAllSizes(icoPath))
                return icoPath;

            using HttpResponseMessage response = await App.HttpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            byte[] pngBytes = await Http.ReadBytesBoundedAsync(response.Content, 8 * 1024 * 1024);

            using var pngStream = new MemoryStream(pngBytes);
            using var bitmap = new Bitmap(pngStream);

            WriteIcon(bitmap, icoPath);

            return icoPath;
        }

        private static bool CountsAllSizes(string icoPath)
        {
            try
            {
                byte[] header = new byte[6];

                using var stream = File.OpenRead(icoPath);

                if (stream.Read(header, 0, header.Length) != header.Length)
                    return false;

                return BitConverter.ToUInt16(header, 4) >= IconSizes.Length;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteIcon(Bitmap source, string icoPath)
        {
            var frames = new List<(int Size, byte[] Png)>(IconSizes.Length);

            foreach (int size in IconSizes)
            {
                using var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);

                using (var graphics = Graphics.FromImage(scaled))
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(0, 0, size, size));
                }

                using var buffer = new MemoryStream();
                scaled.Save(buffer, ImageFormat.Png);
                frames.Add((size, buffer.ToArray()));
            }

            using var fileStream = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fileStream);

            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)frames.Count);

            int offset = 6 + (16 * frames.Count);

            foreach ((int size, byte[] png) in frames)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(png.Length);
                writer.Write(offset);

                offset += png.Length;
            }

            foreach ((_, byte[] png) in frames)
                writer.Write(png);
        }
    }
}
