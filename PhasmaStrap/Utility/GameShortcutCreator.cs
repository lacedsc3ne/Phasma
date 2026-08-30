using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Utility
{
    // Creates a desktop shortcut that deep-links straight into one specific game, using that
    // game's own live icon rather than PhasmaStrap's icon - distinct from the generic app/player/
    // studio shortcuts on ShortcutsPage, which all point at the bootstrapper itself. Icon files are
    // cached under Paths.Base by place ID so re-creating the same game's shortcut doesn't re-download.
    internal static class GameShortcutCreator
    {
        private const string LOG_IDENT = "GameShortcutCreator";

        private static readonly Regex PlaceIdInUrl = new(@"roblox\.com/(?:[a-z-]+/)?games/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static string IconCacheFolder => Path.Combine(Paths.Base, "GameShortcutIcons");

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
                string exeArgs = $"roblox://experiences/start?placeId={placeId}";

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

            if (File.Exists(icoPath))
                return icoPath;

            using HttpResponseMessage response = await App.HttpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            byte[] pngBytes = await Http.ReadBytesBoundedAsync(response.Content, 8 * 1024 * 1024);

            using var pngStream = new MemoryStream(pngBytes);
            using var bitmap = new Bitmap(pngStream);

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using Icon icon = Icon.FromHandle(hIcon);
                using var fileStream = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
                icon.Save(fileStream);
            }
            finally
            {
                DestroyIcon(hIcon);
            }

            return icoPath;
        }
    }
}
