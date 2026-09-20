using PhasmaStrap.Models.SettingTasks.Base;

namespace PhasmaStrap.Models.SettingTasks
{
    public class CustomCursorModPresetTask : StringBaseTask
    {
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
                    string? sourceFile = CursorImages.FindSource(NewState, pair.Key);
                    string targetFile = Path.Combine(Paths.Modifications, pair.Value);

                    if (sourceFile is not null)
                    {
                        CursorImages.WritePng(sourceFile, targetFile);
                    }
                    else if (File.Exists(targetFile))
                    {
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
