using System.Security.Cryptography;
using System.Text.Json;

namespace PhasmaStrap.Integrations
{
    // Port of Voidstrap's Voidstrap.Utility.ClassicClients (src/Voidstrap.App/Utility/ClassicClients.cs,
    // ~2255 lines in the original). This port carries over path/layout conventions, install detection, client
    // listing, process launching, AND (unlike the earlier revision of this file) the acquisition pipeline itself:
    // GitHub-release resolution with digest/size verification, bounded downloads, safe zip extraction, archive
    // staging with atomic commit, post-extraction helper-DLL/config-name normalization, and auto-update checking.
    //
    // The classic engine/client archives are fetched from a third-party GitHub release (see DefaultBaseUrl below) -
    // PhasmaStrap did not build or vet that archive's contents. What IS verified before anything is written to disk:
    // the release must live under github.com/<owner>/<repo>/releases/..., the resolved asset must come back from
    // GitHub's own release-assets API with a sha256 digest and an https://github.com/... download URL, and the
    // downloaded bytes are hashed and length-checked against that digest before extraction ever runs (see
    // ResolveReleaseAssetAsync/DownloadOneAsync). That verification is the same shape Voidstrap uses, and is not
    // weakened here.
    //
    // What this pipeline intentionally does NOT install is a WebServer executable: the archive was built to ship
    // Voidstrap's own compiled web server, but PhasmaStrap has its own separately-built PhasmaStrap.Server project
    // (see PhasmaStrap.Server.csproj / ServerExecutableName below). "Engine install" here therefore only fetches the
    // shared data pack the server needs to run (PrivateKey.pem, scripts/assets under data/, and the maps pack) - it
    // does not touch and is not gated on whether the user has already built/placed PhasmaStrap.Server.exe. See
    // EngineDataInstalled vs ServerEngineInstalled below for that distinction, and ValidateEngine/ForeignServerExeName
    // for how a foreign server binary from the archive (if present) is discarded rather than silently left on disk.
    public class ClassicCatalogEntry
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool HasStudio { get; set; } = true;
    }

    public class ClassicManifest
    {
        public long EngineSize { get; set; }
        public long MapsSize { get; set; }
        public Dictionary<string, long> ClientSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ClassicCatalogEntry> Clients { get; set; } = new();
    }

    public static class ClassicClients
    {
        private const string LOG_IDENT = "ClassicClients";

        public const string ServerExecutableName = "PhasmaStrap.Server.exe";
        public const string ConfigFileName = "PhasmaStrapClient_Config.json";

        // The archive's client zips ship this config file name (matching Voidstrap's own branding, baked into the
        // archive at build time) - it is renamed to ConfigFileName above immediately after extraction so the rest
        // of this class (and PhasmaStrap.Server's WebServer/ClientPaths.cs, which looks for ConfigFileName) never
        // has to know about the archive's original naming.
        private const string ArchiveConfigFileName = "VoidstrapClient_Config.json";

        // The archive's engine.zip ships this web server executable. PhasmaStrap never runs it - it has its own
        // PhasmaStrap.Server.exe (built from the separate PhasmaStrap.Server project). If this file ever shows up
        // after extracting engine.zip it is deleted rather than left sitting in the install root unused.
        private const string ForeignServerExeName = "VoidstrapClient.WebServer.exe";

        /// <summary>
        /// Third-party GitHub release archive that classic client/engine binaries are downloaded from. This is NOT
        /// a PhasmaStrap (or Bloxstrap) release feed and its contents are not built or reviewed by this project -
        /// it is the same community archive Voidstrap points at. What IS verified before any of it is written to
        /// disk is described in ResolveReleaseAssetAsync/DownloadOneAsync below (GitHub release API + per-asset
        /// sha256 digest + size, checked against the actual downloaded bytes).
        /// </summary>
        public const string DefaultBaseUrl = "https://github.com/Orc-Archive/Orc-Voidstrap/releases/tag/v1/";

        /// <summary>Legacy raw-file fallback host for the same archive, kept only so an old saved setting pointing
        /// at it gets silently upgraded to <see cref="DefaultBaseUrl"/> by <see cref="BaseUrl"/> rather than used
        /// directly (raw-file hosting has no per-asset digest to verify against).</summary>
        public const string LegacyDefaultBaseUrl = "https://github.com/voidstrap/RobloxClients/raw/main/";

        private const long MaxArchiveBytes = 2147483648L;
        private const int MaxReleaseMetadataBytes = 4194304;

        public static readonly IReadOnlyList<ClassicCatalogEntry> Catalog = new List<ClassicCatalogEntry>
        {
            new() { Code = "2007E", Name = "March 2007", Description = "The earliest available Roblox client. A very primitive version of Roblox, lacking most of the features that can be seen in the present client." },
            new() { Code = "2007E-FakeFeb", Name = "Fake February 2007", Description = "A recreation of the February 2007 Roblox client using the March 2007 client as a base. Restores Color3 teams, the Eras Bold ITC font, and other removed functionality." },
            new() { Code = "2007M", Name = "August 2007", Description = "Wearable hats have been introduced and the chat bar color has changed from white to gray." },
            new() { Code = "2007L", Name = "December 2007", Description = "Introduces a new graphical look using the OGRE rendering engine. Introduces bevels, seen in many old clients." },
            new() { Code = "2008E", Name = "April 2008", Description = "Adds in sparkles and the new explosion effect, powered by OGRE's particle system." },
            new() { Code = "2008M", Name = "August 2008", Description = "Roblox brings you Shirts and Pants. They also brought you walk speed and team chat." },
            new() { Code = "2008L", Name = "December 2008", Description = "Client sided movement makes moving around feel much better. Heads and faces have been added." },
            new() { Code = "2009E", Name = "March 2009", Description = "Replaces the circle, Lego inspired studs with new square studs, in a short lived variant without the branding." },
            new() { Code = "2009M", Name = "July 2009", Description = "Contains the familiar square studs used for the next couple of years. Adds 31 new brick colors and a new default sky." },
            new() { Code = "2009L", Name = "October 2009", Description = "GUIs were introduced in a barebones state. Badges have been added, allowing players to collect badges on their profile." },
            new() { Code = "2010E", Name = "February 2010", Description = "The Arial font replaced the original Comic Sans font. A new Lua health bar replaced the previous one." },
            new() { Code = "2010M", Name = "July 2010", Description = "Packages have been added. That is all." },
            new() { Code = "2010L", Name = "December 2010", Description = "A user interface overhaul powered by Lua. Data persistence has been introduced, allowing games to save and load player data." },
            new() { Code = "2011E", Name = "April 2011", Description = "Added the ability to record videos, an experimental Lua player list, and the menu with an early form of shift lock." },
            new() { Code = "2011M", Name = "July 2011", Description = "Introduces the new player list and backpack, powered by Lua. Also overhauls the previous menu." },
            new() { Code = "2011L", Name = "October 2011", Description = "Introduces voxel terrain, allowing developers to place and manipulate millions of terrain voxels." },
            new() { Code = "2012E", Name = "March 2012", Description = "Removed the clicking noise that would have played when zooming your camera." },
            new() { Code = "2012M", Name = "August 2012", Description = "Water has been added to the available terrain materials." },
            new() { Code = "2012L", Name = "October 2012", Description = "They moved the shift lock button. For this version only." },
            new() { Code = "2013E", Name = "March 2013", Description = "Introduces an overhaul to the player list, backpack and chat." },
            new() { Code = "2013M", Name = "August 2013", Description = "Introduces dynamic lighting. Bevels are removed on all parts but the player characters. Jump delay is removed." },
            new() { Code = "2013L", Name = "December 2013", Description = "Materials are replaced with more high quality counterparts. A new sky and animation system are introduced." },
        };

        private sealed class ReleaseAssetIntegrity
        {
            public string Url { get; init; } = "";
            public long Size { get; init; }
            public string Sha256 { get; init; } = "";
        }

        private sealed class InstalledAssetRecord
        {
            public long Size { get; set; }
            public string Sha256 { get; set; } = "";
        }

        private const string EngineKey = "__engine";
        private const string MapsKey = "__maps";

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            // A dedicated client, not App.HttpClient: App.HttpClient's Timeout is fixed at 30 seconds (see
            // App.xaml.cs), which is fine for small API calls but far too short for a multi-hundred-MB archive
            // download. GitHub's API also wants an explicit User-Agent, which this sets independently of whatever
            // App.HttpClient happens to be configured with.
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PhasmaStrap");
            return http;
        }

        private static readonly JsonSerializerOptions ConfigJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static ClassicManifest? _manifestCache;
        private static long _manifestCacheTicks;
        private static readonly SemaphoreSlim ManifestLock = new(1, 1);
        private static readonly SemaphoreSlim MutationLock = new(1, 1);
        private static readonly SemaphoreSlim ReleaseLock = new(1, 1);
        private static readonly Dictionary<string, (ReleaseAssetIntegrity Asset, long Ticks)> ReleaseCache = new(StringComparer.OrdinalIgnoreCase);

        public static string Root
        {
            get
            {
                string custom = App.Settings.Prop.ClassicClientInstallLocation;
                return string.IsNullOrWhiteSpace(custom) ? Path.Combine(Paths.Base, "ClassicClients") : custom;
            }
        }

        public static string ClientsDir => Path.Combine(Root, "data", "clients");

        public static string MapsDir => Path.Combine(Root, "maps");

        public static string ServerPath => Path.Combine(Root, ServerExecutableName);

        /// <summary>True if PhasmaStrap's own server executable has been built and placed at <see cref="ServerPath"/>.
        /// Required to actually start a private server session (see ClassicServerManager) - installing engine data
        /// via <see cref="InstallEngineAsync"/> does not affect this, since that data pack never contains our exe.</summary>
        public static bool ServerEngineInstalled => File.Exists(ServerPath);

        /// <summary>True if the shared engine data pack (PrivateKey.pem, scripts/assets under data/) has been
        /// downloaded and extracted. Used internally to decide whether a client install needs to pull the engine
        /// data pack first - independent of whether the user has separately built/placed the server executable.</summary>
        public static bool EngineDataInstalled => File.Exists(Path.Combine(Root, "data", "PrivateKey.pem"));

        public static bool IsSupportedClientCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
                return false;

            return Regex.IsMatch(code, @"^\d{4}[A-Za-z0-9](?:-[A-Za-z0-9]+)?$", RegexOptions.CultureInvariant);
        }

        private static string ResolvePathWithin(string root, params string[] parts)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] combineArgs = new string[parts.Length + 1];
            combineArgs[0] = root;
            Array.Copy(parts, 0, combineArgs, 1, parts.Length);
            string path = Path.GetFullPath(Path.Combine(combineArgs));

            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The classic client configuration contains an unsafe path.");

            return path;
        }

        public static List<string> ListInstalledClients()
        {
            var list = new List<string>();

            try
            {
                if (Directory.Exists(ClientsDir))
                {
                    foreach (string dir in Directory.GetDirectories(ClientsDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (IsSupportedClientCode(name) && File.Exists(Path.Combine(dir, ConfigFileName)))
                            list.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::ListInstalledClients", ex);
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static bool IsClientInstalled(string client)
        {
            if (!IsSupportedClientCode(client))
                return false;

            try
            {
                return File.Exists(ResolvePathWithin(ClientsDir, client, ConfigFileName));
            }
            catch
            {
                return false;
            }
        }

        public class ClassicClientConfig
        {
            public class _Player
            {
                public string ExecutableName { get; set; } = "";
                public string LaunchArguments { get; set; } = "";
            }

            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public _Player Player { get; set; } = new _Player();
        }

        public static ClassicClientConfig? GetInstalledConfig(string client)
        {
            try
            {
                if (!IsSupportedClientCode(client))
                    return null;

                string path = ResolvePathWithin(ClientsDir, client, ConfigFileName);
                if (!File.Exists(path))
                    return null;

                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<ClassicClientConfig>(stream, ConfigJsonOptions);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::GetInstalledConfig", ex);
                return null;
            }
        }

        public static long GetDirectorySize(string dir)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(dir))
                    return 0;

                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        // ---------------------------------------------------------------------------------------------------
        // Manifest: an optional remote manifest.json (fetched from the same archive) that lists which client
        // codes are currently published and their expected sizes. Falls back to the built-in Catalog above if
        // the manifest can't be fetched, so the client list still works even if the archive host is unreachable.
        // ---------------------------------------------------------------------------------------------------

        public static string BaseUrl
        {
            get
            {
                string url = App.Settings.Prop.ClassicDownloadBaseUrl;
                if (string.IsNullOrWhiteSpace(url) || string.Equals(url, LegacyDefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
                    url = DefaultBaseUrl;

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? source) || source.Scheme != Uri.UriSchemeHttps)
                    url = DefaultBaseUrl;

                return url.EndsWith("/") ? url : url + "/";
            }
        }

        public static async Task<ClassicManifest?> FetchManifestAsync(CancellationToken ct, bool force = false)
        {
            if (!force && _manifestCache != null && DateTime.UtcNow.Ticks - _manifestCacheTicks < TimeSpan.FromMinutes(5).Ticks)
                return _manifestCache;

            await ManifestLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!force && _manifestCache != null && DateTime.UtcNow.Ticks - _manifestCacheTicks < TimeSpan.FromMinutes(5).Ticks)
                    return _manifestCache;

                foreach (string url in BuildCandidateUrls("manifest.json"))
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        byte[] bytes = await Utility.Http.ReadBytesBoundedAsync(response.Content, MaxReleaseMetadataBytes, ct).ConfigureAwait(false);

                        using JsonDocument doc = JsonDocument.Parse(bytes);
                        JsonElement root = doc.RootElement;
                        var manifest = new ClassicManifest();

                        if (root.TryGetProperty("engineSize", out JsonElement es) && es.TryGetInt64(out long esv))
                            manifest.EngineSize = esv;
                        if (root.TryGetProperty("mapsSize", out JsonElement ms) && ms.TryGetInt64(out long msv))
                            manifest.MapsSize = msv;

                        if (root.TryGetProperty("clients", out JsonElement clients) && clients.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement c in clients.EnumerateArray())
                            {
                                if (!c.TryGetProperty("code", out JsonElement codeEl))
                                    continue;

                                string? code = codeEl.GetString();
                                if (string.IsNullOrWhiteSpace(code) || !IsSupportedClientCode(code))
                                    continue;

                                long size = c.TryGetProperty("size", out JsonElement sz) && sz.TryGetInt64(out long szv) ? szv : 0L;
                                manifest.ClientSizes[code] = size;
                                manifest.Clients.Add(new ClassicCatalogEntry
                                {
                                    Code = code,
                                    Name = (c.TryGetProperty("name", out JsonElement n) ? n.GetString() : null) ?? code,
                                    Description = (c.TryGetProperty("description", out JsonElement d) ? d.GetString() : null) ?? "",
                                    HasStudio = c.TryGetProperty("hasStudio", out JsonElement h) && h.ValueKind == JsonValueKind.True
                                });
                            }
                        }

                        if (manifest.Clients.Count > 0)
                        {
                            _manifestCache = manifest;
                            _manifestCacheTicks = DateTime.UtcNow.Ticks;
                            return manifest;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }

                _manifestCache = new ClassicManifest
                {
                    Clients = Catalog.Select(entry => new ClassicCatalogEntry
                    {
                        Code = entry.Code,
                        Name = entry.Name,
                        Description = entry.Description,
                        HasStudio = entry.HasStudio
                    }).ToList()
                };
                _manifestCacheTicks = DateTime.UtcNow.Ticks;
                App.Logger.WriteLine(LOG_IDENT + "::FetchManifest", "Remote manifest unavailable, using built in catalog");
                return _manifestCache;
            }
            finally
            {
                ManifestLock.Release();
            }
        }

        public static async Task<List<ClassicCatalogEntry>> FetchManifestClientsAsync(CancellationToken ct)
        {
            ClassicManifest? manifest = await FetchManifestAsync(ct).ConfigureAwait(false);
            return manifest?.Clients ?? new List<ClassicCatalogEntry>();
        }

        private static List<string> BuildCandidateUrls(string relativePath)
        {
            static string EscPath(string p) => string.Join("/", p.Split('/').Select(Uri.EscapeDataString));
            string flat = relativePath.Contains('/') ? relativePath.Substring(relativePath.LastIndexOf('/') + 1) : relativePath;

            string baseUrl = BaseUrl;
            Match raw = Regex.Match(baseUrl, @"github\.com/([^/]+)/([^/]+)/raw/([^/]+)/", RegexOptions.IgnoreCase);
            Match rel = Regex.Match(baseUrl, @"github\.com/([^/]+)/([^/]+)/releases/(?:tag/([^/]+)/)?", RegexOptions.IgnoreCase);

            string? owner = null, repo = null, tag = null;
            string branch = "main";
            if (raw.Success) { owner = raw.Groups[1].Value; repo = raw.Groups[2].Value; branch = raw.Groups[3].Value; }
            else if (rel.Success) { owner = rel.Groups[1].Value; repo = rel.Groups[2].Value; tag = rel.Groups[3].Success ? rel.Groups[3].Value : null; }

            var urls = new List<string>();
            if (owner != null)
            {
                if (!string.IsNullOrEmpty(tag))
                    urls.Add($"https://github.com/{owner}/{repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(flat)}");
                urls.Add($"https://github.com/{owner}/{repo}/releases/latest/download/{Uri.EscapeDataString(flat)}");
                urls.Add($"https://github.com/{owner}/{repo}/raw/{branch}/{EscPath(relativePath)}");
                urls.Add($"https://cdn.jsdelivr.net/gh/{owner}/{repo}@{branch}/{EscPath(relativePath)}");
            }
            else
            {
                urls.Add(baseUrl + EscPath(relativePath));
                if (!baseUrl.Contains("/releases/", StringComparison.OrdinalIgnoreCase))
                    urls.Add(baseUrl + Uri.EscapeDataString(flat));
            }
            return urls.Distinct().ToList();
        }

        // ---------------------------------------------------------------------------------------------------
        // Release asset resolution + integrity verification. This is the security-relevant part of the
        // pipeline: DownloadOneAsync will refuse to accept a download whose length or SHA-256 doesn't match what
        // is resolved here, and this method itself refuses to resolve anything that isn't a real GitHub release
        // asset (draft releases, non-uploaded assets, missing/malformed digests, and non-github.com download
        // hosts are all rejected before a URL is ever returned).
        // ---------------------------------------------------------------------------------------------------

        private static bool TryGetGitHubRelease(string baseUrl, out string owner, out string repo, out string? tag)
        {
            owner = "";
            repo = "";
            tag = null;

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3 || !string.Equals(segments[2], "releases", StringComparison.OrdinalIgnoreCase))
                return false;

            owner = segments[0];
            repo = segments[1];

            if (segments.Length == 3)
                return true;
            if (segments.Length == 4 && string.Equals(segments[3], "latest", StringComparison.OrdinalIgnoreCase))
                return true;
            if (segments.Length == 5 && string.Equals(segments[3], "tag", StringComparison.OrdinalIgnoreCase))
            {
                tag = Uri.UnescapeDataString(segments[4]);
                return !string.IsNullOrWhiteSpace(tag);
            }

            return false;
        }

        private static async Task<ReleaseAssetIntegrity> ResolveReleaseAssetAsync(string relativePath, CancellationToken ct)
        {
            string cacheKey = BaseUrl + "|" + relativePath;

            await ReleaseLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (ReleaseCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow.Ticks - cached.Ticks < TimeSpan.FromMinutes(5).Ticks)
                    return cached.Asset;

                if (!TryGetGitHubRelease(BaseUrl, out string owner, out string repo, out string? tag))
                    throw new InvalidOperationException("Classic archives must come from a GitHub release with verified digests");

                string endpoint = tag == null
                    ? $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest"
                    : $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/tags/{Uri.EscapeDataString(tag)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is long metadataLength && metadataLength > MaxReleaseMetadataBytes)
                    throw new InvalidDataException("The classic release metadata is too large");

                byte[] metadata = await Utility.Http.ReadBytesBoundedAsync(response.Content, MaxReleaseMetadataBytes, ct).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(metadata);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                    throw new InvalidDataException("The classic release is still a draft");
                if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("The classic release has no assets");

                string assetName = relativePath.Split('/').Last();
                foreach (JsonElement assetElement in assets.EnumerateArray())
                {
                    if (!assetElement.TryGetProperty("name", out JsonElement nameElement) || !string.Equals(nameElement.GetString(), assetName, StringComparison.Ordinal))
                        continue;
                    if (!assetElement.TryGetProperty("state", out JsonElement stateElement) || !string.Equals(stateElement.GetString(), "uploaded", StringComparison.Ordinal))
                        throw new InvalidDataException("The classic release asset is not ready");
                    if (!assetElement.TryGetProperty("size", out JsonElement sizeElement) || !sizeElement.TryGetInt64(out long size) || size <= 0 || size > MaxArchiveBytes)
                        throw new InvalidDataException("The classic release asset size is invalid");
                    if (!assetElement.TryGetProperty("digest", out JsonElement digestElement))
                        throw new InvalidDataException("The classic release asset has no digest");

                    string digest = digestElement.GetString() ?? "";
                    if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The classic release asset digest is invalid");

                    string sha256 = digest.Substring(7);
                    if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
                        throw new InvalidDataException("The classic release asset digest is invalid");

                    if (!assetElement.TryGetProperty("browser_download_url", out JsonElement urlElement) ||
                        !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? downloadUri) ||
                        downloadUri.Scheme != Uri.UriSchemeHttps ||
                        !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The classic release asset URL is invalid");

                    var resolved = new ReleaseAssetIntegrity
                    {
                        Url = downloadUri.AbsoluteUri,
                        Size = size,
                        Sha256 = sha256.ToUpperInvariant()
                    };
                    ReleaseCache[cacheKey] = (resolved, DateTime.UtcNow.Ticks);
                    return resolved;
                }

                throw new FileNotFoundException("The requested classic release asset is missing", assetName);
            }
            finally
            {
                ReleaseLock.Release();
            }
        }

        public static async Task<long> GetRemoteClientSizeAsync(string client, CancellationToken ct) =>
            await GetRemoteSizeAsync("clients/" + client + ".zip", ct).ConfigureAwait(false);

        public static async Task<long> GetRemoteSizeAsync(string relativePath, CancellationToken ct)
        {
            try
            {
                return (await ResolveReleaseAssetAsync(relativePath, ct).ConfigureAwait(false)).Size;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            foreach (string url in BuildCandidateUrls(relativePath))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, url);
                    using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                        return response.Content.Headers.ContentLength.Value;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }
            return -1L;
        }

        // ---------------------------------------------------------------------------------------------------
        // Install index: tracks the size/sha256 that was actually installed for the engine, maps, and each
        // client, so IsAssetUpdateAvailableAsync/IsClientUpdateAvailableAsync can tell whether the currently
        // resolved remote release asset differs from what's on disk without re-downloading and re-hashing it.
        // ---------------------------------------------------------------------------------------------------

        private static string InstalledIndexPath => Path.Combine(Root, "installed.json");

        private static Dictionary<string, InstalledAssetRecord> LoadIndex()
        {
            try
            {
                if (File.Exists(InstalledIndexPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(InstalledIndexPath));
                    var result = new Dictionary<string, InstalledAssetRecord>(StringComparer.OrdinalIgnoreCase);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        return result;

                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Object)
                            continue;

                        long size = property.Value.TryGetProperty("Size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long parsedSize) ? parsedSize : -1L;
                        string sha256 = property.Value.TryGetProperty("Sha256", out JsonElement digestElement) ? digestElement.GetString() ?? "" : "";
                        result[property.Name] = new InstalledAssetRecord { Size = size, Sha256 = sha256 };
                    }
                    return result;
                }
            }
            catch
            {
            }
            return new Dictionary<string, InstalledAssetRecord>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveIndex(Dictionary<string, InstalledAssetRecord> index)
        {
            try
            {
                Directory.CreateDirectory(Root);
                string temp = InstalledIndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(index));
                File.Move(temp, InstalledIndexPath, overwrite: true);
            }
            catch
            {
            }
        }

        private static InstalledAssetRecord? GetInstalledRecord(string client) =>
            LoadIndex().TryGetValue(client, out InstalledAssetRecord? record) ? record : null;

        public static long GetInstalledSize(string client) =>
            LoadIndex().TryGetValue(client, out InstalledAssetRecord? record) ? record.Size : -1L;

        private static void RecordInstalled(string client, long size, string sha256)
        {
            var index = LoadIndex();
            index[client] = new InstalledAssetRecord { Size = size, Sha256 = sha256 };
            SaveIndex(index);
        }

        public static async Task<bool> IsAssetUpdateAvailableAsync(string relativePath, string key, CancellationToken ct)
        {
            InstalledAssetRecord? installed = GetInstalledRecord(key);
            if (installed == null || installed.Size <= 0)
                return false;

            ReleaseAssetIntegrity remote = await ResolveReleaseAssetAsync(relativePath, ct).ConfigureAwait(false);
            return remote.Size != installed.Size || !string.Equals(remote.Sha256, installed.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<bool> IsClientUpdateAvailableAsync(string client, CancellationToken ct)
        {
            if (!IsClientInstalled(client))
                return false;

            InstalledAssetRecord? installed = GetInstalledRecord(client);
            if (installed == null || installed.Size <= 0)
                return false;

            ReleaseAssetIntegrity remote = await ResolveReleaseAssetAsync("clients/" + client + ".zip", ct).ConfigureAwait(false);
            return remote.Size != installed.Size || !string.Equals(remote.Sha256, installed.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task UninstallClientAsync(string client, CancellationToken ct = default)
        {
            await MutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!IsSupportedClientCode(client))
                    throw new ArgumentException("The classic client code is invalid", nameof(client));

                string dir = ResolvePathWithin(ClientsDir, client);
                if (Directory.Exists(dir))
                    await Task.Run(() => Directory.Delete(dir, true), ct).ConfigureAwait(false);

                var index = LoadIndex();
                if (index.Remove(client))
                    SaveIndex(index);
            }
            finally
            {
                MutationLock.Release();
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Download + extract. Staged into a sibling temp directory and only swapped into place atomically
        // (CommitDirectory) once extraction and post-extraction validation both succeed, so a crash or
        // cancellation mid-install can never leave Root/ClientsDir/MapsDir in a half-written state.
        // ---------------------------------------------------------------------------------------------------

        private static async Task DownloadOneAsync(ReleaseAssetIntegrity asset, string tempZip, Action<double, string>? progress, CancellationToken ct)
        {
            progress?.Invoke(0, "Connecting");
            using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long contentLength && contentLength != asset.Size)
                throw new InvalidDataException("The classic archive size does not match its release metadata");

            using Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(tempZip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                read = checked(read + n);
                if (read > asset.Size || read > MaxArchiveBytes)
                    throw new InvalidDataException("The classic archive exceeds its release size");

                await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                hash.AppendData(buffer, 0, n);
                progress?.Invoke(read / (double)asset.Size * 95.0, $"Downloading {FormatBytes(read)} of {FormatBytes(asset.Size)}");
            }
            await output.FlushAsync(ct).ConfigureAwait(false);

            if (read != asset.Size)
                throw new InvalidDataException("The classic archive ended before its release size");

            byte[] expected = Convert.FromHexString(asset.Sha256);
            byte[] actual = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                throw new CryptographicException("The classic archive digest does not match its release metadata");
        }

        private static async Task<ReleaseAssetIntegrity> DownloadAndExtractAsync(string relativePath, Action<double, string>? progress, CancellationToken ct)
        {
            ReleaseAssetIntegrity asset = await ResolveReleaseAssetAsync(relativePath, ct).ConfigureAwait(false);
            string tempZip = Path.Combine(Path.GetTempPath(), "phasmastrap_classic_" + Guid.NewGuid().ToString("N") + ".zip");
            string stagedRoot = CreateSiblingPath(Root, "archive");
            try
            {
                await DownloadOneAsync(asset, tempZip, progress, ct).ConfigureAwait(false);
                progress?.Invoke(96, "Extracting");

                if (string.Equals(relativePath, "engine.zip", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Root))
                    await Task.Run(() => CopyTree(Root, stagedRoot, ct), ct).ConfigureAwait(false);

                await Task.Run(() => SafeZipExtractor.ExtractToDirectory(tempZip, stagedRoot, overwrite: true, maxExpandedBytes: 4294967296L), ct).ConfigureAwait(false);

                if (string.Equals(relativePath, "engine.zip", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateEngine(stagedRoot);
                    CommitDirectory(stagedRoot, Root);
                }
                else if (string.Equals(relativePath, "maps.zip", StringComparison.OrdinalIgnoreCase))
                {
                    string stagedMaps = ResolvePathWithin(stagedRoot, "maps");
                    ValidateMaps(stagedMaps);
                    CommitDirectory(stagedMaps, MapsDir);
                }
                else if (relativePath.StartsWith("clients/", StringComparison.OrdinalIgnoreCase) && relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string client = Path.GetFileNameWithoutExtension(relativePath.Split('/').Last());
                    if (!IsSupportedClientCode(client))
                        throw new InvalidDataException("The classic client archive name is invalid");

                    string stagedClient = ResolvePathWithin(stagedRoot, "data", "clients", client);
                    NormalizeClientHelpers(stagedClient);
                    ValidateClient(stagedClient);
                    CommitDirectory(stagedClient, ResolvePathWithin(ClientsDir, client));
                }
                else
                {
                    throw new InvalidDataException("The classic archive type is invalid");
                }

                progress?.Invoke(100, "Done");
                return asset;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                DeleteDirectoryIfExists(stagedRoot);
            }
        }

        private static string CreateSiblingPath(string target, string purpose)
        {
            string fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parent = Path.GetDirectoryName(fullTarget);
            string name = Path.GetFileName(fullTarget);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("The classic install location is invalid");

            Directory.CreateDirectory(parent);
            return Path.Combine(parent, "." + name + "." + purpose + "." + Guid.NewGuid().ToString("N"));
        }

        private static void ValidateEngine(string root)
        {
            // Note: does NOT require ServerExecutableName - PhasmaStrap's own server exe is built/placed
            // separately and is never part of this download. Only the shared data pack is required here.
            if (!File.Exists(Path.Combine(root, "data", "PrivateKey.pem")))
                throw new InvalidDataException("The classic engine archive is incomplete");

            // Discard the archive's own (foreign) web server binary if present - PhasmaStrap never runs it.
            string foreignExe = Path.Combine(root, ForeignServerExeName);
            if (File.Exists(foreignExe))
            {
                try
                {
                    File.Delete(foreignExe);
                    App.Logger.WriteLine(LOG_IDENT + "::ValidateEngine", $"Discarded foreign server executable {ForeignServerExeName} from the downloaded archive");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT + "::ValidateEngine", ex);
                }
            }
        }

        private static void ValidateMaps(string maps)
        {
            if (!Directory.Exists(maps) || !Directory.EnumerateFiles(maps, "*", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("The classic maps archive is empty");
        }

        private static void ValidateClient(string clientDirectory)
        {
            if (!File.Exists(Path.Combine(clientDirectory, ConfigFileName)))
                throw new InvalidDataException("The classic client archive is incomplete");
        }

        private static void CommitDirectory(string staged, string target)
        {
            string fullStaged = Path.GetFullPath(staged);
            string fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(fullStaged))
                throw new DirectoryNotFoundException(fullStaged);

            string backup = CreateSiblingPath(fullTarget, "backup");
            bool movedTarget = false;
            try
            {
                if (Directory.Exists(fullTarget))
                {
                    Directory.Move(fullTarget, backup);
                    movedTarget = true;
                }
                Directory.Move(fullStaged, fullTarget);
            }
            catch
            {
                DeleteDirectoryIfExists(fullTarget);
                if (movedTarget && Directory.Exists(backup))
                    Directory.Move(backup, fullTarget);
                throw;
            }
            DeleteDirectoryIfExists(backup);
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static void CopyTree(string source, string target, CancellationToken ct)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string rel = Path.GetRelativePath(source, file);
                string dest = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.#} {units[unit]}";
        }

        // ---------------------------------------------------------------------------------------------------
        // Post-extraction normalization. The archive ships helper DLLs and executables renamed/patched to avoid
        // AV false-positives on the literal original Roblox helper DLL name, plus a config file name matching
        // Voidstrap's own branding - both are restored/renamed here to what PhasmaStrap.Server and the rest of
        // this class expect. The byte-level patches below operate on fixed-size buffers baked into the archive's
        // own compiled binaries (a third-party classic Roblox client executable, not anything PhasmaStrap builds)
        // so the replacement strings must be the exact same byte length as what they replace - they are NOT
        // renamed to PhasmaStrap branding, since doing so would either overflow the buffer or fail to match.
        // ---------------------------------------------------------------------------------------------------

        private const string OriginalHelperName = "OnlyRetroRobloxHereClientHelper.dll";
        private const string RenamedHelperName = "VoidstrapClientHelper.dll";

        public static void NormalizeClientHelpers(string clientDir)
        {
            try
            {
                if (!Directory.Exists(clientDir))
                    return;

                foreach (string sub in Directory.GetDirectories(clientDir))
                {
                    string renamed = Path.Combine(sub, RenamedHelperName);
                    if (File.Exists(renamed))
                    {
                        foreach (string exe in Directory.GetFiles(sub, "*.exe"))
                            RestoreHelperImport(exe);

                        string moveTarget = Path.Combine(sub, OriginalHelperName);
                        if (File.Exists(moveTarget))
                        {
                            try { File.Delete(renamed); } catch { }
                        }
                        else
                        {
                            try { File.Move(renamed, moveTarget); } catch { }
                        }
                    }

                    string helper = Path.Combine(sub, OriginalHelperName);
                    if (File.Exists(helper))
                        FixHelperFolderString(helper);
                }

                // PhasmaStrap-specific: the archive's client config file is named for Voidstrap's own branding.
                // Rename it to ConfigFileName so ListInstalledClients/IsClientInstalled/GetInstalledConfig (and
                // PhasmaStrap.Server's WebServer/ClientPaths.cs) all find it under the name they expect.
                string archiveConfig = Path.Combine(clientDir, ArchiveConfigFileName);
                string expectedConfig = Path.Combine(clientDir, ConfigFileName);
                if (File.Exists(archiveConfig) && !File.Exists(expectedConfig))
                {
                    try { File.Move(archiveConfig, expectedConfig); }
                    catch (Exception ex) { App.Logger.WriteException(LOG_IDENT + "::NormalizeClientHelpers", ex); }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::NormalizeClientHelpers", ex);
            }
        }

        private static void FixHelperFolderString(string helperPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(helperPath);
                byte[] broken = Encoding.ASCII.GetBytes("VoidstrapClient\0\0\0\0");
                byte[] fixedBytes = Encoding.ASCII.GetBytes("VoidstrapClientData");
                int index = IndexOfBytes(data, broken, 0);
                if (index < 0)
                    return;

                Array.Copy(fixedBytes, 0, data, index, fixedBytes.Length);
                File.WriteAllBytes(helperPath, data);
            }
            catch
            {
            }
        }

        private static void RestoreHelperImport(string exePath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(exePath);
                if (data.Length < 2 || data[0] != (byte)'M' || data[1] != (byte)'Z')
                    return;

                byte[] oldName = Encoding.ASCII.GetBytes(RenamedHelperName);
                byte[] newName = Encoding.ASCII.GetBytes(OriginalHelperName);
                int index = IndexOfBytes(data, oldName, 0);
                if (index < 0)
                    return;
                if (index + newName.Length + 1 > data.Length)
                    return;

                for (int i = oldName.Length; i < newName.Length; i++)
                {
                    if (data[index + i] != 0)
                        return;
                }

                Array.Copy(newName, 0, data, index, newName.Length);
                data[index + newName.Length] = 0;
                File.WriteAllBytes(exePath, data);
            }
            catch
            {
            }
        }

        private static int IndexOfBytes(byte[] haystack, byte[] needle, int start)
        {
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------------------------------------------
        // Public install/update entry points. All mutating operations serialize on MutationLock so an install
        // and an update (or two installs) can never race each other over the same on-disk layout.
        // ---------------------------------------------------------------------------------------------------

        public static async Task InstallEngineAsync(Action<double, string>? progress, CancellationToken ct)
        {
            await MutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await InstallEngineCoreAsync(progress, ct).ConfigureAwait(false);
            }
            finally
            {
                MutationLock.Release();
            }
        }

        private static async Task InstallEngineCoreAsync(Action<double, string>? progress, CancellationToken ct)
        {
            ReleaseAssetIntegrity engine = await DownloadAndExtractAsync("engine.zip", progress, ct).ConfigureAwait(false);
            RecordInstalled(EngineKey, engine.Size, engine.Sha256);

            if (!Directory.Exists(MapsDir) || !Directory.EnumerateFileSystemEntries(MapsDir).Any())
            {
                try
                {
                    ReleaseAssetIntegrity maps = await DownloadAndExtractAsync("maps.zip", progress, ct).ConfigureAwait(false);
                    RecordInstalled(MapsKey, maps.Size, maps.Sha256);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT + "::InstallEngine", $"maps.zip skipped: {ex.Message}");
                }
            }
        }

        public static async Task InstallClientAsync(string client, Action<double, string>? progress, CancellationToken ct)
        {
            await MutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await InstallClientCoreAsync(client, progress, ct).ConfigureAwait(false);
            }
            finally
            {
                MutationLock.Release();
            }
        }

        private static async Task InstallClientCoreAsync(string client, Action<double, string>? progress, CancellationToken ct)
        {
            if (!IsSupportedClientCode(client))
                throw new ArgumentException("The classic client code is invalid", nameof(client));

            if (!EngineDataInstalled)
                await InstallEngineCoreAsync(progress, ct).ConfigureAwait(false);

            ReleaseAssetIntegrity archive = await DownloadAndExtractAsync("clients/" + client + ".zip", progress, ct).ConfigureAwait(false);
            RecordInstalled(client, archive.Size, archive.Sha256);
        }

        public static async Task AutoUpdateAllAsync(CancellationToken ct = default)
        {
            if (!EngineDataInstalled)
                return;

            await MutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                const string LOG_IDENT_LOCAL = LOG_IDENT + "::AutoUpdateAll";
                Action<double, string> quiet = delegate { };

                try { await FetchManifestAsync(ct, force: true).ConfigureAwait(false); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT_LOCAL, "Manifest refresh failed, skipping this pass: " + ex.Message); return; }

                int updated = 0;

                try
                {
                    if (await IsAssetUpdateAvailableAsync("engine.zip", EngineKey, ct).ConfigureAwait(false))
                    {
                        App.Logger.WriteLine(LOG_IDENT_LOCAL, "Updating the classic engine data pack");
                        ReleaseAssetIntegrity engine = await DownloadAndExtractAsync("engine.zip", quiet, ct).ConfigureAwait(false);
                        RecordInstalled(EngineKey, engine.Size, engine.Sha256);
                        updated++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT_LOCAL, "Engine update failed: " + ex.Message); }

                try
                {
                    if (await IsAssetUpdateAvailableAsync("maps.zip", MapsKey, ct).ConfigureAwait(false))
                    {
                        App.Logger.WriteLine(LOG_IDENT_LOCAL, "Updating the classic maps pack");
                        ReleaseAssetIntegrity maps = await DownloadAndExtractAsync("maps.zip", quiet, ct).ConfigureAwait(false);
                        RecordInstalled(MapsKey, maps.Size, maps.Sha256);
                        updated++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT_LOCAL, "Maps update failed: " + ex.Message); }

                foreach (string installed in ListInstalledClients())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (!await IsClientUpdateAvailableAsync(installed, ct).ConfigureAwait(false))
                            continue;

                        App.Logger.WriteLine(LOG_IDENT_LOCAL, "Updating classic client " + installed);
                        await InstallClientCoreAsync(installed, quiet, ct).ConfigureAwait(false);
                        updated++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT_LOCAL, "Update for " + installed + " failed: " + ex.Message); }
                }

                App.Logger.WriteLine(LOG_IDENT_LOCAL, updated == 0 ? "Everything is already up to date" : "Updated " + updated + " item(s)");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                MutationLock.Release();
            }
        }

        public static async Task UpdateEverythingAsync(string client, Action<string>? status, Action<double, string>? progress, CancellationToken ct)
        {
            await MutationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await UpdateEverythingCoreAsync(client, status, progress, ct).ConfigureAwait(false);
            }
            finally
            {
                MutationLock.Release();
            }
        }

        private static async Task UpdateEverythingCoreAsync(string client, Action<string>? status, Action<double, string>? progress, CancellationToken ct)
        {
            if (!EngineDataInstalled)
                return;

            status?.Invoke("Checking for updates");
            try { await FetchManifestAsync(ct, force: true).ConfigureAwait(false); } catch { }

            if (await IsAssetUpdateAvailableAsync("engine.zip", EngineKey, ct).ConfigureAwait(false))
            {
                status?.Invoke("Updating engine data");
                ReleaseAssetIntegrity engine = await DownloadAndExtractAsync("engine.zip", progress, ct).ConfigureAwait(false);
                RecordInstalled(EngineKey, engine.Size, engine.Sha256);
            }

            if (await IsAssetUpdateAvailableAsync("maps.zip", MapsKey, ct).ConfigureAwait(false))
            {
                status?.Invoke("Updating maps");
                ReleaseAssetIntegrity maps = await DownloadAndExtractAsync("maps.zip", progress, ct).ConfigureAwait(false);
                RecordInstalled(MapsKey, maps.Size, maps.Sha256);
            }

            if (IsClientInstalled(client) && await IsClientUpdateAvailableAsync(client, ct).ConfigureAwait(false))
            {
                status?.Invoke("Updating client");
                await InstallClientCoreAsync(client, progress, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// True if any process under an installed classic client's directory currently appears to be running.
        /// Used by <see cref="ClassicHostRedirect"/> to decide whether it is safe to release the hosts redirect.
        /// </summary>
        public static bool AnyClassicClientRunning()
        {
            try
            {
                string clientsDir = ClientsDir;
                if (!Directory.Exists(clientsDir))
                    return false;

                string fullClientsDir = Path.GetFullPath(clientsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        string? path = process.MainModule?.FileName;
                        if (path != null && path.StartsWith(fullClientsDir, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch
                    {
                        // access denied reading MainModule for processes we don't own - ignore
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::AnyClassicClientRunning", ex);
            }

            return false;
        }

        public static bool LaunchClient(string client, out string? error)
        {
            error = null;
            const string LOG_IDENT_LOCAL = LOG_IDENT + "::LaunchClient";

            if (!IsClientInstalled(client))
            {
                error = "The selected classic client is not installed.";
                return false;
            }

            ClassicClientConfig? config = GetInstalledConfig(client);
            if (config is null || string.IsNullOrWhiteSpace(config.Player.ExecutableName))
            {
                error = "The classic client configuration is incomplete.";
                return false;
            }

            try
            {
                string executable = ResolvePathWithin(ClientsDir, client, "Player", config.Player.ExecutableName);
                if (!File.Exists(executable))
                {
                    error = "The classic client executable is missing. Repair the selected client and try again.";
                    return false;
                }

                Process.Start(new ProcessStartInfo(executable, config.Player.LaunchArguments)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                });

                App.Logger.WriteLine(LOG_IDENT_LOCAL, $"Launched classic client {client}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
                error = "Failed to launch the classic client: " + ex.Message;
                return false;
            }
        }
    }
}
