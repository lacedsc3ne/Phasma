namespace PhasmaStrap.Integrations
{
    /// <summary>
    /// Launches and supervises PhasmaStrap.Server.exe (the ported ClientServer subsystem, i.e. PhasmaStrap.Server
    /// project - see PhasmaStrap.Server.csproj) as a child process, and coordinates it with
    /// <see cref="ClassicHostRedirect"/> so the roblox.com hosts redirect is never left dangling if the server
    /// process or PhasmaStrap itself dies unexpectedly.
    /// </summary>
    public static class ClassicServerManager
    {
        private const string LOG_IDENT = "ClassicServerManager";

        private static readonly object s_lock = new();

        private static Process? s_serverProcess;

        private static Thread? s_redirectHolderThread;

        public static bool IsRunning
        {
            get
            {
                lock (s_lock)
                {
                    return s_serverProcess is { HasExited: false };
                }
            }
        }

        /// <summary>
        /// Starts PhasmaStrap.Server.exe for the given classic client, applies the hosts redirect (elevating if
        /// necessary), and starts the background thread that will release the redirect once the classic session
        /// ends. Returns null on success, or a user-facing error message on failure.
        /// </summary>
        public static string? Start(string client)
        {
            const string LOG_IDENT_LOCAL = LOG_IDENT + "::Start";

            if (!App.Settings.Prop.ClassicClientEnabled)
                return "The classic client / private server feature is disabled in settings.";

            lock (s_lock)
            {
                if (s_serverProcess is { HasExited: false })
                    return null; // already running

                if (!ClassicClients.ServerEngineInstalled)
                    return "The classic private server engine (PhasmaStrap.Server.exe) was not found at " + ClassicClients.ServerPath + ".";

                try
                {
                    var startInfo = new ProcessStartInfo(ClassicClients.ServerPath, $"--client={client} --pid={Environment.ProcessId}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = ClassicClients.Root
                    };

                    s_serverProcess = Process.Start(startInfo);
                    if (s_serverProcess is null)
                        return "Failed to start the classic private server process.";

                    App.Logger.WriteLine(LOG_IDENT_LOCAL, $"Started {ClassicClients.ServerExecutableName} for client {client} (pid {s_serverProcess.Id})");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
                    return "Failed to start the classic private server process: " + ex.Message;
                }
            }

            string? redirectError = ClassicHostRedirect.Set(true);
            if (redirectError != null)
            {
                App.Logger.WriteLine(LOG_IDENT_LOCAL, "Redirect failed, stopping server process: " + redirectError);
                Stop();
                return redirectError;
            }

            lock (s_lock)
            {
                if (s_redirectHolderThread is null)
                {
                    s_redirectHolderThread = new Thread(() => ClassicHostRedirect.RemoveWhenSessionEnds())
                    {
                        IsBackground = true,
                        Name = "ClassicRedirectHolder"
                    };
                    s_redirectHolderThread.Start();
                }
            }

            return null;
        }

        /// <summary>
        /// Stops the server process (if running) and removes the hosts redirect. Safe to call multiple times,
        /// and safe to call from the process-exit / unhandled-exception handlers - it must never throw.
        /// </summary>
        public static void Stop()
        {
            const string LOG_IDENT_LOCAL = LOG_IDENT + "::Stop";

            lock (s_lock)
            {
                if (s_serverProcess is not null)
                {
                    try
                    {
                        if (!s_serverProcess.HasExited)
                        {
                            s_serverProcess.Kill(entireProcessTree: true);
                            s_serverProcess.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
                    }
                    finally
                    {
                        s_serverProcess.Dispose();
                        s_serverProcess = null;
                    }
                }
            }

            // always attempt to remove the redirect, even if we didn't think a server process was running -
            // this is the last line of defense against a stale hosts entry on app exit
            try
            {
                ClassicHostRedirect.Set(false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT_LOCAL, ex);
            }
        }
    }
}
