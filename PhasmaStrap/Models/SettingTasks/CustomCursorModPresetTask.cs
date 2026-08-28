using PhasmaStrap.Models.SettingTasks.Base;

namespace PhasmaStrap.Models.SettingTasks
{
    /// <summary>
    /// Applies a user-supplied local folder of cursor images as a raw-file override under
    /// <see cref="Paths.Modifications"/>, mirroring the copy-into-Mods mechanism that
    /// <see cref="EnumModPresetTask{T}"/> uses for the bundled cursor presets - except the
    /// source files come from a folder the user browses to instead of an embedded resource.
    /// </summary>
    public class CustomCursorModPresetTask : StringBaseTask
    {
        // filename (as expected directly inside the user's chosen folder) -> path relative to Paths.Modifications
        private static readonly Dictionary<string, string> FileMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MouseLockedCursor.png", @"content\textures\MouseLockedCursor.png" },
            { "ArrowCursor.png",       @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png" },
            { "ArrowFarCursor.png",    @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png" },
            { "IBeamCursor.png",       @"content\textures\Cursors\KeyboardMouse\IBeamCursor.png" }
        };

        public static IEnumerable<string> RecognizedFileNames => FileMap.Keys;

        public CustomCursorModPresetTask() : base("ModPreset", "CustomCursorSet") { }

        public override void Execute()
        {
            if (!String.IsNullOrEmpty(NewState) && Directory.Exists(NewState))
            {
                foreach (var pair in FileMap)
                {
                    string sourceFile = Path.Combine(NewState, pair.Key);
                    string targetFile = Path.Combine(Paths.Modifications, pair.Value);

                    if (File.Exists(sourceFile))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                        Filesystem.AssertReadOnly(targetFile);
                        File.Copy(sourceFile, targetFile, true);
                    }
                    else if (File.Exists(targetFile))
                    {
                        // the folder no longer supplies this cursor (or never did) - clear any
                        // previously-applied override so switching folders doesn't leave stale files
                        Filesystem.AssertReadOnly(targetFile);
                        File.Delete(targetFile);
                    }
                }
            }
            else
            {
                foreach (string relativePath in FileMap.Values)
                {
                    string targetFile = Path.Combine(Paths.Modifications, relativePath);

                    if (File.Exists(targetFile))
                    {
                        Filesystem.AssertReadOnly(targetFile);
                        File.Delete(targetFile);
                    }
                }
            }

            OriginalState = NewState;
        }
    }
}
