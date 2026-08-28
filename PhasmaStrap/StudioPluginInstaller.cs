namespace PhasmaStrap
{
    // writes the PhasmaStrap Studio companion plugin into Roblox's local plugins folder.
    // Ported from Voidstrap's StudioPluginInstaller, without its icon-file installation step
    // (that copied a texture into every detected Studio install directory - skipped here
    // since there's no matching bundled icon asset for it to install).
    public static class StudioPluginInstaller
    {
        private const string LOG_IDENT = "StudioPluginInstaller";

        private const string PluginFileName = "PhasmaStrapStudio.lua";

        private static string PluginsFolder => Path.Combine(Paths.LocalAppData, "Roblox", "Plugins");

        private static string PluginFile => Path.Combine(PluginsFolder, PluginFileName);

        public static string PluginPath => PluginFile;

        public static bool IsInstalled
        {
            get
            {
                try
                {
                    return File.Exists(PluginFile);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static bool IsStudioRunning()
        {
            try
            {
                return Process.GetProcessesByName(App.RobloxStudioAppName).Length != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void EnsureInstalled(bool force = false)
        {
            const string LOG_IDENT = "StudioPluginInstaller::EnsureInstalled";

            try
            {
                if (!App.Settings.Prop.StudioPluginEnabled || (!force && !IsStudioRunning()))
                    return;

                Directory.CreateDirectory(PluginsFolder);

                string source = Resource.GetString("PhasmaStrapStudio.luau").GetAwaiter().GetResult();

                bool needsWrite = true;
                if (File.Exists(PluginFile))
                {
                    try
                    {
                        needsWrite = File.ReadAllText(PluginFile) != source;
                    }
                    catch (Exception)
                    {
                        needsWrite = true;
                    }
                }

                if (needsWrite)
                {
                    File.WriteAllText(PluginFile, source);
                    App.Logger.WriteLine(LOG_IDENT, $"PhasmaStrap Studio plugin written to {PluginFile}");
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Install failed: {ex.Message}");
            }
        }

        public static bool Reinstall()
        {
            const string LOG_IDENT = "StudioPluginInstaller::Reinstall";

            try
            {
                Directory.CreateDirectory(PluginsFolder);
                string source = Resource.GetString("PhasmaStrapStudio.luau").GetAwaiter().GetResult();
                File.WriteAllText(PluginFile, source);
                App.Logger.WriteLine(LOG_IDENT, $"PhasmaStrap Studio plugin written to {PluginFile}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Install failed: {ex.Message}");
                return false;
            }
        }

        public static void Uninstall()
        {
            const string LOG_IDENT = "StudioPluginInstaller::Uninstall";

            try
            {
                if (File.Exists(PluginFile))
                    File.Delete(PluginFile);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Uninstall failed: {ex.Message}");
            }
        }
    }
}
