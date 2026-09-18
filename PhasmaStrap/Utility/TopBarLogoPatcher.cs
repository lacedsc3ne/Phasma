using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PhasmaStrap.Utility
{
    // Replaces the Roblox logo on the in-game top bar (the round menu button, top left) with the
    // PhasmaStrap mark.
    //
    // That icon is not a file of its own. The current top bar draws the sprite "icons/logo/block"
    // out of three shared spritesheets (1x / 2x / 3x), at offsets listed in a Lua table that ships
    // with the client - and both the sheet numbers and the offsets move with every Roblox update.
    // A static mod file would therefore paint over whatever else lands on that spot in the next
    // version. So this runs at launch instead: it reads the table from the version being started,
    // and draws the logo into exactly that rectangle of exactly those sheets. The older top bar
    // used plain files (textures/ui/TopBar/coloredlogo*.png); those are replaced as well, for
    // clients that still use them.
    //
    // The caller adds the returned paths to the mod manifest, so switching the option off makes
    // the normal "mod file was removed" path restore the original sheets from the packages.
    //
    // No App dependencies, so it can be exercised from a console harness on a copy of the files.
    public static class TopBarLogoPatcher
    {
        public static Action<string>? Log;

        private const string SpriteName = "icons/logo/block";
        private const string MarkerFile = "PhasmaTopBarLogo.json";

        private static readonly Regex SpriteEntry = new(
            @"\[""" + Regex.Escape(SpriteName) + @"""\]\s*=\s*\{\s*ImageRectOffset\s*=\s*Vector2\.new\((\d+),\s*(\d+)\),\s*ImageRectSize\s*=\s*Vector2\.new\((\d+),\s*(\d+)\),\s*ImageSet\s*=\s*""([^""]+)""",
            RegexOptions.Compiled);

        // Returns the files (relative to versionDirectory) that now carry the logo.
        public static List<string> Apply(string versionDirectory, Func<Stream> openLogo)
        {
            var owned = new List<string>();

            try
            {
                using Stream logoStream = openLogo();
                using var source = new Bitmap(logoStream);

                // the logo file has wide transparent margins; in a 36px cell that would leave a tiny
                // mark, so only its visible part is used
                Rectangle visible = VisibleBounds(source);
                using Bitmap logo = source.Clone(visible, PixelFormat.Format32bppArgb);

                Dictionary<string, string> marker = ReadMarker(versionDirectory);

                foreach ((string sheet, Rectangle rect) in FindSprites(versionDirectory))
                    Patch(versionDirectory, sheet, rect, logo, marker, owned);

                // pre-Unibar top bar: the logo is a whole file per scale
                string legacy = Path.Combine(versionDirectory, "content", "textures", "ui", "TopBar");
                foreach (string name in new[] { "coloredlogo.png", "coloredlogo@2x.png", "coloredlogo@3x.png" })
                {
                    string file = Path.Combine(legacy, name);
                    if (File.Exists(file))
                        Patch(versionDirectory, file, Rectangle.Empty, logo, marker, owned);
                }

                WriteMarker(versionDirectory, marker);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not apply the top bar logo: {ex.Message}");
            }

            return owned;
        }

        private static IEnumerable<(string Sheet, Rectangle Rect)> FindSprites(string versionDirectory)
        {
            string root = Path.Combine(versionDirectory, "ExtraContent", "LuaPackages", "Packages", "_Index", "FoundationImages");
            if (!Directory.Exists(root))
            {
                Log?.Invoke("FoundationImages is not where it used to be - only the legacy top bar logo will be replaced");
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dataFile in Directory.GetFiles(root, "*ImageSetData.lua", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(dataFile);

                foreach (Match match in SpriteEntry.Matches(text))
                {
                    string imageSet = match.Groups[5].Value;
                    string? sheet = Directory.GetFiles(root, imageSet + ".png", SearchOption.AllDirectories).FirstOrDefault();

                    if (sheet is null)
                    {
                        Log?.Invoke($"Spritesheet {imageSet} named by {Path.GetFileName(dataFile)} was not found");
                        continue;
                    }

                    var rect = new Rectangle(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value));

                    if (seen.Add($"{sheet}|{rect}"))
                        yield return (sheet, rect);
                }
            }

            if (seen.Count == 0)
                Log?.Invoke($"No {SpriteName} entry found - Roblox may have changed how the top bar icon is stored");
        }

        // rect == Empty means "the whole image"
        private static void Patch(string versionDirectory, string file, Rectangle rect, Bitmap logo, Dictionary<string, string> marker, List<string> owned)
        {
            string relative = Path.GetRelativePath(versionDirectory, file);

            // already carries the logo from an earlier launch of this same version
            if (marker.TryGetValue(relative, out string? patchedHash) && patchedHash == Hash(file))
            {
                owned.Add(relative);
                return;
            }

            Bitmap canvas;
            using (var original = new Bitmap(file))
            {
                // copy out so the file is not held open while it is being replaced
                canvas = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                using Graphics copy = Graphics.FromImage(canvas);
                copy.CompositingMode = CompositingMode.SourceCopy;
                copy.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height));
            }

            using (canvas)
            {
                bool wholeImage = rect.IsEmpty;
                if (wholeImage)
                    rect = new Rectangle(0, 0, canvas.Width, canvas.Height);

                if (rect.Right > canvas.Width || rect.Bottom > canvas.Height)
                {
                    Log?.Invoke($"{relative}: sprite rectangle {rect} is outside the {canvas.Width}x{canvas.Height} sheet - left alone");
                    return;
                }

                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.SetClip(rect);

                    // wipe the Roblox glyph, then draw the logo into the same cell
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (var clear = new SolidBrush(Color.Transparent))
                        g.FillRectangle(clear, rect);

                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;

                    // fit inside the cell, centred, aspect ratio kept, with a sliver of breathing room
                    double scale = Math.Min(rect.Width * 0.94 / logo.Width, rect.Height * 0.94 / logo.Height);
                    int w = Math.Max(1, (int)Math.Round(logo.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(logo.Height * scale));
                    var target = new Rectangle(rect.X + (rect.Width - w) / 2, rect.Y + (rect.Height - h) / 2, w, h);

                    using var attributes = new ImageAttributes();
                    attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe from sampling past the edge
                    g.DrawImage(logo, target, 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
                }

                var info = new FileInfo(file);
                if (info.IsReadOnly)
                    info.IsReadOnly = false;

                canvas.Save(file, ImageFormat.Png);

                Log?.Invoke(wholeImage
                    ? $"Top bar logo written to {relative}"
                    : $"Top bar logo written into {relative} at {rect.X},{rect.Y} ({rect.Width}x{rect.Height})");
            }

            owned.Add(relative);
            marker[relative] = Hash(file);
        }

        // bounding box of everything that is not (nearly) transparent
        private static Rectangle VisibleBounds(Bitmap bitmap)
        {
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] pixels = new byte[stride * bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (pixels[y * stride + x * 4 + 3] <= 12)
                            continue;

                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return right < left || bottom < top
                ? new Rectangle(0, 0, bitmap.Width, bitmap.Height)
                : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private static string Hash(string file)
        {
            using FileStream stream = File.OpenRead(file);
            using var md5 = MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(stream));
        }

        private static Dictionary<string, string> ReadMarker(string versionDirectory)
        {
            try
            {
                string path = Path.Combine(versionDirectory, MarkerFile);
                if (File.Exists(path))
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            }
            catch
            {
            }

            return new Dictionary<string, string>();
        }

        private static void WriteMarker(string versionDirectory, Dictionary<string, string> marker)
        {
            try
            {
                File.WriteAllText(Path.Combine(versionDirectory, MarkerFile), System.Text.Json.JsonSerializer.Serialize(marker));
            }
            catch
            {
            }
        }
    }
}
