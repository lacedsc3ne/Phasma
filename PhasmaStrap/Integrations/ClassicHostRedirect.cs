using PhasmaStrap.Networking;

namespace PhasmaStrap.Integrations
{
    /// <summary>
    /// Ported from Voidstrap's Voidstrap.Integrations.ClassicHostRedirect. Redirects roblox.com/www.roblox.com
    /// to 127.0.0.1 via the hosts file, so a classic/legacy Roblox client launched by PhasmaStrap talks to the
    /// locally running <see cref="ClassicServerManager"/> process instead of the live Roblox servers. All hosts
    /// file mutation goes through <see cref="Networking.ClassicHostsFile"/>.
    ///
    /// This is a significant behaviour change (spoofing name resolution for roblox.com itself), so it is gated
    /// behind Settings.Prop.ClassicClientEnabled, which defaults to false, and the redirect is only ever applied
    /// for the duration of a classic client session - see <see cref="RemoveWhenSessionEnds"/> and
    /// <see cref="CleanStaleRedirect"/> for how it is guaranteed to be cleaned up even if PhasmaStrap crashes or
    /// is killed.
    /// </summary>
    public static class ClassicHostRedirect
    {
        private const string LOG_IDENT = "ClassicHostRedirect";

        private const string Marker = "# PHASMASTRAP-CLASSIC";

        private const string Loopback = "127.0.0.1";

        public static readonly string[] Domains = new[]
        {
            "www.roblox.com",
            "roblox.com"
        };

        public static bool IsApplied() => ClassicHostsFile.IsMarkerPresent(Marker);

        public static bool ResolvesToLoopback()
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(Domains[0]);
                return addresses.Length > 0 && addresses.All(IPAddress.IsLoopback);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies or removes the redirect. If the current process is not elevated, relaunches PhasmaStrap
        /// with the "-classicredirect on/off" flag under UAC elevation (see App.OnStartup handling of
        /// LaunchSettings.ClassicRedirectFlag) to perform the actual hosts file write, then waits for it.
        /// Returns null on success, or a user-facing error message on failure.
        /// </summary>
        public static string? Set(bool enable)
        {
            if (!enable)
            {
                if (!IsApplied())
                    return null;
            }

            if (ClassicHostsFile.IsCurrentProcessAdministrator())
            {
                if (!enable)
                    return ClassicHostsFile.Remove(Marker) ? null : "PhasmaStrap could not restore normal Roblox name resolution.";

                if (!Apply())
                    return "PhasmaStrap could not point the classic Roblox domains at the local server. Check that the Windows hosts file is writable.";

                return null;
            }

            return RunElevated(enable);
        }

        public static bool Apply()
        {
            bool ok = ClassicHostsFile.Apply(Marker, Loopback, Domains);
            if (ok)
            {
                ClassicHostsFile.FlushDnsCache();
                App.Logger.WriteLine(LOG_IDENT, $"Pointed {Domains.Length} classic Roblox domain(s) at the local server");
            }
            return ok;
        }

        public static bool Remove()
        {
            bool ok = ClassicHostsFile.Remove(Marker);
            if (ok)
            {
                ClassicHostsFile.FlushDnsCache();
                App.Logger.WriteLine(LOG_IDENT, "Restored normal Roblox name resolution");
            }
            return ok;
        }

        /// <summary>
        /// Called once at startup (before any classic session begins). If a previous PhasmaStrap session
        /// crashed or was killed while the redirect was applied, and no classic client is currently running,
        /// clears the stale redirect so the user's normal Roblox access isn't broken.
        /// </summary>
        public static void CleanStaleRedirect()
        {
            try
            {
                if (!IsApplied())
                    return;

                if (ClassicClients.AnyClassicClientRunning())
                {
                    App.Logger.WriteLine(LOG_IDENT, "A classic client is still running, leaving the redirect in place");
                    return;
                }

                if (ClassicHostsFile.IsCurrentProcessAdministrator())
                {
                    App.Logger.WriteLine(LOG_IDENT, "A previous classic session left the redirect behind, clearing it");
                    Remove();
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, "A previous classic session left the redirect behind, requesting elevation to restore normal Roblox name resolution");
                string? failure = RunElevated(enable: false);
                if (failure != null)
                    App.Logger.WriteLine(LOG_IDENT, "Stale cleanup was not completed: " + failure);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::CleanStaleRedirect", ex);
            }
        }

        /// <summary>
        /// Blocks on a background thread until the classic client session ends (or never starts), then removes
        /// the redirect. This is the mechanism that guarantees the hosts file is not left permanently pointing
        /// roblox.com at localhost - it should be started right before/after a classic client is launched.
        /// </summary>
        public static void RemoveWhenSessionEnds()
        {
            try
            {
                App.Logger.WriteLine(LOG_IDENT, "Holding the redirect until the classic session ends");

                bool started = false;
                for (int i = 0; i < 240; i++)
                {
                    if (ClassicClients.AnyClassicClientRunning())
                    {
                        started = true;
                        break;
                    }
                    Thread.Sleep(500);
                }

                if (started)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Classic client detected, the redirect stays until it closes");
                    int idle = 0;
                    while (idle < 6)
                    {
                        idle = ClassicClients.AnyClassicClientRunning() ? 0 : idle + 1;
                        Thread.Sleep(500);
                    }
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, "No classic client appeared, releasing the redirect");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RemoveWhenSessionEnds", ex);
            }

            Set(false);
            App.Logger.WriteLine(LOG_IDENT, "Classic session finished, normal Roblox name resolution is back");
        }

        private static string? RunElevated(bool enable)
        {
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
                return "PhasmaStrap could not locate its own executable to request elevation.";

            try
            {
                string argument = enable ? "-classicredirect on" : "-classicredirect off";

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = argument,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                if (process is null)
                    return "Windows did not start the elevated PhasmaStrap helper.";

                process.WaitForExit(20000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                App.Logger.WriteLine(LOG_IDENT, "Elevation was declined, the classic client cannot reach the local server without it");
                return "PhasmaStrap needs administrator rights to point the classic Roblox domains at the local server. The elevation prompt was declined.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::RunElevated", ex);
                return "The elevated PhasmaStrap helper failed: " + ex.Message;
            }

            if (IsApplied() != enable)
            {
                return enable
                    ? "PhasmaStrap could not point the classic Roblox domains at the local server. Check that the Windows hosts file is writable."
                    : "PhasmaStrap could not restore normal Roblox name resolution.";
            }

            return null;
        }
    }
}
