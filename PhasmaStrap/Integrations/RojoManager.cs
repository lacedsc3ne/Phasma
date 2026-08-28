using System.IO.Compression;
using System.Security.Cryptography;

namespace PhasmaStrap.Integrations
{
    // downloads, installs, updates and runs the Rojo CLI (https://github.com/rojo-rbx/rojo),
    // a popular third-party Studio file-sync tool for developers. Unlike the rest of the
    // extension manifest in ExtensionManager, which only ever points at an executable the
    // user already has, this manages the executable's entire lifecycle itself.
    // Ported from Voidstrap.
    public static class RojoManager
    {
        private const long MaxReleaseBytes = 536_870_912L;

        private static readonly SemaphoreSlim InstallGate = new(1, 1);

        private static string RojoDir => Path.Combine(Paths.Integrations, "Rojo");

        public static string RojoExe => Path.Combine(RojoDir, "rojo.exe");

        private static string VersionFile => Path.Combine(RojoDir, "version.txt");

        public static string? InstalledVersion => File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null;

        private static readonly object ServeLock = new();
        private static Process? _serveProcess;

        public static bool IsInstalled => File.Exists(RojoExe);

        public static bool IsServing
        {
            get
            {
                lock (ServeLock)
                {
                    try
                    {
                        return _serveProcess != null && !_serveProcess.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public static async Task EnsureInstalledAsync(Action<string>? progress, CancellationToken ct)
        {
            const string LOG_IDENT = "RojoManager::EnsureInstalledAsync";

            if (IsInstalled)
            {
                await UpdateAsync(progress, ct);
                return;
            }

            Directory.CreateDirectory(RojoDir);
            progress?.Invoke("Finding the latest Rojo release...");

            var (tag, url, digest, size) = await ResolveLatestAsync(ct);
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("Could not find a Windows Rojo release asset.");

            await InstallReleaseAsync(tag, url, digest, size, "Downloading Rojo", progress, ct);

            if (!IsInstalled)
                throw new InvalidOperationException("rojo.exe was not found in the downloaded archive.");

            App.Logger.WriteLine(LOG_IDENT, $"Rojo installed at {RojoExe}");
        }

        // returns true if an update was applied (or install skipped because already current)
        public static async Task<bool> UpdateAsync(Action<string>? progress, CancellationToken ct)
        {
            const string LOG_IDENT = "RojoManager::UpdateAsync";

            if (IsServing)
            {
                progress?.Invoke("Stop rojo serve before updating.");
                return false;
            }

            var (tag, url, digest, size) = await ResolveLatestAsync(ct);
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url))
            {
                progress?.Invoke("Could not check for updates.");
                return false;
            }

            string current = InstalledVersion ?? "";
            if (current == tag)
            {
                progress?.Invoke($"Already up to date ({tag}).");
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Updating Rojo from {(current.Length > 0 ? current : "unknown")} to {tag}");
            await InstallReleaseAsync(tag, url, digest, size, "Updating Rojo", progress, ct);
            App.Logger.WriteLine(LOG_IDENT, $"Rojo updated to {tag}");
            return true;
        }

        private static async Task InstallReleaseAsync(string tag, string url, string digest, long size, string label, Action<string>? progress, CancellationToken ct)
        {
            const string LOG_IDENT = "RojoManager::InstallReleaseAsync";

            await InstallGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(RojoDir);

                string suffix = Guid.NewGuid().ToString("N");
                string zipPath = Path.Combine(RojoDir, $"rojo.{suffix}.zip");
                string stagedExe = Path.Combine(RojoDir, $"rojo.{suffix}.exe");

                try
                {
                    await DownloadAsync(url, zipPath, digest, size, label, progress, ct).ConfigureAwait(false);

                    progress?.Invoke("Extracting Rojo...");
                    ExtractRojoExe(zipPath, stagedExe);

                    if (new FileInfo(stagedExe).Length <= 0)
                        throw new InvalidDataException("The Rojo executable is empty.");

                    File.Move(stagedExe, RojoExe, true);
                    File.WriteAllText(VersionFile, tag);

                    progress?.Invoke($"Rojo {tag} installed.");
                }
                finally
                {
                    TryDelete(zipPath);
                    TryDelete(stagedExe);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                throw;
            }
            finally
            {
                InstallGate.Release();
            }
        }

        private static async Task<(string Tag, string Url, string Digest, long Size)> ResolveLatestAsync(CancellationToken ct)
        {
            var release = await Http.GetJson<GithubRelease>("https://api.github.com/repos/rojo-rbx/rojo/releases/latest");
            if (release?.Assets is null)
                return ("", "", "", 0);

            (string Url, string Digest, long Size)? fallback = null;

            foreach (GithubReleaseAsset asset in release.Assets)
            {
                string name = asset.Name ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.IndexOf("win", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string url = asset.BrowserDownloadUrl ?? "";
                string digest = asset.Digest ?? "";
                long size = asset.Size;

                if (string.IsNullOrEmpty(url) || size <= 0 || size > MaxReleaseBytes ||
                    !Uri.TryCreate(url, UriKind.Absolute, out Uri? assetUri) || assetUri.Scheme != Uri.UriSchemeHttps ||
                    !assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.IndexOf("x86_64", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("win64", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (release.TagName ?? "", url, digest, size);

                fallback ??= (url, digest, size);
            }

            return fallback.HasValue
                ? (release.TagName ?? "", fallback.Value.Url, fallback.Value.Digest, fallback.Value.Size)
                : (release.TagName ?? "", "", "", 0);
        }

        private static void ExtractRojoExe(string zipPath, string destination)
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("rojo.exe", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                throw new InvalidOperationException("rojo.exe is missing from the downloaded archive.");

            if (entry.Length <= 0 || entry.Length > MaxReleaseBytes)
                throw new InvalidDataException("rojo.exe has an invalid size.");

            entry.ExtractToFile(destination, true);
        }

        // streams the download to disk while reporting progress, mirroring the manual
        // buffered-copy pattern Bootstrapper.DownloadPackage uses for Roblox's own packages -
        // and, when GitHub supplied a sha256 digest for the asset, verifies it afterwards
        private static async Task DownloadAsync(string url, string outputPath, string digest, long size, string label, Action<string>? progress, CancellationToken ct)
        {
            var response = await App.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var buffer = new byte[8192];
            long totalRead = 0;

            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, ct);
                    if (bytesRead == 0)
                        break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

                    totalRead += bytesRead;
                    long total = size > 0 ? size : totalRead;
                    int percent = total > 0 ? (int)(totalRead * 100 / total) : 0;
                    progress?.Invoke($"{label} ({percent}%)");
                }
            }

            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                string expected = digest.Substring("sha256:".Length);
                string actual = await ComputeSha256Async(outputPath, ct);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded Rojo archive failed checksum verification.");
            }
        }

        private static Task<string> ComputeSha256Async(string path, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(path);
                byte[] hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }, ct);
        }

        public static async Task UninstallAsync()
        {
            await InstallGate.WaitAsync().ConfigureAwait(false);
            try
            {
                StopServe();
                await Task.Delay(150).ConfigureAwait(false);

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (Directory.Exists(RojoDir))
                            Directory.Delete(RojoDir, true);
                        return;
                    }
                    catch
                    {
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                InstallGate.Release();
            }
        }

        public static bool StartServe(string workingDir)
        {
            const string LOG_IDENT = "RojoManager::StartServe";

            if (!IsInstalled)
                return false;

            StopServe();

            lock (ServeLock)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = RojoExe,
                    Arguments = "serve",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _serveProcess = Process.Start(psi);
                App.Logger.WriteLine(LOG_IDENT, $"rojo serve started in {workingDir}");
                return _serveProcess != null;
            }
        }

        public static void StopServe()
        {
            const string LOG_IDENT = "RojoManager::StopServe";

            lock (ServeLock)
            {
                if (_serveProcess == null)
                    return;

                try
                {
                    if (!_serveProcess.HasExited)
                        _serveProcess.Kill(true);
                }
                catch
                {
                }

                try
                {
                    _serveProcess.Dispose();
                }
                catch
                {
                }

                _serveProcess = null;
                App.Logger.WriteLine(LOG_IDENT, "rojo serve stopped");
            }
        }

        public static void Shutdown() => StopServe();

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
