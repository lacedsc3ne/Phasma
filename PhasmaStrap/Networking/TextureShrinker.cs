using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Networking
{
    public static class TextureShrinker
    {
        public sealed class Outcome
        {
            public byte[] Body = Array.Empty<byte>();
            public int FromWidth, FromHeight, ToWidth, ToHeight;
            public string Format = "";
        }

        public static string Sniff(byte[] data)
        {
            if (data.Length < 12)
                return "";

            if (data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return "png";
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "jpeg";
            if (data[0] == 'B' && data[1] == 'M') return "bmp";
            if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8') return "gif";
            if ((data[0] == 'I' && data[1] == 'I' && data[2] == 42 && data[3] == 0) || (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && data[3] == 42)) return "tiff";

            return "";
        }

        public static Outcome? Shrink(byte[] data, int maxDimension)
        {
            string format = Sniff(data);
            if (format.Length == 0 || maxDimension < 16)
                return null;

            try
            {
                BitmapFrame frame;
                using (var input = new MemoryStream(data, writable: false))
                {
                    var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

                    if (decoder.Frames.Count != 1)
                        return null;

                    frame = decoder.Frames[0];
                }

                int width = frame.PixelWidth, height = frame.PixelHeight;
                int longest = Math.Max(width, height);
                if (longest <= maxDimension || width < 2 || height < 2)
                    return null;

                double scale = (double)maxDimension / longest;
                int newWidth = Math.Max(1, (int)Math.Round(width * scale)), newHeight = Math.Max(1, (int)Math.Round(height * scale));

                BitmapSource source = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);

                var scaled = new TransformedBitmap(source, new ScaleTransform((double)newWidth / width, (double)newHeight / height));
                scaled.Freeze();

                newWidth = scaled.PixelWidth;
                newHeight = scaled.PixelHeight;

                bool hasAlpha = frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32 || frame.Format == PixelFormats.Rgba64
                    || frame.Format == PixelFormats.Prgba64 || frame.Format == PixelFormats.Indexed8 || frame.Format == PixelFormats.Indexed4 || format == "gif";

                BitmapEncoder encoder = format == "jpeg" && !hasAlpha
                    ? new JpegBitmapEncoder { QualityLevel = 88 }
                    : new PngBitmapEncoder();

                BitmapSource output = hasAlpha || encoder is PngBitmapEncoder
                    ? new FormatConvertedBitmap(scaled, hasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24, null, 0)
                    : new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0);

                encoder.Frames.Add(BitmapFrame.Create(output));

                using var result = new MemoryStream();
                encoder.Save(result);

                if (result.Length >= data.Length)
                    return null;

                return new Outcome { Body = result.ToArray(), FromWidth = width, FromHeight = height, ToWidth = newWidth, ToHeight = newHeight, Format = encoder is JpegBitmapEncoder ? "jpeg" : "png" };
            }
            catch
            {
                return null;
            }
        }
    }
}
