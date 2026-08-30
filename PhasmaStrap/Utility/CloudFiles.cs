using System.Runtime.InteropServices;

namespace PhasmaStrap.Utility;

// Detects OneDrive (and other cloud-sync provider) placeholder files - files that show up in
// directory listings but are actually cloud-only stubs Windows hasn't downloaded yet - so that
// install/update file copies don't silently fail when the install folder or a Roblox version
// folder ends up inside a synced directory (eg. via Known Folder Move redirecting AppData/Desktop
// to OneDrive after installation). Ported from Voidstrap.
internal static class CloudFiles
{
    private const int Pinned = 0x00080000;

    private const int Unpinned = 0x00100000;

    private const int RecallOnOpen = 0x00040000;

    private const int RecallOnDataAccess = 0x00400000;

    private const int Offline = 0x00001000;

    private const int PlaceholderMask = RecallOnOpen | RecallOnDataAccess | Offline;

    private static readonly string[] CloudEnvVars = { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" };

    private static readonly string[] CloudFolderNames = { "OneDrive", "Dropbox", "Google Drive", "GoogleDrive", "iCloudDrive", "Creative Cloud Files", "Box Sync", "MEGAsync", "pCloudDrive", "Nextcloud", "Syncthing", "Yandex.Disk" };

    [DllImport("kernel32.dll", EntryPoint = "SetFileAttributesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileAttributes(string fileName, uint attributes);

    /// <summary>
    /// Checks whether the given path is a cloud-sync placeholder (ie. not actually present on disk yet).
    /// </summary>
    public static bool IsPlaceholder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return ((int)File.GetAttributes(path) & PlaceholderMask) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the given path lives under a folder synced by a known cloud storage provider
    /// (OneDrive, Dropbox, Google Drive, etc), regardless of whether the file itself is a placeholder.
    /// </summary>
    public static bool IsCloudSyncedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        }
        catch
        {
            return false;
        }

        foreach (string variable in CloudEnvVars)
        {
            string root;
            try
            {
                root = Environment.GetEnvironmentVariable(variable) ?? "";
            }
            catch
            {
                continue;
            }

            if (root.Length == 0)
                continue;

            try
            {
                root = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            }
            catch
            {
                continue;
            }

            if (full.StartsWith(root, StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        foreach (string folder in CloudFolderNames)
        {
            if (full.Contains($"\\{folder}\\", StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether an exception thrown from a file operation was likely caused by the target
    /// being an unhydrated cloud-sync placeholder, rather than a genuine I/O or permissions failure.
    /// </summary>
    public static bool IsCloudFailure(Exception exception, string? path)
    {
        if (exception is not IOException && exception is not UnauthorizedAccessException)
            return false;

        return IsPlaceholder(path) || IsCloudSyncedPath(path);
    }

    /// <summary>
    /// Forces a cloud-sync placeholder to be downloaded locally by reading a byte from it.
    /// Returns true if the file is now available locally (or wasn't a placeholder to begin with).
    /// </summary>
    public static bool Hydrate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!IsPlaceholder(path))
            return true;

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 0)
                stream.ReadByte();

            return true;
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("CloudFiles::Hydrate", $"Could not download {path} from the cloud provider: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pins a file or folder so the cloud-sync provider keeps it available locally instead of
    /// letting it be dehydrated back into a placeholder.
    /// </summary>
    public static void PinLocally(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !OperatingSystem.IsWindows())
            return;

        try
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                return;

            int current = (int)File.GetAttributes(path);
            int wanted = (current & ~Unpinned) | Pinned;

            if (wanted != current)
                SetFileAttributes(path, (uint)wanted);
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("CloudFiles::PinLocally", $"Could not pin {path}: {ex.Message}");
        }
    }
}
