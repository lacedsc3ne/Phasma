using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility
{
    public static class IconFontPatcher
    {
        public sealed class ColorLayer
        {
            public List<List<PointF>> Contours = new();
            public Color Color;
        }

        private const int WorkingSize = 512;

        private static int[] Rasterize(Bitmap image, out int w, out int h)
        {
            w = h = WorkingSize;

            using var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(image, new Rectangle(0, 0, w, h), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }

            BitmapData data = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[w * h];
                for (int y = 0; y < h; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * w, w);
                return pixels;
            }
            finally
            {
                scaled.UnlockBits(data);
            }
        }

        private static bool IsOpaque(int argb) => ((argb >> 24) & 0xFF) >= 128;

        private static bool IsRed(int argb)
        {
            int r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
            return r > 70 && r > g * 1.5 + 10 && r > b * 1.5 + 10;
        }

        private static double Luminance(int argb) =>
            0.2126 * ((argb >> 16) & 0xFF) + 0.7152 * ((argb >> 8) & 0xFF) + 0.0722 * (argb & 0xFF);

        public static List<List<PointF>> Trace(Bitmap image, double tolerance = 1.1)
        {
            int[] pixels = Rasterize(image, out int w, out int h);
            return TraceMask(pixels.Select(IsOpaque).ToArray(), w, h, (double)image.Width / w, (double)image.Height / h, tolerance);
        }

        public static List<ColorLayer> TraceColorLayers(Bitmap image, int greyBands = 4, int redBands = 2, double tolerance = 1.1)
        {
            int[] pixels = Rasterize(image, out int w, out int h);
            double scaleX = (double)image.Width / w, scaleY = (double)image.Height / h;

            var layers = new List<ColorLayer>();

            void AddFamily(Func<int, bool> member, int bands, bool wholeSilhouetteFirst)
            {
                double[] values = pixels.Where(p => IsOpaque(p) && member(p)).Select(Luminance).OrderBy(v => v).ToArray();
                if (values.Length == 0)
                    return;

                for (int band = 0; band < bands; band++)
                {
                    double from = values[(int)((long)values.Length * band / bands)];
                    double to = band + 1 < bands ? values[(int)((long)values.Length * (band + 1) / bands)] : double.MaxValue;

                    long r = 0, g = 0, b = 0, n = 0;
                    foreach (int p in pixels)
                    {
                        if (!IsOpaque(p) || !member(p))
                            continue;

                        double l = Luminance(p);
                        if (l < from || l >= to)
                            continue;

                        r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF; n++;
                    }

                    if (n == 0)
                        continue;

                    bool[] mask = band == 0 && wholeSilhouetteFirst
                        ? pixels.Select(IsOpaque).ToArray()
                        : pixels.Select(p => IsOpaque(p) && member(p) && (band == 0 || Luminance(p) >= from)).ToArray();

                    var contours = TraceMask(mask, w, h, scaleX, scaleY, tolerance);
                    if (contours.Count > 0)
                        layers.Add(new ColorLayer { Contours = contours, Color = Color.FromArgb(255, (int)(r / n), (int)(g / n), (int)(b / n)) });
                }
            }

            AddFamily(p => !IsRed(p), greyBands, wholeSilhouetteFirst: true);
            AddFamily(IsRed, redBands, wholeSilhouetteFirst: false);

            return layers;
        }

        private static List<List<PointF>> TraceMask(bool[] filled, int w, int h, double scaleX, double scaleY, double tolerance)
        {
            bool At(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && filled[y * w + x];

            var edges = new Dictionary<(int, int), List<(int X, int Y)>>();
            void AddEdge(int x1, int y1, int x2, int y2)
            {
                if (!edges.TryGetValue((x1, y1), out var list))
                    edges[(x1, y1)] = list = new List<(int, int)>(2);
                list.Add((x2, y2));
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!filled[y * w + x])
                        continue;

                    if (!At(x, y - 1)) AddEdge(x, y, x + 1, y);
                    if (!At(x + 1, y)) AddEdge(x + 1, y, x + 1, y + 1);
                    if (!At(x, y + 1)) AddEdge(x + 1, y + 1, x, y + 1);
                    if (!At(x - 1, y)) AddEdge(x, y + 1, x, y);
                }
            }

            var contours = new List<List<PointF>>();

            while (edges.Count > 0)
            {
                (int X, int Y) start = edges.Keys.First();
                (int X, int Y) current = start;
                var loop = new List<PointF>();

                while (true)
                {
                    if (!edges.TryGetValue(current, out var outgoing))
                        break;

                    (int X, int Y) next = outgoing[^1];
                    outgoing.RemoveAt(outgoing.Count - 1);
                    if (outgoing.Count == 0)
                        edges.Remove(current);

                    loop.Add(new PointF((float)(current.X * scaleX), (float)(current.Y * scaleY)));
                    current = next;

                    if (current == start)
                        break;
                }

                if (loop.Count < 8)
                    continue;

                if (Math.Abs(SignedArea(loop)) < 6 * scaleX * scaleY * 4)
                    continue;

                List<PointF> simplified = Simplify(loop, tolerance * scaleX);
                if (simplified.Count >= 3)
                    contours.Add(simplified);
            }

            return contours;
        }

        private static double SignedArea(List<PointF> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                PointF a = points[i], b = points[(i + 1) % points.Count];
                area += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return area / 2;
        }

        private static List<PointF> Simplify(List<PointF> points, double tolerance)
        {
            int far = 0;
            double best = -1;
            for (int i = 1; i < points.Count; i++)
            {
                double d = Distance(points[0], points[i]);
                if (d > best) { best = d; far = i; }
            }

            var result = new List<PointF>();
            SimplifyRange(points, 0, far, tolerance, result);
            var second = points.Skip(far).Append(points[0]).ToList();
            SimplifyRange(second, 0, second.Count - 1, tolerance, result);
            return result;
        }

        private static void SimplifyRange(List<PointF> points, int from, int to, double tolerance, List<PointF> output)
        {
            var keep = new bool[points.Count];
            keep[from] = keep[to] = true;

            var stack = new Stack<(int, int)>();
            stack.Push((from, to));

            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                double max = 0;
                int index = -1;

                for (int i = a + 1; i < b; i++)
                {
                    double d = DistanceToSegment(points[i], points[a], points[b]);
                    if (d > max) { max = d; index = i; }
                }

                if (index >= 0 && max > tolerance)
                {
                    keep[index] = true;
                    stack.Push((a, index));
                    stack.Push((index, b));
                }
            }

            for (int i = from; i < to; i++)
                if (keep[i])
                    output.Add(points[i]);
        }

        private static double Distance(PointF a, PointF b) => Math.Sqrt(((double)a.X - b.X) * (a.X - b.X) + ((double)a.Y - b.Y) * (a.Y - b.Y));

        private static double DistanceToSegment(PointF p, PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9)
                return Distance(p, a);

            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
            return Distance(p, new PointF((float)(a.X + t * dx), (float)(a.Y + t * dy)));
        }

        private sealed class Table
        {
            public string Tag = "";
            public byte[] Data = Array.Empty<byte>();
        }

        public static byte[]? ReplaceGlyph(byte[] font, string glyphName, List<List<PointF>> contours, List<ColorLayer>? layers = null, double sizeFactor = 1.12)
        {
            if (font.Length < 12 || contours.Count == 0)
                return null;

            uint sfntVersion = BinaryPrimitives.ReadUInt32BigEndian(font);
            if (sfntVersion != 0x00010000 && sfntVersion != 0x74727565)
                return null;

            int tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            var tables = new List<Table>();

            for (int i = 0; i < tableCount; i++)
            {
                int record = 12 + i * 16;
                string tag = System.Text.Encoding.ASCII.GetString(font, record, 4);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));

                if (offset < 0 || length < 0 || offset + length > font.Length)
                    return null;

                tables.Add(new Table { Tag = tag, Data = font.AsSpan(offset, length).ToArray() });
            }

            Table? Find(string tag) => tables.FirstOrDefault(t => t.Tag == tag);

            Table? head = Find("head"), maxp = Find("maxp"), loca = Find("loca"), glyf = Find("glyf"), post = Find("post"), hhea = Find("hhea"), hmtx = Find("hmtx");
            if (head is null || maxp is null || loca is null || glyf is null || post is null || hhea is null || hmtx is null)
                return null;

            int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(maxp.Data.AsSpan(4));
            bool longLoca = BinaryPrimitives.ReadInt16BigEndian(head.Data.AsSpan(50)) == 1;

            int glyphId = FindGlyph(post.Data, glyphName, glyphCount);
            if (glyphId < 0)
                return null;

            var offsets = new int[glyphCount + 1];
            for (int i = 0; i <= glyphCount; i++)
            {
                offsets[i] = longLoca
                    ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.Data.AsSpan(i * 4))
                    : BinaryPrimitives.ReadUInt16BigEndian(loca.Data.AsSpan(i * 2)) * 2;
            }

            int originalStart = offsets[glyphId], originalLength = offsets[glyphId + 1] - originalStart;
            double centreX, centreY, extent;

            if (originalLength >= 10)
            {
                var g = glyf.Data.AsSpan(originalStart);
                int xMin = BinaryPrimitives.ReadInt16BigEndian(g[2..]), yMin = BinaryPrimitives.ReadInt16BigEndian(g[4..]);
                int xMax = BinaryPrimitives.ReadInt16BigEndian(g[6..]), yMax = BinaryPrimitives.ReadInt16BigEndian(g[8..]);
                centreX = (xMin + xMax) / 2.0;
                centreY = (yMin + yMax) / 2.0;
                extent = Math.Max(xMax - xMin, yMax - yMin);
            }
            else
            {
                int unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head.Data.AsSpan(18));
                centreX = centreY = unitsPerEm / 2.0;
                extent = unitsPerEm * 0.8;
            }

            int metricsCount = BinaryPrimitives.ReadUInt16BigEndian(hhea.Data.AsSpan(34));
            int advance = BinaryPrimitives.ReadUInt16BigEndian(hmtx.Data.AsSpan(Math.Min(glyphId, metricsCount - 1) * 4));
            double target = extent * sizeFactor;
            if (advance > 0)
                target = Math.Min(target, advance);

            RectangleF source = Bounds(contours);

            byte[] newGlyph = BuildGlyph(contours, source, centreX, centreY, target, out int pointCount, out int contourCount, out _);

            bool colour = layers is { Count: > 0 } && Find("COLR") is null && Find("CPAL") is null && glyphCount + layers.Count < 0xFFFF;

            var layerGlyphs = new List<byte[]>();
            var layerBearings = new List<int>();

            if (colour)
            {
                foreach (ColorLayer layer in layers!)
                {
                    layerGlyphs.Add(BuildGlyph(layer.Contours, source, centreX, centreY, target, out int points, out int loops, out int xMin));
                    layerBearings.Add(xMin);
                    pointCount = Math.Max(pointCount, points);
                    contourCount = Math.Max(contourCount, loops);
                }
            }

            int newGlyphCount = glyphCount + layerGlyphs.Count;

            using var glyfStream = new MemoryStream();
            var newOffsets = new uint[newGlyphCount + 1];

            for (int i = 0; i < newGlyphCount; i++)
            {
                newOffsets[i] = (uint)glyfStream.Position;

                if (i == glyphId)
                    glyfStream.Write(newGlyph);
                else if (i >= glyphCount)
                    glyfStream.Write(layerGlyphs[i - glyphCount]);
                else
                    glyfStream.Write(glyf.Data, offsets[i], offsets[i + 1] - offsets[i]);

                while (glyfStream.Position % 4 != 0)
                    glyfStream.WriteByte(0);
            }
            newOffsets[newGlyphCount] = (uint)glyfStream.Position;

            glyf.Data = glyfStream.ToArray();

            loca.Data = new byte[(newGlyphCount + 1) * 4];
            for (int i = 0; i <= newGlyphCount; i++)
                BinaryPrimitives.WriteUInt32BigEndian(loca.Data.AsSpan(i * 4), newOffsets[i]);

            if (colour)
            {
                BinaryPrimitives.WriteUInt16BigEndian(maxp.Data.AsSpan(4), (ushort)newGlyphCount);

                int expectedHmtx = metricsCount * 4 + (glyphCount - metricsCount) * 2;
                if (hmtx.Data.Length < expectedHmtx)
                    return null;

                var grownHmtx = new byte[expectedHmtx + layerGlyphs.Count * 2];
                Array.Copy(hmtx.Data, grownHmtx, expectedHmtx);
                for (int i = 0; i < layerBearings.Count; i++)
                    BinaryPrimitives.WriteInt16BigEndian(grownHmtx.AsSpan(expectedHmtx + i * 2), (short)layerBearings[i]);
                hmtx.Data = grownHmtx;

                post.Data = GrowPost(post.Data, glyphName, layerGlyphs.Count);

                tables.Add(new Table { Tag = "COLR", Data = BuildColr(glyphId, glyphCount, layerGlyphs.Count) });
                tables.Add(new Table { Tag = "CPAL", Data = BuildCpal(layers!.Select(l => l.Color).ToList()) });
            }

            BinaryPrimitives.WriteInt16BigEndian(head.Data.AsSpan(50), 1);

            if (maxp.Data.Length >= 10)
            {
                if (pointCount > BinaryPrimitives.ReadUInt16BigEndian(maxp.Data.AsSpan(6)))
                    BinaryPrimitives.WriteUInt16BigEndian(maxp.Data.AsSpan(6), (ushort)pointCount);
                if (contourCount > BinaryPrimitives.ReadUInt16BigEndian(maxp.Data.AsSpan(8)))
                    BinaryPrimitives.WriteUInt16BigEndian(maxp.Data.AsSpan(8), (ushort)contourCount);
            }

            return Assemble(sfntVersion, tables);
        }

        private static RectangleF Bounds(List<List<PointF>> contours)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (PointF p in contours.SelectMany(c => c))
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        private static byte[] GrowPost(byte[] post, string baseName, int extra)
        {
            if (post.Length < 34 || BinaryPrimitives.ReadUInt32BigEndian(post) != 0x00020000)
                return post;

            int count = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(32));
            int namesStart = 34 + count * 2;

            int existingNames = 0;
            for (int position = namesStart; position < post.Length; position += 1 + post[position])
                existingNames++;

            using var stream = new MemoryStream();
            stream.Write(post, 0, 32);

            Span<byte> two = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(two, (ushort)(count + extra));
            stream.Write(two);

            stream.Write(post, 34, count * 2);
            for (int i = 0; i < extra; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(two, (ushort)(258 + existingNames + i));
                stream.Write(two);
            }

            stream.Write(post, namesStart, post.Length - namesStart);
            for (int i = 0; i < extra; i++)
            {
                byte[] name = System.Text.Encoding.ASCII.GetBytes($"{baseName}.color{i}");
                stream.WriteByte((byte)name.Length);
                stream.Write(name);
            }

            return stream.ToArray();
        }

        private static byte[] BuildColr(int baseGlyph, int firstLayerGlyph, int layerCount)
        {
            const int header = 14;
            var data = new byte[header + 6 + layerCount * 4];

            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), header);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), header + 6);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), (ushort)layerCount);

            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header), (ushort)baseGlyph);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header + 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header + 4), (ushort)layerCount);

            for (int i = 0; i < layerCount; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header + 6 + i * 4), (ushort)(firstLayerGlyph + i));
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header + 8 + i * 4), (ushort)i);
            }

            return data;
        }

        private static byte[] BuildCpal(List<Color> colors)
        {
            const int header = 14;
            var data = new byte[header + colors.Count * 4];

            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)colors.Count);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), (ushort)colors.Count);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), header);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 0);

            for (int i = 0; i < colors.Count; i++)
            {
                data[header + i * 4] = colors[i].B;
                data[header + i * 4 + 1] = colors[i].G;
                data[header + i * 4 + 2] = colors[i].R;
                data[header + i * 4 + 3] = colors[i].A;
            }

            return data;
        }

        private static int FindGlyph(byte[] post, string name, int glyphCount)
        {
            if (post.Length < 34 || BinaryPrimitives.ReadUInt32BigEndian(post) != 0x00020000)
                return -1;

            int count = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(32));
            if (count > glyphCount || 34 + count * 2 > post.Length)
                return -1;

            var names = new List<string>();
            int position = 34 + count * 2;
            while (position < post.Length)
            {
                int length = post[position];
                if (position + 1 + length > post.Length)
                    break;
                names.Add(System.Text.Encoding.Latin1.GetString(post, position + 1, length));
                position += 1 + length;
            }

            for (int glyph = 0; glyph < count; glyph++)
            {
                int index = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(34 + glyph * 2));
                if (index >= 258 && index - 258 < names.Count && names[index - 258] == name)
                    return glyph;
            }

            return -1;
        }

        private static byte[] BuildGlyph(List<List<PointF>> contours, RectangleF source, double centreX, double centreY, double target, out int pointCount, out int contourCount, out int xMinOut)
        {
            double scale = target / Math.Max(source.Width, source.Height);
            double sourceCentreX = (source.Left + source.Right) / 2.0, sourceCentreY = (source.Top + source.Bottom) / 2.0;

            var glyphContours = new List<List<(int X, int Y)>>();

            foreach (List<PointF> contour in contours)
            {
                var points = new List<(int X, int Y)>(contour.Count);

                foreach (PointF p in contour)
                {
                    var point = ((int)Math.Round(centreX + (p.X - sourceCentreX) * scale), (int)Math.Round(centreY - (p.Y - sourceCentreY) * scale));
                    if (points.Count == 0 || points[^1] != point)
                        points.Add(point);
                }

                if (points.Count > 1 && points[0] == points[^1])
                    points.RemoveAt(points.Count - 1);

                points.Reverse();

                if (points.Count >= 3)
                    glyphContours.Add(points);
            }

            pointCount = glyphContours.Sum(c => c.Count);
            contourCount = glyphContours.Count;

            int xMin = glyphContours.SelectMany(c => c).Min(p => p.X), xMax = glyphContours.SelectMany(c => c).Max(p => p.X);
            int yMin = glyphContours.SelectMany(c => c).Min(p => p.Y), yMax = glyphContours.SelectMany(c => c).Max(p => p.Y);
            xMinOut = xMin;

            using var stream = new MemoryStream();
            void WriteInt16(int value)
            {
                Span<byte> buffer = stackalloc byte[2];
                BinaryPrimitives.WriteInt16BigEndian(buffer, (short)value);
                stream.Write(buffer);
            }
            void WriteUInt16(int value)
            {
                Span<byte> buffer = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
                stream.Write(buffer);
            }

            WriteInt16(contourCount);
            WriteInt16(xMin); WriteInt16(yMin); WriteInt16(xMax); WriteInt16(yMax);

            int end = -1;
            foreach (var contour in glyphContours)
            {
                end += contour.Count;
                WriteUInt16(end);
            }

            WriteUInt16(0);

            for (int i = 0; i < pointCount; i++)
                stream.WriteByte(0x01);

            int previous = 0;
            foreach ((int x, int _) in glyphContours.SelectMany(c => c))
            {
                WriteInt16(x - previous);
                previous = x;
            }

            previous = 0;
            foreach ((int _, int y) in glyphContours.SelectMany(c => c))
            {
                WriteInt16(y - previous);
                previous = y;
            }

            return stream.ToArray();
        }

        private static byte[] Assemble(uint sfntVersion, List<Table> tables)
        {
            var directoryOrder = tables.OrderBy(t => t.Tag, StringComparer.Ordinal).ToList();

            int headerLength = 12 + tables.Count * 16;
            var offsets = new Dictionary<Table, int>();
            int position = headerLength;

            foreach (Table table in tables)
            {
                offsets[table] = position;
                position += (table.Data.Length + 3) & ~3;
            }

            byte[] output = new byte[position];

            Table head = tables.First(t => t.Tag == "head");
            BinaryPrimitives.WriteUInt32BigEndian(head.Data.AsSpan(8), 0);

            BinaryPrimitives.WriteUInt32BigEndian(output, sfntVersion);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)tables.Count);

            int entrySelector = (int)Math.Floor(Math.Log2(tables.Count));
            int searchRange = (1 << entrySelector) * 16;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), (ushort)searchRange);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), (ushort)entrySelector);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), (ushort)(tables.Count * 16 - searchRange));

            foreach (Table table in tables)
                table.Data.CopyTo(output, offsets[table]);

            for (int i = 0; i < directoryOrder.Count; i++)
            {
                Table table = directoryOrder[i];
                int record = 12 + i * 16;

                System.Text.Encoding.ASCII.GetBytes(table.Tag).CopyTo(output, record);
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(record + 4), Checksum(output, offsets[table], (table.Data.Length + 3) & ~3));
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(record + 8), (uint)offsets[table]);
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(record + 12), (uint)table.Data.Length);
            }

            uint adjustment = unchecked(0xB1B0AFBA - Checksum(output, 0, output.Length));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offsets[head] + 8), adjustment);

            return output;
        }

        private static uint Checksum(byte[] data, int offset, int length)
        {
            uint sum = 0;
            for (int i = offset; i + 4 <= offset + length; i += 4)
                sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i)));
            return sum;
        }
    }
}
