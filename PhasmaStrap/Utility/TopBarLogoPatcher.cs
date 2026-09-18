using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace PhasmaStrap.Utility
{
    // Replaces the Roblox logo on the in-game top bar (the round menu button, top left) with the
    // PhasmaStrap mark.
    //
    // That logo is not an image. Every icon on the current top bar is a glyph of the "Builder
    // Icons" font that ships with the client, and the menu button is the glyph named "tilt" - so
    // the mark is traced from the PhasmaStrap logo and swapped into that glyph (IconFontPatcher).
    // The glyph itself is the plain silhouette; on top of it the font gets COLR/CPAL colour layers
    // (the format Roblox's own emoji fonts use) carrying the mark's greys and reds. An engine that
    // ignores colour layers just draws the silhouette, tinted like every other top bar icon.
    // The font gains icons with Roblox updates, so this patches whatever font the version
    // being launched ships rather than dropping in a pre-made one.
    //
    // (An earlier version of this file drew the logo into a FoundationImages spritesheet cell named
    // by a Lua table. That table was a stale leftover pointing at an EMPTY cell - the icon had
    // already moved to the font - and the next Roblox update stopped shipping the Lua at all.)
    //
    // The pre-Unibar top bar used plain files (textures/ui/TopBar/coloredlogo*.png); those are
    // replaced too, in full colour, for clients that still use them.
    //
    // The caller adds the returned paths to the mod manifest, so switching the option off makes
    // the normal "mod file was removed" path restore the originals from the packages.
    //
    // No App dependencies, so it can be exercised from a console harness on a copy of the files.
    public static class TopBarLogoPatcher
    {
        public static Action<string>? Log;

        private const string GlyphName = "tilt";
        private const string MarkerFile = "PhasmaTopBarLogo.json";
        private const string OriginalSuffix = ".phasma-original";

        // bump when what gets written changes (2 = colour layers)
        private const string FormatKey = "#format";
        private const string FormatVersion = "2";

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

                // files patched by an older build of this class get patched again
                if (!marker.TryGetValue(FormatKey, out string? format) || format != FormatVersion)
                    marker.Clear();
                marker[FormatKey] = FormatVersion;

                PatchFonts(versionDirectory, logo, marker, owned);

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

        private static void PatchFonts(string versionDirectory, Bitmap logo, Dictionary<string, string> marker, List<string> owned)
        {
            string root = Path.Combine(versionDirectory, "ExtraContent", "LuaPackages", "Packages", "_Index", "BuilderIcons");
            if (!Directory.Exists(root))
            {
                Log?.Invoke("The Builder Icons font package is not where it used to be - only the legacy top bar logo will be replaced");
                return;
            }

            List<List<PointF>>? outline = null;
            List<IconFontPatcher.ColorLayer>? layers = null;
            int patched = 0;

            foreach (string font in Directory.GetFiles(root, "*.ttf", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(versionDirectory, font);

                // already carries the mark from an earlier launch of this same version
                if (marker.TryGetValue(relative, out string? patchedHash) && patchedHash == Hash(font))
                {
                    owned.Add(relative);
                    patched++;
                    continue;
                }

                // Always start from the untouched font. The new glyph is sized from the ORIGINAL
                // glyph's box; patching an already patched font would measure our own mark instead
                // and grow it a little every time.
                string pristine = font + OriginalSuffix;
                if (!File.Exists(pristine))
                    File.Copy(font, pristine);

                outline ??= IconFontPatcher.Trace(logo);
                layers ??= IconFontPatcher.TraceColorLayers(logo);

                // outline = the plain silhouette every engine can draw; layers = the same mark in
                // its real greys and reds for engines that honour colour fonts
                byte[]? result = IconFontPatcher.ReplaceGlyph(File.ReadAllBytes(pristine), GlyphName, outline, layers);
                if (result is null)
                {
                    Log?.Invoke($"{relative}: no '{GlyphName}' glyph to replace (or not a TrueType font) - left alone");
                    File.Delete(pristine);
                    continue;
                }

                var info = new FileInfo(font);
                if (info.IsReadOnly)
                    info.IsReadOnly = false;

                File.WriteAllBytes(font, result);

                owned.Add(relative);
                marker[relative] = Hash(font);
                patched++;
                Log?.Invoke($"Top bar logo glyph written into {relative}");
            }

            if (patched == 0)
                Log?.Invoke($"No font with a '{GlyphName}' glyph was found - Roblox may have changed how the top bar icon is stored");
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
