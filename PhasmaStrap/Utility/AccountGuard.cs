using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PhasmaStrap.Utility
{
    public sealed class GuardFinding
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
    }

    public sealed class GuardAlert
    {
        public DateTime Utc { get; set; }
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    public sealed class GuardState
    {
        public bool Baselined { get; set; }
        public List<string> RootThumbprints { get; set; } = new();
        public Dictionary<string, string> BundleHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Proxy { get; set; } = "";

        public List<string> Accepted { get; set; } = new();
        public List<string> Alerted { get; set; } = new();

        public List<GuardAlert> History { get; set; } = new();
    }

    public static class AccountGuard
    {
        private const string LOG_IDENT = "AccountGuard";
        private const string RunValueName = "PhasmaStrapAccountGuard";

        private static readonly object _lock = new();

        private static string StatePath => Path.Combine(Paths.Base, "AccountGuard.json");

        private static string StampPath => Path.Combine(Paths.Base, "Cache", "own-sign-in-access");

        private static readonly string[] RobloxDomains = { "roblox.com", "rbxcdn.com", "robloxlabs.com", "rbx.com", "roblox.qq.com", "rbxtrk.com" };

        private static readonly string[] InterceptorNames = { "fiddler", "charles proxy", "mitmproxy", "portswigger", "burp", "http toolkit", "httptoolkit", "proxyman", "do_not_trust", "telerik" };

        public static GuardState Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(StatePath))
                        return JsonSerializer.Deserialize<GuardState>(File.ReadAllText(StatePath)) ?? new GuardState();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not read the guard state: {ex.Message}");
                }

                return new GuardState();
            }
        }

        private static void Save(GuardState state)
        {
            lock (_lock)
            {
                try
                {
                    if (state.History.Count > 50)
                        state.History.RemoveRange(0, state.History.Count - 50);

                    File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not save the guard state: {ex.Message}");
                }
            }
        }

        public static void MarkOwnAccess()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StampPath)!);
                File.WriteAllText(StampPath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
            }
        }

        private static bool OwnAccessWithin(TimeSpan window)
        {
            try
            {
                return File.Exists(StampPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(StampPath) < window;
            }
            catch
            {
                return false;
            }
        }

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

        private static IEnumerable<GuardFinding> CheckHosts()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            string[] lines;

            try { lines = File.ReadAllLines(path); }
            catch { yield break; }

            bool inOwnBlock = false;

            foreach (string raw in lines)
            {
                string line = raw.Trim();

                if (line.StartsWith("# PhasmaStrap", StringComparison.OrdinalIgnoreCase))
                {
                    inOwnBlock = !line.EndsWith(" end", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inOwnBlock || line.Length == 0 || line.StartsWith('#'))
                    continue;

                int comment = line.IndexOf('#');
                if (comment >= 0 && line[comment..].Contains("PHASMASTRAP", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = (comment >= 0 ? line[..comment] : line).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (parts[0] is "0.0.0.0" or "::" or "0:0:0:0:0:0:0:0")
                    continue;

                foreach (string host in parts.Skip(1))
                {
                    string h = host.ToLowerInvariant();
                    if (RobloxDomains.Any(d => h == d || h.EndsWith("." + d)))
                    {
                        yield return new GuardFinding
                        {
                            Id = $"hosts:{h}:{parts[0]}",
                            Title = $"{h} is redirected by your hosts file",
                            Detail = $"Your hosts file sends {h} to {parts[0]}, which PhasmaStrap didn't set up. Programs that steal logins do this to send Roblox traffic to their own server. If you didn't add it yourself, remove that line from {path}.",
                        };
                    }
                }
            }
        }

        private static List<X509Certificate2> RootCertificates(StoreLocation location)
        {
            try
            {
                using var store = new X509Store(StoreName.Root, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                return store.Certificates.Cast<X509Certificate2>().ToList();
            }
            catch
            {
                return new List<X509Certificate2>();
            }
        }

        private static bool IsOwnCertificate(X509Certificate2 certificate) =>
            certificate.Subject.Contains("PhasmaStrap Local Proxy CA", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<GuardFinding> CheckCertificates(GuardState state)
        {
            List<X509Certificate2> mine = RootCertificates(StoreLocation.CurrentUser);

            foreach (X509Certificate2 cert in mine.Concat(RootCertificates(StoreLocation.LocalMachine)))
            {
                string name = cert.Subject.ToLowerInvariant();
                if (InterceptorNames.Any(n => name.Contains(n)))
                {
                    yield return new GuardFinding
                    {
                        Id = $"interceptor:{cert.Thumbprint}",
                        Title = "A traffic-interception certificate is trusted",
                        Detail = $"\"{cert.GetNameInfo(X509NameType.SimpleName, false)}\" can read encrypted traffic from this PC, Roblox logins included. That's fine if you use that tool on purpose; otherwise remove it (certmgr.msc > Trusted Root Certification Authorities).",
                    };
                }
            }

            if (!state.Baselined)
                yield break;

            foreach (X509Certificate2 cert in mine)
            {
                if (IsOwnCertificate(cert) || state.RootThumbprints.Contains(cert.Thumbprint))
                    continue;

                yield return new GuardFinding
                {
                    Id = $"root:{cert.Thumbprint}",
                    Title = "A new trusted certificate was added",
                    Detail = $"\"{cert.GetNameInfo(X509NameType.SimpleName, false)}\" was added to the certificates your Windows account trusts. A trusted certificate lets whoever made it read encrypted traffic, Roblox logins included. If you didn't just install something that needs it, remove it in certmgr.msc.",
                };
            }
        }

        private static IEnumerable<string> TrustBundles()
        {
            if (!Directory.Exists(Paths.Versions))
                yield break;

            foreach (string dir in Directory.GetDirectories(Paths.Versions))
            {
                string bundle = Path.Combine(dir, "ssl", "cacert.pem");
                if (File.Exists(bundle))
                    yield return bundle;
            }
        }

        private static string? BundleHash(string bundle)
        {
            try
            {
                string content = File.ReadAllText(bundle);
                int own = content.IndexOf("# PhasmaStrap Local Proxy CA", StringComparison.Ordinal);
                if (own >= 0)
                {
                    int end = content.IndexOf("-----END CERTIFICATE-----", own, StringComparison.Ordinal);
                    content = end < 0 ? content[..own] : content[..own] + content[(end + "-----END CERTIFICATE-----".Length)..];
                }

                return Hash(content.Trim());
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<GuardFinding> CheckTrustBundles(GuardState state)
        {
            foreach (string bundle in TrustBundles())
            {
                string? hash = BundleHash(bundle);
                if (hash is null || !state.BundleHashes.TryGetValue(bundle, out string? known) || known == hash)
                    continue;

                string version = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(bundle)))!;
                yield return new GuardFinding
                {
                    Id = $"bundle:{bundle}:{hash}",
                    Title = "Roblox's certificate list was changed",
                    Detail = $"The list of certificates Roblox {version} trusts was edited by another program. That lets it read Roblox's encrypted traffic, logins included. Reinstalling Roblox (Deployment > Reinstall) puts the original back.",
                };
            }
        }

        private static string CurrentProxy()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                bool on = key?.GetValue("ProxyEnable") is int enabled && enabled != 0;
                string server = on ? key?.GetValue("ProxyServer") as string ?? "" : "";
                string script = key?.GetValue("AutoConfigURL") as string ?? "";
                return (server.Length > 0 ? $"proxy {server}" : "") + (script.Length > 0 ? $" script {script}" : "");
            }
            catch
            {
                return "";
            }
        }

        private static IEnumerable<GuardFinding> CheckProxy(GuardState state)
        {
            string proxy = CurrentProxy().Trim();
            if (!state.Baselined || proxy == state.Proxy || proxy.Length == 0)
                yield break;

            yield return new GuardFinding
            {
                Id = $"proxy:{Hash(proxy)}",
                Title = "Windows now sends traffic through a proxy",
                Detail = $"Your Windows proxy setting changed to: {proxy}. A proxy sees where your traffic goes, and with a trusted certificate it can read it. If you didn't set this, turn it off in Settings > Network & internet > Proxy.",
            };
        }

        public static List<GuardFinding> Scan()
        {
            GuardState state = Load();

            var findings = new List<GuardFinding>();
            findings.AddRange(CheckHosts());
            findings.AddRange(CheckCertificates(state));
            findings.AddRange(CheckTrustBundles(state));
            findings.AddRange(CheckProxy(state));

            bool changed = false;

            if (!state.Baselined)
            {
                state.RootThumbprints = RootCertificates(StoreLocation.CurrentUser).Select(c => c.Thumbprint).ToList();
                state.Proxy = CurrentProxy().Trim();
                state.Baselined = true;
                changed = true;
            }

            foreach (string bundle in TrustBundles())
            {
                if (!state.BundleHashes.ContainsKey(bundle) && BundleHash(bundle) is string hash)
                {
                    state.BundleHashes[bundle] = hash;
                    changed = true;
                }
            }

            foreach (string gone in state.BundleHashes.Keys.Where(b => !File.Exists(b)).ToList())
            {
                state.BundleHashes.Remove(gone);
                changed = true;
            }

            if (changed)
                Save(state);

            return findings.Where(f => !state.Accepted.Contains(f.Id)).ToList();
        }

        public static void Accept(GuardFinding finding)
        {
            GuardState state = Load();

            if (!state.Accepted.Contains(finding.Id))
                state.Accepted.Add(finding.Id);

            if (finding.Id.StartsWith("root:"))
                state.RootThumbprints.Add(finding.Id[5..]);
            else if (finding.Id.StartsWith("proxy:"))
                state.Proxy = CurrentProxy().Trim();
            else if (finding.Id.StartsWith("bundle:"))
            {
                string[] parts = finding.Id.Split(':');
                string bundle = string.Join(":", parts.Skip(1).Take(parts.Length - 2));
                state.BundleHashes[bundle] = parts[^1];
            }

            Save(state);
        }

        private static void Record(GuardState state, string title, string detail)
        {
            state.History.Add(new GuardAlert { Utc = DateTime.UtcNow, Title = title, Detail = detail });
            App.Logger.WriteLine(LOG_IDENT, $"Alert: {title} - {detail}");

            UI.NotificationCenter.Notify(title, detail, UI.NotificationCategory.General, durationSeconds: 20, kind: UI.NotificationKindId.AccountGuard);
        }

        public static void ScanAndNotify()
        {
            if (!App.Settings.Prop.AccountGuardEnabled)
                return;

            try
            {
                List<GuardFinding> findings = Scan();
                GuardState state = Load();

                foreach (GuardFinding finding in findings.Where(f => !state.Alerted.Contains(f.Id)))
                {
                    state.Alerted.Add(finding.Id);
                    Record(state, finding.Title, finding.Detail);
                }

                Save(state);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Scan failed: {ex.Message}");
            }
        }

        public static bool BackgroundWanted => App.Settings.Prop.AccountGuardEnabled && App.Settings.Prop.AccountGuardBackground;

        public static void ApplyStartup()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");

                if (BackgroundWanted)
                    key.SetValue(RunValueName, $"\"{Paths.Application}\" -guard");
                else if (key.GetValue(RunValueName) is not null)
                    key.DeleteValue(RunValueName, false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not update the start-up entry: {ex.Message}");
            }
        }

        public static void StartBackgroundIfWanted()
        {
            if (!BackgroundWanted)
                return;

            using var running = new Mutex(false, @"Local\PhasmaStrapAccountGuard", out bool createdNew);
            if (createdNew)
                Process.Start(Paths.Application, "-guard");
        }

        private static bool RobloxRunning()
        {
            Process[] processes = Process.GetProcesses();
            try
            {
                return processes.Any(p => p.ProcessName.StartsWith("Roblox", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                foreach (Process p in processes)
                    p.Dispose();
            }
        }

        private static List<string> RecentlyStarted(TimeSpan window)
        {
            var result = new List<string>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int self = Environment.ProcessId;

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == self || process.ProcessName.Equals("PhasmaStrap", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (DateTime.Now - process.StartTime > window)
                        continue;

                    string path = process.MainModule?.FileName ?? process.ProcessName;
                    if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.Add(path);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return result.Distinct().Take(5).ToList();
        }

        private static void OnSignInFileOpened()
        {
            if (RobloxRunning() || OwnAccessWithin(TimeSpan.FromSeconds(5)))
                return;

            List<string> suspects = RecentlyStarted(TimeSpan.FromMinutes(3));

            string detail = "Something opened Roblox's sign-in file while Roblox wasn't running. That's what login stealers read. "
                + (suspects.Count > 0
                    ? "Started in the last few minutes: " + string.Join(", ", suspects.Select(Path.GetFileName)) + ". "
                    : "")
                + "If you don't recognise it, sign out of all sessions on roblox.com (Settings > Security) and change your password.";

            GuardState state = Load();
            Record(state, "Your Roblox sign-in file was opened", detail);
            Save(state);
        }

        private static Mutex? _guardMutex;

        public static void RunBackground()
        {
            _guardMutex = new Mutex(true, @"Local\PhasmaStrapAccountGuard", out bool createdNew);
            if (!createdNew)
            {
                App.Logger.WriteLine(LOG_IDENT, "The guard is already running");
                App.Terminate();
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Background guard started");

            ProcessName.Set("PhasmaStrap Account Guard");

            var stop = new CancellationTokenSource();

            var watcher = new Thread(() => WatchSignInFile(stop.Token)) { IsBackground = true, Name = "AccountGuard.SignInFile" };
            watcher.Start();

            Task.Run(async () =>
            {
                DateTime nextScan = DateTime.MinValue;

                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        if (!BackgroundWanted)
                            break;

                        if (DateTime.UtcNow >= nextScan)
                        {
                            ScanAndNotify();
                            nextScan = DateTime.UtcNow.AddMinutes(5);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Guard loop: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30));
                }

                App.Logger.WriteLine(LOG_IDENT, "Background guard turned off - exiting");
                stop.Cancel();
                App.Current.Dispatcher.Invoke(() => App.Terminate());
            });
        }

        private static void WatchSignInFile(CancellationToken stop)
        {
            string path = Integrations.RobloxCookie.LiveCookiesDatPath;

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!File.Exists(path) || RobloxRunning())
                    {
                        stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                        continue;
                    }

                    switch (Oplock.WaitForOtherOpen(path, stop, () => RobloxRunning()))
                    {
                        case Oplock.Result.Opened:
                            OnSignInFileOpened();
                            stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                            break;

                        case Oplock.Result.NotGranted:

                            stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Sign-in file watch: {ex.Message}");
                    stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                }
            }
        }

        private static class Oplock
        {
            public enum Result { Opened, NotGranted, Stopped }

            public static Result WaitForOtherOpen(string path, CancellationToken stop, Func<bool> giveUp)
            {
                using SafeFileHandle handle = CreateFile(path, 0x80 , 7 , IntPtr.Zero, 3 , 0x40000000 , IntPtr.Zero);
                if (handle.IsInvalid)
                    return Result.NotGranted;

                var input = new REQUEST_OPLOCK_INPUT_BUFFER
                {
                    StructureVersion = 1,
                    StructureLength = (ushort)Marshal.SizeOf<REQUEST_OPLOCK_INPUT_BUFFER>(),
                    RequestedOplockLevel = 1 | 2 | 4,
                    Flags = 1,
                };

                using var done = new ManualResetEvent(false);
                var overlapped = new NativeOverlapped { EventHandle = done.SafeWaitHandle.DangerousGetHandle() };

                IntPtr inPtr = Marshal.AllocHGlobal(Marshal.SizeOf<REQUEST_OPLOCK_INPUT_BUFFER>());
                IntPtr outPtr = Marshal.AllocHGlobal(Marshal.SizeOf<REQUEST_OPLOCK_OUTPUT_BUFFER>());
                IntPtr ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());

                try
                {
                    Marshal.StructureToPtr(input, inPtr, false);
                    Marshal.StructureToPtr(new REQUEST_OPLOCK_OUTPUT_BUFFER(), outPtr, false);
                    Marshal.StructureToPtr(overlapped, ovPtr, false);

                    bool ok = DeviceIoControl(handle, 0x00090240 , inPtr, (uint)Marshal.SizeOf<REQUEST_OPLOCK_INPUT_BUFFER>(), outPtr, (uint)Marshal.SizeOf<REQUEST_OPLOCK_OUTPUT_BUFFER>(), out _, ovPtr);
                    if (ok || Marshal.GetLastWin32Error() != 997 )
                        return Result.NotGranted;

                    while (true)
                    {
                        if (done.WaitOne(TimeSpan.FromSeconds(2)))
                            return Result.Opened;

                        if (stop.IsCancellationRequested || giveUp())
                        {
                            CancelIoEx(handle, ovPtr);
                            done.WaitOne(TimeSpan.FromSeconds(1));
                            return Result.Stopped;
                        }
                    }
                }
                finally
                {
                    handle.Dispose();
                    Marshal.FreeHGlobal(inPtr);
                    Marshal.FreeHGlobal(outPtr);
                    Marshal.FreeHGlobal(ovPtr);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct REQUEST_OPLOCK_INPUT_BUFFER
            {
                public ushort StructureVersion;
                public ushort StructureLength;
                public uint RequestedOplockLevel;
                public uint Flags;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct REQUEST_OPLOCK_OUTPUT_BUFFER
            {
                public ushort StructureVersion;
                public ushort StructureLength;
                public uint OriginalOplockLevel;
                public uint NewOplockLevel;
                public uint Flags;
                public uint AccessMode;
                public ushort ShareMode;
            }

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr inBuffer, uint inSize, IntPtr outBuffer, uint outSize, out uint returned, IntPtr overlapped);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
        }
    }
}
