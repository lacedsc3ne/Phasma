using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;

namespace PhasmaStrap.Integrations.Overlays
{
    public sealed class CrosshairStyle
    {
        public string Name { get; set; } = "";

        public bool Arms { get; set; } = true;
        public int ArmLength { get; set; } = 8;
        public int ArmThickness { get; set; } = 2;
        public int Gap { get; set; } = 4;
        public bool TStyle { get; set; }
        public int Rotation { get; set; }

        public bool Dot { get; set; }
        public int DotSize { get; set; } = 2;
        public bool DotRound { get; set; }

        public bool Ring { get; set; }
        public int RingRadius { get; set; } = 12;
        public int RingThickness { get; set; } = 2;

        public string Color { get; set; } = "#00FF00";
        public double Opacity { get; set; } = 1.0;

        public bool Outline { get; set; } = true;
        public int OutlineThickness { get; set; } = 1;
        public string OutlineColor { get; set; } = "#000000";

        public CrosshairStyle Clamped() => new()
        {
            Name = (Name ?? "").Trim(),
            Arms = Arms,
            ArmLength = Math.Clamp(ArmLength, 1, 60),
            ArmThickness = Math.Clamp(ArmThickness, 1, 16),
            Gap = Math.Clamp(Gap, -10, 40),
            TStyle = TStyle,
            Rotation = ((Rotation % 90) + 90) % 90,
            Dot = Dot,
            DotSize = Math.Clamp(DotSize, 1, 16),
            DotRound = DotRound,
            Ring = Ring,
            RingRadius = Math.Clamp(RingRadius, 2, 60),
            RingThickness = Math.Clamp(RingThickness, 1, 12),
            Color = NormaliseColor(Color, "#00FF00"),
            Opacity = Math.Clamp(Opacity, 0.05, 1.0),
            Outline = Outline,
            OutlineThickness = Math.Clamp(OutlineThickness, 1, 4),
            OutlineColor = NormaliseColor(OutlineColor, "#000000"),
        };

        public CrosshairStyle Copy() => (CrosshairStyle)MemberwiseClone();

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasVisibleParts => Arms || Dot || Ring;

        [System.Text.Json.Serialization.JsonIgnore]
        public string Signature => JsonSerializer.Serialize(Clamped());

        public static string NormaliseColor(string? value, string fallback)
        {
            string text = (value ?? "").Trim().TrimStart('#');
            if ((text.Length == 6 || text.Length == 8) && text.All(Uri.IsHexDigit))
                return "#" + text.ToUpperInvariant();
            return fallback;
        }

        public static System.Drawing.Color ParseColor(string hex, double opacity)
        {
            string text = NormaliseColor(hex, "#00FF00")[1..];
            uint value = Convert.ToUInt32(text, 16);
            int a = text.Length == 8 ? (int)(value >> 24) : 255;
            int r = (int)((value >> 16) & 0xFF), g = (int)((value >> 8) & 0xFF), b = (int)(value & 0xFF);
            return System.Drawing.Color.FromArgb(Math.Clamp((int)Math.Round(a * opacity), 0, 255), r, g, b);
        }

        private const string CodePrefix = "PHX1-";
        private const string ShortPrefix = "PHX2-";
        private const byte ShortFormat = 1;
        private const int ShortFixedBytes = 20;

        public string ToShareCode()
        {
            App.SendStat("shareCode", "crosshairCreated");

            CrosshairStyle style = Clamped();

            var bytes = new List<byte>
            {
                ShortFormat,
                (byte)((style.Arms ? 1 : 0) | (style.TStyle ? 2 : 0) | (style.Dot ? 4 : 0) | (style.DotRound ? 8 : 0) | (style.Ring ? 16 : 0) | (style.Outline ? 32 : 0)),
                (byte)style.ArmLength, (byte)style.ArmThickness, (byte)(style.Gap + 10), (byte)style.Rotation,
                (byte)style.DotSize, (byte)style.RingRadius, (byte)style.RingThickness, (byte)style.OutlineThickness,
            };

            ushort opacity = (ushort)Math.Round(style.Opacity * 10000);
            bytes.Add((byte)(opacity >> 8));
            bytes.Add((byte)opacity);

            AddColor(bytes, style.Color);
            AddColor(bytes, style.OutlineColor);

            string name = style.Name.Length > 40 ? style.Name[..40] : style.Name;
            bytes.AddRange(Encoding.UTF8.GetBytes(name));

            return ShortPrefix + Convert.ToBase64String(bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void AddColor(List<byte> bytes, string color)
        {
            string hex = NormaliseColor(color, "#00FF00")[1..];
            uint value = Convert.ToUInt32(hex, 16);
            if (hex.Length == 6)
                value |= 0xFF000000;

            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        private static string ReadColor(byte[] bytes, int at) => bytes[at] == 0xFF
            ? $"#{bytes[at + 1]:X2}{bytes[at + 2]:X2}{bytes[at + 3]:X2}"
            : $"#{bytes[at]:X2}{bytes[at + 1]:X2}{bytes[at + 2]:X2}{bytes[at + 3]:X2}";

        public static CrosshairStyle? FromShareCode(string? pasted)
        {
            foreach (string code in PhasmaStrap.Utility.FlagLayers.FindCodes(pasted, ShortPrefix, CodePrefix))
            {
                CrosshairStyle? style = code.StartsWith(ShortPrefix, StringComparison.OrdinalIgnoreCase)
                    ? DecodeShort(code[ShortPrefix.Length..])
                    : DecodeJson(code[CodePrefix.Length..]);

                if (style is not null)
                {
                    App.SendStat("shareCode", "crosshairImported");
                    return style;
                }
            }

            return null;
        }

        private static CrosshairStyle? DecodeShort(string body)
        {
            try
            {
                if (body.Length > 400)
                    return null;

                string base64 = body.Replace('-', '+').Replace('_', '/');
                base64 += (base64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
                byte[] b = Convert.FromBase64String(base64);

                if (b.Length < ShortFixedBytes || b[0] != ShortFormat)
                    return null;

                return new CrosshairStyle
                {
                    Arms = (b[1] & 1) != 0,
                    TStyle = (b[1] & 2) != 0,
                    Dot = (b[1] & 4) != 0,
                    DotRound = (b[1] & 8) != 0,
                    Ring = (b[1] & 16) != 0,
                    Outline = (b[1] & 32) != 0,
                    ArmLength = b[2],
                    ArmThickness = b[3],
                    Gap = b[4] - 10,
                    Rotation = b[5],
                    DotSize = b[6],
                    RingRadius = b[7],
                    RingThickness = b[8],
                    OutlineThickness = b[9],
                    Opacity = ((b[10] << 8) | b[11]) / 10000.0,
                    Color = ReadColor(b, 12),
                    OutlineColor = ReadColor(b, 16),
                    Name = Encoding.UTF8.GetString(b, ShortFixedBytes, b.Length - ShortFixedBytes),
                }.Clamped();
            }
            catch
            {
                return null;
            }
        }

        private static CrosshairStyle? DecodeJson(string body)
        {
            if (body.Length > 2000 || PhasmaStrap.Utility.FlagLayers.Unpack(body, 16384) is not byte[] json)
                return null;

            try
            {
                return JsonSerializer.Deserialize<CrosshairStyle>(json)?.Clamped();
            }
            catch
            {
                return null;
            }
        }

        public static List<CrosshairStyle> BuiltIn() => new()
        {
            new() { Name = "Classic", Arms = true, ArmLength = 8, ArmThickness = 2, Gap = 4, Color = "#00FF00" },
            new() { Name = "Tight cross", Arms = true, ArmLength = 5, ArmThickness = 2, Gap = 1, Color = "#00FFFF" },
            new() { Name = "Cross with dot", Arms = true, ArmLength = 7, ArmThickness = 2, Gap = 5, Dot = true, DotSize = 2, Color = "#FF3B3B" },
            new() { Name = "Dot", Arms = false, Dot = true, DotSize = 4, DotRound = true, Color = "#FFFFFF" },
            new() { Name = "Circle dot", Arms = false, Dot = true, DotSize = 3, DotRound = true, Ring = true, RingRadius = 14, RingThickness = 2, Color = "#FFFFFF" },
            new() { Name = "T", Arms = true, TStyle = true, ArmLength = 9, ArmThickness = 2, Gap = 4, Color = "#FFE066" },
            new() { Name = "X", Arms = true, Rotation = 45, ArmLength = 7, ArmThickness = 2, Gap = 3, Color = "#FF66D9" },
            new() { Name = "Sniper ring", Arms = true, ArmLength = 18, ArmThickness = 1, Gap = 20, Ring = true, RingRadius = 20, RingThickness = 1, Dot = true, DotSize = 1, Color = "#FF3B3B", Outline = false },
        };
    }

    public static class CrosshairRenderer
    {
        public const int MaxRadius = 110;

        public static void Draw(Graphics graphics, CrosshairStyle source, float centreX, float centreY)
        {
            CrosshairStyle style = source.Clamped();

            System.Drawing.Color fill = CrosshairStyle.ParseColor(style.Color, style.Opacity);
            System.Drawing.Color outline = CrosshairStyle.ParseColor(style.OutlineColor, style.Opacity);
            float o = style.Outline ? style.OutlineThickness : 0;

            GraphicsState saved = graphics.Save();

            try
            {
                bool crisp = style.Rotation == 0;

                float half = style.ArmThickness / 2f;
                float cx = centreX + (style.ArmThickness % 2 == 1 ? 0.5f : 0f);
                float cy = centreY + (style.ArmThickness % 2 == 1 ? 0.5f : 0f);

                graphics.TranslateTransform(cx, cy);
                if (style.Rotation != 0)
                    graphics.RotateTransform(style.Rotation);

                var arms = new List<RectangleF>();
                if (style.Arms)
                {
                    float inner = style.Gap, length = style.ArmLength, t = style.ArmThickness;

                    arms.Add(new RectangleF(inner, -half, length, t));
                    arms.Add(new RectangleF(-inner - length, -half, length, t));
                    arms.Add(new RectangleF(-half, inner, t, length));
                    if (!style.TStyle)
                        arms.Add(new RectangleF(-half, -inner - length, t, length));
                }

                float dot = style.DotSize;
                RectangleF dotRect = new(-dot / 2f, -dot / 2f, dot, dot);

                if (o > 0)
                {
                    graphics.SmoothingMode = crisp ? SmoothingMode.None : SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = crisp ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;

                    using var outlineBrush = new SolidBrush(outline);
                    foreach (RectangleF arm in arms)
                        graphics.FillRectangle(outlineBrush, RectangleF.Inflate(arm, o, o));

                    if (style.Dot)
                    {
                        graphics.SmoothingMode = style.DotRound ? SmoothingMode.AntiAlias : graphics.SmoothingMode;
                        if (style.DotRound)
                            graphics.FillEllipse(outlineBrush, RectangleF.Inflate(dotRect, o, o));
                        else
                            graphics.FillRectangle(outlineBrush, RectangleF.Inflate(dotRect, o, o));
                    }

                    if (style.Ring)
                    {
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using var ringOutline = new Pen(outline, style.RingThickness + o * 2);
                        graphics.DrawEllipse(ringOutline, -style.RingRadius, -style.RingRadius, style.RingRadius * 2f, style.RingRadius * 2f);
                    }
                }

                using var fillBrush = new SolidBrush(fill);

                graphics.SmoothingMode = crisp ? SmoothingMode.None : SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = crisp ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                foreach (RectangleF arm in arms)
                    graphics.FillRectangle(fillBrush, arm);

                if (style.Dot)
                {
                    if (style.DotRound)
                    {
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.FillEllipse(fillBrush, dotRect);
                    }
                    else
                    {
                        graphics.FillRectangle(fillBrush, dotRect);
                    }
                }

                if (style.Ring)
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using var ringPen = new Pen(fill, style.RingThickness);
                    graphics.DrawEllipse(ringPen, -style.RingRadius, -style.RingRadius, style.RingRadius * 2f, style.RingRadius * 2f);
                }
            }
            finally
            {
                graphics.Restore(saved);
            }
        }

        public static Bitmap Render(CrosshairStyle style, int size)
        {
            var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            Draw(graphics, style, size / 2, size / 2);
            return bitmap;
        }
    }
}
