namespace PhasmaStrap.Networking
{
    /// <summary>
    /// Marker-based editor for the Windows hosts file (%SystemRoot%\System32\drivers\etc\hosts), used
    /// exclusively by <see cref="Integrations.ClassicHostRedirect"/>. Named/scoped separately from the
    /// existing <see cref="HostsFileManager"/> (which manages a single fixed block of proxy-spoof
    /// hostnames) rather than folded into it, matching the same self-contained-per-feature convention
    /// already used by <see cref="Integrations.TelemetryBlocker"/> - each hosts-editing feature owns its
    /// own block/marker so they can coexist safely on the same physical file without a shared class
    /// needing to know about every consumer's hostname list up front.
    /// </summary>
    internal static class ClassicHostsFile
    {
        private const string LOG_IDENT = "ClassicHostsFile";

        private static readonly SemaphoreSlim s_mutationGate = new(1, 1);

        public static string HostsPath => Path.Combine(Paths.System, "drivers", "etc", "hosts");

        /// <summary>
        /// Returns true if any line in the hosts file contains the given marker comment.
        /// </summary>
        public static bool IsMarkerPresent(string marker)
        {
            try
            {
                string path = HostsPath;
                if (!File.Exists(path))
                    return false;

                return File.ReadAllLines(path).Any(line => line.Contains(marker, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Appends "&lt;address&gt; &lt;host&gt; &lt;marker&gt;" lines for every host in <paramref name="hosts"/>,
        /// after first removing any existing lines carrying the same marker. Refuses to write if doing so would
        /// drop any unrelated (non-marker) line already present in the file, since the hosts file is shared with
        /// the rest of the system and other software may have entries in it.
        /// </summary>
        public static bool Apply(string marker, string address, IEnumerable<string> hosts)
        {
            s_mutationGate.Wait();
            try
            {
                List<string> lines = ReadWithoutMarker(marker);

                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.Add(string.Empty);

                foreach (string host in hosts)
                    lines.Add($"{address} {host} {marker}");

                return WriteSafely(marker, lines);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Apply", ex);
                return false;
            }
            finally
            {
                s_mutationGate.Release();
            }
        }

        /// <summary>
        /// Removes every line carrying the given marker. Returns true if the file no longer contains the
        /// marker afterwards (including when it never did).
        /// </summary>
        public static bool Remove(string marker)
        {
            s_mutationGate.Wait();
            try
            {
                string path = HostsPath;
                if (!File.Exists(path))
                    return true;

                string[] all = File.ReadAllLines(path);
                string[] kept = all.Where(line => !line.Contains(marker, StringComparison.Ordinal)).ToArray();

                if (kept.Length == all.Length)
                    return true;

                return WriteSafely(marker, kept.ToList());
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Remove", ex);
                return false;
            }
            finally
            {
                s_mutationGate.Release();
            }
        }

        private static List<string> ReadWithoutMarker(string marker)
        {
            string path = HostsPath;
            if (!File.Exists(path))
                return new List<string>();

            return File.ReadAllLines(path).Where(line => !line.Contains(marker, StringComparison.Ordinal)).ToList();
        }

        // refuses to write if it would silently drop lines that don't belong to us (i.e. something else's hosts entries)
        private static bool WriteSafely(string marker, List<string> newLines)
        {
            string path = HostsPath;

            int existingForeign = File.Exists(path)
                ? File.ReadAllLines(path).Count(line => !line.Contains(marker, StringComparison.Ordinal))
                : 0;

            int keepingForeign = newLines.Count(line => !line.Contains(marker, StringComparison.Ordinal));

            if (existingForeign > 0 && keepingForeign < existingForeign)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Refused to write the hosts file, it would have dropped {existingForeign - keepingForeign} unrelated line(s)");
                return false;
            }

            File.WriteAllLines(path, newLines);
            return true;
        }

        public static void FlushDnsCache()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                process?.WaitForExit(3000);
            }
            catch
            {
            }
        }

        public static bool IsCurrentProcessAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
