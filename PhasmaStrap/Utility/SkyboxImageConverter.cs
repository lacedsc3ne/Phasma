using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Backing logic for the "Skybox Manager" on ModsPage's Preset Mod tab. Ported from
    /// Voidstrap's SkyboxImageConverter, trimmed to the parts that don't need
    /// SixLabors.ImageSharp (PhasmaStrap doesn't reference it) - image decode/resize/crop uses
    /// WPF's own imaging APIs instead, which is exactly what Voidstrap's own converter already
    /// fell back to for formats ImageSharp couldn't decode. Faces are written directly under
    /// <see cref="Paths.CustomSkybox"/> (inside Modifications), so no separate apply/remove step
    /// is needed in Bootstrapper - they flow through the existing flat mod-copy pipeline exactly
    /// like the custom font or custom cursor set.
    ///
    /// Deliberately NOT ported: Voidstrap's remote "skybox pack" browser/downloader (a GitHub-hosted
    /// asset repo it owns). PhasmaStrap has no equivalent hosted pack source, and faking a picker
    /// with no real packs behind it would be decorative, so only the custom (user-supplied) skybox
    /// path is implemented here.
    /// </summary>
    public static class SkyboxImageConverter
    {
        // sky512_<face>.tex, in the order Header/OptionControl rows are shown in the UI
        public static readonly (string FaceName, string FileName)[] Faces =
        {
            ("Back",  "sky512_bk.tex"),
            ("Down",  "sky512_dn.tex"),
            ("Front", "sky512_ft.tex"),
            ("Left",  "sky512_lf.tex"),
            ("Right", "sky512_rt.tex"),
            ("Up",    "sky512_up.tex")
        };

        private const int FaceSize = 512;
        private const long MaximumInputBytes = 67_108_864L;

        public static bool HasCustomPack() => IsValidPackDirectory(Paths.CustomSkybox);

        public static bool IsValidPackDirectory(string directory)
        {
            try
            {
                return Faces.All(face => IsValidFaceFile(Path.Combine(directory, face.FileName)));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidFaceFile(string path)
        {
            FileInfo file = new(path);
            return file.Exists && file.Length > 0 && file.Length <= 16_777_216L;
        }

        /// <summary>Uses one source image, cropped/resized to a square, for every face.</summary>
        public static void ImportSingleImage(string sourcePath)
        {
            byte[] converted = ConvertImage(sourcePath);
            Dictionary<string, string> sources = Faces.ToDictionary(face => face.FileName, _ => sourcePath);
            ImportCore(sources, cachedBytes: converted);
        }

        /// <summary>Uses a distinct source image per face. <paramref name="faceSources"/> is keyed by the face's .tex filename.</summary>
        public static void ImportPerFace(IReadOnlyDictionary<string, string> faceSources)
        {
            if (Faces.Any(face => !faceSources.TryGetValue(face.FileName, out string? source) || string.IsNullOrWhiteSpace(source)))
                throw new InvalidDataException("Choose an image for every skybox face.");

            ImportCore(faceSources, cachedBytes: null);
        }

        public static void Remove()
        {
            if (!Directory.Exists(Paths.CustomSkybox))
                return;
            NormalizeAttributes(Paths.CustomSkybox);
            Directory.Delete(Paths.CustomSkybox, true);
        }

        private static void ImportCore(IReadOnlyDictionary<string, string> faceSources, byte[]? cachedBytes)
        {
            string operationId = Guid.NewGuid().ToString("N");
            string stagingDirectory = Paths.CustomSkybox + ".new." + operationId;
            string backupDirectory = Paths.CustomSkybox + ".backup." + operationId;
            Dictionary<string, byte[]> converted = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(stagingDirectory);

                foreach (var face in Faces)
                {
                    string sourcePath = Path.GetFullPath(faceSources[face.FileName]);

                    if (!converted.TryGetValue(sourcePath, out byte[]? bytes))
                    {
                        bytes = cachedBytes ?? ConvertImage(sourcePath);
                        converted[sourcePath] = bytes;
                    }

                    File.WriteAllBytes(Path.Combine(stagingDirectory, face.FileName), bytes);
                }

                if (!IsValidPackDirectory(stagingDirectory))
                    throw new InvalidDataException("The custom skybox could not be completed.");

                if (Directory.Exists(Paths.CustomSkybox))
                    Directory.Move(Paths.CustomSkybox, backupDirectory);

                Directory.Move(stagingDirectory, Paths.CustomSkybox);
                TryDeleteDirectory(backupDirectory);
            }
            catch
            {
                if (!Directory.Exists(Paths.CustomSkybox) && Directory.Exists(backupDirectory))
                    Directory.Move(backupDirectory, Paths.CustomSkybox);
                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
                if (Directory.Exists(Paths.CustomSkybox))
                    TryDeleteDirectory(backupDirectory);
            }
        }

        private static byte[] ConvertImage(string sourcePath)
        {
            FileInfo file = new(sourcePath);
            if (!file.Exists || file.Length <= 0 || file.Length > MaximumInputBytes)
                throw new InvalidDataException("The selected image is empty or too large.");

            using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                throw new InvalidDataException("The selected image has no usable frames.");

            BitmapSource source = decoder.Frames[0];

            if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
                throw new InvalidDataException("The selected image dimensions are invalid.");

            // center-crop to a square, then resize to FaceSize x FaceSize
            int squareSide = Math.Min(source.PixelWidth, source.PixelHeight);
            int cropX = (source.PixelWidth - squareSide) / 2;
            int cropY = (source.PixelHeight - squareSide) / 2;

            var cropped = new CroppedBitmap(source, new Int32Rect(cropX, cropY, squareSide, squareSide));

            double scale = (double)FaceSize / squareSide;
            var resized = new TransformedBitmap(cropped, new ScaleTransform(scale, scale));

            using MemoryStream output = new();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));
            encoder.Save(output);

            byte[] result = output.ToArray();

            if (result.Length <= 0 || result.Length > 16_777_216L)
                throw new InvalidDataException("The converted skybox face is too large.");

            return result;
        }

        private static void NormalizeAttributes(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;
                NormalizeAttributes(directory);
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }
}
