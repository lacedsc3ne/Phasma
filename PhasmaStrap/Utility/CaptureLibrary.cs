using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    public enum CaptureKind { Screenshot, Video, Gif }

    public sealed record CaptureFile(string Path, CaptureKind Kind, DateTime Taken, long Bytes);

    // The saved screenshots and clips, which game each one came from, and renaming. Shared by the
    // Capture page and the Captures window.
    public static class CaptureLibrary
    {
        private const string LOG_IDENT = "CaptureLibrary";

        public static List<CaptureFile> Screenshots()
        {
            if (!Directory.Exists(ScreenshotCapture.ScreenshotsDir))
                return new();

            return new DirectoryInfo(ScreenshotCapture.ScreenshotsDir)
                .GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new CaptureFile(f.FullName, CaptureKind.Screenshot, f.LastWriteTime, f.Length))
                .ToList();
        }

        public static List<CaptureFile> Clips()
        {
            if (!Directory.Exists(InstantReplayRecorder.ClipsDir))
                return new();

            return new DirectoryInfo(InstantReplayRecorder.ClipsDir)
                .GetFiles()
                .Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Name.EndsWith(".editing.mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new CaptureFile(f.FullName, f.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ? CaptureKind.Gif : CaptureKind.Video, f.LastWriteTime, f.Length))
                .ToList();
        }

        // ------------------------------------------------------------------ which game

        // The game being played when a capture was saved, from the Activity page's session history.
        // Captures from before that history started (or with it switched off) have no game.
        public sealed class GameIndex
        {
            private readonly List<(DateTime From, DateTime To, string Game)> _visits;

            public GameIndex()
            {
                try
                {
                    _visits = SessionStore.Shared.Load().Sessions
                        .SelectMany(s => s.Visits)
                        .Where(v => v.GameName.Length > 0 && v.JoinedUtc != default)
                        // a clip is saved at its end, and a session is saved about once a minute -
                        // allow a little either side of the visit
                        .Select(v => (v.JoinedUtc.AddSeconds(-30), (v.LeftUtc > v.JoinedUtc ? v.LeftUtc : v.JoinedUtc).AddMinutes(2), v.GameName))
                        .OrderBy(v => v.Item1)
                        .ToList();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Session history unreadable: {ex.Message}");
                    _visits = new();
                }
            }

            public string? GameAt(DateTime local)
            {
                DateTime utc = local.ToUniversalTime();
                string? found = null;

                // the latest visit that covers the moment (visits can overlap by the grace above)
                foreach (var (from, to, game) in _visits)
                {
                    if (from > utc)
                        break;
                    if (utc <= to)
                        found = game;
                }

                return found;
            }
        }

        // ------------------------------------------------------------------ rename / delete

        private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        // Renames a capture, keeping its extension. Returns the new path, or null with a reason.
        public static string? Rename(string path, string newName, out string? error)
        {
            error = null;
            string extension = Path.GetExtension(path);
            string name = newName.Trim();

            // someone typing "clip.mp4" means "clip"
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                name = name[..^extension.Length].TrimEnd();

            if (name.Length == 0)
            {
                error = "The name can't be empty.";
                return null;
            }

            if (name.IndexOfAny(Invalid) >= 0)
            {
                error = "A name can't contain any of these: \\ / : * ? \" < > |";
                return null;
            }

            if (name.EndsWith('.') || name.Length > 180)
            {
                error = name.Length > 180 ? "That name is too long." : "A name can't end with a dot.";
                return null;
            }

            string target = Path.Combine(Path.GetDirectoryName(path)!, name + extension);

            if (string.Equals(target, path, StringComparison.Ordinal))
                return path;

            // only a change of capital letters: File.Move to the "same" file is fine on Windows
            bool caseOnly = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);

            if (!caseOnly && File.Exists(target))
            {
                error = $"There's already a file called \"{name}{extension}\".";
                return null;
            }

            try
            {
                File.Move(path, target);
                App.Logger.WriteLine(LOG_IDENT, $"Renamed {Path.GetFileName(path)} to {Path.GetFileName(target)}");
                return target;
            }
            catch (Exception ex)
            {
                error = ex is IOException ? "The file is in use - close whatever has it open and try again." : ex.Message;
                return null;
            }
        }

        // to the Recycle Bin, so a wrong click can be undone
        public static bool Recycle(string path, out string? error)
        {
            error = null;
            try
            {
                if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch (Exception ex)
            {
                error = ex is IOException ? "The file is in use - close whatever has it open and try again." : ex.Message;
                App.Logger.WriteLine(LOG_IDENT, $"Could not delete {path}: {ex.Message}");
                return false;
            }
        }

        // ------------------------------------------------------------------ opening an editor

        // from the notification's Edit button (Watcher) or a list (settings)
        public static void OpenEditor(string path, System.Windows.Window? owner = null)
        {
            try
            {
                // GIFs can't be edited
                if (!File.Exists(path) || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    return;

                System.Windows.Window editor = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? new PhasmaStrap.UI.Elements.Dialogs.ClipEditorWindow(path)
                    : new PhasmaStrap.UI.Elements.Dialogs.ScreenshotEditorWindow(path);

                if (owner is not null)
                {
                    editor.Owner = owner;
                    editor.ShowDialog();
                }
                else
                {
                    editor.Show();
                    editor.Activate();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::OpenEditor", ex);
            }
        }
    }
}
