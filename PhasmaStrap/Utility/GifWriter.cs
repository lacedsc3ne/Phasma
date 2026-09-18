namespace PhasmaStrap.Utility
{
    public sealed class GifExportOptions
    {
        // the GIF is scaled down to this width (never up); 0 = keep the clip's size
        public int MaxWidth = 640;

        public int Fps = 15;

        // ordered dithering: smoother skies and gradients, noticeably bigger file
        public bool Dither;
    }

    // Animated GIF encoder for the clip editor's "Export GIF".
    //
    // WPF's GifBitmapEncoder cannot write frame delays or a loop count, so this writes the file
    // itself. Two things keep the result small enough to share:
    //   - every frame gets its own 255-colour palette (median cut over a 15-bit histogram), built
    //     only from the pixels that frame actually draws;
    //   - a pixel that has not visibly changed since the previous frame is written as transparent
    //     and the frame is cut down to the rectangle that did change. LZW turns those long runs of
    //     one index into almost nothing, which is most of the saving on footage with a HUD or a
    //     still camera.
    //
    // Frames are top-down 32-bit BGRA, all of the size given to the constructor.
    // No App/WPF dependencies, so it can be exercised from a console harness.
    public sealed class GifWriter : IDisposable
    {
        private const int TransparentIndex = 255;
        private const int MaxColors = 255;

        // per channel: below this a pixel counts as "the same as last frame" (H.264 noise sits
        // around 2-4); and how far what is on screen may drift from the source before it is redrawn
        private const int SameThreshold = 5;
        private const int DriftThreshold = 20;

        private static readonly int[] Bayer8 =
        {
             0, 48, 12, 60,  3, 51, 15, 63,
            32, 16, 44, 28, 35, 19, 47, 31,
             8, 56,  4, 52, 11, 59,  7, 55,
            40, 24, 36, 20, 43, 27, 39, 23,
             2, 50, 14, 62,  1, 49, 13, 61,
            34, 18, 46, 30, 33, 17, 45, 29,
            10, 58,  6, 54,  9, 57,  5, 53,
            42, 26, 38, 22, 41, 25, 37, 21,
        };

        private readonly Stream _output;
        private readonly int _width, _height;
        private readonly bool _dither;

        private readonly byte[] _previous;  // last source frame (BGRA)
        private readonly byte[] _shown;     // what a viewer has on screen right now (BGRA)
        private readonly bool[] _changed;
        private readonly byte[] _indices;
        private bool _first = true;

        // 15-bit histogram, reused between frames
        private readonly int[] _count = new int[32768];
        private readonly long[] _sumR = new long[32768], _sumG = new long[32768], _sumB = new long[32768];
        private readonly short[] _nearest = new short[32768];
        private readonly List<int> _usedBins = new();

        private readonly ushort[] _trie = new ushort[4096 * 256];

        public int FrameCount { get; private set; }

        public GifWriter(Stream output, int width, int height, bool dither)
        {
            if (width < 1 || height < 1 || width > 65535 || height > 65535)
                throw new ArgumentOutOfRangeException(nameof(width), "GIF dimensions have to be between 1 and 65535.");

            _output = output;
            _width = width;
            _height = height;
            _dither = dither;

            _previous = new byte[width * height * 4];
            _shown = new byte[width * height * 4];
            _changed = new bool[width * height];
            _indices = new byte[width * height];

            // header + logical screen (no global colour table)
            Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
            WriteU16(width);
            WriteU16(height);
            _output.WriteByte(0x70);
            _output.WriteByte(0);
            _output.WriteByte(0);

            // loop forever
            Write(new byte[] { 0x21, 0xFF, 0x0B });
            Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        public void AddFrame(byte[] bgra, int delayCentiseconds)
        {
            if (bgra.Length < _width * _height * 4)
                throw new ArgumentException("The frame is smaller than the GIF.", nameof(bgra));

            // ---- which pixels need drawing, and the rectangle they sit in
            int left = _width, top = _height, right = -1, bottom = -1;

            for (int y = 0, p = 0, i = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++, p++, i += 4)
                {
                    bool changed = _first
                        || Math.Abs(bgra[i] - _previous[i]) > SameThreshold
                        || Math.Abs(bgra[i + 1] - _previous[i + 1]) > SameThreshold
                        || Math.Abs(bgra[i + 2] - _previous[i + 2]) > SameThreshold
                        || Math.Abs(bgra[i] - _shown[i]) > DriftThreshold
                        || Math.Abs(bgra[i + 1] - _shown[i + 1]) > DriftThreshold
                        || Math.Abs(bgra[i + 2] - _shown[i + 2]) > DriftThreshold;

                    _changed[p] = changed;

                    if (changed)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
            }

            Buffer.BlockCopy(bgra, 0, _previous, 0, _width * _height * 4);

            byte[] palette = new byte[768];

            if (right < 0)
            {
                // nothing moved: a single transparent pixel just carries the delay
                left = top = right = bottom = 0;
                _indices[0] = TransparentIndex;
            }
            else
            {
                BuildPalette(bgra, palette, out int colors);
                MapPixels(bgra, palette, colors, left, top, right, bottom);
            }

            int w = right - left + 1, h = bottom - top + 1;

            // graphic control: leave the frame in place, index 255 is transparent (the first frame
            // has nothing under it, so it draws everything)
            Write(new byte[] { 0x21, 0xF9, 0x04, (byte)(_first ? 0x04 : 0x05) });
            WriteU16(Math.Clamp(delayCentiseconds, 2, 65535));
            _output.WriteByte(TransparentIndex);
            _output.WriteByte(0);

            // image descriptor with a 256 entry local colour table
            _output.WriteByte(0x2C);
            WriteU16(left);
            WriteU16(top);
            WriteU16(w);
            WriteU16(h);
            _output.WriteByte(0x87);
            _output.Write(palette, 0, palette.Length);

            WriteLzw(_indices, w * h);

            _first = false;
            FrameCount++;
        }

        public void Dispose()
        {
            _output.WriteByte(0x3B);
            _output.Flush();
        }

        // ------------------------------------------------------------------ palette

        private sealed class Box
        {
            public int Start, Length;          // range in _usedBins
            public int MinR, MaxR, MinG, MaxG, MinB, MaxB;
            public long Pixels;
        }

        private void BuildPalette(byte[] bgra, byte[] palette, out int colors)
        {
            foreach (int bin in _usedBins)
            {
                _count[bin] = 0;
                _sumR[bin] = _sumG[bin] = _sumB[bin] = 0;
            }
            _usedBins.Clear();

            for (int p = 0, i = 0; p < _changed.Length; p++, i += 4)
            {
                if (!_changed[p])
                    continue;

                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);

                if (_count[bin]++ == 0)
                    _usedBins.Add(bin);

                _sumR[bin] += r;
                _sumG[bin] += g;
                _sumB[bin] += b;
            }

            var boxes = new List<Box> { Shrink(new Box { Start = 0, Length = _usedBins.Count }) };

            while (boxes.Count < MaxColors)
            {
                // split the box that matters most: many pixels spread over a wide range
                Box? target = null;
                double best = 0;
                foreach (Box box in boxes)
                {
                    if (box.Length < 2)
                        continue;

                    int range = Math.Max(box.MaxR - box.MinR, Math.Max(box.MaxG - box.MinG, box.MaxB - box.MinB)) + 1;
                    double score = range * Math.Sqrt(box.Pixels);
                    if (score > best)
                    {
                        best = score;
                        target = box;
                    }
                }

                if (target is null)
                    break;

                boxes.Add(Split(target));
            }

            colors = boxes.Count;
            for (int n = 0; n < boxes.Count; n++)
            {
                Box box = boxes[n];
                long r = 0, g = 0, b = 0, pixels = 0;
                for (int k = box.Start; k < box.Start + box.Length; k++)
                {
                    int bin = _usedBins[k];
                    r += _sumR[bin];
                    g += _sumG[bin];
                    b += _sumB[bin];
                    pixels += _count[bin];
                }

                pixels = Math.Max(1, pixels);
                palette[n * 3] = (byte)(r / pixels);
                palette[n * 3 + 1] = (byte)(g / pixels);
                palette[n * 3 + 2] = (byte)(b / pixels);
            }
        }

        private Box Shrink(Box box)
        {
            box.MinR = box.MinG = box.MinB = 31;
            box.MaxR = box.MaxG = box.MaxB = 0;
            box.Pixels = 0;

            for (int k = box.Start; k < box.Start + box.Length; k++)
            {
                int bin = _usedBins[k];
                int r = bin >> 10, g = (bin >> 5) & 31, b = bin & 31;

                if (r < box.MinR) box.MinR = r;
                if (r > box.MaxR) box.MaxR = r;
                if (g < box.MinG) box.MinG = g;
                if (g > box.MaxG) box.MaxG = g;
                if (b < box.MinB) box.MinB = b;
                if (b > box.MaxB) box.MaxB = b;

                box.Pixels += _count[bin];
            }

            return box;
        }

        // cuts `box` at the pixel-count median of its longest axis; returns the new upper half
        private Box Split(Box box)
        {
            int rangeR = box.MaxR - box.MinR, rangeG = box.MaxG - box.MinG, rangeB = box.MaxB - box.MinB;

            // green first on ties: the eye is most sensitive to it
            int shift = rangeG >= rangeR && rangeG >= rangeB ? 5 : rangeR >= rangeB ? 10 : 0;

            _usedBins.Sort(box.Start, box.Length, Comparer<int>.Create((a, b) => ((a >> shift) & 31) - ((b >> shift) & 31)));

            long half = box.Pixels / 2, running = 0;
            int cut = box.Start + 1;
            for (int k = box.Start; k < box.Start + box.Length - 1; k++)
            {
                running += _count[_usedBins[k]];
                cut = k + 1;
                if (running >= half)
                    break;
            }

            var upper = new Box { Start = cut, Length = box.Start + box.Length - cut };
            box.Length = cut - box.Start;

            Shrink(box);
            return Shrink(upper);
        }

        private void MapPixels(byte[] bgra, byte[] palette, int colors, int left, int top, int right, int bottom)
        {
            Array.Fill(_nearest, (short)-1);

            int o = 0;
            for (int y = top; y <= bottom; y++)
            {
                int p = y * _width + left;
                int i = p * 4;

                for (int x = left; x <= right; x++, p++, i += 4, o++)
                {
                    if (!_changed[p])
                    {
                        _indices[o] = TransparentIndex;
                        continue;
                    }

                    int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];

                    if (_dither)
                    {
                        // +-8: about the distance between neighbouring palette entries in a gradient
                        int nudge = (Bayer8[((y & 7) << 3) | (x & 7)] >> 2) - 8;
                        r = Math.Clamp(r + nudge, 0, 255);
                        g = Math.Clamp(g + nudge, 0, 255);
                        b = Math.Clamp(b + nudge, 0, 255);
                    }

                    int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                    int index = _nearest[bin];

                    if (index < 0)
                    {
                        int cr = (r & 0xF8) | 4, cg = (g & 0xF8) | 4, cb = (b & 0xF8) | 4;
                        int bestDistance = int.MaxValue;

                        for (int n = 0; n < colors; n++)
                        {
                            int dr = palette[n * 3] - cr, dg = palette[n * 3 + 1] - cg, db = palette[n * 3 + 2] - cb;
                            int distance = dr * dr * 3 + dg * dg * 6 + db * db;
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                index = n;
                            }
                        }

                        _nearest[bin] = (short)index;
                    }

                    _indices[o] = (byte)index;

                    _shown[i] = palette[index * 3 + 2];
                    _shown[i + 1] = palette[index * 3 + 1];
                    _shown[i + 2] = palette[index * 3];
                }
            }
        }

        // ------------------------------------------------------------------ LZW

        private readonly byte[] _block = new byte[255];
        private int _blockLength;
        private uint _bits;
        private int _bitCount;

        private void WriteLzw(byte[] indices, int count)
        {
            const int MinCodeSize = 8;
            const int ClearCode = 1 << MinCodeSize;
            const int EndCode = ClearCode + 1;

            _output.WriteByte(MinCodeSize);

            _blockLength = 0;
            _bits = 0;
            _bitCount = 0;

            int codeSize = MinCodeSize + 1;
            int next = EndCode + 1;

            Emit(ClearCode, codeSize);

            int prefix = indices[0];

            for (int n = 1; n < count; n++)
            {
                int c = indices[n];
                int slot = (prefix << 8) | c;
                int existing = _trie[slot];

                if (existing != 0)
                {
                    prefix = existing;
                    continue;
                }

                Emit(prefix, codeSize);

                // the decoder is one table entry behind, so it widens its codes exactly here too
                if (next >= (1 << codeSize) && codeSize < 12)
                    codeSize++;

                if (next < 4096)
                {
                    _trie[slot] = (ushort)next++;
                }
                else
                {
                    Emit(ClearCode, codeSize);
                    Array.Clear(_trie, 0, _trie.Length);
                    codeSize = MinCodeSize + 1;
                    next = EndCode + 1;
                }

                prefix = c;
            }

            Emit(prefix, codeSize);
            if (next >= (1 << codeSize) && codeSize < 12)
                codeSize++;

            Emit(EndCode, codeSize);

            if (_bitCount > 0)
                PutByte((byte)_bits);

            FlushBlock();
            _output.WriteByte(0);

            Array.Clear(_trie, 0, Math.Min(_trie.Length, next * 256));
        }

        private void Emit(int code, int size)
        {
            _bits |= (uint)code << _bitCount;
            _bitCount += size;

            while (_bitCount >= 8)
            {
                PutByte((byte)_bits);
                _bits >>= 8;
                _bitCount -= 8;
            }
        }

        private void PutByte(byte value)
        {
            _block[_blockLength++] = value;
            if (_blockLength == 255)
                FlushBlock();
        }

        private void FlushBlock()
        {
            if (_blockLength == 0)
                return;

            _output.WriteByte((byte)_blockLength);
            _output.Write(_block, 0, _blockLength);
            _blockLength = 0;
        }

        private void Write(byte[] bytes) => _output.Write(bytes, 0, bytes.Length);

        private void WriteU16(int value)
        {
            _output.WriteByte((byte)value);
            _output.WriteByte((byte)(value >> 8));
        }
    }
}
