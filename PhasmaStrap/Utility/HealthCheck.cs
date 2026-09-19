using System.Net.Http;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace PhasmaStrap.Utility
{
    public enum HealthStatus { Ok, Info, Warning, Problem }

    public sealed class HealthResult
    {
        public string Title { get; init; } = "";
        public HealthStatus Status { get; init; }
        public string Detail { get; init; } = "";

        // optional one-click repair; null = nothing PhasmaStrap can (or should) do by itself
        public string? FixLabel { get; init; }
        public Action? Fix { get; init; }
    }

    // The Diagnostics page's health check: the things that, when wrong, make "Roblox won't
    // start" or "clicking Play does nothing" - each looked at directly and reported in plain
    // words. Everything is read-only; the only thing that changes anything is a Fix the user clicks.
    internal static class HealthCheck
    {
        private const string LOG_IDENT = "HealthCheck";

        public static async Task<List<HealthResult>> RunAsync(Action<HealthResult> found, CancellationToken token)
        {
            var results = new List<HealthResult>();

            void Add(HealthResult result)
            {
                results.Add(result);
                found(result);
            }

            var checks = new Func<Task<IEnumerable<HealthResult>>>[]
            {
                () => Sync(CheckProtocols),
                () => Sync(CheckInstall),
                () => Sync(CheckRoblox),
                () => Sync(CheckFastFlags),
                () => Sync(CheckDisk),
                () => Sync(CheckGraphics),
                () => Sync(CheckSecurity),
                () => Sync(CheckHosts),
                () => Sync(CheckProxy),
                () => Sync(CheckWebView2),
                CheckInternetAsync,
            };

            foreach (var check in checks)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    foreach (HealthResult result in await check())
                        Add(result);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"A check failed: {ex}");
                }
            }

            return results;
        }

        private static Task<IEnumerable<HealthResult>> Sync(Func<IEnumerable<HealthResult>> check) => Task.Run(() => (IEnumerable<HealthResult>)check().ToList());

        // ------------------------------------------------------------------ the launch chain

        private static IEnumerable<HealthResult> CheckProtocols()
        {
            foreach (string protocol in new[] { "roblox-player", "roblox" })
            {
                string? command = null;
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{protocol}\shell\open\command"))
                    command = key?.GetValue("") as string;

                string title = $"\"Play\" links ({protocol}://)";

                if (string.IsNullOrEmpty(command))
                {
                    yield return new HealthResult
                    {
                        Title = title, Status = HealthStatus.Problem,
                        Detail = "Nothing is registered to open these links for your Windows account, so clicking Play on the website does nothing (or asks which app to use).",
                        FixLabel = "Register PhasmaStrap", Fix = WindowsRegistry.RegisterPlayer,
                    };
                    continue;
                }

                string target = command.StartsWith('"') ? command[1..Math.Max(1, command.IndexOf('"', 1))] : command.Split(' ')[0];

                if (!File.Exists(target))
                {
                    yield return new HealthResult
                    {
                        Title = title, Status = HealthStatus.Problem,
                        Detail = $"These links point at a program that no longer exists: {target}",
                        FixLabel = "Point them at PhasmaStrap", Fix = WindowsRegistry.RegisterPlayer,
                    };
                }
                else if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(Paths.Application), StringComparison.OrdinalIgnoreCase))
                {
                    yield return new HealthResult
                    {
                        Title = title, Status = HealthStatus.Warning,
                        Detail = $"These links open {target} instead of PhasmaStrap, so games started from the website or the Roblox app skip PhasmaStrap (no FastFlags, mods or overlays). Roblox's own installer takes this over whenever it runs.",
                        FixLabel = "Point them at PhasmaStrap", Fix = WindowsRegistry.RegisterPlayer,
                    };
                }
                else
                {
                    yield return new HealthResult { Title = title, Status = HealthStatus.Ok, Detail = "Open PhasmaStrap." };
                }
            }
        }

        private static IEnumerable<HealthResult> CheckInstall()
        {
            if (!File.Exists(Paths.Application))
            {
                yield return new HealthResult { Title = "PhasmaStrap installation", Status = HealthStatus.Problem, Detail = $"{Paths.Application} is missing - reinstall PhasmaStrap." };
                yield break;
            }

            // can the data folder be written? (OneDrive "known folder move", read-only attributes, security tools)
            string probe = Path.Combine(Paths.Base, $".healthcheck-{Guid.NewGuid():N}");
            string? writeError = null;
            try
            {
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                writeError = ex.Message;
            }

            yield return writeError is null
                ? new HealthResult { Title = "PhasmaStrap installation", Status = HealthStatus.Ok, Detail = $"{Paths.Base} is in place and writable. Version {App.Version}, .NET {Environment.Version}." }
                : new HealthResult { Title = "PhasmaStrap installation", Status = HealthStatus.Problem, Detail = $"PhasmaStrap cannot write to its own folder ({Paths.Base}): {writeError} Settings, updates and mods all need that - a security tool with \"controlled folder access\" or a read-only folder are the usual causes." };
        }

        private static IEnumerable<HealthResult> CheckRoblox()
        {
            string version = App.PlayerState.Prop.VersionGuid;

            if (string.IsNullOrEmpty(version))
            {
                yield return new HealthResult { Title = "Roblox installation", Status = HealthStatus.Info, Detail = "Roblox has not been installed through PhasmaStrap yet - it is downloaded the first time you launch." };
                yield break;
            }

            string folder = Path.Combine(Paths.Versions, version);
            string exe = Path.Combine(folder, "RobloxPlayerBeta.exe");

            if (!File.Exists(exe))
            {
                yield return new HealthResult { Title = "Roblox installation", Status = HealthStatus.Problem, Detail = $"PhasmaStrap believes {version} is installed, but {exe} is missing (antivirus quarantine and disk cleaners do this). The next launch reinstalls it; if it keeps disappearing, add {Paths.Base} to your antivirus exclusions." };
                yield break;
            }

            long bytes = 0;
            int files = 0;
            try
            {
                foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    bytes += file.Length;
                    files++;
                }
            }
            catch
            {
            }

            yield return new HealthResult { Title = "Roblox installation", Status = HealthStatus.Ok, Detail = $"{version}: {files} files, {bytes / 1048576.0:0} MB." };

            // compatibility shims on the game's exe are a classic source of odd behaviour
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using RegistryKey? layers = root.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                if (layers?.GetValue(exe) is string flags && flags.Length > 0)
                    yield return new HealthResult { Title = "Compatibility settings on Roblox", Status = HealthStatus.Info, Detail = $"Windows runs RobloxPlayerBeta.exe with compatibility settings: {flags.Trim()}. \"Run as administrator\" in particular breaks Play links and overlays - remove it under the exe's Properties > Compatibility if you did not set it on purpose." };
            }
        }

        private static IEnumerable<HealthResult> CheckFastFlags()
        {
            string file = App.FastFlags.FileLocation;
            if (!File.Exists(file))
            {
                yield return new HealthResult { Title = "FastFlags file", Status = HealthStatus.Ok, Detail = "No custom FastFlags." };
                yield break;
            }

            string? error = null;
            int count = 0;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                count = document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.EnumerateObject().Count() : 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            yield return error is null
                ? new HealthResult { Title = "FastFlags file", Status = HealthStatus.Ok, Detail = $"Valid, {count} flag(s). (If the game misbehaves after a Roblox update, flags are the first thing to switch off - Settings history on the PhasmaStrap page can put an older set back.)" }
                : new HealthResult { Title = "FastFlags file", Status = HealthStatus.Problem, Detail = $"ClientAppSettings.json is not valid JSON, so Roblox ignores every flag in it: {error}" };
        }

        private static IEnumerable<HealthResult> CheckDisk()
        {
            HealthResult? result = null;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Paths.Base)!);
                double freeGb = drive.AvailableFreeSpace / 1073741824.0;

                result = freeGb < 2
                    ? new HealthResult { Title = "Disk space", Status = HealthStatus.Problem, Detail = $"Only {freeGb:0.0} GB free on {drive.Name} - a Roblox update needs about 1 GB to unpack and fails half way when it runs out." }
                    : freeGb < 8
                        ? new HealthResult { Title = "Disk space", Status = HealthStatus.Warning, Detail = $"{freeGb:0.0} GB free on {drive.Name}. Enough for now; Windows itself gets slow below about 10 %." }
                        : new HealthResult { Title = "Disk space", Status = HealthStatus.Ok, Detail = $"{freeGb:0} GB free on {drive.Name}." };
            }
            catch
            {
            }

            if (result is not null)
                yield return result;
        }

        // ------------------------------------------------------------------ the PC

        private static IEnumerable<HealthResult> CheckGraphics()
        {
            var lines = new List<(string Name, DateTime? Driver, string Version)>();

            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");
                foreach (System.Management.ManagementBaseObject gpu in searcher.Get())
                {
                    string name = gpu["Name"] as string ?? "";
                    DateTime? date = null;
                    if (gpu["DriverDate"] is string raw && raw.Length >= 8 && DateTime.TryParseExact(raw[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime parsed))
                        date = parsed;

                    if (name.Length > 0)
                        lines.Add((name, date, gpu["DriverVersion"] as string ?? ""));
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"GPU query failed: {ex.Message}");
            }

            if (lines.Count == 0)
            {
                yield return new HealthResult { Title = "Graphics driver", Status = HealthStatus.Info, Detail = "Windows would not say which graphics adapter is installed." };
                yield break;
            }

            foreach (var gpu in lines)
            {
                bool basic = gpu.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || gpu.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase);
                double? months = gpu.Driver is null ? null : (DateTime.Now - gpu.Driver.Value).TotalDays / 30.4;
                string age = gpu.Driver is null ? "" : $", driver from {gpu.Driver:MMMM yyyy}";

                if (basic)
                    yield return new HealthResult { Title = "Graphics driver", Status = HealthStatus.Problem, Detail = $"{gpu.Name}: Windows is using its fallback driver, which has no real 3D acceleration - Roblox runs at a few frames per second or not at all. Install the driver from your graphics card's maker." };
                else if (months > 24)
                    yield return new HealthResult { Title = "Graphics driver", Status = HealthStatus.Warning, Detail = $"{gpu.Name}{age} - more than two years old. Crashes inside the graphics driver and black screens after Roblox updates are typical for that; a current driver is worth trying first." };
                else
                    yield return new HealthResult { Title = "Graphics driver", Status = HealthStatus.Ok, Detail = $"{gpu.Name}{age} ({gpu.Version})." };
            }

            if (lines.Count(l => !l.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase)) > 1)
                yield return new HealthResult { Title = "More than one graphics adapter", Status = HealthStatus.Info, Detail = "On a laptop Windows decides which one Roblox gets. If the game is slow, set RobloxPlayerBeta.exe to \"High performance\" under Windows Settings > Display > Graphics." };
        }

        private static IEnumerable<HealthResult> CheckSecurity()
        {
            var products = new List<string>();

            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
                foreach (System.Management.ManagementBaseObject product in searcher.Get())
                {
                    if (product["displayName"] is string name && !products.Contains(name))
                        products.Add(name);
                }
            }
            catch
            {
                // not available on server / LTSC editions without Security Center
            }

            bool controlledFolders = false;
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access");
                controlledFolders = key?.GetValue("EnableControlledFolderAccess") is int enabled && enabled == 1;
            }
            catch
            {
            }

            if (controlledFolders)
                yield return new HealthResult { Title = "Controlled folder access", Status = HealthStatus.Warning, Detail = "Windows Security's ransomware protection is on. It silently blocks programs it does not know from writing to Documents, Pictures and Videos - if screenshots or clips saved there go missing, allow PhasmaStrap under Windows Security > Ransomware protection > Allow an app." };

            string[] thirdParty = products.Where(p => !p.Contains("Defender", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (thirdParty.Length > 0)
                yield return new HealthResult { Title = "Antivirus", Status = HealthStatus.Info, Detail = $"{string.Join(", ", thirdParty)} is installed. Third-party antivirus is the most common reason a Roblox update \"installs\" but the game then fails to start (files quarantined mid-install). If that happens, exclude {Paths.Base}." };
            else if (products.Count > 0)
                yield return new HealthResult { Title = "Antivirus", Status = HealthStatus.Ok, Detail = $"{string.Join(", ", products)} - not known to interfere." };
        }

        private static IEnumerable<HealthResult> CheckHosts()
        {
            string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            var blocked = new List<string>();
            int own = 0;

            try
            {
                foreach (string raw in File.ReadAllLines(hosts))
                {
                    // PhasmaStrap marks the lines it writes itself (telemetry block, proxy)
                    if (raw.Contains("# PHASMASTRAP-", StringComparison.OrdinalIgnoreCase))
                    {
                        own++;
                        continue;
                    }

                    string line = raw.Split('#')[0].Trim();
                    if (line.Length == 0)
                        continue;

                    string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    foreach (string host in parts.Skip(1))
                    {
                        if (host.EndsWith("roblox.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith("rbxcdn.com", StringComparison.OrdinalIgnoreCase))
                            blocked.Add($"{host} -> {parts[0]}");
                    }
                }
            }
            catch
            {
            }

            if (blocked.Count == 0)
            {
                yield return new HealthResult
                {
                    Title = "Hosts file",
                    Status = HealthStatus.Ok,
                    Detail = own > 0
                        ? $"Only PhasmaStrap's own entries ({own} lines from the telemetry block / proxy options you switched on). Nothing else redirects Roblox."
                        : "No Roblox addresses are redirected.",
                };
                yield break;
            }

            yield return new HealthResult
            {
                Title = "Hosts file",
                Status = HealthStatus.Warning,
                Detail = $"{blocked.Count} Roblox address(es) are redirected in the Windows hosts file by something other than PhasmaStrap: {string.Join(", ", blocked.Take(6))}{(blocked.Count > 6 ? ", ..." : "")}. Entries like these can stop Roblox from logging in or downloading - remove them unless you put them there on purpose.",
            };
        }

        private static IEnumerable<HealthResult> CheckProxy()
        {
            HealthResult? result = null;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                bool enabled = key?.GetValue("ProxyEnable") is int value && value == 1;
                string server = key?.GetValue("ProxyServer") as string ?? "";

                if (enabled && server.Length > 0)
                    result = new HealthResult { Title = "System proxy", Status = HealthStatus.Info, Detail = $"Windows sends web traffic through {server}. If that proxy is not running, PhasmaStrap cannot download Roblox and the game cannot log in." };
            }
            catch
            {
            }

            if (result is not null)
                yield return result;
        }

        private static IEnumerable<HealthResult> CheckWebView2()
        {
            string? version = null;

            foreach ((RegistryKey root, string path) in new[]
            {
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"),
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"),
                (Registry.CurrentUser, @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"),
            })
            {
                using RegistryKey? key = root.OpenSubKey(path);
                if (key?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0")
                {
                    version = pv;
                    break;
                }
            }

            yield return version is not null
                ? new HealthResult { Title = "WebView2 runtime", Status = HealthStatus.Ok, Detail = $"Installed ({version}). Roblox's own menus and PhasmaStrap's browser login use it." }
                : new HealthResult { Title = "WebView2 runtime", Status = HealthStatus.Warning, Detail = "Not found. Roblox needs it for parts of its interface (a blank in-game menu or login window is the symptom), and PhasmaStrap's \"log in with browser\" cannot open without it. Roblox installs it on first launch; it can also be downloaded from Microsoft." };
        }

        // ------------------------------------------------------------------ the network

        private static async Task<IEnumerable<HealthResult>> CheckInternetAsync()
        {
            var results = new List<HealthResult>();

            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                results.Add(new HealthResult { Title = "Internet connection", Status = HealthStatus.Problem, Detail = "Windows reports no network connection at all." });
                return results;
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            DateTimeOffset? serverTime = null;

            foreach ((string name, string url, string what) in new[]
            {
                ("Roblox website", "https://www.roblox.com/robots.txt", "logging in and joining games"),
                ("Roblox downloads", "https://setup.rbxcdn.com/version", "installing and updating Roblox"),
                ("Roblox settings service", "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer", "finding out which Roblox version to launch"),
            })
            {
                var timer = Stopwatch.StartNew();

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    serverTime ??= response.Headers.Date;

                    results.Add((int)response.StatusCode < 500
                        ? new HealthResult { Title = name, Status = timer.ElapsedMilliseconds > 3000 ? HealthStatus.Warning : HealthStatus.Ok, Detail = $"Reachable in {timer.ElapsedMilliseconds} ms{(timer.ElapsedMilliseconds > 3000 ? " - that is slow; launches will feel stuck on \"Connecting\"" : "")}." }
                        : new HealthResult { Title = name, Status = HealthStatus.Warning, Detail = $"Answered with an error ({(int)response.StatusCode}). That is on Roblox's side and usually passes; it affects {what}." });
                }
                catch (Exception ex)
                {
                    string reason = ex.InnerException?.Message ?? ex.Message;
                    results.Add(new HealthResult { Title = name, Status = HealthStatus.Problem, Detail = $"Cannot be reached ({reason.Trim()}). Needed for {what}. A firewall, DNS filter, VPN or the hosts file is the usual cause when other websites work." });
                }
            }

            // a wrong clock makes every secure connection fail with a confusing certificate error
            if (serverTime is not null)
            {
                double skew = Math.Abs((DateTimeOffset.UtcNow - serverTime.Value).TotalMinutes);
                results.Add(skew > 5
                    ? new HealthResult { Title = "System clock", Status = HealthStatus.Problem, Detail = $"Your PC's clock is off by about {skew:0} minutes. Secure connections check the time, so logins and downloads start failing - set the time automatically under Windows Settings > Time & language." }
                    : new HealthResult { Title = "System clock", Status = HealthStatus.Ok, Detail = "Matches the internet time." });
            }

            return results;
        }
    }
}
