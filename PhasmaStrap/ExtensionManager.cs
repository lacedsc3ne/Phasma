using PhasmaStrap.Models;

namespace PhasmaStrap
{
    public static class ExtensionManager
    {
        public static readonly IReadOnlyList<Extension> KnownExtensions = new List<Extension>
        {
            new()
            {
                Id = "RiShade",
                DisplayName = "RiShade",
                Description = "A ReShade-style visual shader overlay for Roblox.",
                ExecutableName = "RiShade.exe"
            },
            new()
            {
                Id = "Fleasion",
                DisplayName = "Fleasion",
                Description = "An asset injection tool for custom textures/models.",
                ExecutableName = "Fleasion.exe"
            },
            // Rojo is not listed here - PhasmaStrap can install and manage it directly
            // (see RojoManager / the dedicated card on the Extensions page), so it doesn't
            // need the generic "browse to an existing install" flow the others use.
            new()
            {
                Id = "RobloxApiDumpTool",
                DisplayName = "Roblox API Dump Tool",
                Description = "Dumps the current Roblox API surface for reference.",
                ExecutableName = "RobloxApiDumpTool.exe"
            }
        };

        public static string? GetSavedPath(string id)
        {
            if (App.Settings.Prop.ExtensionPaths.TryGetValue(id, out string? path) && File.Exists(path))
                return path;

            return null;
        }

        public static void SetSavedPath(string id, string path)
        {
            App.Settings.Prop.ExtensionPaths[id] = path;
        }

        public static void ClearSavedPath(string id)
        {
            App.Settings.Prop.ExtensionPaths.Remove(id);
        }

        public static bool IsInstalled(string id) => GetSavedPath(id) is not null;

        public static bool Launch(string id)
        {
            const string LOG_IDENT = "ExtensionManager::Launch";

            string? path = GetSavedPath(id);
            if (path is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No saved path for extension {id}");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
                App.Logger.WriteLine(LOG_IDENT, $"Launched extension {id} from {path}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }
    }
}
