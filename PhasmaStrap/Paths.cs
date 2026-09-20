namespace PhasmaStrap
{
    static class Paths
    {
        public static string Temp => Path.Combine(Path.GetTempPath(), App.ProjectName);
        public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static string WindowsStartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        public static string System => Environment.GetFolderPath(Environment.SpecialFolder.System);

        public static string RobloxLogs => Path.Combine(LocalAppData, "Roblox", "logs");
        public static string RobloxCache => Path.Combine(LocalAppData, "Roblox", "rbx-storage");
        public static string ServerFetch => Path.Combine(LocalAppData, "PhasmaStrap", "ServerFetch");
        public static string PlayTime => Path.Combine(LocalAppData, "PhasmaStrap", "PlayTime");

        public static string Process => Environment.ProcessPath!;

        public static string TempUpdates => Path.Combine(Temp, "Updates");
        public static string TempLogs => Path.Combine(Temp, "Logs");

        public static string Base { get; private set; } = "";
        public static string Downloads { get; private set; } = "";
        public static string Logs { get; private set; } = "";
        public static string Integrations { get; private set; } = "";
        public static string Versions { get; private set; } = "";
        public static string Modifications { get; private set; } = "";
        public static string CustomThemes { get; private set; } = "";
        public static string AccountBackups { get; private set; } = "";

        public static string ManagedMods { get; private set; } = "";
        public static string ManagedModPackages { get; private set; } = "";
        public static string ManagedModIndex { get; private set; } = "";

        public static string CursorSets { get; private set; } = "";
        public static string CursorSetsRoot { get; private set; } = "";
        public static string CursorSetsIndex { get; private set; } = "";

        public static string Application { get; private set; } = "";

        public static string CustomFont => Path.Combine(Modifications, "content\\fonts\\CustomFont.ttf");

        public static string CustomDeathSound => Path.Combine(Modifications, "content\\sounds\\oof.ogg");

        public static string CustomSkybox => Path.Combine(Modifications, "PlatformContent\\pc\\textures\\sky");

        public static string CustomColorThemeXaml => Path.Combine(Base, "CustomColorTheme.xaml");

        public static string SettingsBackups => Path.Combine(Base, "Backups");

        public static bool Initialized => !String.IsNullOrEmpty(Base);

        public static void Initialize(string baseDirectory)
        {
            Base = baseDirectory;
            Downloads = Path.Combine(Base, "Downloads");
            Logs = Path.Combine(Base, "Logs");
            Integrations = Path.Combine(Base, "Integrations");
            Versions = Path.Combine(Base, "Versions");
            Modifications = Path.Combine(Base, "Modifications");
            CustomThemes = Path.Combine(Base, "CustomThemes");
            AccountBackups = Path.Combine(Base, "Accounts");

            ManagedMods = Path.Combine(Base, "ManagedMods");
            ManagedModPackages = Path.Combine(ManagedMods, "Packages");
            ManagedModIndex = Path.Combine(ManagedMods, "Index.json");

            CursorSets = Path.Combine(Base, "CursorSets");
            CursorSetsRoot = Path.Combine(CursorSets, "Sets");
            CursorSetsIndex = Path.Combine(CursorSets, "Index.json");

            Application = Path.Combine(Base, $"{App.ProjectName}.exe");
        }
    }
}
