namespace PhasmaStrap.Integrations
{
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

        public static string? Start(string client)
        {
            const string LOG_IDENT_LOCAL = LOG_IDENT + "::Start";

            if (!App.Settings.Prop.ClassicClientEnabled)
                return "The classic client / private server feature is disabled in settings.";

            lock (s_lock)
            {
                if (s_serverProcess is { HasExited: false })
                    return null;

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
