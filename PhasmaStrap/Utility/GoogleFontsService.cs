using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PhasmaStrap.Utility
{
    public sealed class GoogleFontOption
    {
        public string Family { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(Category) ? Family : Family + ", " + Category;
    }

    /// <summary>
    /// Fetches a Google Fonts catalog and lets the user browse/preview/download a font, ported
    /// from Voidstrap's GoogleFontsService.
    ///
    /// Voidstrap's original implementation fetched its catalog from a Voidstrap-owned proxy
    /// (voidstrapp.pages.dev). PhasmaStrap does not run or depend on that infrastructure, so this
    /// port is repointed at "https://fonts.google.com/metadata/fonts" instead - the public,
    /// unofficial Google Fonts metadata endpoint that fonts.google.com's own web UI calls. It
    /// requires no API key, has been stable for years, and is relied on directly by many other
    /// open-source projects. Its response shape is different from Voidstrap's catalog envelope
    /// (it's { "familyMetadataList": [ { "family", "category", ... }, ... ] } rather than
    /// { "fonts": [ { "family", "category", "file" }, ... ] }), and it doesn't hand back a
    /// downloadable file URL directly - that's still resolved lazily per-family via the css2
    /// endpoint in ResolveFileUrlAsync below, exactly as Voidstrap did whenever a catalog entry's
    /// File was already empty.
    ///
    /// This endpoint also has a long-standing quirk: it sometimes prefixes its JSON body with an
    /// XSSI-protection line (e.g. ")]}'") before the actual JSON object starts. StripXssiPrefix
    /// defensively strips anything before the first '{' before handing the bytes to
    /// JsonSerializer, without assuming the prefix is always present.
    /// </summary>
    internal static class GoogleFontsService
    {
        // see the class-level comment above for why this specific endpoint was chosen
        private const string CatalogUrl = "https://fonts.google.com/metadata/fonts";

        // the metadata endpoint's payload is considerably larger than Voidstrap's proxied
        // catalog (it carries subsets/axes/etc. for every family), so the guard is generous
        private const int MaximumCatalogBytes = 16777216;

        internal const int MaximumFontBytes = 33554432;

        private const long MaximumFontCacheBytes = 268435456;

        private static readonly string CacheDirectory = Path.Combine(Paths.LocalAppData, "PhasmaStrap", "GoogleFonts");

        private static readonly string CatalogPath = Path.Combine(CacheDirectory, "catalog.json");

        private static readonly string FontCacheDirectory = Path.Combine(CacheDirectory, "Files");

        private static readonly SemaphoreSlim CacheMaintenanceGate = new(1, 1);

        private static readonly SemaphoreSlim FontDownloadGate = new(1, 1);

        private static readonly Regex FontUrlPattern = new(@"https://fonts\.gstatic\.com/[^)'""\s]+\.ttf", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // net6.0 doesn't have C# 11 UTF-8 string literals ("tag"u8), so the sfnt table tags this
        // file compares against are plain byte arrays instead
        private static readonly byte[] SfntTag = new byte[] { 0, 1, 0, 0 };

        private static readonly byte[] OttoTag = Encoding.ASCII.GetBytes("OTTO");

        private static readonly byte[] NameTag = Encoding.ASCII.GetBytes("name");

        private static readonly string[] StarterFamilies = new string[]
        {
            "Bebas Neue", "Dancing Script", "Fira Sans", "Inter", "JetBrains Mono", "Lato", "Merriweather", "Montserrat", "Noto Sans", "Nunito", "Open Sans", "Oswald", "Pacifico", "Playfair Display", "Poppins", "Raleway", "Roboto", "Rubik", "Source Sans 3", "Ubuntu"
        };

        private static readonly JsonSerializerOptions TolerantJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private sealed class CatalogEnvelope
        {
            [JsonPropertyName("fonts")]
            public List<GoogleFontOption> Fonts { get; set; } = new();
        }

        private sealed class MetadataEnvelope
        {
            [JsonPropertyName("familyMetadataList")]
            public List<FamilyMetadata>? FamilyMetadataList { get; set; }
        }

        private sealed class FamilyMetadata
        {
            [JsonPropertyName("family")]
            public string? Family { get; set; }

            [JsonPropertyName("category")]
            public string? Category { get; set; }
        }

        public static async Task<IReadOnlyList<GoogleFontOption>> LoadCatalogAsync(bool force, CancellationToken token)
        {
            if (!force && TryLoadCache(out IReadOnlyList<GoogleFontOption> fresh) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CatalogPath) < TimeSpan.FromHours(24))
                return fresh;

            try
            {
                using HttpResponseMessage response = await App.HttpClient.GetAsync(CatalogUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                string body = await Http.ReadStringBoundedAsync(response.Content, MaximumCatalogBytes, token);
                byte[] data = Encoding.UTF8.GetBytes(StripXssiPrefix(body));

                MetadataEnvelope? envelope = JsonSerializer.Deserialize<MetadataEnvelope>(data, TolerantJsonOptions);
                List<GoogleFontOption> fonts = Normalize(envelope?.FamilyMetadataList?.Select(entry => new GoogleFontOption
                {
                    Family = entry.Family ?? string.Empty,
                    Category = entry.Category ?? string.Empty
                }));

                if (fonts.Count == 0)
                    throw new InvalidDataException("The font catalog was empty");

                Directory.CreateDirectory(CacheDirectory);
                await Task.Run(() => SerializeAtomic(CatalogPath, new CatalogEnvelope { Fonts = fonts }), token);
                return fonts;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GoogleFontsService::LoadCatalog", "Font catalog unavailable: " + ex.Message);
                if (TryLoadCache(out IReadOnlyList<GoogleFontOption> cached))
                    return cached;
                return StarterFamilies.Select(family => new GoogleFontOption { Family = family, Category = "starter" }).ToArray();
            }
        }

        /// <summary>
        /// Strips a leading XSSI-protection line (commonly ")]}'") that some Google endpoints,
        /// including this metadata endpoint, sometimes prepend before the actual JSON body.
        /// Defensive: if no such prefix is present, the input is returned unchanged.
        /// </summary>
        private static string StripXssiPrefix(string body)
        {
            int braceIndex = body.IndexOf('{');
            if (braceIndex <= 0)
                return body;

            return body.Substring(braceIndex);
        }

        public static async Task<string> DownloadAsync(GoogleFontOption font, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(font.Family))
                throw new InvalidDataException("Select a font first");

            await FontDownloadGate.WaitAsync(token);
            try
            {
                return await DownloadCoreAsync(font, token);
            }
            finally
            {
                FontDownloadGate.Release();
            }
        }

        private static async Task<string> DownloadCoreAsync(GoogleFontOption font, CancellationToken token)
        {
            Directory.CreateDirectory(FontCacheDirectory);
            string fileUrl = ValidateFileUrl(font.File) ?? await ResolveFileUrlAsync(font.Family, token);
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(font.Family + "|" + fileUrl))).Substring(0, 20);
            string destination = Path.Combine(FontCacheDirectory, id + ".ttf");

            if (IsValidFont(destination))
                return destination;

            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using HttpResponseMessage response = await App.HttpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is long size && (size <= 0 || size > MaximumFontBytes))
                    throw new InvalidDataException("The font file size is invalid");

                await using Stream source = await response.Content.ReadAsStreamAsync(token);
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer.AsMemory(), token);
                        if (read == 0)
                            break;

                        total += read;
                        if (total > MaximumFontBytes)
                            throw new InvalidDataException("The font file is too large");

                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                    }
                    await output.FlushAsync(token);
                }

                if (!IsValidFont(temporary))
                    throw new InvalidDataException("The downloaded file is not a supported font");

                File.Move(temporary, destination, true);
                await MaintainCacheAsync(destination, token).ConfigureAwait(false);
                return destination;
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        public static async Task<string> ImportLocalAsync(string sourcePath, CancellationToken token)
        {
            FileInfo sourceInfo = new(sourcePath);
            if (!sourceInfo.Exists || sourceInfo.Length < 12 || sourceInfo.Length > MaximumFontBytes)
                throw new InvalidDataException("The font file size is invalid");

            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension is not ".ttf" and not ".otf")
                throw new InvalidDataException("The font file type is not supported");

            string localDirectory = Path.Combine(FontCacheDirectory, "Local");
            Directory.CreateDirectory(localDirectory);
            string temporary = Path.Combine(localDirectory, Guid.NewGuid().ToString("N") + ".importing");
            try
            {
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                        if (read == 0)
                            break;

                        total += read;
                        if (total > MaximumFontBytes)
                            throw new InvalidDataException("The font file is too large");

                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                    await output.FlushAsync(token).ConfigureAwait(false);
                }

                if (!IsValidFont(temporary))
                    throw new InvalidDataException("The font file is not supported");

                string id = Convert.ToHexString(hash.GetHashAndReset());
                string destination = Path.Combine(localDirectory, id + extension);
                if (IsValidFont(destination))
                    return destination;

                File.Move(temporary, destination, true);
                await MaintainCacheAsync(destination, token).ConfigureAwait(false);
                return destination;
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        private static async Task MaintainCacheAsync(string protectedPath, CancellationToken token)
        {
            await CacheMaintenanceGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(FontCacheDirectory))
                            return;

                        FileInfo[] files = Directory.EnumerateFiles(FontCacheDirectory, "*", SearchOption.AllDirectories)
                            .Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                            .Select(path => new FileInfo(path))
                            .Where(file => file.Exists)
                            .OrderBy(file => file.LastWriteTimeUtc)
                            .ToArray();

                        long total = files.Sum(file => file.Length);
                        foreach (FileInfo file in files)
                        {
                            if (total <= MaximumFontCacheBytes)
                                break;

                            if (file.FullName.Equals(protectedPath, StringComparison.OrdinalIgnoreCase))
                                continue;

                            try
                            {
                                long length = file.Length;
                                file.Delete();
                                total -= length;
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("GoogleFontsService::MaintainCache", "Font cache maintenance failed: " + ex.Message);
                    }
                }, token).ConfigureAwait(false);
            }
            finally
            {
                CacheMaintenanceGate.Release();
            }
        }

        private static bool TryLoadCache(out IReadOnlyList<GoogleFontOption> fonts)
        {
            fonts = Array.Empty<GoogleFontOption>();
            try
            {
                if (!File.Exists(CatalogPath))
                    return false;

                byte[] data = File.ReadAllBytes(CatalogPath);
                if (data.Length == 0 || data.Length > MaximumCatalogBytes)
                    return false;

                CatalogEnvelope? envelope = JsonSerializer.Deserialize<CatalogEnvelope>(data, TolerantJsonOptions);
                List<GoogleFontOption> normalized = Normalize(envelope?.Fonts);
                if (normalized.Count == 0)
                    return false;

                fonts = normalized;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SerializeAtomic(string path, CatalogEnvelope envelope)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(envelope));
            File.Move(temporary, path, true);
        }

        private static List<GoogleFontOption> Normalize(IEnumerable<GoogleFontOption>? values)
        {
            return (values ?? Enumerable.Empty<GoogleFontOption>())
                .Where(value => !string.IsNullOrWhiteSpace(value.Family) && value.Family.Length <= 128)
                .GroupBy(value => value.Family.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(value => value.Family, StringComparer.CurrentCultureIgnoreCase)
                .Take(3000)
                .ToList();
        }

        private static async Task<string> ResolveFileUrlAsync(string family, CancellationToken token)
        {
            string url = "https://fonts.googleapis.com/css2?family=" + Uri.EscapeDataString(family).Replace("%20", "+", StringComparison.OrdinalIgnoreCase) + "&display=swap";
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using HttpResponseMessage response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            string css = await Http.ReadStringBoundedAsync(response.Content, 262144, token);
            foreach (Match match in FontUrlPattern.Matches(css))
            {
                string? validated = ValidateFileUrl(match.Value);
                if (validated != null)
                    return validated;
            }

            throw new InvalidDataException("No compatible font file was found");
        }

        private static string? ValidateFileUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!Uri.TryCreate(value.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out Uri? uri))
                return null;

            if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("fonts.gstatic.com", StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                return null;

            return uri.AbsoluteUri;
        }

        private static bool IsValidFont(string path)
        {
            try
            {
                FileInfo file = new(path);
                if (!file.Exists || file.Length < 4 || file.Length > MaximumFontBytes)
                    return false;

                Span<byte> header = stackalloc byte[4];
                using FileStream stream = File.OpenRead(path);
                if (stream.Read(header) != 4)
                    return false;

                return (header.SequenceEqual(SfntTag) || header.SequenceEqual(OttoTag)) && TryReadFamilyName(path, out _);
            }
            catch
            {
                return false;
            }
        }

        // net6.0's Stream doesn't have the .NET 7+ ReadExactly helper, so this fills the whole
        // span or throws, the same contract ReadExactly provides
        private static void ReadExactlyCompat(Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(total));
                if (read == 0)
                    throw new EndOfStreamException();

                total += read;
            }
        }

        internal static bool TryReadFamilyName(string path, out string familyName)
        {
            familyName = string.Empty;
            try
            {
                FileInfo file = new(path);
                if (!file.Exists || file.Length < 28 || file.Length > MaximumFontBytes)
                    return false;

                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> header = stackalloc byte[12];
                ReadExactlyCompat(stream, header);
                bool supported = header[..4].SequenceEqual(SfntTag) || header[..4].SequenceEqual(OttoTag);
                if (!supported)
                    return false;

                int tableCount = BinaryPrimitives.ReadUInt16BigEndian(header[4..6]);
                if (tableCount <= 0 || tableCount > 256)
                    return false;

                uint nameOffset = 0;
                uint nameLength = 0;
                Span<byte> tableRecord = stackalloc byte[16];
                for (int index = 0; index < tableCount; index++)
                {
                    ReadExactlyCompat(stream, tableRecord);
                    if (!tableRecord[..4].SequenceEqual(NameTag))
                        continue;

                    nameOffset = BinaryPrimitives.ReadUInt32BigEndian(tableRecord[8..12]);
                    nameLength = BinaryPrimitives.ReadUInt32BigEndian(tableRecord[12..16]);
                    break;
                }

                if (nameLength < 6 || nameLength > 4194304 || (ulong)nameOffset + nameLength > (ulong)file.Length)
                    return false;

                byte[] nameData = new byte[(int)nameLength];
                stream.Position = nameOffset;
                ReadExactlyCompat(stream, nameData);
                ReadOnlySpan<byte> names = nameData;
                int nameCount = BinaryPrimitives.ReadUInt16BigEndian(names[2..4]);
                int stringOffset = BinaryPrimitives.ReadUInt16BigEndian(names[4..6]);
                if (nameCount <= 0 || nameCount > 4096 || 6L + nameCount * 12L > names.Length || stringOffset < 6 || stringOffset > names.Length)
                    return false;

                string? bestName = null;
                int bestScore = int.MinValue;
                for (int index = 0; index < nameCount; index++)
                {
                    int recordOffset = 6 + index * 12;
                    ReadOnlySpan<byte> record = names.Slice(recordOffset, 12);
                    int platformId = BinaryPrimitives.ReadUInt16BigEndian(record[0..2]);
                    int encodingId = BinaryPrimitives.ReadUInt16BigEndian(record[2..4]);
                    int languageId = BinaryPrimitives.ReadUInt16BigEndian(record[4..6]);
                    int nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..8]);
                    int length = BinaryPrimitives.ReadUInt16BigEndian(record[8..10]);
                    int offset = BinaryPrimitives.ReadUInt16BigEndian(record[10..12]);
                    if (nameId is not 1 and not 16 || length <= 0 || length > 2048)
                        continue;

                    int start = stringOffset + offset;
                    if (start < stringOffset || start > names.Length || length > names.Length - start)
                        continue;

                    string value = platformId is 0 or 3
                        ? Encoding.BigEndianUnicode.GetString(names.Slice(start, length))
                        : Encoding.Latin1.GetString(names.Slice(start, length));
                    value = string.Join(" ", value.Replace('\0', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Contains('#') || value.Any(char.IsControl))
                        continue;

                    int score = nameId == 16 ? 100 : 50;
                    score += platformId == 3 ? 40 : platformId == 0 ? 30 : platformId == 1 ? 20 : 0;
                    if (languageId == 1033)
                        score += 10;
                    if (encodingId is 1 or 10)
                        score += 2;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestName = value;
                    }
                }

                if (string.IsNullOrWhiteSpace(bestName))
                    return false;

                familyName = bestName;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
