using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>How the stats HUD looks and where it sits (Rendering > Overlays > HUD look).</summary>
    internal sealed class HudStyle
    {
        public const string TopLeft = "TopLeft", TopCenter = "TopCenter", TopRight = "TopRight";
        public const string BottomLeft = "BottomLeft", BottomCenter = "BottomCenter", BottomRight = "BottomRight";

        public static readonly string[] Positions = { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight };

        public string Position = TopLeft;
        public int OffsetX = 18, OffsetY = 18;
        public int BackgroundOpacity = 75;          // percent; 0 = no background at all
        public Color Background = Color.FromArgb(12, 13, 16);
        public Color Label = Color.FromArgb(226, 229, 233);
        public Color Value = Color.FromArgb(150, 226, 150);
        public float Scale = 1f;
        public bool OneLine;
        public bool ShowLabels = true;
        public bool Shadow = true;
        public int CornerRadius = 6;

        public static HudStyle Current
        {
            get
            {
                var s = App.Settings.Prop;
                return new HudStyle
                {
                    Position = Array.IndexOf(Positions, s.OverlayHudPosition) >= 0 ? s.OverlayHudPosition : TopLeft,
                    OffsetX = Math.Clamp(s.OverlayHudOffsetX, 0, 4000),
                    OffsetY = Math.Clamp(s.OverlayHudOffsetY, 0, 4000),
                    BackgroundOpacity = Math.Clamp(s.OverlayHudBackgroundOpacity, 0, 100),
                    Background = ParseColor(s.OverlayHudBackgroundColor, Color.FromArgb(12, 13, 16)),
                    Label = ParseColor(s.OverlayHudLabelColor, Color.FromArgb(226, 229, 233)),
                    Value = ParseColor(s.OverlayHudValueColor, Color.FromArgb(150, 226, 150)),
                    Scale = Math.Clamp(s.OverlayHudScale, 50, 300) / 100f,
                    OneLine = s.OverlayHudLayout == "Line",
                    ShowLabels = s.OverlayHudShowLabels,
                    Shadow = s.OverlayHudTextShadow,
                    CornerRadius = Math.Clamp(s.OverlayHudCornerRadius, 0, 24),
                };
            }
        }

        public string Signature =>
            $"{BackgroundOpacity}|{Background.ToArgb()}|{Label.ToArgb()}|{Value.ToArgb()}|{Scale}|{OneLine}|{ShowLabels}|{Shadow}|{CornerRadius}";

        public static Color ParseColor(string? hex, Color fallback)
        {
            try
            {
                string h = (hex ?? "").Trim().TrimStart('#');
                if (h.Length == 6)
                    return Color.FromArgb(Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16));
            }
            catch
            {
            }
            return fallback;
        }

        // where the HUD's top-left corner goes inside an area of the given size
        public (int X, int Y) Place(int areaWidth, int areaHeight, int hudWidth, int hudHeight)
        {
            int x = Position switch
            {
                TopRight or BottomRight => areaWidth - hudWidth - OffsetX,
                TopCenter or BottomCenter => (areaWidth - hudWidth) / 2 + OffsetX,
                _ => OffsetX,
            };
            int y = Position is BottomLeft or BottomCenter or BottomRight ? areaHeight - hudHeight - OffsetY : OffsetY;

            return (Math.Clamp(x, 0, Math.Max(0, areaWidth - hudWidth)), Math.Clamp(y, 0, Math.Max(0, areaHeight - hudHeight)));
        }
    }

    /// <summary>
    /// The stats readout (FPS and friends), drawn with GDI+ into a bitmap and blitted as a GPU
    /// texture by OverlayCompositor. The same RenderBitmap draws the settings page's preview, so
    /// the preview is exactly what appears in game.
    /// </summary>
    internal sealed class OverlayHud
    {
        public int TexWidth { get; private set; } = 1;
        public int TexHeight { get; private set; } = 1;

        private const int LabelCols = 8;

        private ID3D11Device _device = null!;
        private ID3D11Texture2D? _tex;
        private ID3D11ShaderResourceView? _srv;
        private string _last = "";

        public ID3D11ShaderResourceView? Srv => _srv;

        public void Init(ID3D11Device device, int maxRows = 1)
        {
            Dispose();
            _device = device;
        }

        public void Update(ID3D11DeviceContext context, string[] labels, string[] values)
        {
            if (_device is null)
                return;

            HudStyle style = HudStyle.Current;
            int rows = Math.Min(labels.Length, values.Length);
            string signature = string.Join("", labels, 0, rows) + "" + string.Join("", values, 0, rows) + "" + style.Signature;
            if (signature == _last)
                return;
            _last = signature;

            using Bitmap bitmap = RenderBitmap(labels, values, style);

            if (_tex is null || bitmap.Width != TexWidth || bitmap.Height != TexHeight)
            {
                _srv?.Dispose();
                _tex?.Dispose();
                TexWidth = bitmap.Width;
                TexHeight = bitmap.Height;
                _tex = _device.CreateTexture2D(new Texture2DDescription
                {
                    Width = TexWidth,
                    Height = TexHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.Write,
                });
                _srv = _device.CreateShaderResourceView(_tex);
            }

            var locked = bitmap.LockBits(new Rectangle(0, 0, TexWidth, TexHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var mapped = context.Map(_tex, 0, MapMode.WriteDiscard, MapFlags.None);
                try
                {
                    int rowBytes = TexWidth * 4;
                    unsafe
                    {
                        byte* src = (byte*)locked.Scan0;
                        byte* dst = (byte*)mapped.DataPointer;
                        for (int py = 0; py < TexHeight; py++)
                            Buffer.MemoryCopy(src + py * locked.Stride, dst + py * (int)mapped.RowPitch, rowBytes, rowBytes);
                    }
                }
                finally
                {
                    context.Unmap(_tex, 0);
                }
            }
            finally
            {
                bitmap.UnlockBits(locked);
            }
        }

        // the HUD as a picture (straight alpha). Monospaced and padded to fixed columns, so its
        // size doesn't change every second as the numbers do.
        public static Bitmap RenderBitmap(string[] labels, string[] values, HudStyle style)
        {
            int rows = Math.Max(0, Math.Min(labels.Length, values.Length));
            float scale = style.Scale;

            using var font = new Font("Consolas", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };

            float charWidth, lineHeight;
            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
            {
                charWidth = g.MeasureString("00000000", font, PointF.Empty, format).Width / 8f;
                lineHeight = font.GetHeight(g);
            }

            int valueCols = 6;
            for (int i = 0; i < rows; i++)
                valueCols = Math.Max(valueCols, values[i].Length);

            float padX = 9f * scale, padY = 6f * scale, rowGap = 4f * scale, itemGap = 2f * charWidth;

            // one entry per row: (label text, value text, their x positions)
            var pieces = new List<(string Text, float X, float Y, bool IsValue)>();
            float width, height;

            if (style.OneLine)
            {
                float x = padX;
                for (int i = 0; i < rows; i++)
                {
                    if (i > 0)
                        x += itemGap;
                    if (style.ShowLabels)
                    {
                        pieces.Add((labels[i], x, padY, false));
                        x += (labels[i].Length + 1) * charWidth;
                    }
                    string value = values[i].PadLeft(Math.Max(values[i].Length, i == 0 ? 6 : 0));
                    pieces.Add((value, x, padY, true));
                    x += value.Length * charWidth;
                }
                width = x + padX;
                height = padY * 2 + lineHeight;
            }
            else
            {
                float y = padY;
                for (int i = 0; i < rows; i++)
                {
                    float x = padX;
                    if (style.ShowLabels)
                    {
                        string label = labels[i];
                        pieces.Add((label + new string('.', Math.Max(1, LabelCols - label.Length)) + ":", x, y, false));
                        x += (LabelCols + 2) * charWidth;
                    }
                    pieces.Add((values[i], x, y, true));
                    y += lineHeight + (i < rows - 1 ? rowGap : 0);
                }
                width = padX * 2 + ((style.ShowLabels ? LabelCols + 2 : 0) + valueCols) * charWidth;
                height = y + padY;
            }

            var bitmap = new Bitmap(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // no ClearType on a transparent background: it needs an opaque one to blend against
                g.TextRenderingHint = style.BackgroundOpacity >= 100 ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                if (style.BackgroundOpacity > 0)
                {
                    using var background = new SolidBrush(Color.FromArgb(style.BackgroundOpacity * 255 / 100, style.Background));
                    float radius = Math.Min(style.CornerRadius * scale, Math.Min(bitmap.Width, bitmap.Height) / 2f);
                    if (radius < 0.5f)
                    {
                        g.FillRectangle(background, 0, 0, bitmap.Width, bitmap.Height);
                    }
                    else
                    {
                        using var path = RoundedRect(new RectangleF(0, 0, bitmap.Width, bitmap.Height), radius);
                        g.FillPath(background, path);
                    }
                }

                using var labelBrush = new SolidBrush(Color.FromArgb(240, style.Label));
                using var valueBrush = new SolidBrush(style.Value);
                using var shadowBrush = new SolidBrush(Color.FromArgb(style.BackgroundOpacity > 0 ? 110 : 200, 0, 0, 0));
                float shadowOffset = Math.Max(1f, scale);

                foreach (var (text, x, y, isValue) in pieces)
                {
                    if (style.Shadow)
                        g.DrawString(text, font, shadowBrush, x + shadowOffset, y + shadowOffset, format);
                    g.DrawString(text, font, isValue ? valueBrush : labelBrush, x, y, format);
                }
            }

            return bitmap;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            _srv?.Dispose();
            _tex?.Dispose();
            _srv = null;
            _tex = null;
            TexWidth = TexHeight = 1;
            _last = "";
        }
    }
}
