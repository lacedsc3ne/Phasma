using PhasmaStrap.Integrations;

namespace PhasmaStrap.Utility
{
    public enum CaptureKind { Screenshot, Video, Gif }

    public sealed record CaptureFile(string Path, CaptureKind Kind, DateTime Taken, long Bytes);

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

        private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        public static string? Rename(string path, string newName, out string? error)
        {
            error = null;
            string extension = Path.GetExtension(path);
            string name = newName.Trim();

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

        public static void OpenEditor(string path, System.Windows.Window? owner = null)
        {
            try
            {
                if (!File.Exists(path) || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    return;

                System.Windows.Window editor = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? new PhasmaStrap.UI.Elements.Dialogs.ClipEditorWindow(path)
                    : new PhasmaStrap.UI.Elements.Dialogs.ScreenshotEditorWindow(path) { Modal = owner is not null };

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
