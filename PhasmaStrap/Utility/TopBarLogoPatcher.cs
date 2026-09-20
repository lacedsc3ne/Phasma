using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace PhasmaStrap.Utility
{
    public static class TopBarLogoPatcher
    {
        public static Action<string>? Log;

        private const string GlyphName = "tilt";
        private const string MarkerFile = "PhasmaTopBarLogo.json";
        private const string OriginalSuffix = ".phasma-original";

        private const string FormatKey = "#format";
        private const string FormatVersion = "2";

        public static List<string> Apply(string versionDirectory, Func<Stream> openLogo)
        {
            var owned = new List<string>();

            try
            {
                using Stream logoStream = openLogo();
                using var source = new Bitmap(logoStream);

                Rectangle visible = VisibleBounds(source);
                using Bitmap logo = source.Clone(visible, PixelFormat.Format32bppArgb);

                Dictionary<string, string> marker = ReadMarker(versionDirectory);

                if (!marker.TryGetValue(FormatKey, out string? format) || format != FormatVersion)
                    marker.Clear();
                marker[FormatKey] = FormatVersion;

                PatchFonts(versionDirectory, logo, marker, owned);

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

                if (marker.TryGetValue(relative, out string? patchedHash) && patchedHash == Hash(font))
                {
                    owned.Add(relative);
                    patched++;
                    continue;
                }

                string pristine = font + OriginalSuffix;
                if (!File.Exists(pristine))
                    File.Copy(font, pristine);

                outline ??= IconFontPatcher.Trace(logo);
                layers ??= IconFontPatcher.TraceColorLayers(logo);

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

        private static void Patch(string versionDirectory, string file, Rectangle rect, Bitmap logo, Dictionary<string, string> marker, List<string> owned)
        {
            string relative = Path.GetRelativePath(versionDirectory, file);

            if (marker.TryGetValue(relative, out string? patchedHash) && patchedHash == Hash(file))
            {
                owned.Add(relative);
                return;
            }

            Bitmap canvas;
            using (var original = new Bitmap(file))
            {
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

                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (var clear = new SolidBrush(Color.Transparent))
                        g.FillRectangle(clear, rect);

                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;

                    double scale = Math.Min(rect.Width * 0.94 / logo.Width, rect.Height * 0.94 / logo.Height);
                    int w = Math.Max(1, (int)Math.Round(logo.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(logo.Height * scale));
                    var target = new Rectangle(rect.X + (rect.Width - w) / 2, rect.Y + (rect.Height - h) / 2, w, h);

                    using var attributes = new ImageAttributes();
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
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
