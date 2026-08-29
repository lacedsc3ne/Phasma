using System.Text.Json;

namespace PhasmaStrap.Integrations
{
    // Simplified port of Voidstrap's Voidstrap.Utility.ClassicClients (src/Voidstrap.App/Utility/ClassicClients.cs,
    // ~2255 lines in the original). This port carries over path/layout conventions, install detection, client
    // listing and process launching. It intentionally OMITS Voidstrap's engine/client acquisition pipeline
    // (GitHub release resolution, signed-digest download+extraction, local-folder staging, binary helper-DLL
    // patching, auto-update checking). That pipeline is specific to Voidstrap's own release infrastructure and
    // porting it verbatim would mean pointing PhasmaStrap at a third party's release feed. For PhasmaStrap, engine
    // and client files are expected to already be present under <ClassicClientInstallLocation>\data\clients\<code>,
    // matching the same on-disk layout Voidstrap uses (see ClientsDir/ConfigFileName below) - installation tooling
    // for populating that folder is follow-up work, not included in this port.
    public static class ClassicClients
    {
        private const string LOG_IDENT = "ClassicClients";

        public const string ServerExecutableName = "PhasmaStrap.Server.exe";
        public const string ConfigFileName = "PhasmaStrapClient_Config.json";

        private static readonly JsonSerializerOptions ConfigJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string Root
        {
            get
            {
                string custom = App.Settings.Prop.ClassicClientInstallLocation;
                return string.IsNullOrWhiteSpace(custom) ? Path.Combine(Paths.Base, "ClassicClients") : custom;
            }
        }

        public static string ClientsDir => Path.Combine(Root, "data", "clients");

        public static string ServerPath => Path.Combine(Root, ServerExecutableName);

        public static bool ServerEngineInstalled => File.Exists(ServerPath);

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
