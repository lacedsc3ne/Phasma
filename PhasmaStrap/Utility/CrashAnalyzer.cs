using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.RegularExpressions;

namespace PhasmaStrap.Utility
{
    public sealed class CrashReport
    {
        public DateTime WhenLocal { get; set; }
        public string LogFile { get; set; } = "";
        public bool CleanExit { get; set; }
        public double SessionMinutes { get; set; }

        public string Cause { get; set; } = "";
        public string Confidence { get; set; } = "";
        public List<string> Suggestions { get; set; } = new();
        public List<string> Evidence { get; set; } = new();

        public string FaultModule { get; set; } = "";
        public string ExceptionCode { get; set; } = "";
        public string DumpFile { get; set; } = "";
        public List<string> ForeignModules { get; set; } = new();
        public List<string> BlockedModules { get; set; } = new();
    }

    public static class CrashAnalyzer
    {
        public static Action<string>? Log;

        public sealed class Context
        {
            public int CustomFastFlags;
            public int ActiveMods;
            public List<string> DumpDirectories = new();
        }

        private static readonly Regex LineTime = new(@"^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z),", RegexOptions.Compiled);

        private static readonly (Regex Pattern, string Kind)[] LogSigns =
        {
            (new Regex(@"out of memory|bad_alloc|E_OUTOFMEMORY|std::bad_alloc|failed to allocate", RegexOptions.IgnoreCase | RegexOptions.Compiled), "memory"),
            (new Regex(@"DXGI_ERROR_DEVICE_(REMOVED|HUNG|RESET)|device (was )?(removed|lost)|D3D device lost", RegexOptions.IgnoreCase | RegexOptions.Compiled), "gpu"),
            (new Regex(@"\bfatal\b|unhandled exception|assertion failed|\bcrash(ed|ing)?\b(?!.*(Upload|inferred|Denied))", RegexOptions.IgnoreCase | RegexOptions.Compiled), "fatal"),
        };

        private static List<string> ReadTail(string file, int maxBytes)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxBytes)
                stream.Seek(-maxBytes, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        }

        private static DateTime? TimeOf(string line)
        {
            Match match = LineTime.Match(line);
            return match.Success && DateTime.TryParse(match.Groups[1].Value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime time) ? time : null;
        }

        private static string Shorten(string line)
        {
            int bracket = line.IndexOf('[');
            string text = bracket > 0 && bracket < 70 ? line[bracket..] : line;
            return text.Length > 220 ? text[..220] + "..." : text;
        }

        public static CrashReport Analyze(string logFile, Context context)
        {
            var report = new CrashReport { LogFile = logFile, WhenLocal = File.GetLastWriteTime(logFile) };

            List<string> tail = ReadTail(logFile, 600_000);

            DateTime? first = null, last = null;
            try
            {
                using var head = new StreamReader(new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                for (int i = 0; i < 40 && first is null && head.ReadLine() is string line; i++)
                    first = TimeOf(line);
            }
            catch
            {
            }

            for (int i = tail.Count - 1; i >= 0 && last is null; i--)
                last = TimeOf(tail[i]);

            if (last is not null)
                report.WhenLocal = last.Value.ToLocalTime();
            if (first is not null && last is not null)
                report.SessionMinutes = (last.Value - first.Value).TotalMinutes;

            report.CleanExit = tail.TakeLast(40).Any(l => l.Contains("handler was destroyed", StringComparison.OrdinalIgnoreCase));

            foreach (string line in tail)
            {
                Match blocked = BlockedImage.Match(line);

                if (!blocked.Success)
                    continue;

                string name = Path.GetFileName(blocked.Groups["path"].Value.Replace('/', '\\'));

                if (name.Length > 0 && !report.BlockedModules.Contains(name, StringComparer.OrdinalIgnoreCase))
                    report.BlockedModules.Add(name);
            }

            var signs = new Dictionary<string, string>();
            foreach (string line in tail.TakeLast(1500))
            {
                if (line.Contains("HttpTraceError") || line.Contains("Denied local configuration"))
                    continue;

                foreach ((Regex pattern, string kind) in LogSigns)
                {
                    if (pattern.IsMatch(line))
                        signs[kind] = Shorten(line);
                }
            }

            DateTime end = (last ?? File.GetLastWriteTimeUtc(logFile)).ToUniversalTime();
            List<WindowsEvent> events = ReadEvents(end.AddMinutes(-3), end.AddMinutes(4));

            DumpInfo? dump = FindDump(context.DumpDirectories, end);
            if (dump is not null)
            {
                report.DumpFile = dump.File;
                report.FaultModule = dump.FaultModule;
                report.ExceptionCode = dump.ExceptionCode != 0 ? $"0x{dump.ExceptionCode:X8}" : "";
                report.ForeignModules = dump.ForeignModules;
            }

            WindowsEvent? fault = events.FirstOrDefault(e => e.Kind == "fault");
            if (fault is not null)
            {
                if (report.FaultModule.Length == 0) report.FaultModule = fault.Module;
                if (report.ExceptionCode.Length == 0) report.ExceptionCode = fault.Code;
            }

            Conclude(report, context, signs, events);
            return report;
        }

        private static bool IsGpuDriver(string module)
        {
            string m = module.ToLowerInvariant();
            return m.StartsWith("nv") && (m.Contains("wgf2um") || m.Contains("d3dum") || m.Contains("oglv") || m.Contains("ldumd") || m.Contains("api64") || m.Contains("ngx"))
                || m.StartsWith("ati") || m.StartsWith("amd") || m.StartsWith("aticfx") || m.StartsWith("igd") || m.StartsWith("igc") || m.StartsWith("ig9") || m.StartsWith("ig7") || m.StartsWith("igxe");
        }

        private static readonly (string Match, string Program)[] KnownOverlays =
        {
            ("discordhook", "Discord's in-game overlay"), ("discord_hook", "Discord's in-game overlay"),
            ("rtsshooks", "RivaTuner / MSI Afterburner's overlay"), ("gameoverlayrenderer", "the Steam overlay"),
            ("graphics-hook", "OBS game capture"), ("medal", "Medal"), ("overwolf", "Overwolf"), ("ow-graphics", "Overwolf"),
            ("reshade", "ReShade"), ("bdcam", "Bandicam"), ("fraps", "Fraps"), ("nahimic", "Nahimic audio"), ("sonic", "Sonic audio software"),
            ("easyhook", "an injected hook library"), ("minhook", "an injected hook library"),
            ("nvspcap", "the NVIDIA overlay (GeForce Experience / NVIDIA App)"), ("nvcamera", "the NVIDIA overlay"),
            ("amf-capture", "the AMD overlay"), ("amdow", "the AMD overlay"), ("igoproxy", "the Intel overlay"),
            ("xboxgamebar", "the Xbox Game Bar"), ("gameinput", "the Xbox Game Bar"),
        };

        private static readonly Regex BlockedImage = new(@"Blocked DLL:\s*(?<path>\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string? OverlayName(string module)
        {
            string m = module.ToLowerInvariant();
            foreach ((string match, string program) in KnownOverlays)
            {
                if (m.Contains(match))
                    return program;
            }
            return null;
        }

        private static void Conclude(CrashReport report, Context context, Dictionary<string, string> signs, List<WindowsEvent> events)
        {
            foreach (WindowsEvent e in events)
                report.Evidence.Add($"Windows {e.Log} log, {e.TimeLocal:T}: {e.Summary}");
            foreach (KeyValuePair<string, string> sign in signs)
                report.Evidence.Add($"Roblox log: {sign.Value}");
            if (report.DumpFile.Length > 0)
                report.Evidence.Add($"Crash dump {Path.GetFileName(report.DumpFile)}: exception {report.ExceptionCode} in {(report.FaultModule.Length > 0 ? report.FaultModule : "an unknown module")}");

            bool anyEvidence = events.Count > 0 || signs.Count > 0 || report.DumpFile.Length > 0;

            if (report.CleanExit && !anyEvidence)
            {
                report.Cause = "This was a normal exit, not a crash - the log ends with Roblox's regular shutdown.";
                return;
            }

            string tweaks = context.CustomFastFlags > 0 || context.ActiveMods > 0
                ? $"You run {context.CustomFastFlags} custom FastFlag(s) and {context.ActiveMods} mod(s): switch them off for a session to rule them out - flags that were fine can break with a Roblox update."
                : "";

            if (events.Any(e => e.Kind == "power"))
            {
                report.Cause = "The whole PC lost power or reset - this was not Roblox crashing.";
                report.Confidence = "Strong";
                report.Suggestions.Add("If it happens under load: power supply, overheating or an unstable overclock / undervolt. Check temperatures while playing.");
            }
            else if (events.Any(e => e.Kind == "hardware"))
            {
                report.Cause = "Windows logged a hardware error at that moment (CPU, memory or PCIe).";
                report.Confidence = "Likely";
                report.Suggestions.Add("Undo any overclock, undervolt or memory XMP/EXPO profile and see whether the crashes stop. If they do not, run a memory test.");
            }
            else if (events.Any(e => e.Kind == "memory") || signs.ContainsKey("memory"))
            {
                report.Cause = "The PC (or Roblox) ran out of memory.";
                report.Confidence = events.Any(e => e.Kind == "memory") ? "Strong" : "Likely";
                report.Suggestions.Add("Close browsers and other games before playing; big experiences can take 4-6 GB on their own.");
                report.Suggestions.Add("Make sure Windows manages the page file (System > Advanced system settings > Performance > Advanced > Virtual memory) - a disabled or tiny page file causes exactly this.");
                report.Suggestions.Add("PhasmaStrap's memory cleaner and a lower texture quality reduce the peak.");
            }
            else if (events.Any(e => e.Kind == "gpu") || signs.ContainsKey("gpu"))
            {
                report.Cause = "The graphics driver stopped responding and Windows reset it, which takes every 3D program down with it.";
                report.Confidence = events.Any(e => e.Kind == "gpu") ? "Strong" : "Likely";
                report.Suggestions.Add("Undo any GPU overclock or undervolt first - it is the most common cause.");
                report.Suggestions.Add("Install the current graphics driver with the \"clean installation\" option.");
                report.Suggestions.Add("Watch the GPU temperature; above about 85 degrees the driver starts to give up.");
            }
            else if (report.FaultModule.Length > 0 && IsGpuDriver(report.FaultModule))
            {
                report.Cause = $"Roblox crashed inside the graphics driver ({report.FaultModule}).";
                report.Confidence = "Strong";
                report.Suggestions.Add("Update the graphics driver (clean installation). If it started right after a driver update, go back one version instead.");
                if (tweaks.Length > 0) report.Suggestions.Add("Rendering FastFlags (graphics API, MSAA, lighting technology) are handled by this driver. " + tweaks);
            }
            else if (report.FaultModule.Length > 0 && OverlayName(report.FaultModule) is string overlay)
            {
                report.Cause = $"Roblox crashed inside {overlay} ({report.FaultModule}), which had loaded itself into the game.";
                report.Confidence = "Strong";
                report.Suggestions.Add($"Switch that program's overlay / game capture off for Roblox, or close it while you play.");
            }
            else if (events.Any(e => e.Kind == "hang"))
            {
                report.Cause = "Roblox froze and Windows closed it as \"not responding\".";
                report.Confidence = "Likely";
                report.Suggestions.Add("A freeze during loading usually means the disk or the connection stalled; a freeze in the middle of play points at the graphics driver or memory.");
                if (tweaks.Length > 0) report.Suggestions.Add(tweaks);
            }
            else if (report.FaultModule.Length > 0)
            {
                bool own = report.FaultModule.StartsWith("RobloxPlayer", StringComparison.OrdinalIgnoreCase);
                string code = report.ExceptionCode.ToUpperInvariant();

                report.Cause = own
                    ? $"Roblox crashed in its own code ({(code.Length > 0 ? ExplainCode(code) : "no exception code recorded")})."
                    : $"Roblox crashed inside {report.FaultModule}{(code.Length > 0 ? $" ({ExplainCode(code)})" : "")}.";
                report.Confidence = "Likely";

                if (tweaks.Length > 0)
                    report.Suggestions.Add(tweaks);

                report.Suggestions.Add(own
                    ? "If it is always the same game, it is that game (or a Roblox bug it triggers) - nothing on your PC. If it is every game, let PhasmaStrap reinstall Roblox (Deployment > force reinstall)."
                    : $"Look up what {report.FaultModule} belongs to - a driver or a program that hooks into games - and update or remove it.");
            }
            else if (signs.ContainsKey("fatal"))
            {
                report.Cause = "Roblox reported a fatal error in its own log before it closed.";
                report.Confidence = "Likely";
                if (tweaks.Length > 0) report.Suggestions.Add(tweaks);
                report.Suggestions.Add("The line is under Evidence; searching for it usually finds whether it is a known Roblox problem.");
            }
            else
            {
                report.Cause = "Roblox closed without its normal shutdown, but neither Roblox nor Windows wrote down why.";
                report.Confidence = "Unclear";
                report.Suggestions.Add("That is what it looks like when the process is ended from outside: Task Manager, a \"game booster\", antivirus, or Roblox's anti-cheat closing the game because it disliked another program.");
                if (tweaks.Length > 0) report.Suggestions.Add(tweaks);
            }

            if (report.BlockedModules.Count > 0)
            {
                List<string> named = report.BlockedModules.Select(m => OverlayName(m) is string p ? $"{m} ({p})" : m).Take(10).ToList();
                report.Evidence.Add($"Roblox blocked these from loading into the game: {string.Join(", ", named)}");

                string? known = report.BlockedModules.Select(OverlayName).FirstOrDefault(n => n is not null);

                if (report.Confidence != "Strong")
                {
                    report.Cause = known is null
                        ? "Roblox's anti-cheat blocked another program from loading into the game."
                        : $"Roblox's anti-cheat blocked {known} from loading into the game.";
                    report.Confidence = "Likely";
                }

                report.Suggestions.Add(known is null
                    ? "Turn off in-game overlays and capture software, then play a session to see if it stops."
                    : $"Turn off {known} and play a session. That is what Roblox objected to, and it is not PhasmaStrap.");
            }

            if (report.ForeignModules.Count > 0)
            {
                List<string> named = report.ForeignModules.Select(m => OverlayName(m) is string p ? $"{m} ({p})" : m).Take(10).ToList();
                report.Evidence.Add($"Third-party DLLs that were loaded inside Roblox: {string.Join(", ", named)}");

                if (report.Confidence != "Strong")
                    report.Suggestions.Add("Programs that load themselves into the game (listed under Evidence) are frequent crash causes and what the anti-cheat objects to - try a session without them.");
            }
        }

        private static string ExplainCode(string code) => code switch
        {
            "0XC0000005" => "access violation - it touched memory it must not",
            "0XC0000409" => "fail-fast / stack buffer check - the process ended itself deliberately, which is also how anti-tamper protection reacts",
            "0XC000001D" => "illegal instruction - an unstable overclock or a CPU feature the PC lacks",
            "0XC00000FD" => "stack overflow",
            "0XE06D7363" => "an unhandled C++ exception",
            "0XC0000374" => "heap corruption",
            "0X80000003" => "a deliberate breakpoint - an internal assertion failed",
            "0XC000013A" => "ended by Ctrl+C / console close",
            _ => $"exception {code.Replace("0X", "0x")}",
        };

        private sealed class WindowsEvent
        {
            public string Log = "", Kind = "", Summary = "", Module = "", Code = "";
            public DateTime TimeLocal;
        }

        private static List<WindowsEvent> ReadEvents(DateTime fromUtc, DateTime toUtc)
        {
            var result = new List<WindowsEvent>();
            string range = $"TimeCreated[@SystemTime>='{fromUtc:yyyy-MM-ddTHH:mm:ss}.000Z' and @SystemTime<='{toUtc:yyyy-MM-ddTHH:mm:ss}.999Z']";

            void Query(string log, string providers, Func<EventRecord, WindowsEvent?> map)
            {
                try
                {
                    var query = new EventLogQuery(log, PathType.LogName, $"*[System[({providers}) and {range}]]");
                    using var reader = new EventLogReader(query);

                    for (EventRecord? record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
                    {
                        using (record)
                        {
                            WindowsEvent? mapped = map(record);
                            if (mapped is not null)
                            {
                                mapped.Log = log;
                                mapped.TimeLocal = record.TimeCreated ?? DateTime.Now;
                                result.Add(mapped);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not read the {log} event log: {ex.Message}");
                }
            }

            static string Property(EventRecord record, int index) =>
                index < record.Properties.Count ? record.Properties[index].Value?.ToString() ?? "" : "";

            Query("Application", "Provider[@Name='Application Error'] or Provider[@Name='Application Hang']", record =>
            {
                string app = Property(record, 0);
                if (!app.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (record.ProviderName == "Application Hang")
                    return new WindowsEvent { Kind = "hang", Summary = $"{app} stopped responding and was closed" };

                string module = Property(record, 3), code = Property(record, 6);
                if (code.Length > 0 && !code.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    code = "0x" + code;

                return new WindowsEvent { Kind = "fault", Module = module, Code = code, Summary = $"{app} faulted in {module}, exception {code}" };
            });

            Query("System", "Provider[@Name='Display'] or Provider[@Name='nvlddmkm'] or Provider[@Name='amdkmdag'] or Provider[@Name='amdwddmg'] or Provider[@Name='igfx'] or Provider[@Name='Microsoft-Windows-Resource-Exhaustion-Detector'] or Provider[@Name='Microsoft-Windows-Kernel-Power'] or Provider[@Name='Microsoft-Windows-WHEA-Logger']", record =>
            {
                switch (record.ProviderName)
                {
                    case "Microsoft-Windows-Resource-Exhaustion-Detector":
                        return new WindowsEvent { Kind = "memory", Summary = "Windows diagnosed a low virtual memory condition" };
                    case "Microsoft-Windows-Kernel-Power":
                        return record.Id == 41 ? new WindowsEvent { Kind = "power", Summary = "the system restarted without shutting down cleanly" } : null;
                    case "Microsoft-Windows-WHEA-Logger":
                        return new WindowsEvent { Kind = "hardware", Summary = $"hardware error reported by the platform (event {record.Id})" };
                    case "Display":
                        return record.Id == 4101 ? new WindowsEvent { Kind = "gpu", Summary = "the display driver stopped responding and was recovered" } : null;
                    default:
                        return new WindowsEvent { Kind = "gpu", Summary = $"graphics driver error ({record.ProviderName}, event {record.Id})" };
                }
            });

            return result.OrderBy(e => e.TimeLocal).ToList();
        }

        public sealed class DumpInfo
        {
            public string File = "";
            public uint ExceptionCode;
            public ulong ExceptionAddress;
            public string FaultModule = "";
            public List<string> Modules = new();
            public List<string> ForeignModules = new();
        }

        private static DumpInfo? FindDump(List<string> directories, DateTime crashUtc)
        {
            foreach (string directory in directories)
            {
                try
                {
                    if (!Directory.Exists(directory))
                        continue;

                    FileInfo? file = new DirectoryInfo(directory).EnumerateFiles("*.*dmp", SearchOption.AllDirectories)
                        .Where(f => Math.Abs((f.LastWriteTimeUtc - crashUtc).TotalMinutes) < 5)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (file is not null && ParseDump(file.FullName) is DumpInfo info)
                        return info;
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Looking for dumps in {directory}: {ex.Message}");
                }
            }

            return null;
        }

        public static DumpInfo? ParseDump(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new BinaryReader(stream);

                if (reader.ReadUInt32() != 0x504D444D)
                    return null;

                reader.ReadUInt32();
                uint streams = reader.ReadUInt32();
                uint directory = reader.ReadUInt32();

                var info = new DumpInfo { File = path };
                var modules = new List<(ulong Base, uint Size, string Name)>();

                for (uint i = 0; i < streams && i < 512; i++)
                {
                    stream.Position = directory + i * 12;
                    uint type = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();
                    uint rva = reader.ReadUInt32();

                    if (type == 6 && size >= 8 + 32)
                    {
                        stream.Position = rva + 8;
                        info.ExceptionCode = reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt64();
                        info.ExceptionAddress = reader.ReadUInt64();
                    }
                    else if (type == 4)
                    {
                        stream.Position = rva;
                        uint count = reader.ReadUInt32();

                        for (uint m = 0; m < count && m < 2048; m++)
                        {
                            stream.Position = rva + 4 + m * 108;
                            ulong baseOfImage = reader.ReadUInt64();
                            uint sizeOfImage = reader.ReadUInt32();
                            reader.ReadUInt32();
                            reader.ReadUInt32();
                            uint nameRva = reader.ReadUInt32();

                            stream.Position = nameRva;
                            uint length = reader.ReadUInt32();
                            string name = length is > 0 and < 2048 ? Encoding.Unicode.GetString(reader.ReadBytes((int)length)) : "";

                            modules.Add((baseOfImage, sizeOfImage, name));
                        }
                    }
                }

                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string? gameFolder = modules.Select(m => m.Name).FirstOrDefault(n => n.EndsWith("RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase)) is string exe ? Path.GetDirectoryName(exe) : null;

                foreach (var module in modules)
                {
                    string file = Path.GetFileName(module.Name);
                    info.Modules.Add(file);

                    if (info.ExceptionAddress >= module.Base && info.ExceptionAddress < module.Base + module.Size)
                        info.FaultModule = file;

                    bool system = module.Name.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
                    bool game = gameFolder is not null && module.Name.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase);

                    if (!system && !game && file.Length > 0 && !IsGpuDriver(file))
                        info.ForeignModules.Add(file);
                }

                return info;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Could not read {path}: {ex.Message}");
                return null;
            }
        }
    }
}
